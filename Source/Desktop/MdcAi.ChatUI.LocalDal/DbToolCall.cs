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

namespace MdcAi.ChatUI.LocalDal;

using System.ComponentModel.DataAnnotations;

/// <summary>
/// Normalized lifecycle/presentation projection of ONE tool call inside an assistant message.
/// The assistant message's ToolCallsJson remains the authoritative model-visible array; this row
/// tracks approval/execution state and the locale-neutral call/result presentation intents.
/// Never reconstruct the assistant wire array from these mutable rows.
/// </summary>
public class DbToolCall
{
    [Key] public string IdToolCall { get; set; }
    public string IdAssistantMessage { get; set; }
    public string IdTurn { get; set; }
    public string IdStep { get; set; }

    /// <summary>The exact wire tool_call_id (provider-side).</summary>
    public string ToolCallId { get; set; }

    /// <summary>Non-negative zero-based model order within the assistant message.</summary>
    public int CallIndex { get; set; }

    public string ToolName { get; set; }
    public string ArgumentsJson { get; set; }
    public string ArgumentsHash { get; set; }

    /// <summary>readonly | write | process</summary>
    public string Risk { get; set; }

    /// <summary>proposed | awaiting_approval | queued | running | completed | denied | failed | timed_out | cancelled | skipped</summary>
    public string Status { get; set; }

    public DateTime? ProposedTs { get; set; }
    public DateTime? StartedTs { get; set; }
    public DateTime? EndedTs { get; set; }

    /// <summary>Stable terminal error code (approval_denied, stale_read, ...), null when ok.</summary>
    public string ErrorCode { get; set; }

    /// <summary>The id of the role:"tool" message carrying this call's result (paired after commit).</summary>
    public string ResultMessageId { get; set; }

    /// <summary>Versioned locale-neutral call-presentation intent (DSH §7.2); no HTML, no executable behavior.</summary>
    public string CallPresentationJson { get; set; }

    /// <summary>Versioned locale-neutral result-presentation intent; replayed, never re-executed.</summary>
    public string ResultPresentationJson { get; set; }
}