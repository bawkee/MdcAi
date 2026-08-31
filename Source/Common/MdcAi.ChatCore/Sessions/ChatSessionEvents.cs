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

namespace MdcAi.ChatCore.Sessions;

using Newtonsoft.Json.Linq;
using OpenAiApi;

/// <summary>Which model step within a turn an event belongs to.</summary>
public sealed record ChatStepInfo(string TurnId, int StepNumber);

/// <summary>
/// The current selected branch projected into OpenAI protocol messages. This is the ONLY
/// source a request history is derived from - the sink adapter owns fork selection, so the
/// core never sees a private second message list (DSH proposal §3.2).
/// </summary>
public sealed record ChatTranscriptSnapshot(
    string TurnId,
    IReadOnlyList<ChatMessage> Messages);

/// <summary>One streaming update for the active assistant placeholder node.</summary>
public sealed record ChatAssistantDelta(
    string Content,
    string ReasoningContent,
    JToken Reasoning,
    JArray ReasoningDetails,
    IReadOnlyList<ChatMessageToolCall> ToolCallDeltas)
{
    public static ChatAssistantDelta Empty { get; } = new(null, null, null, null, Array.Empty<ChatMessageToolCall>());
}

/// <summary>The fully assembled assistant protocol message plus step-level metadata.</summary>
public sealed record ChatAssistantRecord(
    ChatMessage Message,
    bool IsMaxTokens,
    bool HasCompleteToolArguments,
    string FinishReason = null,
    string RequestId = null,
    ChatUsage Usage = null)
{
    public static ChatAssistantRecord Completed(ChatMessage message, string finishReason, string requestId, ChatUsage usage) =>
        new(message, false, true, finishReason, requestId, usage);

    public static ChatAssistantRecord MaxTokens(ChatMessage message, string finishReason) =>
        new(message, true, false, finishReason);
}

/// <summary>A durable-before-wait view of one provider request attempt (DSH proposal §6.5).</summary>
public sealed record ChatModelRequestAttemptView(
    int AttemptNumber,
    string Status, // started | completed | failed | cancelled
    string RetryDisposition, // none | scheduled | started | cancelled
    string FailureCategory,
    string FailureCode,
    DateTimeOffset? ScheduledDelayUntil,
    string RequestId = null);

/// <summary>A live per-call state update for the transcript/UI.</summary>
public sealed record ChatToolExecutionView(
    string ToolCallId,
    string ToolName,
    string Status, // awaiting_approval | queued | running | completed | denied | failed | timed_out | cancelled
    ChatToolRisk Risk);

/// <summary>
/// One committed tool result in model order. The sink adapter materializes the protocol
/// <c>role:"tool"</c> message (content = bounded model content, tool_call_id = ToolCallId).
/// </summary>
public sealed record ChatToolResultRecord(
    string ToolCallId,
    string ToolName,
    int CallIndex,
    ChatToolExecutionResult Result);

/// <summary>Durable turn checkpoint (DSH proposal §6.5 checkpoint boundaries).</summary>
public sealed record ChatTurnCheckpoint(
    string TurnId,
    string Status, // started | running | completed | max_tokens | max_steps | cancelled | failed
    int StepCount,
    int TotalToolCalls,
    string Outcome = null);

/// <summary>
/// The ONLY transcript mutation boundary. The main-conversation adapter implements this over the
/// current fork + repository; a one-shot helper uses an in-memory sink. Core never references
/// view models or EF entities (DSH proposal §5.1).
/// </summary>
public interface IChatSessionSink
{
    ValueTask<ChatTranscriptSnapshot> GetCurrentBranchAsync(CancellationToken ct);

    /// <summary>Preassigns one stable assistant node id; streaming deltas and the commit address it.</summary>
    ValueTask<string> BeginAssistantAsync(ChatStepInfo step, CancellationToken ct);

    ValueTask ApplyAssistantDeltaAsync(string messageId, ChatAssistantDelta delta, CancellationToken ct);
    ValueTask CommitAssistantAsync(string messageId, ChatAssistantRecord message, CancellationToken ct);

    /// <summary>Removes the placeholder (no prefix) or finalizes the same node as failed/interrupted.</summary>
    ValueTask AbandonAssistantAsync(string messageId, bool keepDeliveredPrefix, CancellationToken ct);

    ValueTask SetModelRequestAttemptAsync(ChatModelRequestAttemptView attempt, CancellationToken ct);
    ValueTask SetToolStateAsync(ChatToolExecutionView tool, CancellationToken ct);
    ValueTask AppendToolResultAsync(ChatToolResultRecord result, CancellationToken ct);
    ValueTask CheckpointTurnAsync(ChatTurnCheckpoint checkpoint, CancellationToken ct);
}