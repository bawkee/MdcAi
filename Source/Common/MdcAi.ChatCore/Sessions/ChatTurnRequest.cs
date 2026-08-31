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

using MdcAi.ChatCore.Security;

/// <summary>
/// Why a turn was started - provenance, never a display label. Human turns come from the
/// composer; goal/subagent turns are started by the loop machinery and must render as synthetic.
/// </summary>
public enum ChatTurnOrigin
{
    Human,
    Goal,
    Subagent
}

/// <summary>
/// One user-turn invocation. Provider/model/effort/tool schema are stamped once at turn start and
/// never varied mid-turn (DSH proposal §9.2). ApprovalService may be null when approval UI is not
/// wired (Phase 1 core tests) - mutating/process tools then deny by policy.
/// </summary>
public sealed record ChatTurnRequest(
    string ConversationId,
    string TurnId,
    string TriggerMessageId,
    string ProviderKey,
    string Model,
    string Effort,
    string Premise,
    string WorkspacePath,
    IReadOnlyList<string> EnabledToolNames,
    ChatTurnOrigin Origin,
    IChatToolApprovalService ApprovalService,
    ChatTurnLimits Limits);

/// <summary>
/// Conservative, configurable loop guards (DSH proposal §6.2). Exceeding a guard ends the turn
/// with an explicit outcome instead of silently presenting a truncated answer as complete.
/// </summary>
public sealed record ChatTurnLimits(
    int MaxSteps = 12,
    int MaxToolCallsPerStep = 8,
    int MaxToolCallsPerTurn = 32,
    long MaxModelVisibleToolResultBytes = 32 * 1024,
    int MaxStreamedToolJsonBytesPerCall = 64 * 1024,
    int MaxRepeatedIdenticalCalls = 3)
{
    public static ChatTurnLimits Default { get; } = new();
}