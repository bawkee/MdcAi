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

namespace MdcAi.ChatUI.ViewModels;

using LocalDal;
using MdcAi.ChatUI.Sessions;
using Newtonsoft.Json.Linq;
using OpenAiApi;

/// <summary>
/// Projects the CURRENT selected fork into the versioned transcript snapshot (DSH proposal
/// §7.2). The host owns transcript order: messages, a thinking activity immediately before each
/// reasoning-bearing assistant, and each assistant tool call PAIRED with its matching
/// role:"tool" result as ONE tool activity (the plain tool-result bubble is suppressed when the
/// paired activity exists, so nothing renders twice). All ids are stable and deterministic.
/// </summary>
public static class WebViewTranscriptProjector
{
    public const int CurrentContractVersion = 2;

    /// <summary>
    /// Projects the current branch. Per-call presentation intent is loaded from reconciliation
    /// state when present (live records already carry it); the projector itself never re-runs
    /// tool presenters for replay - persisted intent wins, generic fallback otherwise.
    /// </summary>
    public static WebViewTranscriptSnapshotDto Project(
        ConversationVm convo,
        IEnumerable<ChatMessageVm> nodes,
        long revision,
        Func<string, DbToolCall> toolCallLookup = null,
        PendingApproval pendingApproval = null,
        ChatTurnUsageSummary turnUsage = null)
    {
        var items = new List<WebViewTranscriptItemDto>();

        foreach (var node in nodes ?? Enumerable.Empty<ChatMessageVm>())
        {
            if (node.Role == ChatMessageRole.Tool)
            {
                // A role:"tool" message is only a message bubble when NO paired activity exists
                // (paired = the assistant's tool call above it). The pairing check happens at
                // projection time: if the previous node is an assistant with that ToolCallId,
                // the activity already rendered that call+result together.
                if (IsPaired(node))
                    continue;
            }

            // Thinking is a separate activity immediately before its assistant narration.
            if (!string.IsNullOrEmpty(node.ReasoningContent)
                && node.Role == ChatMessageRole.Assistant)
            {
                items.Add(new WebViewTranscriptItemDto
                {
                    Id = $"thinking:{node.Id}",
                    Kind = "activity",
                    TurnId = null,
                    StepNumber = null,
                    Revision = revision,
                    Activity = new WebViewActivityDto
                    {
                        ActivityKind = "thinking",
                        PresentationKind = "thinking",
                        Status = node.CompletionState ?? "completed",
                        Title = "Thinking",
                        Summary = FirstLine(node.ReasoningContent ?? node.ReasoningPreview),
                        SourceMessageId = node.Id,
                        Details = new WebViewActivityDetailsDto
                        {
                            Version = 1,
                            Kind = "thinking",
                            Context = new WebViewContextDetailsDto
                            {
                                SourceKind = "reasoning",
                                Content = node.ReasoningContent
                            }
                        }
                    }
                });
            }

            // The message itself.
            items.Add(new WebViewTranscriptItemDto
            {
                Id = $"message:{node.Id}",
                Kind = "message",
                TurnId = null,
                StepNumber = null,
                Revision = revision,
                Message = node.GetWebViewDtoEx()
            });

            // Tool calls of an assistant message each become one paired tool activity.
            if (node.ToolCalls is { Length: > 0 })
            {
                foreach (var call in node.ToolCalls)
                {
                    items.Add(new WebViewTranscriptItemDto
                    {
                        Id = $"tool:{call.Id}",
                        Kind = "activity",
                        TurnId = null,
                        StepNumber = null,
                        Revision = revision,
                        Activity = BuildToolActivity(node, call, toolCallLookup)
                    });
                }
            }
        }

        // Inline approval card (Phase 2.5): shown while a call awaits user consent. It is a
        // live-only surface - approval grants are never inferred from persisted cards.
        if (pendingApproval?.Request != null && pendingApproval.ToActivity() is { } approvalActivity)
        {
            items.Add(new WebViewTranscriptItemDto
            {
                Id = $"approval:{approvalActivity.ToolCallId}",
                Kind = "activity",
                TurnId = pendingApproval.Request.TurnId,
                Revision = revision,
                Activity = approvalActivity
            });
        }

        // Turn usage disclosure: derived from durable step/call/usage records, labeled unknown rather
// than zero when a metric wasn't supplied (DSH proposal §7.3).
        if (turnUsage != null && turnUsage.StepCount > 0)
        {
            items.Add(new WebViewTranscriptItemDto
            {
                Id = $"turn-summary:{turnUsage.TurnId}",
                Kind = "turn_summary",
                TurnId = turnUsage.TurnId,
                Revision = revision,
                TurnSummary = new WebViewTurnSummaryDto
                {
                    TurnId = turnUsage.TurnId,
                    ProviderModel = turnUsage.ProviderModel,
                    StepCount = turnUsage.StepCount,
                    ToolCallCount = turnUsage.ToolCallCount,
                    PromptTokens = turnUsage.PromptTokens,
                    CompletionTokens = turnUsage.CompletionTokens,
                    ReasoningTokens = turnUsage.ReasoningTokens,
                    PromptCacheReadTokens = turnUsage.PromptCacheReadTokens,
                    PromptCacheWriteTokens = turnUsage.PromptCacheWriteTokens,
                    Cost = turnUsage.Cost,
                    WallTimeMs = turnUsage.WallTimeMs,
                    Outcome = turnUsage.Outcome
                }
            });
        }

        return new WebViewTranscriptSnapshotDto
        {
            ContractVersion = CurrentContractVersion,
            ConversationId = convo?.Id,
            Revision = revision,
            Items = items.ToArray()
        };
    }

    /// <summary>
    /// Projects a SINGLE node as one transcript item for UpsertTranscriptItem live updates.
    /// Only streaming assistant nodes (content/reasoning) and completed nodes with tool calls go
    /// through here; the renderer replaces the whole item by stable id + revision.
    /// </summary>
    public static WebViewTranscriptItemDto ProjectSingleItem(ChatMessageVm node, long revision)
    {
        if (node == null)
            return null;

        if (node.Role == ChatMessageRole.Assistant)
        {
            // The active streaming placeholder / completed assistant node.
            var item = new WebViewTranscriptItemDto
            {
                Id = $"message:{node.Id}",
                Kind = "message",
                TurnId = null,
                StepNumber = null,
                Revision = revision,
                Message = node.GetWebViewDtoEx()
            };

            return item;
        }

        if (node.Role == ChatMessageRole.Tool)
            return null; // paired tool activity covers it; the snapshot already carries it

        return null;
    }

    private static bool IsPaired(ChatMessageVm toolResult)
    {
        // A tool result is paired when the previous node is the assistant whose tool call id
        // this result answers. The generic tool bubble is then suppressed.
        var prev = toolResult.Previous;
        return prev != null
            && prev.Role == ChatMessageRole.Assistant
            && prev.ToolCalls is { Length: > 0 }
            && prev.ToolCalls.Any(c => c.Id == toolResult.ToolCallId);
    }

    private static WebViewActivityDto BuildToolActivity(
        ChatMessageVm assistant,
        ChatMessageToolCall call,
        Func<string, DbToolCall> toolCallLookup)
    {
        var toolName = call.Function?.Name ?? "tool";

        // Preferred: the PERSISTED locale-neutral call/result presentation (replay must not
        // depend on the originating tool still existing). Fallback: generic bounded shapes from
        // the raw wire args/result.
        DbToolCall db = toolCallLookup?.Invoke(call.Id);

        var presentationKind = "generic";
        string title = HumanizeToolName(toolName);
        string summary = toolName;
        string status = "completed";
        string pill = null;

        // Start with the persisted typed details when present, else a generic bounded shape.
        WebViewActivityDetailsDto details;
        if (db?.ResultPresentationJson != null)
        {
            try
            {
                details = Newtonsoft.Json.JsonConvert.DeserializeObject<WebViewActivityDetailsDto>(db.ResultPresentationJson);
                if (details != null)
                    presentationKind = details.Kind;
            }
            catch
            {
                details = null; // corrupt/unknown intent -> generic fallback
            }
        }
        else
            details = null;

        details ??= new WebViewActivityDetailsDto
        {
            Version = 1,
            Kind = "generic"
        };

        // If there's a matching tool-result node below, fold its content into the summary.
        var resultNode = FindResultNode(assistant, call.Id);
        var resolvedSummary = resultNode != null ? Truncate(resultNode.Content, 120) : summary;
        if (resultNode != null)
        {
            status = resultNode.CompletionState ?? "completed";
            if (resultNode.CompletionState == "interrupted")
                pill = "interrupted";
        }

        // Keep the persisted typed payload; only generic fallbacks derive from raw wire data.
        if (presentationKind == "generic" && details.Generic == null)
        {
            details.Generic = new WebViewGenericDetailsDto
            {
                Input = call.Function?.Arguments ?? "{}",
                Output = resultNode?.Content ?? ""
            };
        }

        return new WebViewActivityDto
        {
            ActivityKind = "tool",
            PresentationKind = presentationKind,
            Status = status,
            Title = title,
            Summary = resolvedSummary,
            SourceMessageId = assistant.Id,
            ToolCallId = call.Id,
            ArgumentHash = null,
            Pill = pill,
            Details = details
        };
    }

    /// <summary>snake_case -> Title Case (read_file -> "Read file").</summary>
    private static string HumanizeToolName(string name)
    {
        if (string.IsNullOrEmpty(name))
            return name;

        var words = name.Replace('_', ' ').Split(' ');
        return string.Join(" ", words.Where(w => w.Length > 0)
                                     .Select(w => char.ToUpperInvariant(w[0]) + w.Substring(1)));
    }

    private static ChatMessageVm FindResultNode(ChatMessageVm assistant, string toolCallId)    {
        var next = assistant.Next;
        while (next != null && next.Role == ChatMessageRole.Tool)
        {
            if (next.ToolCallId == toolCallId)
                return next;
            next = next.Next;
        }

        return null;
    }

    private static string FirstLine(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;

        var first = text.Split('\n').FirstOrDefault(l => !string.IsNullOrWhiteSpace(l))?.Trim();
        return first ?? "Thinking";
    }

    private static string Truncate(string text, int max)
    {
        if (text == null)
            return null;
        return text.Length <= max ? text : text[..max] + "…";
    }
}