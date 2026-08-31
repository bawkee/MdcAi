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

namespace MdcAi.ChatUI.Tests;

using MdcAi.ChatUI.ViewModels;
using OpenAiApi;

/// <summary>
/// P1-10 conversation controller: tools-enabled conversations run their whole turn through
/// ChatSessionService (explicit RunTurnCmd), while tools-disabled chat keeps the classic
/// tail-driven completion. One session controller per conversation; navigation does not cancel.
/// </summary>
public class ConversationAgenticTests : IDisposable
{
    private readonly string _workspace;

    public ConversationAgenticTests()
    {
        TestRx.Init();
        _workspace = Path.Combine(Path.GetTempPath(), "mdcai-chatui-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_workspace);
    }

    public void Dispose()
    {
        try { Directory.Delete(_workspace, recursive: true); }
        catch { /* best-effort */ }
    }

    private (ConversationVm convo, FakeOpenAiApi api) MakeAgentic()
    {
        var api = new FakeOpenAiApi();
        var store = new InMemoryCredsStore();
        store.SetValue("openai:ApiKey", "sk-oa");
        store.SetValue("openrouter:ApiKey", "sk-or");
        var settings = TestSettings.Build(store);
        var chatSettings = new ChatSettingsVm(api);
        var convo = new ConversationVm(api, settings, chatSettings)
        {
            ToolsEnabled = true,
            WorkspacePath = _workspace,
            SelectedModel = "deepseek/deepseek-chat",
            SelectedEffort = "medium"
        };
        return (convo, api);
    }

    private static IAsyncEnumerable<ChatResult> Stream(params ChatResult[] chunks)
    {
        async IAsyncEnumerable<ChatResult> Enumerate()
        {
            foreach (var c in chunks)
                yield return c;
        }

        return Enumerate();
    }

    [Fact]
    public async Task Agentic_send_runs_read_then_answer_through_the_controller()
    {
        var (convo, api) = MakeAgentic();
        await File.WriteAllTextAsync(Path.Combine(_workspace, "a.txt"), "hello workspace");

        // Step 1: assistant asks to read the file; step 2: final answer.
        api.ScriptedStreams.Enqueue(Stream(
            RoleChunk(), ToolCallChunk(0, "call_1", "read_file", """{"path":"a.txt"}"""), Finish("tool_calls")));
        api.ScriptedStreams.Enqueue(Stream(
            RoleChunk(), Content("The file contains: hello workspace"), Finish("stop")));

        convo.Prompt.Contents = "read the file";
        await convo.SendPromptCmd.Execute();

        await WaitUntilAsync(() => !convo.IsCompleting);

        // Transcript: user → assistant(tool_calls) → tool result → assistant(final).
        var messages = convo.Head.Message.GetNextMessages().ToArray();
        Assert.Equal(4, messages.Length);
        Assert.Equal(new[] { "user", "assistant", "tool", "assistant" }, messages.Select(m => m.Role).ToArray());

        var toolAssistant = messages[1];
        Assert.Single(toolAssistant.ToolCalls);
        Assert.Equal("call_1", toolAssistant.ToolCalls[0].Id);
        Assert.Equal("read_file", toolAssistant.ToolCalls[0].Function.Name);
        Assert.Null(toolAssistant.Content); // tool-call step has explicit null content

        var toolResult = messages[2];
        Assert.Equal("call_1", toolResult.ToolCallId);
        Assert.Equal("read_file", toolResult.ToolName);
        Assert.Contains("a.txt", toolResult.Content);

        var final = messages[3];
        Assert.Equal("The file contains: hello workspace", final.Content);
        Assert.Equal("completed", final.CompletionState);

        // The request history carried the assistant tool call + the tool result into step 2.
        Assert.Equal(2, api.Requests.Count);
        Assert.Contains(api.Requests[1].Messages, m => m.Role == ChatMessageRole.Tool && m.ToolCallId == "call_1");
        Assert.Contains(api.Requests[1].Messages, m => m.Role == ChatMessageRole.Assistant && m.ToolCalls is { Length: 1 });

        // Steady state: once the prompt is non-empty another send is allowed.
        convo.Prompt.Contents = "next";
        await Task.Yield();
        Assert.True(convo.CanSendPrompt);
    }

    [Fact]
    public async Task Tools_disabled_keeps_the_classic_tail_driven_completion()
    {
        var (convo, _) = MakeAgentic();
        convo.ToolsEnabled = false; // default anyway

        convo.Prompt.Contents = "hi";
        await convo.SendPromptCmd.Execute();

        await WaitUntilAsync(() => !convo.IsCompleting);

        var messages = convo.Head.Message.GetNextMessages().ToArray();
        Assert.Equal(2, messages.Length);
        Assert.Equal(ChatMessageRole.User, messages[0].Role);
        Assert.Equal(ChatMessageRole.Assistant, messages[1].Role);
        // Classic fake response text.
        Assert.StartsWith("hello from", messages[1].Content);
        // No agentic artifacts.
        Assert.Null(messages[1].ToolCalls);

        convo.Prompt.Contents = "next";
        await Task.Yield();
        Assert.True(convo.CanSendPrompt);
    }

    [Fact]
    public async Task Cancellation_interrupts_agentic_run_and_reenables_send()
    {
        var (convo, api) = MakeAgentic();

        // Stream delivers a prefix and then hangs; stop cancels it.
        using var cts = new CancellationTokenSource();
        api.ScriptedStreams.Enqueue(HangAfterContent("partial answer "));

        convo.Prompt.Contents = "long task";
        await convo.SendPromptCmd.Execute();

        try
        {
            await WaitUntilAsync(() => convo.IsCompleting);
        }
        catch (TimeoutException)
        {
            var dumpNow = convo.Head == null
                              ? "<no head>"
                              : string.Join(" | ", convo.Head.Message.GetNextMessages()
                                                          .Select(m => $"{m.Role}:{(m.Content ?? "<null>")}:{m.CompletionState ?? "-"}"));
            throw new TimeoutException(
                $"IsCompleting never set. Requests={api.Requests.Count} Transcript: {dumpNow}");
        }

        // Wait for the prefix with a dump on timeout so failures show the transcript state.
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (convo.Tail?.Message.Content != "partial answer ")
        {
            if (sw.ElapsedMilliseconds > 10000)
            {
                var dumpNow = string.Join(" | ", convo.Head.Message.GetNextMessages()
                                                        .Select(m => $"{m.Role}:{(m.Content ?? "<null>")}:{m.CompletionState ?? "-"}"));
                throw new TimeoutException("Prefix never landed. Transcript: " + dumpNow);
            }

            await Task.Delay(25);
        }

        convo.Prompt.Contents = "next";
        await Task.Yield();
        Assert.False(convo.CanSendPrompt, "send must stay disabled while the turn is active");

        convo.StopSessionCmd.Execute().Subscribe();

        await WaitUntilAsync(() => !convo.IsCompleting);
        convo.Prompt.Contents = "next";
        await Task.Yield();
        Assert.True(convo.CanSendPrompt);

        // The delivered prefix is kept, honestly interrupted.
        Assert.Equal("interrupted", convo.Tail.Message.CompletionState);
        Assert.Equal("partial answer ", convo.Tail.Message.Content);
    }

    [Fact]
    public async Task Two_conversations_run_independently()
    {
        var (convoA, apiA) = MakeAgentic();
        var (convoB, apiB) = MakeAgentic();
        await File.WriteAllTextAsync(Path.Combine(_workspace, "a.txt"), "shared workspace");

        apiA.ScriptedStreams.Enqueue(Stream(RoleChunk(), Content("answer A"), Finish("stop")));
        apiB.ScriptedStreams.Enqueue(Stream(RoleChunk(), Content("answer B"), Finish("stop")));

        convoA.Prompt.Contents = "go a";
        convoB.Prompt.Contents = "go b";
        await convoA.SendPromptCmd.Execute();
        await convoB.SendPromptCmd.Execute();

        // Both complete (none cancels the other - per-conversation controllers).
        await WaitUntilAsync(() => !convoA.IsCompleting && !convoB.IsCompleting);

        Assert.Equal("answer A", convoA.Head.Message.GetNextMessages().Last().Content);
        Assert.Equal("answer B", convoB.Head.Message.GetNextMessages().Last().Content);
        Assert.Equal(1, apiA.Requests.Count);
        Assert.Equal(1, apiB.Requests.Count);
    }

    [Fact]
    public async Task Regenerate_reruns_only_the_final_step_from_accepted_results()
    {
        var (convo, api) = MakeAgentic();
        await File.WriteAllTextAsync(Path.Combine(_workspace, "a.txt"), "hello workspace");

        // Original run: read → "first answer".
        api.ScriptedStreams.Enqueue(Stream(RoleChunk(), ToolCallChunk(0, "call_1", "read_file", """{"path":"a.txt"}"""), Finish("tool_calls")));
        api.ScriptedStreams.Enqueue(Stream(RoleChunk(), Content("first answer"), Finish("stop")));

        convo.Prompt.Contents = "read the file";
        await convo.SendPromptCmd.Execute();
        await WaitUntilAsync(() => !convo.IsCompleting);

        var toolResultsBefore = convo.Head.Message.GetNextMessages().Count(m => m.Role == ChatMessageRole.Tool);
        Assert.Equal(1, toolResultsBefore); // reads do NOT repeat on regenerate

        // Regenerate the tail: only the final assistant step re-runs.
        convo.SelectedMessage = convo.Tail;
        api.ScriptedStreams.Enqueue(Stream(RoleChunk(), Content("regenerated answer"), Finish("stop")));

        convo.RegenerateSelectedCmd.Execute().Subscribe();
        await WaitUntilAsync(() => !convo.IsCompleting);

        var messages = convo.Head.Message.GetNextMessages().ToArray();
        Assert.Equal(4, messages.Length); // user, assistant(tool), tool, assistant(regenerated)
        Assert.Equal(1, messages.Count(m => m.Role == ChatMessageRole.Tool)); // still one read result
        Assert.Equal("regenerated answer", messages[^1].Content);
    }

    private static ChatResult RoleChunk() => new()
    {
        Id = "req",
        Choices = new[] { new ChatChoice { Index = 0, Delta = new ChatMessage(ChatMessageRole.Assistant) } }
    };

    private static ChatResult Content(string text) => new()
    {
        Id = "req",
        Choices = new[] { new ChatChoice { Index = 0, Delta = new ChatMessage(ChatMessageRole.Assistant, text) } }
    };

    private static ChatResult ToolCallChunk(int index, string id, string name, string args) => new()
    {
        Id = "req",
        Choices = new[]
        {
            new ChatChoice
            {
                Index = 0,
                Delta = new ChatMessage(ChatMessageRole.Assistant)
                {
                    ToolCalls = new[]
                    {
                        new ChatMessageToolCall
                        {
                            Index = index,
                            Id = id,
                            Function = new ChatMessageFunction { Name = name, Arguments = args }
                        }
                    }
                }
            }
        }
    };

    private static ChatResult Finish(string reason) => new()
    {
        Id = "req",
        Choices = new[] { new ChatChoice { Index = 0, FinishReason = reason } }
    };

    private static IAsyncEnumerable<ChatResult> HangAfterContent(string prefix)
    {
        async IAsyncEnumerable<ChatResult> Enumerate()
        {
            yield return Content(prefix);
            await new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously).Task;
        }

        return Enumerate();
    }

    private static async Task WaitUntilAsync(Func<bool> condition, int timeoutMs = 10000)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (!condition())
        {
            if (sw.ElapsedMilliseconds > timeoutMs)
                throw new TimeoutException("Condition was not met in time.");
            await Task.Delay(25);
        }
    }
}