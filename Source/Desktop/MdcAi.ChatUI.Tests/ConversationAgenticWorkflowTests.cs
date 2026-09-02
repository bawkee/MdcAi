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
using Newtonsoft.Json.Linq;
using OpenAiApi;

/// <summary>
/// The real user workflows the app must survive (regression net for the round of bug reports):
///   1. two consecutive prompts both complete (second one used to hang in "working" forever);
///   2. Stop Generation cancels a running agentic turn and leaves an honest interrupted prefix;
///   3. reasoning from OpenRouter's raw <c>delta.reasoning</c> (not reasoning_content) is
///      VISIBLE: the streaming node carries display reasoning and the final snapshot emits a
///      thinking activity;
///   4. a new conversation gets an auto-generated name from its first user message.
/// </summary>
public class ConversationAgenticWorkflowTests : IDisposable
{
    private readonly string _workspace;

    public ConversationAgenticWorkflowTests()
    {
        TestRx.Init();
        _workspace = Path.Combine(Path.GetTempPath(), "mdcai-workflow-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_workspace);
    }

    public void Dispose()
    {
        try { Directory.Delete(_workspace, recursive: true); }
        catch { }
    }

    private (ConversationVm convo, FakeOpenAiApi api) MakeAgentic()
    {
        var api = new FakeOpenAiApi();
        var store = new InMemoryCredsStore();
        store.SetValue("openai:ApiKey", "sk-oa");
        store.SetValue("openrouter:ApiKey", "sk-or");
        var chatSettings = new ChatSettingsVm(api);
        var convo = new ConversationVm(api, TestSettings.Build(store), chatSettings)
        {
            ToolsEnabled = true,
            WorkspacePath = _workspace,
            SelectedModel = "deepseek/deepseek-chat",
            SelectedEffort = "medium"
        };
        return (convo, api);
    }

    private static IAsyncEnumerable<ChatResult> Stream(params ChatResult[] chunks) =>
        Enumerate(chunks);

    private static async IAsyncEnumerable<ChatResult> Enumerate(ChatResult[] chunks)
    {
        foreach (var c in chunks)
            yield return c;
        await Task.CompletedTask;
    }

    private static ChatResult RoleChunk() => new()
    {
        Id = "r",
        Choices = new[] { new ChatChoice { Index = 0, Delta = new ChatMessage(ChatMessageRole.Assistant) } }
    };

    private static ChatResult Content(string text) => new()
    {
        Id = "r",
        Choices = new[] { new ChatChoice { Index = 0, Delta = new ChatMessage(ChatMessageRole.Assistant, text) } }
    };

    private static ChatResult RawReasoning(string text) => new()
    {
        Id = "r",
        Choices = new[]
        {
            new ChatChoice
            {
                Index = 0,
                // OpenRouter routes (e.g. some DeepSeek hosts) send thinking in delta.reasoning
                // as a raw STRING - never in reasoning_content.
                Delta = new ChatMessage(ChatMessageRole.Assistant) { ReasoningRaw = new JValue(text) }
            }
        }
    };

    private static ChatResult ReasoningContent(string text) => new()
    {
        Id = "r",
        Choices = new[] { new ChatChoice { Index = 0, Delta = new ChatMessage(ChatMessageRole.Assistant) { ReasoningContent = text } } }
    };

    private static ChatResult ToolCallChunk(int index, string id, string name, string args) => new()
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
    public async Task Two_consecutive_prompts_both_complete_and_reenable_send()
    {
        var (convo, api) = MakeAgentic();
        await File.WriteAllTextAsync(Path.Combine(_workspace, "a.txt"), "time data");

        // Prompt 1: read a file -> answer.
        api.ScriptedStreams.Enqueue(Stream(RoleChunk(), ToolCallChunk(0, "c1", "read_file", """{"path":"a.txt"}"""), Finish("tool_calls")));
        api.ScriptedStreams.Enqueue(Stream(RoleChunk(), Content("Currently 10:30 AM."), Finish("stop")));

        convo.Prompt.Contents = "what time is it";
        await convo.SendPromptCmd.Execute();

        await WaitUntilAsync(() => !convo.IsCompleting);

        Assert.False(convo.IsCompleting);
        Assert.Equal("Currently 10:30 AM.", convo.Tail.Message.Content);
        convo.Prompt.Contents = "next";
        await Task.Yield();
        Assert.True(convo.CanSendPrompt, "prompt 1 done - send must be enabled again");

        // Prompt 2: plain question on the same conversation (history now has a tool turn).
        api.ScriptedStreams.Enqueue(Stream(RoleChunk(), Content("257 days left."), Finish("stop")));

        convo.Prompt.Contents = "how many days until end of year";
        await convo.SendPromptCmd.Execute();

        // THE regression: the second turn must terminate, not spin forever.
        await WaitUntilAsync(() => !convo.IsCompleting);

        var nodes = convo.Head.Message.GetNextMessages().ToArray();
        Assert.Equal("257 days left.", nodes[^1].Content);
        Assert.False(convo.IsCompleting);
        convo.Prompt.Contents = "next";
        await Task.Yield();
        Assert.True(convo.CanSendPrompt, "prompt 2 done - send must be enabled again");
    }

    [Fact]
    public async Task Stop_generation_cancels_a_running_agentic_turn_and_marks_interrupted()
    {
        var (convo, api) = MakeAgentic();

        // Stream delivers a prefix and then hangs; Stop must cut it and re-enable send.
        api.ScriptedStreams.Enqueue(HangAfterContent("partial "));

        convo.Prompt.Contents = "long task";
        await convo.SendPromptCmd.Execute();

        await WaitUntilAsync(() => convo.IsCompleting);
        await WaitUntilAsync(() => convo.Tail?.Message.Content == "partial ");
        Assert.False(convo.CanSendPrompt);

        convo.StopSessionCmd.Execute().Subscribe();

        await WaitUntilAsync(() => !convo.IsCompleting);

        convo.Prompt.Contents = "next";
        await Task.Yield();
        Assert.True(convo.CanSendPrompt);
        Assert.Equal("interrupted", convo.Tail.Message.CompletionState);
        Assert.Equal("partial ", convo.Tail.Message.Content);
    }

    [Fact]
    public async Task OpenRouter_raw_reasoning_is_visible_live_and_as_a_thinking_activity()
    {
        var (convo, api) = MakeAgentic();
        await File.WriteAllTextAsync(Path.Combine(_workspace, "a.txt"), "data");

        // Some OpenRouter routes send thinking ONLY as delta.reasoning (raw string), never
        // reasoning_content. The streaming node must still surface it, and the committed
        // snapshot must emit a thinking activity.
        api.ScriptedStreams.Enqueue(Stream(
            RoleChunk(),
            RawReasoning("I should read the file "),
            RawReasoning("to know the answer."),
            ToolCallChunk(0, "c1", "read_file", """{"path":"a.txt"}"""),
            Finish("tool_calls")));
        api.ScriptedStreams.Enqueue(Stream(RoleChunk(), Content("Answer is data."), Finish("stop")));

        convo.Prompt.Contents = "read it";
        await convo.SendPromptCmd.Execute();

        await WaitUntilAsync(() => !convo.IsCompleting);

        // The tool-calling assistant node must carry DISPLAYABLE reasoning derived from raw.
        var assistant = convo.Head.Message.GetNextMessages()
                             .First(m => m.Role == ChatMessageRole.Assistant && m.ToolCalls != null);
        Assert.False(string.IsNullOrWhiteSpace(assistant.ReasoningContent),
                     "raw delta.reasoning must surface as display reasoning");
        Assert.Contains("read the file", assistant.ReasoningContent);

        // The final transcript snapshot contains a thinking activity for that node.
        var snapshot = WebViewTranscriptProjector.Project(convo, convo.Head.Message.GetNextMessages(), convo.Revision);
        var thinking = snapshot.Items.FirstOrDefault(i => i.Id == $"thinking:{assistant.Id}");
        Assert.NotNull(thinking);
        Assert.Equal("thinking", thinking.Activity.ActivityKind);
        Assert.Contains("read the file", thinking.Activity.Summary);
    }

    [Fact]
    public async Task New_conversation_gets_a_name_from_the_first_user_message()
    {
        var (convo, _) = MakeAgentic();
        Assert.Equal("My Conversation", convo.Name); // placeholder until first message

        convo.Prompt.Contents = "Help me understand how HTTP works please";
        // Simulate a non-agentic-looking first turn so the naming hook sees the user message:
        // append the user node exactly like SendPromptCmd does (agentic runs also do this).
        var user = new ChatMessageVm(convo, ChatMessageRole.User)
        {
            Content = "Help me understand how HTTP works please",
            Origin = "human",
            Previous = convo.Tail?.Message
        };
        convo.Head = user.Selector;

        // The naming hook subscribes to the first user message and derives a short title.
        await Task.Delay(50);
        convo.GenerateConversationName();

        Assert.NotEqual("My Conversation", convo.Name);
        Assert.True(convo.Name.Length <= 48, $"name too long: {convo.Name}");
        Assert.Contains("HTTP", convo.Name, StringComparison.OrdinalIgnoreCase);
    }

    private static IAsyncEnumerable<ChatResult> HangAfterContent(string prefix)
    {
        async IAsyncEnumerable<ChatResult> Enumerate()
        {
            yield return Content(prefix);
            await new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously).Task;
        }

        return Enumerate();
    }

    internal static async Task WaitUntilAsync(Func<bool> condition, int timeoutMs = 15000)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (!condition())
        {
            if (sw.ElapsedMilliseconds > timeoutMs)
                throw new TimeoutException("Workflow condition was not met in time.");
            await Task.Delay(25);
        }
    }
}