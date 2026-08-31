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
using LocalDal;
using Newtonsoft.Json.Linq;
using OpenAiApi;

/// <summary>
/// P1-07 fork adapter: the CURRENT selected branch must round-trip through ChatMessageVm →
/// DbMessage → ChatMessageVm → protocol messages WITHOUT losing tool calls, structured
/// reasoning, tool call ids, origins or completion state (DSH proposal §5.3 / §9.1).
/// </summary>
public class ForkRoundTripTests
{
    public ForkRoundTripTests() { TestRx.Init(); }

    private static (ConversationVm convo, FakeOpenAiApi api) Make()
    {
        var api = new FakeOpenAiApi();
        var store = new InMemoryCredsStore();
        store.SetValue("openai:ApiKey", "sk-oa");
        store.SetValue("openrouter:ApiKey", "sk-or");
        var settings = TestSettings.Build(store);
        var chatSettings = new ChatSettingsVm(api);
        var convo = new ConversationVm(api, settings, chatSettings);
        return (convo, api);
    }

    /// <summary>A variable-length agentic branch: user → assistant(tool call) → tool → assistant(final).</summary>
    private static ChatMessageVm BuildAgenticBranch(ConversationVm convo)
    {
        var user = new ChatMessageVm(convo, ChatMessageRole.User)
        {
            Content = "read the file",
            Origin = "human"
        };

        var assistant = new ChatMessageVm(convo, ChatMessageRole.Assistant)
        {
            Content = null,
            ReasoningContent = "I should read it.",
            ReasoningRaw = JToken.Parse("""{"summary":["I should read it."]}"""),
            ReasoningDetails = JArray.Parse(
                """[{"type":"reasoning","content":[{"type":"text","text":"line"}],"signature":"SIG-1"}]"""),
            ToolCalls = new[]
            {
                new ChatMessageToolCall
                {
                    Id = "call_1",
                    Type = "function",
                    Function = new ChatMessageFunction { Name = "read_file", Arguments = """{"path":"a.txt"}""" }
                }
            },
            CompletionState = "completed",
            FinishReason = "tool_calls",
            Model = "deepseek/deepseek-chat",
            Effort = "medium",
            ProviderKey = AiProviders.OpenRouterKey,
            Origin = "model",
            Previous = user
        };
        user.Next = assistant;

        var toolResult = new ChatMessageVm(convo, ChatMessageRole.Tool)
        {
            Content = "file a.txt: hello",
            ToolCallId = "call_1",
            ToolName = "read_file",
            ToolResultJson = new JObject { ["path"] = "a.txt", ["text"] = "hello" },
            CompletionState = "completed",
            Origin = "tool",
            ProviderKey = AiProviders.OpenRouterKey,
            Previous = assistant
        };
        assistant.Next = toolResult;

        var final = new ChatMessageVm(convo, ChatMessageRole.Assistant)
        {
            Content = "The file says hello.",
            Model = "deepseek/deepseek-chat",
            Effort = "medium",
            ProviderKey = AiProviders.OpenRouterKey,
            Origin = "model",
            CompletionState = "completed",
            FinishReason = "stop",
            Previous = toolResult
        };
        toolResult.Next = final;

        convo.Head = user.Selector;
        return user;
    }

    [Fact]
    public void Db_round_trip_preserves_the_full_protocol_surface()
    {
        var (convo, _) = Make();
        BuildAgenticBranch(convo);

        var dbMessages = convo.Head.Message.ToDbMessages().ToArray();
        Assert.Equal(4, dbMessages.Length);

        var wired = convo.Head.Message.GetNextMessages().ToArray();
        var assistant = wired.First(m => m.Role == ChatMessageRole.Assistant && m.ToolCalls != null);

        // Round trip through the DB entities.
        var reloaded = new ConversationVm(convo.Api, convo.GlobalSettings, convo.Settings);
        var head = dbMessages.FromDbMessages(reloaded);

        Assert.NotNull(head);
        var reloadedMessages = head.Message.GetNextMessages().ToArray();
        Assert.Equal(4, reloadedMessages.Length);

        var reloadedAssistant = reloadedMessages.First(m => m.Role == ChatMessageRole.Assistant && m.ToolCalls != null);
        Assert.Equal("call_1", reloadedAssistant.ToolCalls[0].Id);
        Assert.Equal("read_file", reloadedAssistant.ToolCalls[0].Function.Name);
        Assert.Equal("""{"path":"a.txt"}""", reloadedAssistant.ToolCalls[0].Function.Arguments);
        Assert.Equal("SIG-1", (string)reloadedAssistant.ReasoningDetails[0]["signature"]);
        Assert.Equal("I should read it.", reloadedAssistant.ReasoningContent);
        Assert.Equal(AiProviders.OpenRouterKey, reloadedAssistant.ProviderKey);
        Assert.Equal("completed", reloadedAssistant.CompletionState);

        var reloadedTool = reloadedMessages.First(m => m.Role == ChatMessageRole.Tool);
        Assert.Equal("call_1", reloadedTool.ToolCallId);
        Assert.Equal("hello", (string)reloadedTool.ToolResultJson["text"]);
        Assert.Equal("tool", reloadedTool.Origin);
    }

    [Fact]
    public void Protocol_projection_is_exact_and_ordered()
    {
        var (convo, _) = Make();
        BuildAgenticBranch(convo);

        var protocol = convo.ToProtocolBranch();

        Assert.Equal(4, protocol.Count);
        Assert.Equal(new[] { "user", "assistant", "tool", "assistant" },
                     protocol.Select(m => (string)m.Role).ToArray());

        // Assistant with tool calls keeps content:null + reasoning + tool_calls together.
        var assistant = protocol[1];
        Assert.Null(assistant.Content);
        Assert.Equal("I should read it.", assistant.ReasoningContent);
        Assert.NotNull(assistant.ReasoningDetails);
        Assert.Equal("call_1", assistant.ToolCalls[0].Id);

        // Tool result pairs by exact id, in model order.
        var tool = protocol[2];
        Assert.Equal("call_1", tool.ToolCallId);
        Assert.Equal("file a.txt: hello", tool.Content);
        Assert.Equal("The file says hello.", protocol[3].Content);
    }

    [Fact]
    public void Protocol_projection_excludes_the_in_flight_placeholder()
    {
        var (convo, _) = Make();
        BuildAgenticBranch(convo);

        // A streaming placeholder is being assembled for step 2.
        var placeholder = new ChatMessageVm(convo, ChatMessageRole.Assistant)
        {
            Previous = convo.Tail.Message,
            CompletionState = "streaming",
            Content = "partial"
        };
        convo.Tail.Message.Next = placeholder;

        var branch = convo.ToProtocolBranch(excludeMessageId: placeholder.Id);

        // The partial/streaming node must never leak into a request.
        Assert.Equal(4, branch.Count);
        Assert.DoesNotContain(branch, m => m.Content == "partial");
    }

    [Fact]
    public async Task Editing_an_earlier_user_message_keeps_old_branch_tool_results_out_of_new_branch()
    {
        var (convo, api) = Make();
        convo.ToolsEnabled = true; // agentic: edited sends run explicit turns, not tail-driven
        BuildAgenticBranch(convo);

        // The new branch must not see the old branch's tool results. Script one simple final
        // answer for the edited run.
        api.ScriptedStreams.Enqueue(Stream(RoleChunk(), Content("new run answer"), Finish("stop")));

        // Real app flow: select the user message, edit it, send.
        convo.SelectedMessage = convo.Head;
        convo.EditSelectedCmd.Execute().Subscribe();
        convo.Prompt.Contents = "read the OTHER file";
        await convo.SendPromptCmd.Execute();

        // The edited version starts a new downstream run.
        await WaitUntilAsync(() => convo.Tail.Message.Role == ChatMessageRole.Assistant
                                   && convo.Tail.Message.Content == "new run answer");

        var currentBranch = convo.ToProtocolBranch();
        Assert.Equal("read the OTHER file", currentBranch[0].Content);
        // Old branch tool result (call_1 / "file a.txt: hello") never leaks into the new branch.
        Assert.DoesNotContain(currentBranch, m => m.Role == ChatMessageRole.Tool);
        Assert.DoesNotContain(currentBranch, m => m.Content == "file a.txt: hello");
        Assert.Equal("new run answer", currentBranch[^1].Content);
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

    private static ChatResult Finish(string reason) => new()
    {
        Id = "req",
        Choices = new[] { new ChatChoice { Index = 0, FinishReason = reason } }
    };

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