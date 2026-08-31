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
/// Relational run checkpoint for one user turn (DSH proposal §6.5). Not a full session event
/// store - enough durable state to explain what happened and reconcile interrupted runs.
/// </summary>
public class DbChatTurn
{
    [Key] public string IdTurn { get; set; }
    public string IdConversation { get; set; }
    public string IdTriggerMessage { get; set; }

    /// <summary>Stable origin values: human | goal | subagent.</summary>
    public string Origin { get; set; }

    /// <summary>Live state: started | running | completed | max_tokens | max_steps | cancelled | failed.</summary>
    public string Status { get; set; }

    /// <summary>Terminal outcome: Completed | MaxTokens | MaxSteps | BlockedOnApproval | Cancelled | Failed.</summary>
    public string Outcome { get; set; }

    public string ProviderKey { get; set; }
    public string Model { get; set; }
    public string Effort { get; set; }

    /// <summary>Ordered prompt-builder sections (id/title/content/hash) as JSON; no credentials.</summary>
    public string PromptSectionsJson { get; set; }

    /// <summary>The exact assembled system prompt sent on the wire (diagnosis/replay; no credentials).</summary>
    public string PromptSnapshot { get; set; }

    /// <summary>The advertised tool-schema snapshot used on the wire.</summary>
    public string ToolsSchemaSnapshot { get; set; }

    /// <summary>Which goal (if any) admitted this turn; goal attribution arrives in Phase 3.</summary>
    public string IdGoal { get; set; }
    public int? GoalRevision { get; set; }
    public int? GoalRound { get; set; }

    public DateTime StartedTs { get; set; }
    public DateTime? EndedTs { get; set; }
    public int StepCount { get; set; }

    /// <summary>Sanitized terminal error text (never raw secrets); null when the turn finished cleanly.</summary>
    public string LastError { get; set; }

    public List<DbChatStep> Steps { get; set; } = new();
    public List<DbMessage> Messages { get; set; } = new();
}

public class DbChatStep
{
    [Key] public string IdStep { get; set; }
    public string IdTurn { get; set; }
    public int StepNumber { get; set; }

    public DateTime? StartedTs { get; set; }
    public DateTime? FirstDeltaTs { get; set; }
    public DateTime? FirstOutputTs { get; set; }
    public DateTime? FinishedTs { get; set; }

    public string ProviderKey { get; set; }
    public string Model { get; set; }
    public string Effort { get; set; }
    public string FinishReason { get; set; }
    public string RequestId { get; set; }

    public int? PromptTokens { get; set; }
    public int? CompletionTokens { get; set; }
    public int? ReasoningTokens { get; set; }
    public int? PromptCacheReadTokens { get; set; }
    public int? PromptCacheWriteTokens { get; set; }
    public int? TotalTokens { get; set; }
    public decimal? Cost { get; set; }

    /// <summary>Derived timings, null when the provider/stream did not expose enough facts.</summary>
    public int? FirstTokenLatencyMs { get; set; }
    public int? DecodeDurationMs { get; set; }
    public int? ModelDurationMs { get; set; }

    /// <summary>Context plan diagnostic (budget/estimates/included ids) - populated in Phase 3.</summary>
    public string ContextPlanJson { get; set; }

    public DbChatTurn Turn { get; set; }
    public List<DbMessage> Messages { get; set; } = new();
    public List<DbModelRequestAttempt> Attempts { get; set; } = new();
}

/// <summary>
/// One provider request attempt with a durable retry disposition. The disposition is kept
/// separate from Status so a failed provider attempt is never mislabeled as an in-progress
/// request (DSH proposal §6.5).
/// </summary>
public class DbModelRequestAttempt
{
    [Key] public string IdAttempt { get; set; }
    public string IdTurn { get; set; }
    public string IdStep { get; set; }
    public int AttemptNumber { get; set; }

    public string ProviderKey { get; set; }
    public string Model { get; set; }
    public string RetryPolicyKey { get; set; }

    /// <summary>started | completed | failed | cancelled</summary>
    public string Status { get; set; }

    /// <summary>none | scheduled | started | cancelled</summary>
    public string RetryDisposition { get; set; }

    public DateTime? StartedTs { get; set; }
    public DateTime? EndedTs { get; set; }
    public int? ScheduledDelayMs { get; set; }
    public bool RetryDelayFromHeader { get; set; }
    public DateTime? RetryStartedTs { get; set; }

    public string FailureCategory { get; set; }
    public string FailureCode { get; set; }
    public string FailureDetail { get; set; }
    public int? HttpStatus { get; set; }
    public string RequestId { get; set; }

    public DbChatStep Step { get; set; }
}