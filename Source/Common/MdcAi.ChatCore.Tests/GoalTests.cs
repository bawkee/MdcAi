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

namespace MdcAi.ChatCore.Tests;

using MdcAi.ChatCore.Goals;

/// <summary>
/// P3-04/05 durable goals: state transitions, one-active-goal semantics, exactly-once round
/// admission with optimistic concurrency, round/budget caps stopping deterministically, and the
/// terminal goal tools concluding the turn with ConcludesTurn.
/// </summary>
public class GoalTests
{
    private sealed class InMemoryGoalStore : IGoalStore
    {
        public GoalState Goal { get; private set; }
        public int UpdateFailures { get; set; }

        public Task<GoalState> CreateAsync(string conversationId, string objective, int maxRounds,
                                           long? tokenLimit, decimal? costLimit, CancellationToken ct)
        {
            Goal = GoalState.Create("goal-1", conversationId, objective, maxRounds, tokenLimit, costLimit);
            return Task.FromResult(Goal);
        }

        public Task<GoalState> GetActiveAsync(string conversationId, CancellationToken ct) =>
            Task.FromResult(Goal);

        public Task<bool> TryUpdateAsync(GoalState expected, GoalState next, CancellationToken ct)
        {
            if (UpdateFailures > 0)
            {
                UpdateFailures--;
                return Task.FromResult(false);
            }

            if (Goal != expected)
                return Task.FromResult(false);

            Goal = next;
            return Task.FromResult(true);
        }
    }

    [Fact]
    public async Task Create_and_state_transitions_follow_the_table()
    {
        var store = new InMemoryGoalStore();
        var goal = await store.CreateAsync("c1", "make the tests pass", 5, null, null, CancellationToken.None);

        Assert.True(goal.IsLive);
        Assert.Equal(1, goal.Revision);

        // Complete -> no more rounds.
        var completed = goal.Complete("done", """["evidence"]""");
        Assert.Equal(GoalStatus.Complete, completed.Status);
        Assert.False(completed.MayAdmitRound().Allowed);

        // Block -> no more rounds, honest reason kept.
        var blocked = goal.Block("waiting_for_input", "need the fixture");
        Assert.Equal(GoalStatus.Blocked, blocked.Status);
        Assert.False(blocked.MayAdmitRound().Allowed);

        // Cancel / pause.
        Assert.Equal(GoalStatus.Cancelled, goal.Cancel().Status);
        Assert.Equal(GoalStatus.Paused, goal.Pause().Status);
    }

    [Fact]
    public async Task Round_cap_stops_deterministically_as_round_limit()
    {
        var store = new InMemoryGoalStore();
        var goal = await store.CreateAsync("c1", "fix", 2, null, null, CancellationToken.None);

        goal = await GoalConcurrency.AdmitOnceAsync(store, "c1", CancellationToken.None);
        Assert.Equal(1, goal.RoundsStarted);

        goal = await GoalConcurrency.AdmitOnceAsync(store, "c1", CancellationToken.None);
        Assert.Equal(2, goal.RoundsStarted);

        // Third admission refuses: round_limit (NOT blocked).
        var (allowed, reason) = (await store.GetActiveAsync("c1", CancellationToken.None)).MayAdmitRound();
        Assert.False(allowed);
        Assert.Equal(GoalStatus.RoundLimit, reason);
    }

    [Fact]
    public async Task Token_budget_exhaustion_is_budget_exhausted_not_blocked()
    {
        var store = new InMemoryGoalStore();
        var goal = await store.CreateAsync("c1", "big", 10, tokenLimit: 1000, costLimit: null, CancellationToken.None);

        // Consume 900 then try to admit a round costing 200 more -> over budget.
        var consumed = goal.ConsumeBudget(900, 0);
        await store.TryUpdateAsync(goal, consumed, CancellationToken.None);

        var (allowed, reason) = consumed.MayAdmitRound(additionalTokens: 200);
        Assert.False(allowed);
        Assert.Equal(GoalStatus.BudgetExhausted, reason);
    }

    [Fact]
    public async Task Concurrent_revision_mismatch_fails_admission_exactly_once()
    {
        var store = new InMemoryGoalStore();
        await store.CreateAsync("c1", "fix", 3, null, null, CancellationToken.None);

        // Simulate a concurrent writer bumping the revision between load and update.
        store.UpdateFailures = 1;

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => GoalConcurrency.AdmitOnceAsync(store, "c1", CancellationToken.None));

        // Admitted round was NOT counted (exactly-once admission under retry).
        var goal = await store.GetActiveAsync("c1", CancellationToken.None);
        Assert.Equal(0, goal.RoundsStarted);

        // A clean retry succeeds and counts exactly one.
        var admitted = await GoalConcurrency.AdmitOnceAsync(store, "c1", CancellationToken.None);
        Assert.Equal(1, admitted.RoundsStarted);
    }

    [Fact]
    public void Continuation_message_is_framed_and_model_visible()
    {
        var goal = GoalState.Create("goal-1", "c1", "objective", 6);
        var msg = GoalContinuationMessage.Build(goal, 3);

        Assert.Contains("<mdcai-goal-continuation", msg);
        Assert.Contains("goal-id=\"goal-1\"", msg);
        Assert.Contains("round=\"3\"", msg);
        Assert.Contains("max-rounds=\"6\"", msg);
    }

    [Fact]
    public async Task AfterTurn_admits_one_round_then_stops_at_cap()
    {
        var store = new InMemoryGoalStore();
        var controller = new GoalContinuationController(store);
        await store.CreateAsync("c1", "objective", 2, null, null, CancellationToken.None);

        var d1 = await controller.AfterTurnAsync("c1", 100, 0.001m, CancellationToken.None);
        Assert.True(d1.ShouldContinue);
        Assert.Equal(1, d1.GoalRound);

        var d2 = await controller.AfterTurnAsync("c1", 100, 0.001m, CancellationToken.None);
        Assert.True(d2.ShouldContinue);
        Assert.Equal(2, d2.GoalRound);

        // Third turn: no more rounds -> deterministic stop with round_limit.
        var d3 = await controller.AfterTurnAsync("c1", 100, 0.001m, CancellationToken.None);
        Assert.False(d3.ShouldContinue);
        Assert.Equal(GoalStatus.RoundLimit, d3.StopReason);
    }
}