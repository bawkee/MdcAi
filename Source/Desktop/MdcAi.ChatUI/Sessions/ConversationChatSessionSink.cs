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

namespace MdcAi.ChatUI.Sessions;

using System.Reactive.Concurrency;
using System.Threading;
using LocalDal;
using ChatCore.Sessions;
using MdcAi.ChatUI.ViewModels;
using OpenAiApi;

/// <summary>
/// The main-conversation IChatSessionSink adapter: turns core events into ChatMessageVm nodes
/// on the selected fork, pins one stable placeholder id per assistant request, updates that
/// same node during streaming, and checkpoints turns relationally. This is the ONLY transcript
/// mutation boundary for the agentic path; the core never touches view models directly.
///
/// Collection mutations are marshaled to the UI thread (ObserveOnMainThread equivalent); in the
/// pinned-thread unit tests that scheduler is synchronous.
/// </summary>
public sealed class ConversationChatSessionSink : IChatSessionSink
{
    private readonly ConversationVm _convo;
    private readonly IChatSessionPersistence _persistence;
    private readonly Dictionary<string, ChatMessageVm> _activeAssistant = new(StringComparer.Ordinal);
    private readonly List<ChatToolExecutionView> _toolStates = new();
    private SessionTurnContext _turn;
    private DateTime _startedUtc;

    public IReadOnlyList<ChatToolExecutionView> ToolStates => _toolStates;

    /// <summary>The trigger message (first user node) of the current turn; null until a turn starts.</summary>
    public ChatMessageVm TriggerMessage { get; private set; }

    public ConversationChatSessionSink(ConversationVm convo, IChatSessionPersistence persistence = null)
    {
        _convo = convo;
        _persistence = persistence ?? new SqliteChatSessionPersistence();
    }

    /// <summary>The controller stamps the turn context before invoking the driver.</summary>
    public void StartTurn(SessionTurnContext context)
    {
        _turn = context;
        _startedUtc = DateTime.UtcNow;
        TriggerMessage = _convo.Head?.Message.GetNextMessages()
                                   .FirstOrDefault(m => m.Id == context.TriggerMessageId);
    }

    public ValueTask<ChatTranscriptSnapshot> GetCurrentBranchAsync(CancellationToken ct)
    {
        var exclude = _activeAssistant.Keys.FirstOrDefault();
        var messages = _convo.ToProtocolBranch(excludeMessageId: exclude);
        return new ValueTask<ChatTranscriptSnapshot>(new ChatTranscriptSnapshot(_turn?.TurnId, messages));
    }

    public async ValueTask<string> BeginAssistantAsync(ChatStepInfo step, CancellationToken ct)
    {
        string id = null;

        await MarshalUiAsync(() =>
        {
            var node = new ChatMessageVm(_convo, ChatMessageRole.Assistant)
            {
                Previous = _convo.Tail?.Message,
                CompletionState = "pending",
                Origin = "model",
                Model = _turn?.Model,
                Effort = _turn?.Effort,
                ProviderKey = _turn?.ProviderKey
            };

            if (_convo.Head == null)
                _convo.Head = node.Selector;
            else
                _convo.Tail.Message.Next = node;

            _activeAssistant[node.Id] = node;
            id = node.Id;
        });

        return id;
    }

    public ValueTask ApplyAssistantDeltaAsync(string messageId, ChatAssistantDelta delta, CancellationToken ct) =>
        new(MarshalUiAsync(() =>
        {
            if (_activeAssistant.TryGetValue(messageId, out var node))
            {
                node.Content = delta.Content;
                node.ReasoningContent = delta.ReasoningContent;
                node.ReasoningRaw = delta.Reasoning;
                node.ReasoningDetails = delta.ReasoningDetails;
                node.CompletionState = "streaming";
            }
        }));

    public ValueTask CommitAssistantAsync(string messageId, ChatAssistantRecord record, CancellationToken ct) =>
        new(MarshalUiAsync(() =>
        {
            if (!_activeAssistant.TryGetValue(messageId, out var node))
                return;

            var message = record.Message;
            node.Content = message.Content;
            node.ReasoningContent = message.ReasoningContent;
            node.ReasoningRaw = message.ReasoningRaw;
            node.ReasoningDetails = message.ReasoningDetails;
            node.ToolCalls = message.ToolCalls;
            node.FinishReason = record.FinishReason;
            node.CompletionState = "completed";
            node.ProviderKey = _turn?.ProviderKey;

            _activeAssistant.Remove(messageId);
        }));

    public ValueTask AbandonAssistantAsync(string messageId, bool keepDeliveredPrefix, CancellationToken ct) =>
        new(MarshalUiAsync(() =>
        {
            if (!_activeAssistant.TryGetValue(messageId, out var node))
                return;

            _activeAssistant.Remove(messageId);

            if (!keepDeliveredPrefix || string.IsNullOrEmpty(node.Content))
            {
                // Nothing delivered: detach the placeholder cleanly (no phantom node).
                if (node.Previous != null)
                    node.Previous.Next = null;
                else if (_convo.Head?.Message == node)
                    _convo.Head = null;
            }
            else
            {
                // A delivered prefix stays, honestly marked interrupted (never mislabeled complete).
                node.CompletionState = "interrupted";
                node.FinishReason = "interrupted";
            }
        }));

    public ValueTask SetModelRequestAttemptAsync(ChatModelRequestAttemptView attempt, CancellationToken ct)
    {
        // Phase 1: attempt lifecycle is persisted by the repository; renderer retry rows are Phase 2.
        return ValueTask.CompletedTask;
    }

    public ValueTask SetToolStateAsync(ChatToolExecutionView tool, CancellationToken ct)
    {
        lock (_toolStates)
            _toolStates.Add(tool);
        return ValueTask.CompletedTask;
    }

    public ValueTask AppendToolResultAsync(ChatToolResultRecord result, CancellationToken ct) =>
        new(MarshalUiAsync(() =>
        {
            var node = new ChatMessageVm(_convo, ChatMessageRole.Tool)
            {
                Previous = _convo.Tail?.Message,
                Content = result.Result.ModelContent,
                ToolCallId = result.ToolCallId,
                ToolName = result.ToolName,
                ToolResultJson = result.Result.Value,
                CompletionState = "completed",
                Origin = "tool",
                ProviderKey = _turn?.ProviderKey
            };

            if (_convo.Head == null)
                _convo.Head = node.Selector;
            else
                _convo.Tail.Message.Next = node;
        }));

    public async ValueTask CheckpointTurnAsync(ChatTurnCheckpoint checkpoint, CancellationToken ct)
    {
        if (_turn == null)
            return;

        var turn = new DbChatTurn
        {
            IdTurn = _turn.TurnId,
            IdConversation = _convo.Id,
            IdTriggerMessage = _turn.TriggerMessageId,
            Origin = _turn.Origin,
            ProviderKey = _turn.ProviderKey,
            Model = _turn.Model,
            Effort = _turn.Effort,
            StartedTs = _startedUtc,
            Status = checkpoint.Status
        };

        // "started" -> insert with a live status; any other status is a terminal checkpoint.
        // Persistence has no UI touch, so it runs directly without scheduler marshaling.
        if (checkpoint.Status != "started")
        {
            turn.Outcome = checkpoint.Outcome;
            turn.EndedTs = DateTime.UtcNow;
            turn.StepCount = checkpoint.StepCount;
        }

        await _persistence.SaveTurnCheckpointAsync(turn, ct);
    }

    /// <summary>
    /// Runs a transcript mutation on the UI scheduler. In the pinned-thread unit tests
    /// (CurrentThreadScheduler) this executes inline; in the WinUI app it hops to the
    /// dispatcher so ObservableCollection mutations never happen off the UI thread.
    /// </summary>
    private static Task MarshalUiAsync(Action action)
    {
        if (ReactiveUI.RxApp.MainThreadScheduler is System.Reactive.Concurrency.CurrentThreadScheduler)
        {
            action();
            return Task.CompletedTask;
        }

        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        ReactiveUI.RxApp.MainThreadScheduler.Schedule(() =>
        {
            try
            {
                action();
                tcs.SetResult(true);
            }
            catch (Exception ex)
            {
                tcs.SetException(ex);
            }
        });

        return tcs.Task;
    }
}