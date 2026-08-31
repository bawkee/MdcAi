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

namespace MdcAi.ChatCore.Security;

using MdcAi.ChatCore.Tools;

public enum ChatApprovalDecision
{
    Approved,
    Denied
}

/// <summary>
/// An immutable approval subject. The response must match conversation/turn/tool-call id and the
/// exact arguments hash; a stale renderer click can never approve a changed call (DSH §6.4).
/// </summary>
public sealed record ChatApprovalRequest(
    string ConversationId,
    string TurnId,
    string ToolCallId,
    string ToolName,
    ChatToolRisk Risk,
    ChatToolCallPresentation Presentation,
    string ArgumentsHash);

public sealed record ChatApprovalResponse(
    string ConversationId,
    string TurnId,
    string ToolCallId,
    string ArgumentsHash,
    ChatApprovalDecision Decision);

/// <summary>
/// The authority boundary between the model (untrusted planner) and side effects (host-approved).
/// Phase 1 tests use a fake; Phase 2 implements pending inline approval state in ConversationVm.
/// Read/write grants may be turn-scoped; every PowerShell call remains individually approved.
/// </summary>
public interface IChatToolApprovalService
{
    ValueTask<ChatApprovalResponse> RequestApprovalAsync(ChatApprovalRequest request, CancellationToken ct);

    /// <summary>Whether a ReadOnly call may proceed without asking again (turn-scoped grant).</summary>
    ValueTask<bool> HasReadGrantAsync(string conversationId, string turnId, CancellationToken ct);
}