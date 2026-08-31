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
using LocalDal;
using Newtonsoft.Json.Linq;
using OpenAiApi;

/// <summary>
/// ConversationChatSessionSink in isolation: nodes are appended to the fork, the placeholder id
/// stays stable through commit, branch projection is exact, and abandonment detaches cleanly.
/// </summary>
public class ConversationChatSessionSinkTests
{
    public ConversationChatSessionSinkTests() { TestRx.Init(); }

    private (ConversationVm convo, FakeOpenAiApi api) Make()
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
            SelectedModel = "deepseek/deepseek-chat"
        };
        return (convo, api);
    }

    private static ConversationChatSessionSink SinkWithUser(ConversationVm convo)
    {
        var user = new ChatMessageVm(convo, ChatMessageRole.User) { Content = "hi", Origin = "human" };
        convo.Head = user.Selector;

        var sink = new ConversationChatSessionSink(convo, new FakePersistence());
        sink.StartTurn(new SessionTurnContext("turn-1", user.Id, AiProviders.OpenRouterKey,
                                              "deepseek/deepseek-chat", "medium", null, "human"));
        return sink;
    }

    private sealed class FakePersistence : IChatSessionPersistence
    {
        public List<DbChatTurn> Saved { get; } = new();

        public Task SaveTurnCheckpointAsync(DbChatTurn turn, CancellationToken ct)
        {
            Saved.Add(turn);
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task Begin_append_delta_commit_keeps_one_stable_node()
    {
        var (convo, _) = Make();
        var sink = SinkWithUser(convo);

        var id = await sink.BeginAssistantAsync(new ChatStepInfo("turn-1", 1), CancellationToken.None);

        Assert.Equal(2, convo.Head.Message.GetNextMessages().Count());
        Assert.Equal(ChatMessageRole.Assistant, convo.Tail.Message.Role);
        Assert.Equal("pending", convo.Tail.Message.CompletionState);

        // Delta addresses the same node; the branch projection excludes it while in flight.
        await sink.ApplyAssistantDeltaAsync(id, new ChatAssistantDelta("partial", null, null, null, Array.Empty<ChatMessageToolCall>()), CancellationToken.None);
        Assert.Equal("partial", convo.Tail.Message.Content);
        Assert.Equal(1, convo.ToProtocolBranch(excludeMessageId: id).Count); // user only

        // Commit finalizes the same node with the full protocol payload.
        var message = new ChatMessage(ChatMessageRole.Assistant)
        {
            Content = "answer",
            ToolCalls = new[]
            {
                new ChatMessageToolCall { Id = "call_1", Function = new ChatMessageFunction { Name = "read_file", Arguments = "{}" } }
            }
        };
        await sink.CommitAssistantAsync(id, ChatAssistantRecord.Completed(message, "tool_calls", "req-1", null), CancellationToken.None);

        var nodes = convo.Head.Message.GetNextMessages().ToArray();
        Assert.Equal(2, nodes.Length);
        Assert.Equal("answer", convo.Tail.Message.Content);
        Assert.Equal("call_1", convo.Tail.Message.ToolCalls[0].Id);
        Assert.Equal("completed", convo.Tail.Message.CompletionState);
        Assert.Equal(2, convo.ToProtocolBranch().Count);
    }

    [Fact]
    public async Task Tool_result_is_appended_as_a_paired_tool_node()
    {
        var (convo, _) = Make();
        var sink = SinkWithUser(convo);

        var result = ChatToolExecutionResult.Success(JToken.Parse("""{"path":"a.txt"}"""), "file loaded");
        await sink.AppendToolResultAsync(new ChatToolResultRecord("call_1", "read_file", 0, result), CancellationToken.None);

        var node = convo.Tail.Message;
        Assert.Equal(ChatMessageRole.Tool, node.Role);
        Assert.Equal("call_1", node.ToolCallId);
        Assert.Equal("file loaded", node.Content);
        Assert.Equal("tool", node.Origin);
    }

    [Fact]
    public async Task Abandon_without_prefix_detaches_the_placeholder()
    {
        var (convo, _) = Make();
        var sink = SinkWithUser(convo);

        var id = await sink.BeginAssistantAsync(new ChatStepInfo("turn-1", 1), CancellationToken.None);
        Assert.Equal(2, convo.Head.Message.GetNextMessages().Count());

        await sink.AbandonAssistantAsync(id, keepDeliveredPrefix: false, CancellationToken.None);

        Assert.Equal(1, convo.Head.Message.GetNextMessages().Count());
        Assert.Equal(ChatMessageRole.User, convo.Tail.Message.Role);
    }

    [Fact]
    public async Task Abandon_with_prefix_keeps_an_interrupted_node()
    {
        var (convo, _) = Make();
        var sink = SinkWithUser(convo);

        var id = await sink.BeginAssistantAsync(new ChatStepInfo("turn-1", 1), CancellationToken.None);
        await sink.ApplyAssistantDeltaAsync(id, new ChatAssistantDelta("prefix ", null, null, null, Array.Empty<ChatMessageToolCall>()), CancellationToken.None);

        await sink.AbandonAssistantAsync(id, keepDeliveredPrefix: true, CancellationToken.None);

        var node = convo.Tail.Message;
        Assert.Equal("prefix ", node.Content);
        Assert.Equal("interrupted", node.CompletionState);
        Assert.Equal("interrupted", node.FinishReason);
    }

    [Fact]
    public async Task Checkpoint_persists_started_and_terminal_turns()
    {
        var (convo, _) = Make();
        var user = new ChatMessageVm(convo, ChatMessageRole.User) { Content = "hi", Origin = "human" };
        convo.Head = user.Selector;

        var fake = new FakePersistence();
        var customSink = new ConversationChatSessionSink(convo, fake);
        customSink.StartTurn(new SessionTurnContext("turn-7", user.Id, AiProviders.OpenRouterKey,
                                                    "deepseek/deepseek-chat", null, null, "human"));

        await customSink.CheckpointTurnAsync(new ChatTurnCheckpoint("turn-7", "started", 0, 0), CancellationToken.None);
        await customSink.CheckpointTurnAsync(new ChatTurnCheckpoint("turn-7", "completed", 3, 5, "Completed"), CancellationToken.None);

        Assert.Equal(2, fake.Saved.Count);
        var started = fake.Saved[0];
        Assert.Equal("turn-7", started.IdTurn);
        Assert.Equal("started", started.Status);
        Assert.Equal(0, started.StepCount);

        var completed = fake.Saved[1];
        Assert.Equal("completed", completed.Status);
        Assert.Equal("Completed", completed.Outcome);
        Assert.Equal(3, completed.StepCount);
        Assert.Equal("deepseek/deepseek-chat", completed.Model);
    }
}