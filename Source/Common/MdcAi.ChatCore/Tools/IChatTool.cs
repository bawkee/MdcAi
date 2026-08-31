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

namespace MdcAi.ChatCore.Tools;

using MdcAi.ChatCore.Security;
using Newtonsoft.Json.Linq;

/// <summary>
/// Whether a tool may run in parallel with other calls. Writes, patches, PowerShell and goal
/// state are Exclusive barriers; read-only independent operations may be ParallelSafe (bounded
/// parallel scheduling is Phase 3; the scheduler still honors the mode).
/// </summary>
public enum ChatToolExecutionMode
{
    ParallelSafe,
    Exclusive
}

/// <summary>Host risk tier used by approval policy and presentation.</summary>
public enum ChatToolRisk
{
    ReadOnly,
    Write,
    Process
}

/// <summary>
/// The canonical execution result. <see cref="Value"/> is the structured result retained for
/// audit/UI; <see cref="ModelContent"/> is the bounded exact string sent to the model as the
/// <c>role:"tool"</c> content. <see cref="ConcludesTurn"/> is host-only (terminal goal tools)
/// and never serializes into the model-facing result. <see cref="Observation"/> is the prior-step
/// read observation a file-read tool wants registered AFTER its result is durably committed -
/// the optimistic-concurrency guard behind read-before-write (host-only, never serialized).
/// </summary>
public sealed record ChatToolExecutionResult(
    bool Ok,
    ChatToolStatus Status,
    JToken Value,
    string ModelContent,
    string ErrorCode = null,
    bool Retryable = false,
    bool ConcludesTurn = false,
    bool Truncated = false,
    long? TotalBytes = null,
    string ArtifactId = null,
    FileReadObservation Observation = null)
{
    public static ChatToolExecutionResult Success(JToken value, string modelContent) =>
        new(true, ChatToolStatus.Completed, value, modelContent);

    public static ChatToolExecutionResult Success(JToken value, string modelContent, FileReadObservation observation) =>
        new(true, ChatToolStatus.Completed, value, modelContent, Observation: observation);

    public static ChatToolExecutionResult Failure(ChatToolStatus status, string errorCode, string summary) =>
        new(false, status, null, summary, errorCode);
}

/// <summary>Terminal tool states; all of them materialize a protocol-valid tool result.</summary>
public enum ChatToolStatus
{
    Completed,
    Denied,
    Failed,
    TimedOut,
    Cancelled,
    Skipped
}

/// <summary>
/// Per-call execution context. Carries the conversation workspace root and the turn-scoped read
/// observation set (used by read-before-write enforcement) plus the approval service.
/// </summary>
public sealed record ChatToolExecutionContext(
    string ConversationId,
    string TurnId,
    int StepNumber,
    string ToolCallId,
    string WorkspacePath,
    WorkspaceReadObservationSet ReadObservations,
    IChatToolApprovalService ApprovalService);

/// <summary>
/// A registered tool. Name/Description/ParametersSchema are the model-facing surface; execution
/// delegates, timeouts, risk, presenters and DI objects never serialize. PresentCall/PresentResult
/// are PURE functions over validated arguments/results - no IO, no side effects, no view models.
/// </summary>
public interface IChatTool
{
    string Name { get; }
    string Description { get; }
    JObject ParametersSchema { get; }
    ChatToolExecutionMode ExecutionMode { get; }
    ChatToolRisk Risk { get; }
    TimeSpan Timeout { get; }

    ValueTask<ChatToolExecutionResult> ExecuteAsync(
        JObject arguments,
        ChatToolExecutionContext context,
        CancellationToken ct);

    ChatToolCallPresentation PresentCall(JObject arguments);
    ChatToolResultPresentation PresentResult(JObject arguments, ChatToolExecutionResult result);
}