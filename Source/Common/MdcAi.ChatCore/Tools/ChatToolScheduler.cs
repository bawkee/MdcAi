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

namespace MdcAi.ChatCore.Tools;

using MdcAi.ChatCore.Security;
using MdcAi.ChatCore.Sessions;
using Newtonsoft.Json.Linq;

/// <summary>
/// Validates, gains approval for, and executes an assistant message's tool calls, then commits
/// their results in MODEL order. Expected tool failures - invalid JSON args, a missing file,
/// denied consent, timeout, nonzero exit, unknown tool - become structured <c>role:"tool"</c>
/// results that continue the turn; they never crash it (DSH proposal §5.2).
///
/// Phase 3 (P3-08): contiguous ParallelSafe calls may execute concurrently on a bounded pool
/// (default 4); approval/preflight still happens in model order; results are COMMITTED in model
/// order. An Exclusive call is a barrier before and after itself. The execution mode on a tool
/// definition is authoritative - never run writes/shell concurrently merely because the model
/// emitted them together.
/// </summary>
public sealed class ChatToolScheduler
{
    private readonly ChatToolRegistry _registry;
    private readonly int _maxConsecutiveRepeats;
    private readonly int _parallelPool;

    public ChatToolScheduler(ChatToolRegistry registry, int maxConsecutiveRepeats = 3, int parallelPool = 4)
    {
        _registry = registry;
        _maxConsecutiveRepeats = maxConsecutiveRepeats;
        _parallelPool = Math.Max(1, parallelPool);
    }

    public async ValueTask<IReadOnlyList<ChatToolResultRecord>> ExecuteAsync(
        IReadOnlyList<ChatMessageToolCall> calls,
        ChatTurnRequest turn,
        IChatSessionSink sink,
        WorkspaceReadObservationSet readSet,
        int stepNumber,
        CancellationToken ct)
    {
        var results = new List<ChatToolResultRecord>();
        string lastRepeatKey = null;
        var consecutiveRepeats = 0;
        var concluded = false;

        var i = 0;
        while (i < calls.Count)
        {
            ct.ThrowIfCancellationRequested();

            var call = calls[i];
            var name = call.Function?.Name;

            if (concluded)
            {
                // A terminal goal tool is an exclusive ordering barrier: never run calls that
                // come after it, but still materialize a protocol-valid result for each id.
                var skipped = ErrorResult(
                    ChatToolStatus.Skipped, "skipped_goal_terminal",
                    "This call was not executed because an earlier call concluded the goal/turn.");
                var skipRecord = new ChatToolResultRecord(call.Id, name, i, skipped);
                results.Add(skipRecord);
                await sink.AppendToolResultAsync(skipRecord, ct);
                i++;
                continue;
            }

            var isStateChanging = _registry.TryGet(name, out var tool) && tool.Risk != ChatToolRisk.ReadOnly;

            // Probable-loop guard checked BEFORE execution: the same (name, canonical args) call
            // proposed a third time without an intervening successful state change is refused,
            // committed as a structured guard failure, and ends this scheduler run.
            var repeatKey = BuildRepeatKey(call);
            if (!isStateChanging && repeatKey == lastRepeatKey && consecutiveRepeats + 1 >= _maxConsecutiveRepeats)
            {
                var guard = ErrorResult(
                    ChatToolStatus.Failed, "probable_loop",
                    $"The identical call '{name}' was repeated {consecutiveRepeats + 1} times without an intervening successful state change. Stop and reassess.");
                var guardRecord = new ChatToolResultRecord(call.Id, name, i, guard);
                results.Add(guardRecord);
                await sink.AppendToolResultAsync(guardRecord, ct);
                break;
            }

            // Partition: a contiguous run of ParallelSafe calls can execute concurrently; an
            // Exclusive call is a barrier it must run in its own group, alone. Consecutive
            // IDENTICAL calls are never parallelized - they are the loop-guard's business.
            var run = new List<int>();
            var canParallel = _registry.TryGet(name, out var firstTool) && firstTool.ExecutionMode == ChatToolExecutionMode.ParallelSafe;
            run.Add(i);

            if (canParallel)
            {
                var j = i + 1;
                while (j < calls.Count)
                {
                    var nextName = calls[j].Function?.Name;
                    var nextIsParallel = _registry.TryGet(nextName, out var nextTool)
                                        && nextTool.ExecutionMode == ChatToolExecutionMode.ParallelSafe;
                    if (!nextIsParallel)
                        break;

                    // A repeat of the last call in the run breaks the partition so the loop guard
                    // can see and stop the 3rd consecutive identical call.
                    if (string.Equals(BuildRepeatKey(calls[j]), BuildRepeatKey(calls[j - 1]), StringComparison.Ordinal))
                        break;

                    run.Add(j);
                    j++;
                }
            }

            var group = run.ToArray();

            if (canParallel && group.Length > 1)
            {
                // Execute concurrently on a bounded pool; results come back per index so commit
                // stays in MODEL order. The probability-loop repeat bookkeeping below only counts
                // the first call of the group - a repeated call inside one parallel group is
                // already suspicious and the model can see all results together.
                var executed = await ExecuteGroupParallelAsync(calls, group, turn, sink, readSet, stepNumber, ct);
                foreach (var rec in executed)
                {
                    results.Add(rec);
                    await sink.AppendToolResultAsync(rec, ct);
                    if (rec.Result.Ok && rec.Result.Observation != null)
                        readSet.Record(rec.Result.Observation);
                }
            }
            else
            {
                var record = await ExecuteOneAsync(call, i, name, tool, turn, sink, readSet, stepNumber, ct);
                results.Add(record);
                await sink.AppendToolResultAsync(record, ct);
                if (record.Result.Ok && record.Result.Observation != null)
                    readSet.Record(record.Result.Observation);
            }

            var firstExecuted = results[results.Count - group.Length];
            if (firstExecuted.Result.ConcludesTurn)
                concluded = true;
            else if (firstExecuted.Result.Status == ChatToolStatus.Completed && isStateChanging)
            {
                consecutiveRepeats = 0;
                lastRepeatKey = null;
            }
            else
            {
                consecutiveRepeats = repeatKey == lastRepeatKey ? consecutiveRepeats + 1 : 1;
                lastRepeatKey = repeatKey;
            }

            i += group.Length;
        }

        return results;
    }

    /// <summary>
    /// Bounded parallel execution of a contiguous ParallelSafe run: approval/preflight happens
    /// in MODEL order, execution overlaps on a small pool, and results are returned in model
    /// order for ordered commit.
    /// </summary>
    private async ValueTask<ChatToolResultRecord[]> ExecuteGroupParallelAsync(
        IReadOnlyList<ChatMessageToolCall> calls,
        int[] group,
        ChatTurnRequest turn,
        IChatSessionSink sink,
        WorkspaceReadObservationSet readSet,
        int stepNumber,
        CancellationToken ct)
    {
        var records = new ChatToolResultRecord[group.Length];
        using var pool = new SemaphoreSlim(_parallelPool);

        var tasks = new Task[group.Length];
        for (var k = 0; k < group.Length; k++)
        {
            var idx = group[k];
            var slot = k;
            tasks[slot] = Task.Run(async () =>
            {
                await pool.WaitAsync(ct);
                try
                {
                    records[slot] = await ExecuteOneAsync(calls[idx], idx, calls[idx].Function?.Name, null,
                                                          turn, sink, readSet, stepNumber, ct);
                }
                finally
                {
                    pool.Release();
                }
            }, ct);
        }

        await Task.WhenAll(tasks);
        return records;
    }

    private static string BuildRepeatKey(ChatMessageToolCall call)
    {
        var name = call.Function?.Name ?? "<none>";
        var args = call.Function?.Arguments ?? string.Empty;
        return name + "|" + NormalizeJson(args);
    }

    /// <summary>
    /// Standard model-facing failure envelope (DSH proposal §5.2): the model receives a
    /// predictable JSON result with a stable error code it can react to, never a stack trace.
    /// </summary>
    private static ChatToolExecutionResult ErrorResult(ChatToolStatus status, string code, string summary, bool retryable = false)
    {
        var envelope = new JObject
        {
            ["ok"] = false,
            ["status"] = status.ToString().ToLowerInvariant(),
            ["summary"] = summary,
            ["error"] = new JObject
            {
                ["code"] = code,
                ["retryable"] = retryable
            }
        };

        return ChatToolExecutionResult.Failure(status, code, envelope.ToString(Formatting.None));
    }

    private static string NormalizeJson(string arguments)
    {
        try
        {
            return JToken.Parse(arguments)?.ToString(Formatting.None) ?? arguments;
        }
        catch (JsonReaderException)
        {
            return arguments;
        }
    }

    private async ValueTask<ChatToolResultRecord> ExecuteOneAsync(
        ChatMessageToolCall call,
        int callIndex,
        string name,
        IChatTool tool,
        ChatTurnRequest turn,
        IChatSessionSink sink,
        WorkspaceReadObservationSet readSet,
        int stepNumber,
        CancellationToken ct)
    {
        // Unknown tool - a structured failure the model can recover from.
        if (string.IsNullOrEmpty(name) || tool == null)
        {
            // Parallel groups pass null from the caller; resolve from the registry here.
            if (!string.IsNullOrEmpty(name) && tool == null && _registry.TryGet(name, out var resolved))
                tool = resolved;
            else
            {
                var fail = ErrorResult(
                    ChatToolStatus.Failed, "unknown_tool",
                    string.IsNullOrEmpty(name) ? "Tool call is missing a tool name." : $"Unknown or disabled tool '{name}'.");
                return new ChatToolResultRecord(call.Id, name, callIndex, fail);
            }
        }

        // Arguments must parse as JSON.
        JObject arguments;
        try
        {
            arguments = JObject.Parse(call.Function?.Arguments ?? string.Empty);
        }
        catch (JsonReaderException ex)
        {
            var fail = ErrorResult(
                ChatToolStatus.Failed, "invalid_json_arguments",
                $"Tool '{name}' received invalid JSON arguments: {ex.Message}");
            return new ChatToolResultRecord(call.Id, name, callIndex, fail);
        }

        // Host validation regardless of provider "strict mode".
        var validation = ChatToolArgumentValidator.Instance.Validate(arguments, tool.ParametersSchema);
        if (!validation.IsValid)
        {
            var fail = ErrorResult(
                ChatToolStatus.Failed, validation.ErrorCode, $"{name}: {validation.Error}");
            return new ChatToolResultRecord(call.Id, name, callIndex, fail);
        }

        var context = new ChatToolExecutionContext(
            turn.ConversationId, turn.TurnId, stepNumber, call.Id,
            turn.WorkspacePath, readSet, turn.ApprovalService, turn.JobService,
            turn.ProviderKey, turn.Model, turn.Effort);

        var approval = await RequestApprovalAsync(tool, call.Id, name, arguments, turn, ct);
        if (approval == ChatApprovalDecision.Denied)
        {
            var denied = ErrorResult(
                ChatToolStatus.Denied, "approval_denied",
                $"Execution of '{name}' was not approved by the user.");
            return new ChatToolResultRecord(call.Id, name, callIndex, denied);
        }

        await sink.SetToolStateAsync(new ChatToolExecutionView(call.Id, name, "running", tool.Risk), ct);

        ChatToolExecutionResult result;
        try
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(tool.Timeout);
            result = await tool.ExecuteAsync(arguments, context, timeoutCts.Token);
        }
        catch (OperationCanceledException)
        {
            result = ct.IsCancellationRequested
                         ? ErrorResult(ChatToolStatus.Cancelled, "cancelled", $"Tool '{name}' was cancelled.")
                         : ErrorResult(ChatToolStatus.TimedOut, "timed_out", $"Tool '{name}' exceeded its timeout.");
        }
        catch (Exception ex)
        {
            result = ErrorResult(ChatToolStatus.Failed, "tool_exception",
                                 $"Tool '{name}' threw: {ex.GetType().Name}: {ex.Message}");
        }

        result = EnforceModelVisibleCap(result, turn.Limits.MaxModelVisibleToolResultBytes);

        return new ChatToolResultRecord(call.Id, name, callIndex, result);
    }

    /// <summary>
    /// A model-visible result cap: content beyond the limit is replaced by a bounded prefix with
    /// an explicit truncation marker; a capped result must never look complete. The canonical
    /// structured value is retained (bounded separately by the artifact layer).
    /// </summary>
    private static ChatToolExecutionResult EnforceModelVisibleCap(ChatToolExecutionResult result, long cap)
    {
        if (cap <= 0 || result.ModelContent == null || result.ModelContent.Length <= cap)
            return result;

        var keep = Math.Max(0, (int)cap - 96);
        var prefix = result.ModelContent[..Math.Min(keep, result.ModelContent.Length)];
        var marker = $"{prefix}…(truncated: {result.ModelContent.Length} chars total; see artifact for the full result)";
        return result with
        {
            ModelContent = marker,
            Truncated = true,
            TotalBytes = result.ModelContent.Length
        };
    }

    private async ValueTask<ChatApprovalDecision> RequestApprovalAsync(
        IChatTool tool,
        string toolCallId,
        string toolName,
        JObject arguments,
        ChatTurnRequest turn,
        CancellationToken ct)
    {
        // Read-only work may proceed in Phase 1 (the internal loop tests need it); mutating and
        // process work require an approval service and deny when none is wired (feature-gated).
        if (tool.Risk == ChatToolRisk.ReadOnly)
            return ChatApprovalDecision.Approved;

        if (turn.ApprovalService == null)
            return ChatApprovalDecision.Denied;

        var presentation = tool.PresentCall(arguments);
        var hash = ComputeArgumentsHash(arguments);
        var request = new ChatApprovalRequest(
            turn.ConversationId, turn.TurnId, toolCallId, toolName, tool.Risk, presentation, hash);
        var response = await turn.ApprovalService.RequestApprovalAsync(request, ct);
        return response.Decision;
    }

    public static string ComputeArgumentsHash(JObject arguments) =>
        Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(arguments?.ToString(Formatting.None) ?? string.Empty)));
}