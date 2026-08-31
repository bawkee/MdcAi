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

using System.Text;
using MdcAi.ChatCore.Context;
using MdcAi.OpenAiApi;

/// <summary>
/// P3-06 context planner: conservative token budgets, atomic whole-turn units, pinned units
/// surviving trimming, the irreducible active group failing loudly when it cannot fit, and
/// tool-schema overhead taken out of the budget.
/// </summary>
public class ContextPlannerTests
{
    private static ChatMessage Msg(string content, int multiplier = 1) =>
        new(ChatMessageRole.User, new string('a', 190 * multiplier)); // ~65 tokens via estimator (190/3.8*1.3)

    private static ContextUnit TurnUnit(string id, params ChatMessage[] messages)
    {
        var est = new ChatTokenEstimator();
        var tokens = (long)messages.Sum(m => est.Estimate(m));
        return new ContextUnit(id, "turn", messages, tokens, IsIrreducible: false);
    }

    private static ContextUnit IrreducibleUnit(string id, params ChatMessage[] messages)
    {
        var est = new ChatTokenEstimator();
        var tokens = (long)messages.Sum(m => est.Estimate(m));
        return new ContextUnit(id, "turn", messages, tokens, IsIrreducible: true);
    }

    private static ChatContextPlanner.PlanInput Input(
        IReadOnlyList<ContextUnit> units,
        IReadOnlyList<string> active = null,
        IReadOnlyList<string> pinned = null,
        long contextLength = 4096,
        long completionBudget = 1024,
        IReadOnlyList<ChatTool> tools = null) =>
        new(units, pinned ?? Array.Empty<string>(), active ?? Array.Empty<string>(),
            contextLength, completionBudget, tools ?? Array.Empty<ChatTool>(), null);

    private readonly ChatContextPlanner _planner = new();

    [Fact]
    public void Everything_fits_within_budget()
    {
        var turn1 = TurnUnit("turn:1", Msg("a"), Msg("b"));
        var turn2 = TurnUnit("turn:2", Msg("c"));
        var plan = _planner.Plan(Input(new[] { turn1, turn2 }, active: new[] { "turn:2" }));

        Assert.True(plan.Fits);
        Assert.Equal(2, plan.Included.Count);
        Assert.Empty(plan.OmittedUnitIds);
        Assert.True(plan.EstimatedTotal <= plan.Budget);
    }

    [Fact]
    public void Old_turns_are_omitted_as_whole_units_never_partial()
    {
        var big = TurnUnit("turn:0", Msg("x", 25), Msg("y", 25)); // ~3,250 tokens
        var active1 = TurnUnit("turn:1", Msg("small"));
        var active2 = TurnUnit("turn:2", Msg("tiny"));

        var plan = _planner.Plan(Input(new[] { big, active1, active2 },
                                        active: new[] { "turn:1", "turn:2" },
                                        contextLength: 6000, completionBudget: 1024));

        // budget ≈ 6000 - 1024 - 600(margin) - tool(0) = 4376 -> big (~3250) FITS whole.
        Assert.Empty(plan.OmittedUnitIds);
        Assert.Contains("turn:0", plan.Included.Select(u => u.Id));

        var tight = _planner.Plan(Input(new[] { big, active1, active2 },
                                        active: new[] { "turn:1", "turn:2" },
                                        contextLength: 3000, completionBudget: 512));
        // budget ≈ 3000 - 512 - 300 - 0 = 2188 -> big turn is a WHOLE unit, omit it.
        Assert.Contains("turn:0", tight.OmittedUnitIds);
        Assert.DoesNotContain(tight.Included, u => u.Id == "turn:0");
    }

    [Fact]
    public void Pinned_units_survive_trimming()
    {
        var big = TurnUnit("turn:0", Msg("x", 40));
        var active = TurnUnit("turn:1", Msg("small"));
        var pinned = TurnUnit("pinned:req", Msg("the original requirement"));

        var tight = _planner.Plan(Input(new[] { big, active, pinned },
                                        active: new[] { "turn:1" },
                                        pinned: new[] { "pinned:req" },
                                        contextLength: 3000, completionBudget: 512));

        Assert.Contains(tight.Included, u => u.Id == "pinned:req");
        Assert.Contains(tight.Included, u => u.Id == "turn:1");
        Assert.Contains("turn:0", tight.OmittedUnitIds);
    }

    [Fact]
    public void Irreducible_active_group_bigger_than_budget_fails_loudly()
    {
        var hugeGroup = IrreducibleUnit("active:group",
            Msg("tool assistant content", 400), Msg("result", 400)); // ~30k tokens

        var plan = _planner.Plan(Input(new[] { hugeGroup },
                                        active: new[] { "active:group" },
                                        contextLength: 4096, completionBudget: 1024));

        Assert.False(plan.Fits);
        Assert.True(plan.OverBudget);
        Assert.Empty(plan.Included);
        // Never drop the group to make JSON fit.
        Assert.Contains("active:group", plan.OmittedUnitIds);
    }

    [Fact]
    public void Tool_schema_overhead_is_taken_from_the_budget()
    {
        var turn = TurnUnit("turn:1", Msg("hi"));
        var tools = new[]
        {
            new ChatTool { Type = "function", Function = new FunctionTool { Name = "read_file", Description = new string('d', 500), Parameters = new JObject() } }
        };

        var planWithTools = _planner.Plan(Input(new[] { turn },
                                                 active: new[] { "turn:1" },
                                                 contextLength: 4096, completionBudget: 1024, tools: tools));
        var planNoTools = _planner.Plan(Input(new[] { turn },
                                                 active: new[] { "turn:1" },
                                                 contextLength: 4096, completionBudget: 1024));

        Assert.True(planWithTools.ToolSchemaTokens > 0);
        Assert.True(planWithTools.Budget < planNoTools.Budget);
    }

    [Fact]
    public void Estimator_is_conservative_and_counts_reasoning_and_tool_calls()
    {
        var est = new ChatTokenEstimator();

        var plain = new ChatMessage(ChatMessageRole.User, new string('x', 3800)); // ~1300 chars -> ~445 tokens
        Assert.True(est.Estimate(plain) > 300);

        var assistant = new ChatMessage(ChatMessageRole.Assistant)
        {
            Content = null,
            ReasoningContent = new string('r', 3800),
            ReasoningDetails = JArray.Parse("""[{"type":"reasoning","content":[{"type":"text","text":"t"}]}]"""),
            ToolCalls = new[] { new ChatMessageToolCall { Id = "c1", Function = new ChatMessageFunction { Name = "read_file", Arguments = """{"path":"a.txt"}""" } } }
        };
        var withReasoning = est.Estimate(assistant);
        Assert.True(withReasoning > est.Estimate(plain)); // reasoning + tool call inflate the estimate
    }
}