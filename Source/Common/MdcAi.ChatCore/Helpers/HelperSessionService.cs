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

namespace MdcAi.ChatCore.Helpers;

using MdcAi.ChatCore.Sessions;
using MdcAi.ChatCore.Tools;
using MdcAi.OpenAiApi;

/// <summary>
/// A one-shot helper session (DSH proposal §8.2): a SECOND ChatSessionService invocation sharing
/// the parent's provider/model but running over its OWN in-memory transcript with a filtered
/// READ-ONLY tool registry and its own limits. The parent loop does not continue its next LLM
/// step until this settles, and parent cancellation is linked to child cancellation.
/// </summary>
public sealed record HelperRunLimits(
    int MaxSteps,
    TimeSpan MaxWallTime)
{
    /// <summary>Proposal defaults: 6 child steps, 5 minutes wall time.</summary>
    public static HelperRunLimits Default { get; } = new(6, TimeSpan.FromMinutes(5));
}

public sealed record HelperRunResult(
    bool Ok,
    string Status, // completed | failed | cancelled | timed_out
    string FinalAnswer,
    IReadOnlyList<string> FileReferences,
    int StepCount,
    int ToolCallCount,
    string ErrorCode = null,
    string ErrorMessage = null);

public sealed record HelperRunRequest(
    string ParentConversationId,
    string ParentTurnId,
    string ParentToolCallId,
    string ProviderKey,
    string Model,
    string Effort,
    string WorkspacePath,
    string TaskPrompt,
    IReadOnlyList<string> EnabledToolNames,
    string RecentSuffix,
    HelperRunLimits Limits);
public sealed class HelperSessionService
{
    /// <summary>Helper depth 1 is the only supported depth in Phase 3: helpers never spawn helpers.</summary>
    public const int MaxHelperDepth = 1;

    private readonly IOpenAiApi _api;
    private readonly ChatToolRegistry _parentRegistry;
    private readonly Func<string, ChatSessionService> _serviceFactory;

    public HelperSessionService(
        IOpenAiApi api,
        ChatToolRegistry parentRegistry,
        Func<string, ChatSessionService> serviceFactory = null)
    {
        _api = api;
        _parentRegistry = parentRegistry;
        _serviceFactory = serviceFactory ?? (name => new ChatSessionService(_api, SubRegistry(name)));
    }

    /// <summary>The read-only tool subset a depth-1 helper is allowed to use.</summary>
    public ChatToolRegistry ReadOnlyRegistry() => SubRegistry("helper-readonly");

    private ChatToolRegistry SubRegistry(string cacheKey)
    {
        // Only read-only tools are ever granted to a helper (no writes, no shell, no helpers).
        var allowed = _parentRegistry.All
                                     .Where(t => t.Risk == ChatToolRisk.ReadOnly
                                                 && t.Name is "read_file" or "list_dir" or "grep")
                                     .Select(t => t.Name);
        return _parentRegistry.Filtered(allowed);
    }

    /// <summary>
    /// Runs the helper to completion and returns an ordinary structured result the parent can
    /// fold into its own transcript.
    /// </summary>
    public async Task<HelperRunResult> RunAsync(HelperRunRequest request, CancellationToken ct)
    {
        using var wallClockCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        wallClockCts.CancelAfter(request.Limits.MaxWallTime);

        var sink = new HelperTranscriptSink();
        var registry = SubRegistry("helper-readonly");
        var service = new ChatSessionService(_api, registry);

        // Scoped helper system prompt: bounded task, report back to parent, no ungranted power.
        var helperPremise =
            "You are a one-shot helper performing a bounded reading task for the parent agent. " +
            "Use only the provided read-only tools. Do not address the end user directly. " +
            "When done, answer concisely with evidence (exact file content, line numbers) and " +
            "the final conclusion the parent asked for.";

        var turn = new ChatTurnRequest(
            "helper:" + request.ParentConversationId,
            "helper-" + Guid.NewGuid().ToString("N"),
            null,
            request.ProviderKey,
            request.Model,
            request.Effort,
            helperPremise,
            request.WorkspacePath,
            request.EnabledToolNames,
            ChatTurnOrigin.Subagent,
            null, // helpers are read-only; no approvals needed
            new ChatTurnLimits(MaxSteps: request.Limits.MaxSteps),
            null);

        sink.Messages.Add(new ChatMessage(ChatMessageRole.User, request.TaskPrompt));
        if (!string.IsNullOrEmpty(request.RecentSuffix))
            sink.Messages.Add(new ChatMessage(ChatMessageRole.User,
                "Context from the parent conversation (recent tail):\n" + request.RecentSuffix));

        ChatTurnResult turnResult;
        try
        {
            turnResult = await service.RunTurnAsync(turn, sink, wallClockCts.Token);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return new HelperRunResult(false, "cancelled", null, Array.Empty<string>(), 0, 0,
                                       "cancelled", "The helper was cancelled with the parent.");
        }
        catch (OperationCanceledException)
        {
            return new HelperRunResult(false, "timed_out", null, Array.Empty<string>(), 0, 0,
                                       "timed_out", "The helper exceeded its wall-clock limit.");
        }

        if (!turnResult.IsSuccess)
        {
            // The driver maps an OperationCanceledException to a Cancelled outcome. Distinguish
            // parent cancellation (cancelled) from the helper's own wall-clock expiry (timed_out).
            var timedOut = turnResult.Outcome == ChatTurnOutcome.Cancelled
                           && wallClockCts.IsCancellationRequested
                           && !ct.IsCancellationRequested;

            return new HelperRunResult(false,
                                       timedOut ? "timed_out" : turnResult.Outcome.ToString().ToLowerInvariant(),
                                       null, Array.Empty<string>(),
                                       turnResult.StepCount, turnResult.TotalToolCalls,
                                       timedOut ? "timed_out" : turnResult.ErrorCode,
                                       timedOut ? "The helper exceeded its wall-clock limit." : turnResult.ErrorMessage);
        }

        var final = sink.Messages.LastOrDefault(m => m.Role == ChatMessageRole.Assistant)?.Content;
        var references = sink.Messages
                             .Where(m => m.Role == ChatMessageRole.Tool)
                             .SelectMany(ExtractReferences)
                             .Distinct(StringComparer.Ordinal)
                             .ToArray();

        return new HelperRunResult(true, "completed", final, references,
                                   turnResult.StepCount, turnResult.TotalToolCalls);
    }

    private static IEnumerable<string> ExtractReferences(ChatMessage toolMessage)
    {
        // Pull workspace-relative paths out of the structured tool result when present.
        string path = null;
        try
        {
            var value = Newtonsoft.Json.Linq.JToken.Parse(toolMessage.Content);
            if (value is Newtonsoft.Json.Linq.JObject obj && obj["path"] != null)
                path = obj["path"].ToString();
        }
        catch
        {
            // not structured JSON - fall through to the plain-text scan below
        }

        if (path == null && toolMessage.Content != null)
        {
            // read_file model content carries "path: <relative>" on its first line.
            var line = toolMessage.Content.Split('\n').FirstOrDefault(l => l.StartsWith("path: ", StringComparison.Ordinal));
            if (line != null)
                path = line["path: ".Length..].Trim();
        }

        if (path != null)
            yield return path;
    }

    /// <summary>In-memory transcript sink for one helper run.</summary>
    internal sealed class HelperTranscriptSink : IChatSessionSink
    {
        public List<ChatMessage> Messages { get; } = new();
        public string RunId { get; } = "helper-" + Guid.NewGuid().ToString("N");

        public ValueTask<ChatTranscriptSnapshot> GetCurrentBranchAsync(CancellationToken ct) =>
            new(new ChatTranscriptSnapshot(RunId, Messages.ToList()));

        public ValueTask<string> BeginAssistantAsync(ChatStepInfo step, CancellationToken ct) =>
            new(RunId + "-" + step.StepNumber);

        public ValueTask ApplyAssistantDeltaAsync(string messageId, ChatAssistantDelta delta, CancellationToken ct) =>
            ValueTask.CompletedTask;

        public ValueTask CommitAssistantAsync(string messageId, ChatAssistantRecord record, CancellationToken ct)
        {
            Messages.Add(record.Message);
            return ValueTask.CompletedTask;
        }

        public ValueTask AbandonAssistantAsync(string messageId, bool keepDeliveredPrefix, CancellationToken ct) =>
            ValueTask.CompletedTask;

        public ValueTask SetModelRequestAttemptAsync(ChatModelRequestAttemptView attempt, CancellationToken ct) =>
            ValueTask.CompletedTask;

        public ValueTask SetToolStateAsync(ChatToolExecutionView tool, CancellationToken ct) =>
            ValueTask.CompletedTask;

        public ValueTask AppendToolResultAsync(ChatToolResultRecord result, CancellationToken ct)
        {
            Messages.Add(new ChatMessage(ChatMessageRole.Tool, result.Result.ModelContent)
            {
                ToolCallId = result.ToolCallId
            });
            return ValueTask.CompletedTask;
        }

        public ValueTask CheckpointTurnAsync(ChatTurnCheckpoint checkpoint, CancellationToken ct) =>
            ValueTask.CompletedTask;
    }
}