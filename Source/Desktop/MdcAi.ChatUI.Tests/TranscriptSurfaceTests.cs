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
using MdcAi.ChatUI.Sessions;
using MdcAi.ChatUI.ViewModels;
using Newtonsoft.Json.Linq;
using OpenAiApi;

/// <summary>
/// P2-03/P2-04: the transcript projects an approval card and a turn-summary activity; live
/// usage aggregates token/cost figures from committed step records.
/// </summary>
public class TranscriptSurfaceTests
{
    public TranscriptSurfaceTests() { TestRx.Init(); }

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

    [Fact]
    public void Turn_usage_aggregates_from_committed_records_and_projects_summary()
    {
        var (convo, _) = Make();
        var sink = new ConversationChatSessionSink(convo, null) as IChatSessionSink;

        var user = new ChatMessageVm(convo, ChatMessageRole.User) { Content = "go" };
        convo.Head = user.Selector;

        // Commit two assistant records with usage.
        sink.CommitAssistantAsync("m1", new ChatAssistantRecord(
            new ChatMessage(ChatMessageRole.Assistant) { Content = "a" },
            false, true, "stop", "req-1",
            new ChatUsage { PromptTokens = 100, CompletionTokens = 20, TotalTokens = 120,
                            CompletionDetails = new TokenDetails { ReasoningTokens = 5 },
                            PromptDetails = new TokenDetails { CachedTokens = 30 },
                            Cost = 0.001m }), CancellationToken.None).AsTask().GetAwaiter().GetResult();

        sink.CommitAssistantAsync("m2", new ChatAssistantRecord(
            new ChatMessage(ChatMessageRole.Assistant) { Content = "b" },
            false, true, "stop", "req-2", null), CancellationToken.None).AsTask().GetAwaiter().GetResult();

        // Turn-summary projection via the projector.
        var snapshot = WebViewTranscriptProjector.Project(convo,
            convo.Head.Message.GetNextMessages().TakeWhile(m => m.Id != "b"), 1,
            turnUsage: ((ConversationChatSessionSink)sink).Usage);

        var summary = snapshot.Items.FirstOrDefault(i => i.Kind == "turn_summary");
        Assert.NotNull(summary);
        Assert.Equal(2, summary.TurnSummary.StepCount);
        Assert.Equal(100, summary.TurnSummary.PromptTokens);
        Assert.Equal(20, summary.TurnSummary.CompletionTokens);
        Assert.Equal(5, summary.TurnSummary.ReasoningTokens);
        Assert.Equal(30, summary.TurnSummary.PromptCacheReadTokens);
        Assert.Equal(0.001m, summary.TurnSummary.Cost);
    }

    [Fact]
    public void Approval_card_projects_as_pending_activity()
    {
        var (convo, _) = Make();
        var pending = new MdcAi.ChatCore.Security.ChatApprovalRequest(
            "c1", "turn-1", "call_x", "write_file", MdcAi.ChatCore.Tools.ChatToolRisk.Write,
            MdcAi.ChatCore.Tools.ChatToolCallPresentation.Diff("Write", "Write · a.txt",
                new JObject { ["path"] = "a.txt" }),
            "HASH");

        var snapshot = WebViewTranscriptProjector.Project(convo,
            Array.Empty<ChatMessageVm>(), 1,
            pendingApproval: new PendingApproval(pending, Cancelled: false));

        var approval = snapshot.Items.FirstOrDefault(i => i.Id == "approval:call_x");
        Assert.NotNull(approval);
        Assert.Equal("awaiting_approval", approval.Activity.Status);
        Assert.Equal("proposed", approval.Activity.Details.Diff.State);
        Assert.Equal("call_x", approval.Activity.ToolCallId);
        Assert.Equal("HASH", approval.Activity.ArgumentHash);
    }
}