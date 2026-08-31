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

namespace MdcAi.ChatCore.Goals;

/// <summary>Durable goal status vocabulary (DSH proposal §8.3).</summary>
public static class GoalStatus
{
    public const string Active = "active";
    public const string Paused = "paused";
    public const string Blocked = "blocked";
    public const string Complete = "complete";
    public const string Cancelled = "cancelled";
    public const string RoundLimit = "round_limit";
    public const string BudgetExhausted = "budget_exhausted";
    public const string Failed = "failed";

    /// <summary>Live (non-terminal) statuses that occupy the one-goal-per-conversation slot.</summary>
    public static bool IsLive(string status) =>
        status is Active or Paused or Blocked;
}

/// <summary>
/// The goal state domain: DURABLE state plus pure transition logic (DSH proposal §8.3). The
/// controller uses these transitions transactionally; the model can never mutate limits or
/// resume itself. RoundsStarted increments only on an ADMITTED continuation turn - retries of a
/// failed DB transaction do not.
/// </summary>
public sealed record GoalState(
    string IdGoal,
    string ConversationId,
    string Objective,
    string Status,
    int Revision,
    int MaxRounds,
    int RoundsStarted,
    long? TokenLimit,
    long? TokensConsumed,
    decimal? CostLimit,
    decimal? CostConsumed,
    string BlockedCode,
    string BlockedReason,
    string FinalSummary = null,
    string EvidenceJson = null,
    System.DateTime? UpdatedUtc = null)
{
    public static GoalState Create(string id, string conversationId, string objective, int maxRounds,
                                   long? tokenLimit = null, decimal? costLimit = null) =>
        new(id, conversationId, objective, GoalStatus.Active, 1, maxRounds, 0,
            tokenLimit, null, costLimit, null, null, null, null, null, null);

    public bool IsLive => GoalStatus.IsLive(Status);

    /// <summary>
    /// Whether one more continuation round may be admitted. Returns (allowed, stopReason).
    /// A round is admitted ONLY while Active; reaching the cap yields round_limit, budget
    /// exhaustion yields budget_exhausted - never mislabeled blocked.
    /// </summary>
    public (bool Allowed, string StopReason) MayAdmitRound(long additionalTokens = 0, decimal additionalCost = 0)
    {
        if (Status == GoalStatus.Cancelled)
            return (false, GoalStatus.Cancelled);
        if (Status == GoalStatus.Complete)
            return (false, GoalStatus.Complete);
        if (Status == GoalStatus.Blocked)
            return (false, GoalStatus.Blocked);
        if (Status == GoalStatus.Paused)
            return (false, GoalStatus.Paused);
        if (Status != GoalStatus.Active)
            return (false, Status);

        if (RoundsStarted >= MaxRounds)
            return (false, GoalStatus.RoundLimit);

        var tokensAfter = (TokensConsumed ?? 0) + additionalTokens;
        if (TokenLimit is { } tl && tokensAfter > tl)
            return (false, GoalStatus.BudgetExhausted);

        var costAfter = (CostConsumed ?? 0m) + additionalCost;
        if (CostLimit is { } cl && costAfter > cl)
            return (false, GoalStatus.BudgetExhausted);

        return (true, null);
    }

    /// <summary>Admits exactly one round: Revision++ and RoundsStarted++ (called ONLY when MayAdmitRound said yes).</summary>
    public GoalState AdmitRound() => this with { Revision = Revision + 1, RoundsStarted = RoundsStarted + 1 };

    public GoalState Complete(string summary, string evidenceJson) =>
        this with { Status = GoalStatus.Complete, FinalSummary = summary, EvidenceJson = evidenceJson, UpdatedUtc = System.DateTime.UtcNow };

    public GoalState Block(string code, string reason) =>
        this with { Status = GoalStatus.Blocked, BlockedCode = code, BlockedReason = reason, UpdatedUtc = System.DateTime.UtcNow };

    public GoalState Pause() => this with { Status = GoalStatus.Paused, UpdatedUtc = System.DateTime.UtcNow };

    public GoalState Cancel() => this with { Status = GoalStatus.Cancelled, UpdatedUtc = System.DateTime.UtcNow };

    public GoalState ConsumeBudget(long tokens, decimal cost) =>
        this with
        {
            TokensConsumed = (TokensConsumed ?? 0) + tokens,
            CostConsumed = (CostConsumed ?? 0m) + cost,
            UpdatedUtc = System.DateTime.UtcNow
        };

    public GoalState WithStatus(string status) => this with { Status = status, UpdatedUtc = System.DateTime.UtcNow };
}

/// <summary>
/// Persistence seam for goals (DSH proposal §8.3 / §8.0 dependency rule): narrow abstraction the
/// ChatUI/LocalDal adapters implement; ChatCore never sees EF directly.
/// </summary>
public interface IGoalStore
{
    Task<GoalState> CreateAsync(string conversationId, string objective, int maxRounds,
                                long? tokenLimit, decimal? costLimit, CancellationToken ct);
    Task<GoalState> GetActiveAsync(string conversationId, CancellationToken ct);
    Task<bool> TryUpdateAsync(GoalState expected, GoalState next, CancellationToken ct);
}

/// <summary>
/// The durable-blocking index guard: exactly one live goal per conversation. The store enforces
/// it via a filtered unique index or a transactional invariant (checked on Create/Update).
/// </summary>
public static class GoalConcurrency
{
    /// <summary>Admits a round with optimistic concurrency: only the expected revision may advance.</summary>
    public static async Task<GoalState> AdmitOnceAsync(
        IGoalStore store, string conversationId, CancellationToken ct,
        long additionalTokens = 0, decimal additionalCost = 0)
    {
        var goal = await store.GetActiveAsync(conversationId, ct);
        if (goal == null)
            throw new InvalidOperationException("No active goal for this conversation.");

        var (allowed, reason) = goal.MayAdmitRound(additionalTokens, additionalCost);
        if (!allowed)
            return goal with { Status = reason };

        var admitted = goal.AdmitRound();
        if (!await store.TryUpdateAsync(goal, admitted, ct))
            throw new InvalidOperationException("Goal revision changed concurrently - retry the admission.");

        return admitted;
    }
}