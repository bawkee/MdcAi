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

/// <summary>
/// The continuation controller (DSH proposal §8.3): after a goal-attributed turn ends, decides
/// whether to admit exactly one more round and invokes the SAME conversation turn runner again.
/// It yields to the UI between rounds so pause/stop stays responsive, and it NEVER silently
/// resumes API spend - after a restart the goal stays Paused until the user resumes.
/// </summary>
public sealed class GoalContinuationController
{
    private readonly IGoalStore _store;

    public GoalContinuationController(IGoalStore store) => _store = store;

    /// <summary>
    /// Runs one continuation round (or stops deterministically). Called after each goal turn
    /// with the turn's usage. Returns the terminal stop reason, or null when another round was
    /// admitted and the caller should invoke the turn runner with the new GoalRound.
    /// </summary>
    public async Task<GoalContinuationDecision> AfterTurnAsync(
        string conversationId,
        long turnsTokens,
        decimal turnsCost,
        CancellationToken ct)
    {
        var goal = await _store.GetActiveAsync(conversationId, ct);
        if (goal == null)
            return GoalContinuationDecision.Stop("no_active_goal");

        // Consume the turn's budget atomically with the admission check.
        var consumed = goal.ConsumeBudget(turnsTokens, turnsCost);
        await _store.TryUpdateAsync(goal, consumed, ct);
        goal = consumed;

        var (allowed, reason) = goal.MayAdmitRound();
        if (!allowed)
            return GoalContinuationDecision.Stop(reason);

        var admitted = await GoalConcurrency.AdmitOnceAsync(_store, conversationId, ct);
        return GoalContinuationDecision.Admit(admitted.Revision, admitted.RoundsStarted);
    }
}

public sealed record GoalContinuationDecision(bool ShouldContinue, string StopReason, int GoalRevision, int GoalRound)
{
    public static GoalContinuationDecision Admit(int revision, int round) =>
        new(true, null, revision, round);

    public static GoalContinuationDecision Stop(string reason) =>
        new(false, reason, 0, 0);
}

/// <summary>
/// The framed synthetic user-role continuation message persisted before each admitted round.
/// Model-visible, branch-local, durable; rendered as a compact continuation card, never as a
/// message allegedly typed by the user (DSH proposal §8.3).
/// </summary>
public static class GoalContinuationMessage
{
    public static string Build(GoalState goal, int round) =>
        $"<mdcai-goal-continuation goal-id=\"{goal.IdGoal}\" revision=\"{goal.Revision}\" round=\"{round}\" max-rounds=\"{goal.MaxRounds}\">\n" +
        "Continue working toward the active objective. Review the latest tool results, make concrete " +
        "progress, and either continue, call complete_goal with evidence, or call block_goal with the " +
        "specific input or external change required.\n" +
        "</mdcai-goal-continuation>";
}