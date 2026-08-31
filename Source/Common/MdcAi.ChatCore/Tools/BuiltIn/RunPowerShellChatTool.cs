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

using MdcAi.ChatCore.Process;
using MdcAi.ChatCore.Security;

/// <summary>
/// run_powershell: executes an exact script through pwsh.exe with redirected stdin
/// (-NoLogo -NoProfile -NonInteractive), drains stdout/stderr concurrently, bounds retained
/// output, and kills the whole process tree on timeout/cancellation. A nonzero exit is a
/// COMPLETED tool result with ok:false - never a thrown loop exception. PowerShell is
/// full-trust: the approval copy says plainly it can touch anything the user can.
/// </summary>
public sealed class RunPowerShellChatTool : WorkspaceToolBase
{
    public const int DefaultTimeoutMs = 120_000;

    private readonly IChatProcessRunner _runner;
    private readonly Func<string> _pwshResolver;

    public RunPowerShellChatTool(IChatProcessRunner runner = null, Func<string> pwshResolver = null)
    {
        _runner = runner ?? new SystemProcessRunner();
        _pwshResolver = pwshResolver ?? ResolvePwsh;
    }

    public override string Name => "run_powershell";
    public override string Description =>
        "Run a PowerShell 7 script (stdin, no quoting games). Output is bounded and polled; " +
        "the script runs with YOUR full user permissions - outside any workspace boundary.";

    public override JObject ParametersSchema => JObject.Parse($$"""
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

    public override ChatToolExecutionMode ExecutionMode => ChatToolExecutionMode.Exclusive;
    public override ChatToolRisk Risk => ChatToolRisk.Process;
    public override TimeSpan Timeout => TimeSpan.FromMinutes(15);

    protected override async ValueTask<ChatToolExecutionResult> ExecuteWorkspaceAsync(
        JObject arguments,
        WorkspacePathGuard guard,
        ChatToolExecutionContext context,
        CancellationToken ct)
    {
        var script = (string)arguments["script"];
        if (string.IsNullOrWhiteSpace(script))
            return Error("script_required", "run_powershell requires a non-empty script.");

        var cwd = (string)arguments["working_directory"] ?? ".";

        var pwshPath = _pwshResolver();
        if (pwshPath == null)
            return Error("pwsh_unavailable",
                         "PowerShell 7 (pwsh.exe) was not found. Install PowerShell 7 or configure its location.");

        var cwdFull = guard.TryResolveRelative(cwd, out var rejection);
        if (rejection != null)
            return Error(rejection, $"Working directory rejected ({rejection}): {cwd}");

        var timeoutMs = arguments["timeout_ms"]?.Value<int?>() ?? DefaultTimeoutMs;

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

        var summary = BuildSummary(processResult);

        return new ChatToolExecutionResult(
            Ok: processResult.Success,
            ChatToolStatus.Completed, // a nonzero exit is still a completed, model-visible result
            new JObject
            {
                ["exit_code"] = processResult.ExitCode,
                ["stdout"] = processResult.Stdout,
                ["stderr"] = processResult.Stderr,
                ["duration_ms"] = (long)processResult.Duration.TotalMilliseconds,
                ["timed_out"] = processResult.TimedOut,
                ["cancelled"] = processResult.Cancelled,
                ["stdout_truncated"] = processResult.StdoutTruncated,
                ["stderr_truncated"] = processResult.StderrTruncated,
                ["total_stdout_bytes"] = processResult.TotalStdoutBytes,
                ["total_stderr_bytes"] = processResult.TotalStderrBytes
            },
            summary,
            ErrorCode: processResult.Success ? null : "nonzero_exit",
            Truncated: processResult.StdoutTruncated || processResult.StderrTruncated);
    }

    private static string BuildSummary(ChatProcessResult r)
    {
        var status = r.Cancelled ? "cancelled"
                   : r.TimedOut ? "timed out"
                   : r.Success ? "ok"
                   : $"exit code {r.ExitCode}";

        var sb = new System.Text.StringBuilder();
        sb.Append($"pwsh: {status}");
        if (!string.IsNullOrWhiteSpace(r.Stdout))
            sb.Append("\nstdout:\n").Append(r.Stdout.TrimEnd());
        if (!string.IsNullOrWhiteSpace(r.Stderr))
            sb.Append("\nstderr:\n").Append(r.Stderr.TrimEnd());
        if (r.StdoutTruncated || r.StderrTruncated)
            sb.Append("\n(output truncated; retained the first 96 KiB)");

        return sb.ToString();
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

    public override ChatToolCallPresentation PresentCall(JObject arguments)
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

    public override ChatToolResultPresentation PresentResult(JObject arguments, ChatToolExecutionResult result) =>
        new(1, ChatToolResultPresentationKind.Terminal, "PowerShell",
            result.Ok ? "Pwsh · ok" : $"Pwsh · {result.ErrorCode ?? "failed"}",
            result.Value as JObject ?? new JObject { ["script"] = arguments["script"] });
}