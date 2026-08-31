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

namespace MdcAi.ChatCore.Tools.BuiltIn;

using MdcAi.ChatCore.Jobs;
using MdcAi.ChatCore.Process;

/// <summary>
/// run_powershell: executes an exact script through pwsh.exe with redirected stdin
/// (-NoLogo -NoProfile -NonInteractive), drains stdout/stderr concurrently, bounds retained
/// output, and kills the whole process tree on timeout/cancellation. A nonzero exit is a
/// COMPLETED tool result with ok:false - never a thrown loop exception.
///
/// When a conversation-level background-job service is present (Phase 3A), execution goes
/// through the JOB path: the process starts as a job, a short fast-path window is awaited, and
/// a still-running command returns status:"running" + job_id so the model polls with get_job.
/// PowerShell is full-trust: the approval copy says plainly it can touch anything the user can.
/// </summary>
public sealed class RunPowerShellChatTool : IChatTool
{
    public const int DefaultTimeoutMs = 120_000;

    private readonly IChatProcessRunner _runner;
    private readonly Func<string> _pwshResolver;
    private readonly int _fastPathWaitMs;

    public RunPowerShellChatTool(IChatProcessRunner runner = null, Func<string> pwshResolver = null, int? fastPathWaitMs = null)
    {
        _runner = runner ?? new SystemProcessRunner();
        _pwshResolver = pwshResolver ?? ResolvePwsh;
        _fastPathWaitMs = fastPathWaitMs ?? JobServiceOptions.Default.FastPathWaitMs;
    }

    public string Name => "run_powershell";
    public string Description =>
        "Run a PowerShell 7 script (stdin, no quoting games). Output is bounded and polled; " +
        "the script runs with YOUR full user permissions - outside any workspace boundary.";

    public JObject ParametersSchema => JObject.Parse($$"""
        {
          "type": "object",
          "additionalProperties": false,
          "properties": {
            "script": { "type": "string", "description": "Exact PowerShell script text." },
            "working_directory": { "type": "string", "description": "Workspace-relative cwd; omit for the workspace root." },
            "timeout_ms": { "type": "integer", "minimum": 1000, "maximum": 600000 },
            "description": { "type": "string", "description": "One-line intent shown on the approval card." }
          },
          "required": ["script"]
        }
        """);

    public ChatToolExecutionMode ExecutionMode => ChatToolExecutionMode.Exclusive;
    public ChatToolRisk Risk => ChatToolRisk.Process;
    public TimeSpan Timeout => TimeSpan.FromMinutes(15);

    public async ValueTask<ChatToolExecutionResult> ExecuteAsync(
        JObject arguments,
        ChatToolExecutionContext context,
        CancellationToken ct)
    {
        var script = (string)arguments["script"];
        if (string.IsNullOrWhiteSpace(script))
            return Error("script_required", "run_powershell requires a non-empty script.");

        var cwd = (string)arguments["working_directory"] ?? ".";
        var timeoutMs = arguments["timeout_ms"]?.Value<int?>() ?? DefaultTimeoutMs;

        // Resolve the workspace-relative cwd (guarded like every file tool).
        var guard = string.IsNullOrEmpty(context.WorkspacePath) ? null : new Security.WorkspacePathGuard(context.WorkspacePath);
        string cwdFull;
        if (guard != null)
        {
            cwdFull = guard.TryResolveRelative(cwd, out var rejection);
            if (rejection != null)
                return Error(rejection, $"Working directory rejected ({rejection}): {cwd}");
        }
        else
            cwdFull = cwd;

        var pwshPath = _pwshResolver();
        if (pwshPath == null)
            return Error("pwsh_unavailable",
                         "PowerShell 7 (pwsh.exe) was not found. Install PowerShell 7 or configure its location.");

        // Phase 3A job path: the conversation owns a job service -> run as a background job.
        if (context.JobService != null)
        {
            return await RunAsJobAsync(
                context.JobService, context, script, pwshPath, cwdFull, timeoutMs, ct);
        }

        // Fallback (no job service - unit tests / legacy): synchronous bounded run.
        return await RunSynchronouslyAsync(script, pwshPath, cwdFull, timeoutMs, ct);
    }

    private async ValueTask<ChatToolExecutionResult> RunAsJobAsync(
        IBackgroundJobService jobService,
        ChatToolExecutionContext context,
        string script,
        string pwshPath,
        string cwdFull,
        int timeoutMs,
        CancellationToken ct)
    {
        var request = new ChatProcessRequest(
            pwshPath,
            new[] { "-NoLogo", "-NoProfile", "-NonInteractive", "-Command", "-" },
            cwdFull,
            script,
            MaxRetainedOutputBytes: 96 * 1024,
            TimeoutMs: timeoutMs);

        // The worker streams process output into the job buffer as it arrives.
        var start = await jobService.StartAsync(new BackgroundJobStartRequest(
            "powershell", context.ConversationId, context.TurnId, context.ToolCallId,
            async (writer, workerCt) =>
            {
                var streamed = false;
                var streaming = request with
                {
                    OnStdoutChunk = (text, _) =>
                    {
                        streamed = true;
                        writer.WriteAsync(text, workerCt).AsTask().GetAwaiter().GetResult();
                    },
                    OnStderrChunk = (text, _) =>
                    {
                        streamed = true;
                        writer.WriteAsync(text, workerCt).AsTask().GetAwaiter().GetResult();
                    }
                };

                var processResult = await _runner.RunAsync(streaming, workerCt);
                writer.SetExitCode(processResult.ExitCode);

                // A runner that did not stream (e.g. a simple fake) still must land its
                // output; a streaming runner already wrote everything via the callbacks.
                if (!streamed)
                {
                    if (!string.IsNullOrEmpty(processResult.Stdout))
                        await writer.WriteAsync(processResult.Stdout, workerCt);
                    if (!string.IsNullOrEmpty(processResult.Stderr))
                        await writer.WriteAsync(processResult.Stderr, workerCt);
                }
            },
            MaxRetainedOutputBytes: 96 * 1024), ct);

        // Fast path: wait briefly; a short command returns its terminal result immediately.
        var fastPathCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        fastPathCts.CancelAfter(_fastPathWaitMs);

        try
        {
            var poll = await jobService.PollAsync(
                start.JobId, context.ConversationId, 0, _fastPathWaitMs, fastPathCts.Token);

            if (poll.IsTerminal)
                return BuildJobTerminalResult(poll);
            if (ct.IsCancellationRequested)
                return Error("cancelled", "PowerShell execution was cancelled.");

            return BuildJobRunningResult(poll);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            // Fast path elapsed (not user cancellation) -> still running, model must poll.
            var record = await jobService.GetAsync(start.JobId, context.ConversationId, ct);
            var running = new BackgroundJobPollResult(record, "", record.TotalOutputBytes);
            return BuildJobRunningResult(running);
        }
    }

    private ChatToolExecutionResult BuildJobRunningResult(BackgroundJobPollResult poll) =>
        ChatToolExecutionResult.Success(
            JobToolPayloads.RunningPayload(poll.Record.JobId, poll.NewOutput, poll.NextCursor),
            JobToolPayloads.RunningPayload(poll.Record.JobId, poll.NewOutput, poll.NextCursor).ToString(Formatting.None));

    private ChatToolExecutionResult BuildJobTerminalResult(BackgroundJobPollResult poll)
    {
        var payload = JobToolPayloads.TerminalPayload(poll.Record, poll.NewOutput, poll.NextCursor);

        var ok = poll.Record.Status == BackgroundJobStatus.Completed && poll.Record.ExitCode == 0;
        return new ChatToolExecutionResult(
            ok,
            ChatToolStatus.Completed, // a nonzero exit is still a completed, model-visible result
            payload,
            payload.ToString(Formatting.None),
            ErrorCode: ok ? null : "nonzero_exit",
            Truncated: poll.Record.OutputTruncated);
    }

    private async ValueTask<ChatToolExecutionResult> RunSynchronouslyAsync(
        string script, string pwshPath, string cwdFull, int timeoutMs, CancellationToken ct)
    {
        var processRequest = new ChatProcessRequest(
            pwshPath,
            new[] { "-NoLogo", "-NoProfile", "-NonInteractive", "-Command", "-" },
            cwdFull,
            script,
            MaxRetainedOutputBytes: 96 * 1024,
            TimeoutMs: timeoutMs);

        ChatProcessResult processResult;
        try
        {
            processResult = await _runner.RunAsync(processRequest, ct);
        }
        catch (OperationCanceledException)
        {
            return ChatToolExecutionResult.Failure(ChatToolStatus.Cancelled, "cancelled",
                                                   "PowerShell execution was cancelled.");
        }

        var payload = new JObject
        {
            ["status"] = processResult.Cancelled ? "cancelled" : processResult.TimedOut ? "timed_out" : processResult.Success ? "completed" : "failed",
            ["exit_code"] = processResult.ExitCode,
            ["stdout"] = processResult.Stdout,
            ["stderr"] = processResult.Stderr,
            ["duration_ms"] = (long)processResult.Duration.TotalMilliseconds,
            ["truncated"] = processResult.StdoutTruncated || processResult.StderrTruncated
        };

        return new ChatToolExecutionResult(
            processResult.Success,
            ChatToolStatus.Completed,
            payload,
            payload.ToString(Formatting.None),
            ErrorCode: processResult.Success ? null : "nonzero_exit",
            Truncated: processResult.StdoutTruncated || processResult.StderrTruncated);
    }

    /// <summary>Resolve pwsh.exe deliberately: standard install path first, then PATH.</summary>
    private static string ResolvePwsh()
    {
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var candidates = new List<string>
        {
            Path.Combine(programFiles, "PowerShell", "7", "pwsh.exe"),
            Path.Combine(programFiles, "PowerShell", "7-preview", "pwsh.exe")
        };

        foreach (var candidate in candidates)
            if (File.Exists(candidate))
                return candidate;

        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo("where.exe", "pwsh.exe")
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                CreateNoWindow = true
            };
            using var p = System.Diagnostics.Process.Start(psi);
            var output = p.StandardOutput.ReadToEnd();
            p.WaitForExit(5_000);
            var first = output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                             .FirstOrDefault(File.Exists);
            return first;
        }
        catch
        {
            return null;
        }
    }

    public ChatToolCallPresentation PresentCall(JObject arguments)
    {
        var description = (string)arguments["description"];
        var label = description ?? "run script";
        var summary = label.Length <= 60 ? label : label[..57] + "...";
        return ChatToolCallPresentation.Terminal(
            description ?? "Run PowerShell",
            $"Pwsh · {summary}",
            new JObject
            {
                ["script"] = arguments["script"],
                ["working_directory"] = arguments["working_directory"] ?? "."
            });
    }

    public ChatToolResultPresentation PresentResult(JObject arguments, ChatToolExecutionResult result) =>
        new(1, ChatToolResultPresentationKind.Terminal, "PowerShell",
            result.Ok ? "Pwsh · ok" : $"Pwsh · {result.ErrorCode ?? "failed"}",
            result.Value as JObject ?? new JObject { ["script"] = arguments["script"] });

    private static ChatToolExecutionResult Error(string code, string summary) =>
        ChatToolExecutionResult.Failure(ChatToolStatus.Failed, code, summary);
}