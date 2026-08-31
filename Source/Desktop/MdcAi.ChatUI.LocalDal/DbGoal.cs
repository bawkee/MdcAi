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
/// Durable, bounded goal (DSH proposal §8.3): user-authorized continuation with round and cost
/// budgets. One active/paused/blocked goal per conversation; revisions give optimistic
/// concurrency; on restart an active goal appears Paused and never resumes API spend by itself.
/// </summary>
public class DbGoal
{
    [Key] public string IdGoal { get; set; }
    public string IdConversation { get; set; }
    public string Objective { get; set; }

    /// <summary>active | paused | blocked | complete | cancelled | round_limit | budget_exhausted | failed</summary>
    public string Status { get; set; }

    /// <summary>Positive revision for optimistic concurrency.</summary>
    public int Revision { get; set; }

    public int MaxRounds { get; set; }
    public int RoundsStarted { get; set; }

    public long? TokenLimit { get; set; }
    public long? TokensConsumed { get; set; }
    public decimal? CostLimit { get; set; }
    public decimal? CostConsumed { get; set; }

    public DateTime CreatedUtc { get; set; }
    public DateTime? UpdatedUtc { get; set; }
    public DateTime? StartedUtc { get; set; }
    public DateTime? EndedUtc { get; set; }

    /// <summary>Structured blocked code/reason; null when not blocked.</summary>
    public string BlockedCode { get; set; }
    public string BlockedReason { get; set; }

    /// <summary>Final summary + evidence when complete/blocked.</summary>
    public string FinalSummary { get; set; }
    public string EvidenceJson { get; set; }
}