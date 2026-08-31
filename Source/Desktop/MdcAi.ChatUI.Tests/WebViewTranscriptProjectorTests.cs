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
/// P2-01 contract tests: the versioned transcript projector emits discriminated items with
/// stable ids, pairs each assistant tool call with its result into ONE activity, suppresses the
/// duplicate tool-result bubble, orders thinking before narration, and carries revision/contract.
/// </summary>
public class WebViewTranscriptProjectorTests
{
    public WebViewTranscriptProjectorTests() { TestRx.Init(); }

    private static (ConversationVm convo, FakeOpenAiApi api) Make()
    {
        var api = new FakeOpenAiApi();
        var store = new InMemoryCredsStore();
        store.SetValue("openai:ApiKey", "sk-oa");
        store.SetValue("openrouter:ApiKey", "sk-or");
        var settings = TestSettings.Build(store);
        var chatSettings = new ChatSettingsVm(api);
        var convo = new ConversationVm(api, settings, chatSettings) { ToolsEnabled = true };
        return (convo, api);
    }

    /// <summary>user → assistant(reasoning+tool_calls) → tool result → assistant(final).</summary>
    private static void BuildAgenticBranch(ConversationVm convo)
    {
        var user = new ChatMessageVm(convo, ChatMessageRole.User)
        {
            Id = "user-1",
            Content = "read the file",
            Origin = "human"
        };

        var assistant = new ChatMessageVm(convo, ChatMessageRole.Assistant)
        {
            Id = "ai-1",
            Content = null,
            ReasoningContent = "I should read it.",
            ReasoningHTMLContent = "<p>I should read it.</p>",
            ToolCalls = new[]
            {
                new ChatMessageToolCall { Id = "call_1", Type = "function", Function = new ChatMessageFunction { Name = "read_file", Arguments = """{"path":"a.txt"}""" } }
            },
            CompletionState = "completed",
            FinishReason = "tool_calls",
            ProviderKey = AiProviders.OpenRouterKey,
            Origin = "model",
            IsIntermediate = true,
            Previous = user
        };
        user.Next = assistant;

        var toolResult = new ChatMessageVm(convo, ChatMessageRole.Tool)
        {
            Id = "tool-1",
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
            Id = "ai-3",
            Content = "The file says hello.",
            ProviderKey = AiProviders.OpenRouterKey,
            Origin = "model",
            CompletionState = "completed",
            FinishReason = "stop",
            Previous = toolResult
        };
        toolResult.Next = final;

        convo.Head = user.Selector;
    }

    [Fact]
    public void Projects_discriminated_items_in_semantic_order()
    {
        var (convo, _) = Make();
        BuildAgenticBranch(convo);

        var snapshot = WebViewTranscriptProjector.Project(convo, convo.Head.Message.GetNextMessages(), 5);

        Assert.Equal(2, snapshot.ContractVersion);
        Assert.Equal(5, snapshot.Revision);
        Assert.Equal(convo.Id, snapshot.ConversationId);

        // user message → thinking activity → assistant message → tool activity → final message.
        // (the role:"tool" bubble is suppressed because its call is paired in the tool activity)
        var items = snapshot.Items;
        Assert.Equal(new[] { "message", "activity", "message", "activity", "message" },
                     items.Select(i => i.Kind).ToArray());
        Assert.Equal(new[] { "message:user-1", "thinking:ai-1", "message:ai-1", "tool:call_1", "message:ai-3" },
                     items.Select(i => i.Id).ToArray());

        // Semantic order inside the step: thinking before narration.
        Assert.Equal("thinking", items[1].Activity.ActivityKind);
        Assert.Equal("Thinking", items[1].Activity.Title);
        Assert.Equal("I should read it.", items[1].Activity.Summary);

        // Tool activity pairs the call with its result in ONE row; no duplicate bubble.
        var tool = items[3].Activity;
        Assert.Equal("tool", tool.ActivityKind);
        Assert.Equal("Read File", tool.Title);
        Assert.Equal("call_1", tool.ToolCallId);
        Assert.Equal("file a.txt: hello", tool.Summary);
        Assert.Contains("a.txt", tool.Details.Generic.Input);

        // The assistant message itself is intermediate.
        Assert.True(items[2].Message.IsIntermediate);
    }

    [Fact]
    public void Result_presentation_persisted_intent_wins_over_generic()
    {
        var (convo, _) = Make();
        BuildAgenticBranch(convo);

        // Simulate persisted read intent on the DbToolCall row.
        var lookup = new Func<string, DbToolCall>(callId =>
        {
            if (callId != "call_1")
                return null;

            return new DbToolCall
            {
                ToolCallId = "call_1",
                ToolName = "read_file",
                ResultPresentationJson =
                    """{"version":1,"kind":"read","read":{"locationId":"loc-42","path":"a.txt","offset":0,"lines":[{"number":1,"text":"hello"}],"retainedLineCount":1,"totalLineCount":1,"language":"txt","truncated":false,"artifactId":null}}"""
            };
        });

        var snapshot = WebViewTranscriptProjector.Project(convo, convo.Head.Message.GetNextMessages(), 7, lookup);

        var tool = snapshot.Items.First(i => i.Id == "tool:call_1").Activity;
        Assert.Equal("read", tool.PresentationKind);
        Assert.Equal("read", tool.Details.Kind);
        Assert.Equal("loc-42", tool.Details.Read.LocationId);
        Assert.Equal("hello", tool.Details.Read.Lines[0].Text);
        Assert.Equal("a.txt", tool.Details.Read.Path);
    }

    [Fact]
    public void Legacy_plain_chat_projects_messages_without_activities()
    {
        var (convo, _) = Make();

        var user = new ChatMessageVm(convo, ChatMessageRole.User)
        {
            Content = "hi",
            HTMLContent = "<p>hi</p>",
            Origin = "human"
        };
        var assistant = new ChatMessageVm(convo, ChatMessageRole.Assistant)
        {
            Content = "hello",
            HTMLContent = "<p>hello</p>",
            CompletionState = "completed",
            Origin = "model"
        };
        user.Next = assistant;
        convo.Head = user.Selector;

        var snapshot = WebViewTranscriptProjector.Project(convo, convo.Head.Message.GetNextMessages(), 1);

        var items = snapshot.Items;
        Assert.Equal(2, items.Length);
        Assert.All(items, i => Assert.Equal("message", i.Kind));
        Assert.Equal("<p>hi</p>", items[0].Message.Content);
        Assert.Equal("<p>hello</p>", items[1].Message.Content);
        Assert.Equal("human", items[0].Message.Origin);
        Assert.Equal("model", items[1].Message.Origin);
        Assert.Equal("completed", items[1].Message.CompletionState);
    }

    [Fact]
    public void Interrupted_tool_step_activity_carries_honest_state()
    {
        var (convo, _) = Make();

        var user = new ChatMessageVm(convo, ChatMessageRole.User) { Content = "go" };
        var assistant = new ChatMessageVm(convo, ChatMessageRole.Assistant)
        {
            Content = null,
            ToolCalls = new[] { new ChatMessageToolCall { Id = "call_x", Function = new ChatMessageFunction { Name = "run_powershell" } } },
            CompletionState = "completed",
            IsIntermediate = true,
            Previous = user
        };
        user.Next = assistant;

        // Interrupted (cancelled mid-tool - the paired result is the deterministic cancelled result).
        var result = new ChatMessageVm(convo, ChatMessageRole.Tool)
        {
            Content = """{"ok":false,"status":"cancelled","summary":"Tool 'run_powershell' was cancelled.","error":{"code":"cancelled","retryable":false}}""",
            ToolCallId = "call_x",
            ToolName = "run_powershell",
            CompletionState = "completed",
            Origin = "tool",
            Previous = assistant
        };
        assistant.Next = result;
        convo.Head = user.Selector;

        var snapshot = WebViewTranscriptProjector.Project(convo, convo.Head.Message.GetNextMessages(), 1);
        var tool = snapshot.Items.Single(i => i.Id == "tool:call_x").Activity;

        Assert.Equal("Run Powershell", tool.Title);
        Assert.Contains("cancelled", tool.Summary);
        Assert.Equal("completed", tool.Status);
    }

    [Fact]
    public void ProjectSingleItem_matches_the_snapshot_message_item()
    {
        var (convo, _) = Make();
        BuildAgenticBranch(convo);

        // The tail is the final assistant message; its single-item upsert must match what a
        // full snapshot would carry for that node (stable id, same DTO surface).
        var tail = convo.Tail.Message;
        var single = WebViewTranscriptProjector.ProjectSingleItem(tail, 9);

        Assert.NotNull(single);
        Assert.Equal($"message:{tail.Id}", single.Id);
        Assert.Equal(9, single.Revision);
        Assert.Equal("<p>The file says hello.</p>", single.Message.Content); // HTML surface, like the snapshot path
        Assert.Equal("model", single.Message.Origin);

        // Tool/result nodes are not individually upserted (snapshot covers them).
        var toolNode = convo.Head.Message.GetNextMessages().First(m => m.Role == ChatMessageRole.Tool);
        Assert.Null(WebViewTranscriptProjector.ProjectSingleItem(toolNode, 9));
    }
}