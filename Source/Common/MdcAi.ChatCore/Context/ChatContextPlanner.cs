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

namespace MdcAi.ChatCore.Context;

using MdcAi.OpenAiApi;

/// <summary>
/// Token estimation (DSH proposal §8.4): exact tokenizers are only known for certain OpenAI
/// families; arbitrary OpenRouter models use a CONSERVATIVE UTF-8/character estimator with a
/// safety multiplier and protocol overhead. Actual provider usage.prompt_tokens calibrates the
/// estimate; the estimator never pretends it is exact.
/// </summary>
public interface IChatTokenEstimator
{
    long Estimate(OpenAiApi.ChatMessage message);
    long Estimate(string text);
    long EstimateToolSchema(IReadOnlyList<OpenAiApi.ChatTool> tools);
}

public sealed class ChatTokenEstimator : IChatTokenEstimator
{
    /// <summary>Conservative multiplier applied to the raw char-based estimate.</summary>
    public const double SafetyMultiplier = 1.3;

    /// <summary>Average characters per token used by the conservative estimator.</summary>
    public const double CharsPerToken = 3.8;

    /// <summary>Per-protocol-field JSON overhead in tokens (role, content, name, finish...).</summary>
    public const long PerMessageOverhead = 4;

    public const long PerToolOverhead = 20;

    public long Estimate(string text)
    {
        if (string.IsNullOrEmpty(text))
            return 0;
        return (long)Math.Ceiling(text.Length / CharsPerToken * SafetyMultiplier);
    }

    public long Estimate(OpenAiApi.ChatMessage message)
    {
        if (message == null)
            return 0;

        var tokens = PerMessageOverhead;
        tokens += Estimate(message.Content ?? "");
        tokens += Estimate(message.ReasoningContent ?? "");
        tokens += Estimate(message.ReasoningRaw?.ToString() ?? "");
        tokens += Estimate(message.ReasoningDetails?.ToString() ?? "");

        if (message.ToolCalls is { Length: > 0 })
        {
            // Assistant tool calls are structured and verbose; count generously.
            foreach (var call in message.ToolCalls)
                tokens += 12 + Estimate(call.Function?.Name ?? "") + Estimate(call.Function?.Arguments ?? "");
        }

        return tokens;
    }

    public long EstimateToolSchema(IReadOnlyList<OpenAiApi.ChatTool> tools)
    {
        if (tools == null || tools.Count == 0)
            return 0;

        var total = PerToolOverhead * tools.Count;
        foreach (var tool in tools)
        {
            total += Estimate(tool.Function?.Name ?? "");
            total += Estimate(tool.Function?.Description ?? "");
            total += Estimate(tool.Function?.Parameters?.ToString() ?? "");
        }

        return total;
    }
}

/// <summary>
/// Atomic context units (DSH proposal §8.4): a completed human turn including every
/// assistant/tool step is indivisible; an assistant-with-tool-calls plus every matching result
/// is indivisible; reasoning fields stay on every included assistant message when tools are
/// advertised. The planner works on whole units, never inside a group.
/// </summary>
public sealed record ContextUnit(
    string Id,            // "turn:{id}" | "summary:{id}" | "keep:{id}"
    string Kind,          // turn | summary | pinned
    IReadOnlyList<OpenAiApi.ChatMessage> Messages,
    long EstimatedTokens,
    bool IsIrreducible);  // active turn / grouped assistant+results / reasoning-replay groups

/// <summary>The planner's decision: included units in order, omitted unit ids, and totals.</summary>
public sealed record ContextPlan(
    IReadOnlyList<ContextUnit> Included,
    IReadOnlyList<string> OmittedUnitIds,
    long Budget,
    long EstimatedTotal,
    long ToolSchemaTokens,
    string PlannerVersion,
    bool Fits)
{
    public bool OverBudget => !Fits;
}

/// <summary>
/// Deterministic retention planner (DSH proposal §8.4): active turn + immediately preceding
/// human request always retained; explicitly pinned units retained; a recent completed-turn
/// suffix retained until the budget is approached; the latest valid summary covers the omitted
/// prefix. Never drops part of a tool/reasoning group to make JSON fit - if the irreducible
/// active group alone exceeds the budget, the plan fails loudly BEFORE the API request.
/// </summary>
public sealed class ChatContextPlanner
{
    public const string PlannerVersion = "2026-08";

    private readonly IChatTokenEstimator _estimator;

    public ChatContextPlanner(IChatTokenEstimator estimator = null)
    {
        _estimator = estimator ?? new ChatTokenEstimator();
    }

    public sealed record PlanInput(
        IReadOnlyList<ContextUnit> Units,          // oldest first
        IReadOnlyList<string> PinnedUnitIds,       // never omitted
        IReadOnlyList<string> ActiveUnitIds,       // the active turn + preceding human request
        long ContextLength,                        // model context length (tokens)
        long MaxCompletionBudget,                  // reserved for output/reasoning
        IReadOnlyList<OpenAiApi.ChatTool> ToolSchemas,
        Func<long, string> SummaryById = null);    // latest valid summary text for an omitted range

    public ContextPlan Plan(PlanInput input)
    {
        var toolTokens = _estimator.EstimateToolSchema(input.ToolSchemas);
        var safetyMargin = (long)(input.ContextLength * 0.1);
        var budget = input.ContextLength - input.MaxCompletionBudget - toolTokens - safetyMargin;

        var irreducible = input.Units.Where(u => u.IsIrreducible).Sum(u => u.EstimatedTokens);

        // The irreducible active group must ALWAYS fit - fail loudly, never drop a result.
        var activeIds = new HashSet<string>(input.ActiveUnitIds, StringComparer.Ordinal);
        var activeTokens = input.Units.Where(u => activeIds.Contains(u.Id)).Sum(u => u.EstimatedTokens);
        if (activeTokens > budget)
        {
            return new ContextPlan(Array.Empty<ContextUnit>(),
                                   input.Units.Select(u => u.Id).ToArray(),
                                   budget, activeTokens, toolTokens, PlannerVersion, Fits: false);
        }

        var pinned = new HashSet<string>(input.PinnedUnitIds ?? Enumerable.Empty<string>(), StringComparer.Ordinal);
        var included = new List<ContextUnit>();
        var omitted = new List<string>();
        var total = 0L;

        foreach (var unit in input.Units)
        {
            if (activeIds.Contains(unit.Id) || pinned.Contains(unit.Id) || unit.IsIrreducible)
            {
                included.Add(unit);
                total += unit.EstimatedTokens;
                continue;
            }

            // A completed turn fits only whole; otherwise omit and let a summary cover it.
            var wouldBe = total + unit.EstimatedTokens;
            if (wouldBe <= budget)
            {
                included.Add(unit);
                total = wouldBe;
            }
            else
            {
                omitted.Add(unit.Id);
            }
        }

        // Summaries replace whole omitted ranges; keep the plan honest about what was covered.
        var summaryTokens = 0L;
        if (omitted.Count > 0 && input.SummaryById != null)
        {
            var summary = input.SummaryById(total);
            if (!string.IsNullOrEmpty(summary))
                summaryTokens = _estimator.Estimate(summary);
        }

        return new ContextPlan(included, omitted, budget, total + summaryTokens, toolTokens,
                               PlannerVersion, Fits: true);
    }
}