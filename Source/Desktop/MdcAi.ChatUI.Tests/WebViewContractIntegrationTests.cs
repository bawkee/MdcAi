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

using MdcAi.ChatCore.Sessions;
using MdcAi.ChatCore.Tools;
using MdcAi.ChatUI.Sessions;
using MdcAi.ChatUI.ViewModels;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using OpenAiApi;

/// <summary>
/// Host-side WebView contract integration: runs the REAL agentic turn through ConversationVm +
/// the session sink, then projects the payloads through ProjectForWebView() exactly like the
/// WhenActivated chain does, and asserts the wire contract the renderer depends on:
///   1. the FIRST payload is always a full SetMessages snapshot - never an upsert;
///   2. structural changes (tool results, commits) re-post full snapshots that contain them;
///   3. pure content deltas post single-item UpsertTranscriptItem;
///   4. every payload serializes to the exact JSON shape the React reducer consumes.
/// This is the regression net for "first message showed a blank chat" and friends.
/// </summary>
public class WebViewContractIntegrationTests : IDisposable
{
    private readonly string _workspace;

    public WebViewContractIntegrationTests()
    {
        TestRx.Init();
        _workspace = Path.Combine(Path.GetTempPath(), "mdcai-contract-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_workspace);
    }

    public void Dispose()
    {
        try { Directory.Delete(_workspace, recursive: true); }
        catch { }
    }

    private static IAsyncEnumerable<ChatResult> Stream(params ChatResult[] chunks) =>
        Enumerate(chunks);

    private static async IAsyncEnumerable<ChatResult> Enumerate(ChatResult[] chunks)
    {
        foreach (var c in chunks)
            yield return c;
        await Task.CompletedTask;
    }

    private static ChatResult Role() => new()
    {
        Id = "r",
        Choices = new[] { new ChatChoice { Index = 0, Delta = new ChatMessage(ChatMessageRole.Assistant) } }
    };

    private static ChatResult Content(string text) => new()
    {
        Id = "r",
        Choices = new[] { new ChatChoice { Index = 0, Delta = new ChatMessage(ChatMessageRole.Assistant, text) } }
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

    /// <summary>
    /// Drives one agentic turn by directly invoking the sink (the same calls the ChatSessionService
    /// makes), bumping the conversation structure via the sink, then projecting through
    /// ProjectForWebView after each mutation - capturing the exact posted sequence.
    /// </summary>
    private async Task<List<WebViewRequestDto>> RunAndCapturePostsAsync(Action<ConversationVm> setup)
    {
        var api = new FakeOpenAiApi();
        var store = new InMemoryCredsStore();
        store.SetValue("openai:ApiKey", "sk-oa");
        store.SetValue("openrouter:ApiKey", "sk-or");
        var chatSettings = new ChatSettingsVm(api);
        var convo = new ConversationVm(api, TestSettings.Build(store), chatSettings)
        {
            ToolsEnabled = true,
            WorkspacePath = _workspace
        };
        setup?.Invoke(convo);

        var sink = new ConversationChatSessionSink(convo, null);
        sink.StartTurn(new SessionTurnContext("turn-1", convo.Head?.Message.Id, AiProviders.OpenRouterKey,
                                              "deepseek/deepseek-chat", null, null, "human"));

        var posts = new List<WebViewRequestDto>();

        // User message appended (structural -> snapshot).
        posts.Add(convo.ProjectForWebView());

        // Assistant placeholder begins (structural -> snapshot).
        var assistantId = await sink.BeginAssistantAsync(new ChatStepInfo("turn-1", 1), CancellationToken.None);
        posts.Add(convo.ProjectForWebView());

        // Streaming deltas (same structure -> upserts).
        await sink.ApplyAssistantDeltaAsync(assistantId, new ChatAssistantDelta("thinking...", "hmm", null, null, Array.Empty<ChatMessageToolCall>()), CancellationToken.None);
        posts.Add(convo.ProjectForWebView());
        await sink.ApplyAssistantDeltaAsync(assistantId, new ChatAssistantDelta("answer ", null, null, null, Array.Empty<ChatMessageToolCall>()), CancellationToken.None);
        posts.Add(convo.ProjectForWebView());

        // Commit with tool calls (structural -> snapshot that shows the tool call + thinking).
        var committed = new ChatMessage(ChatMessageRole.Assistant)
        {
            Content = null,
            ReasoningContent = "hmm",
            ToolCalls = new[] { new ChatMessageToolCall { Id = "call_1", Function = new ChatMessageFunction { Name = "read_file", Arguments = """{"path":"a.txt"}""" } } }
        };
        await sink.CommitAssistantAsync(assistantId, ChatAssistantRecord.Completed(committed, "tool_calls", "r1", null), CancellationToken.None);
        posts.Add(convo.ProjectForWebView());

        // Tool result appended (structural -> snapshot with the paired read activity).
        await sink.AppendToolResultAsync(new ChatToolResultRecord(
            "call_1", "read_file", 0,
            ChatToolExecutionResult.Success(JToken.Parse("""{"path":"a.txt"}"""), "path: a.txt\nfile loaded")), CancellationToken.None);
        posts.Add(convo.ProjectForWebView());

        return posts;
    }

    [Fact]
    public async Task First_post_is_always_a_full_snapshot_never_an_upsert()
    {
        var posts = await RunAndCapturePostsAsync(convo =>
        {
            var user = new ChatMessageVm(convo, ChatMessageRole.User) { Content = "read the file", Origin = "human", Id = "msg-user" };
            convo.Head = user.Selector;
        });

        Assert.True(posts.Count >= 2, "expected at least the user + placeholder snapshots");

        // The very first payload must be a full snapshot.
        Assert.Equal("SetMessages", posts[0].Name);
        var first = Assert.IsType<WebViewTranscriptSnapshotDto>(posts[0].Data);
        Assert.NotEmpty(first.Items);
        Assert.True(first.Revision >= 0);
    }

    [Fact]
    public async Task Streaming_deltas_post_upserts_but_structure_reposts_snapshots()
    {
        var posts = await RunAndCapturePostsAsync(convo =>
        {
            var user = new ChatMessageVm(convo, ChatMessageRole.User) { Content = "go", Origin = "human", Id = "msg-user" };
            convo.Head = user.Selector;
        });

        var names = posts.Select(p => p.Name).ToArray();
        // Structure changes (user, placeholder, commit, tool result) = snapshots; deltas = upserts.
        Assert.Contains(names, n => n == "UpsertTranscriptItem");
        Assert.Equal("SetMessages", names[0]);
        Assert.Equal("SetMessages", names[^1]); // final structure settles to a snapshot

        // The final snapshot contains the paired tool activity (read) for call_1.
        var final = Assert.IsType<WebViewTranscriptSnapshotDto>(posts[^1].Data);
        var toolActivity = final.Items.FirstOrDefault(i => i.Id == "tool:call_1");
        Assert.NotNull(toolActivity);
        Assert.Equal("tool", toolActivity.Activity.ActivityKind);
    }

    [Fact]
    public async Task Upsert_payloads_have_the_exact_shape_the_react_reducer_reads()
    {
        var posts = await RunAndCapturePostsAsync(convo =>
        {
            var user = new ChatMessageVm(convo, ChatMessageRole.User) { Content = "go", Origin = "human", Id = "msg-user" };
            convo.Head = user.Selector;
        });

        var upsert = posts.FirstOrDefault(p => p.Name == "UpsertTranscriptItem");
        Assert.NotNull(upsert);

        // Serialize exactly like PostWebMessageAsJson would, then parse the JSON keys the
        // React reducer (transcriptReducer.applyUpsert) touches: Item.Id, Item.Revision,
        // BaseRevision.
        var json = JsonConvert.SerializeObject(upsert);
        var parsed = JObject.Parse(json);

        Assert.Equal("UpsertTranscriptItem", (string)parsed["Name"]);
        Assert.NotNull(parsed["Data"]["Item"]["Id"]);
        Assert.NotNull(parsed["Data"]["BaseRevision"]);
        Assert.Contains("message:", (string)parsed["Data"]["Item"]["Id"]);

        // Same shape check for the snapshot: Data.Items must be an array (isTranscriptPayload).
        var snapshotJson = JsonConvert.SerializeObject(posts[0]);
        var snapshotParsed = JObject.Parse(snapshotJson);
        Assert.Equal("SetMessages", (string)snapshotParsed["Name"]);
        Assert.True(snapshotParsed["Data"]["Items"] is JArray);
        Assert.NotEmpty((JArray)snapshotParsed["Data"]["Items"]);
    }

    [Fact]
    public async Task New_conversation_with_tools_posts_full_snapshot_before_any_upsert()
    {
        // The exact "new conversation, first prompt" scenario: the turn's IsCompleting may be
        // true before the first projection, but the snapshot gate must win.
        var api = new FakeOpenAiApi();
        var store = new InMemoryCredsStore();
        store.SetValue("openai:ApiKey", "sk-oa");
        var chatSettings = new ChatSettingsVm(api);
        var convo = new ConversationVm(api, TestSettings.Build(store), chatSettings)
        {
            ToolsEnabled = true,
            WorkspacePath = _workspace
        };

        var user = new ChatMessageVm(convo, ChatMessageRole.User) { Content = "hello", Origin = "human", Id = "msg-user" };
        convo.Head = user.Selector;

        // First projection of the session: MUST be a snapshot, even though nothing else happened.
        var first = convo.ProjectForWebView();
        Assert.Equal("SetMessages", first.Name);
        Assert.NotEmpty(Assert.IsType<WebViewTranscriptSnapshotDto>(first.Data).Items);

        // Second projection with no structural change: the tail is a USER node (not an upsertable
        // assistant placeholder), so another full snapshot is correct and harmless. Upserts only
        // kick in once a streaming assistant placeholder is the tail (covered by the
        // Streaming_deltas test).
        var second = convo.ProjectForWebView();
        Assert.Equal("SetMessages", second.Name);
    }
}