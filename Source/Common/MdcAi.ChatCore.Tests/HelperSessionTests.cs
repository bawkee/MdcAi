#region Copyright Notice
// Copyright (c) 2023 Bojan Sala
//   Licensed under the Apache License, Version 2.0 (the "License");
//   you may not use this file except in compliance with the License.
//   You may obtain a copy of the License at
//      http: www.apache.org/licenses/LICENSE-2.0
//   Unless required by applicable law or agreed to in writing, software
//   distributed under the License is distributed on an "AS IS" BASIS,
//   WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
//   See the License for the specific language governing permissions and
//   limitations under the License.
#endregion

namespace MdcAi.ChatCore.Tests;

using MdcAi.ChatCore.Helpers;
using MdcAi.ChatCore.Security;
using MdcAi.ChatCore.Tools;
using MdcAi.ChatCore.Tools.BuiltIn;
using MdcAi.OpenAiApi;

/// <summary>
/// P3-03 one-shot helper: independent transcript, read-only filtered tools (no shell/writes),
/// its own limits, parent cancellation linked, and results that come back as an ordinary
/// structured tool result.
/// </summary>
public class HelperSessionTests
{
    private readonly ScriptedFakeApi _api = new();
    private readonly ChatToolRegistry _parentRegistry;

    public HelperSessionTests()
    {
        // Build the parent registry WITHOUT the delegate tool first (the tool needs the helper
        // service, which needs the parent registry - no cycles).
        var plain = ChatToolRegistry.Build(new IChatTool[]
        {
            new ReadFileChatTool(),
            new ListDirChatTool(),
            new GrepChatTool(),
            new WriteFileChatTool(),   // must NOT be granted to helpers
            new RunPowerShellChatTool(null, () => null) // must NOT be granted
        });

        _apiBoundHelper = new HelperSessionService(_api, plain);
        _parentRegistry = ChatToolRegistry.Build(plain.All.Append(new DelegateTaskChatTool(_apiBoundHelper)));
    }

    private readonly HelperSessionService _apiBoundHelper;

    private HelperSessionService MakeHelperService() => new(_api, _parentRegistry);

    private static readonly string[] DefaultTools = { "read_file", "list_dir", "grep" };

    private static IAsyncEnumerable<ChatResult> Stream(params ChatResult[] chunks) =>
        Enumerate(chunks);

    private static async IAsyncEnumerable<ChatResult> Enumerate(ChatResult[] chunks)
    {
        foreach (var c in chunks)
            yield return c;
        await Task.CompletedTask;
    }

    private static ChatResult Content(string text) => new()
    {
        Id = "r",
        Choices = new[] { new ChatChoice { Index = 0, Delta = new ChatMessage(ChatMessageRole.Assistant, text) } }
    };

    private static ChatResult Role() => new()
    {
        Id = "r",
        Choices = new[] { new ChatChoice { Index = 0, Delta = new ChatMessage(ChatMessageRole.Assistant) } }
    };

    private static ChatResult ToolCall(int index, string id, string name, string args) => new()
    {
        Id = "r",
        Choices = new[]
        {
            new ChatChoice
            {
                Index = 0,
                Delta = new ChatMessage(ChatMessageRole.Assistant)
                {
                    ToolCalls = new[] { new ChatMessageToolCall { Index = index, Id = id, Function = new ChatMessageFunction { Name = name, Arguments = args } } }
                }
            }
        }
    };

    private static ChatResult Finish(string reason) => new()
    {
        Id = "r",
        Choices = new[] { new ChatChoice { Index = 0, FinishReason = reason } }
    };

    [Fact]
    public async Task Helper_reads_a_file_and_returns_final_answer_with_reference()
    {
        var ws = Path.Combine(Path.GetTempPath(), "mdcai-helper-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(ws);
        await File.WriteAllTextAsync(Path.Combine(ws, "a.txt"), "found-it");

        try
        {
            // The helper: step 1 asks to read; step 2 answers.
            _api.EnqueueStream(inspect =>
                {
                    // The child request carries the parent's provider/model and a scoped premise.
                    Assert.Equal("deepseek/deepseek-chat", inspect.Model);
                    var system = inspect.Messages.First(m => m.Role == ChatMessageRole.System);
                    Assert.Contains("one-shot helper", system.Content);
                },
                Role(), ToolCall(0, "hc1", "read_file", """{"path":"a.txt"}"""), Finish("tool_calls"));

            _api.EnqueueStream(Role(), Content("The file a.txt says 'found-it'."), Finish("stop"));

            var service = MakeHelperService();
            var result = await service.RunAsync(new HelperRunRequest(
                "c1", "turn-1", "call_p", AiProviders.OpenRouterKey, "deepseek/deepseek-chat", null,
                ws, "Read a.txt and report its content.", DefaultTools, null, HelperRunLimits.Default),
                CancellationToken.None);

            Assert.True(result.Ok);
            Assert.Equal("completed", result.Status);
            Assert.Contains("found-it", result.FinalAnswer);
            Assert.Contains("a.txt", result.FileReferences);
            Assert.Equal(2, result.StepCount);
        }
        finally
        {
            try { Directory.Delete(ws, recursive: true); } catch { }
        }
    }

    [Fact]
    public async Task Helper_only_sets_up_read_only_tools()
    {
        var registry = ChatToolRegistry.Build(new IChatTool[]
        {
            new ReadFileChatTool(), new GrepChatTool(), new WriteFileChatTool()
        });
        var helperService = new HelperSessionService(_api, registry);

        var names = helperService.ReadOnlyRegistry().All.Select(t => t.Name).OrderBy(n => n).ToArray();
        Assert.Equal(new[] { "grep", "read_file" }, names); // write_file filtered out
        Assert.DoesNotContain("write_file", names);
        Assert.DoesNotContain("run_powershell", names);
    }

    [Fact]
    public async Task Helper_can_be_cancelled_with_the_parent()
    {
        using var cts = new CancellationTokenSource();

        // The child stream delivers a content prefix and then hangs - cancellation must cut it.
        _api.EnqueueHanging(Content("starting "));

        var service = MakeHelperService();
        var run = service.RunAsync(new HelperRunRequest(
            "c1", "turn-1", "call_p", AiProviders.OpenRouterKey, "deepseek/deepseek-chat", null,
            @"C:\ws", "inspect", DefaultTools, null, HelperRunLimits.Default),
            cts.Token);

        await Task.Delay(20);
        cts.Cancel();

        var result = await run;
        Assert.False(result.Ok);
        Assert.Equal("cancelled", result.Status);
    }

    [Fact]
    public async Task Helper_wall_clock_timeout_returns_timed_out()
    {
        // A stream that never completes + a hair-thin wall limit forces the timeout.
        _api.EnqueueHanging(Content("still going "));

        var service = MakeHelperService();
        var limits = new HelperRunLimits(6, TimeSpan.FromMilliseconds(50));
        var result = await service.RunAsync(new HelperRunRequest(
            "c1", "turn-1", "call_p", AiProviders.OpenRouterKey, "deepseek/deepseek-chat", null,
            @"C:\ws", "inspect", DefaultTools, null, limits),
            CancellationToken.None);

        Assert.False(result.Ok);
        Assert.Equal("timed_out", result.Status);
    }

    [Fact]
    public async Task Helper_step_limit_is_enforced()
    {
        // Every helper step ends with another tool call -> step guard hits MaxSteps.
        for (var i = 0; i < 8; i++)
            _api.EnqueueStream(Role(), ToolCall(0, "h" + i, "read_file", """{"path":"a.txt"}"""), Finish("tool_calls"));

        var service = MakeHelperService();
        var limits = new HelperRunLimits(3, TimeSpan.FromMinutes(5));
        var result = await service.RunAsync(new HelperRunRequest(
            "c1", "turn-1", "call_p", AiProviders.OpenRouterKey, "deepseek/deepseek-chat", null,
            @"C:\ws", "loop", DefaultTools, null, limits),
            CancellationToken.None);

        // The helper hit the step cap - a structured non-ok result (not a crash).
        Assert.Equal("maxsteps", result.Status);
    }

    [Fact]
    public async Task DelegateTaskTool_returns_helper_result_as_structured_tool_result()
    {
        _api.EnqueueStream(Role(), Content("The answer is: 42 in file b.txt."), Finish("stop"));

        var registry = ChatToolRegistry.Build(new IChatTool[] { new DelegateTaskChatTool(MakeHelperService()) });
        var tool = registry.All.First();

        var result = await tool.ExecuteAsync(
            JObject.Parse("""{"description":"inspect","prompt":"Find the answer."}"""),
            new ChatToolExecutionContext("c1", "turn-1", 4, "call_d", @"C:\ws",
                new WorkspaceReadObservationSet(), null, null, AiProviders.OpenRouterKey,
                "deepseek/deepseek-chat", null),
            CancellationToken.None);

        Assert.True(result.Ok);
        Assert.Equal("completed", (string)result.Value["status"]);
        Assert.Contains("42", result.ModelContent);
        Assert.Equal("delegate_task", tool.Name);
    }
}