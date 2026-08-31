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

using MdcAi.ChatCore.Jobs;
using MdcAi.ChatCore.Process;
using MdcAi.ChatCore.Security;
using MdcAi.ChatCore.Tools;
using MdcAi.ChatCore.Tools.BuiltIn;

/// <summary>
/// P3-02 job-backed PowerShell: short commands take the fast path and return a terminal result;
/// long commands return status:"running" + job_id, produce incremental output through the job
/// buffer, and get_job/stop_job complete the loop with ownership enforced.
/// </summary>
public class JobBackedPowerShellTests
{
    private static ChatToolExecutionContext Ctx(string convo = "c1", IBackgroundJobService jobService = null, string turnId = "turn-1") =>
        new(convo, turnId, 1, "call_1", null, new WorkspaceReadObservationSet(), null, jobService);

    private static ChatProcessResult OkResult(string stdout, string stderr = "", int exitCode = 0) =>
        new(exitCode, stdout, stderr, TimeSpan.FromMilliseconds(100), false, false,
            System.Text.Encoding.UTF8.GetByteCount(stdout), System.Text.Encoding.UTF8.GetByteCount(stderr), false, false);

    [Fact]
    public async Task Short_command_fast_path_returns_terminal_result()
    {
        using var svc = new BackgroundJobService(new JobServiceOptions(FastPathWaitMs: 300));
        var runner = new RecordingRunner(r => OkResult("fast output"));
        var tool = new RunPowerShellChatTool(runner, () => @"C:\pwsh\pwsh.exe", fastPathWaitMs: 300);

        var result = await tool.ExecuteAsync(
            JObject.Parse("""{"script":"Write-Output 'fast output'"}"""),
            Ctx(jobService: svc), CancellationToken.None);

        Assert.True(result.Ok);
        Assert.Equal("completed", (string)result.Value["status"]);
        Assert.Contains("fast output", result.ModelContent);
        // Script went via stdin, not the command line.
        Assert.Equal("Write-Output 'fast output'", runner.LastRequest.StandardInputScript);
    }

    [Fact]
    public async Task Long_command_returns_running_job_id_and_polls_to_terminal()
    {
        using var svc = new BackgroundJobService(new JobServiceOptions(FastPathWaitMs: 50));
        var runner = new RecordingRunner(async (r, ct) =>
        {
            // Simulate a slow command that streams chunks.
            foreach (var chunk in new[] { "step1\n", "step2\n", "done\n" })
            {
                r.OnStdoutChunk?.Invoke(chunk, ct);
                await Task.Delay(80, ct);
            }

            return OkResult("done");
        });
        var tool = new RunPowerShellChatTool(runner, () => @"C:\pwsh\pwsh.exe", fastPathWaitMs: 50);

        var start = await tool.ExecuteAsync(
            JObject.Parse("""{"script":"Start-Sleep 1; 'done'"}"""),
            Ctx(jobService: svc), CancellationToken.None);

        // Fast path elapsed - still running (a normal result with status:"running").
        Assert.True(start.Ok);
        Assert.Equal("running", (string)start.Value["status"]);
        var jobId = (string)start.Value["job_id"];
        Assert.NotNull(jobId);

        // Poll with the get_job tool.
        var getJob = new GetJobChatTool();
        var poll1 = await getJob.ExecuteAsync(
            JObject.Parse($$"""{"job_id":"{{jobId}}","cursor":0,"wait_ms":100}"""),
            Ctx(jobService: svc), CancellationToken.None);
        Assert.Equal("running", (string)poll1.Value["status"]);

        // Keep polling to terminal, accumulating chunks via cursors.
        long cursor = (long)poll1.Value["next_cursor"];
        var accumulated = (string)poll1.Value["new_output"];
        JObject final = null;
        for (var i = 0; i < 50; i++)
        {
            var poll = await getJob.ExecuteAsync(
                JObject.Parse($$"""{"job_id":"{{jobId}}","cursor":{{cursor}},"wait_ms":100}"""),
                Ctx(jobService: svc), CancellationToken.None);
            accumulated += (string)poll.Value["new_output"];
            cursor = (long)poll.Value["next_cursor"];

            if ((string)poll.Value["status"] != "running")
            {
                final = (JObject)poll.Value;
                break;
            }
        }

        Assert.NotNull(final);
        Assert.Equal("completed", (string)final["status"]);
        Assert.Contains("step1", accumulated);
        Assert.Contains("step2", accumulated);
        Assert.Contains("done", accumulated);
    }

    [Fact]
    public async Task Stop_job_requires_ownership_and_kills()
    {
        using var svc = new BackgroundJobService(new JobServiceOptions(FastPathWaitMs: 50));
        var runner = new RecordingRunner(async (r, ct) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, ct);
            return OkResult("never finishes");
        });
        var tool = new RunPowerShellChatTool(runner, () => @"C:\pwsh\pwsh.exe", fastPathWaitMs: 50);

        var start = await tool.ExecuteAsync(
            JObject.Parse("""{"script":"Start-Sleep 999"}"""),
            Ctx(jobService: svc), CancellationToken.None);
        var jobId = (string)start.Value["job_id"];

        var stopJob = new StopJobChatTool();

        // Foreign conversation cannot stop it.
        var foreign = await stopJob.ExecuteAsync(
            JObject.Parse($$"""{"job_id":"{{jobId}}"}"""),
            Ctx(convo: "other", jobService: svc), CancellationToken.None);
        Assert.Equal("job_ownership_mismatch", foreign.ErrorCode);

        // Owner stops it -> killed.
        var stopped = await stopJob.ExecuteAsync(
            JObject.Parse($$"""{"job_id":"{{jobId}}"}"""),
            Ctx(jobService: svc), CancellationToken.None);
        Assert.True(stopped.Ok);
        Assert.Equal("killed", (string)stopped.Value["status"]);
    }

    [Fact]
    public async Task Nonzero_exit_is_a_completed_failure_result_via_jobs()
    {
        using var svc = new BackgroundJobService(new JobServiceOptions(FastPathWaitMs: 300));
        var runner = new RecordingRunner(r => new ChatProcessResult(
            1, "", "boom", TimeSpan.FromMilliseconds(50), false, false, 0, 5, false, false));
        var tool = new RunPowerShellChatTool(runner, () => @"C:\pwsh\pwsh.exe", fastPathWaitMs: 300);

        var result = await tool.ExecuteAsync(
            JObject.Parse("""{"script":"throw 'boom'"}"""),
            Ctx(jobService: svc), CancellationToken.None);

        Assert.False(result.Ok);
        Assert.Equal("nonzero_exit", result.ErrorCode);
        Assert.Equal("completed", (string)result.Value["status"]);
        Assert.Equal(1, (int)result.Value["exit_code"]);
    }

    [Fact]
    public async Task Pwsh_unavailable_is_clear_and_no_job_is_started()
    {
        using var svc = new BackgroundJobService();
        var tool = new RunPowerShellChatTool(new RecordingRunner(_ => OkResult("x")), () => null);

        var result = await tool.ExecuteAsync(
            JObject.Parse("""{"script":"Get-Date"}"""),
            Ctx(jobService: svc), CancellationToken.None);

        Assert.Equal("pwsh_unavailable", result.ErrorCode);
    }

    private sealed class RecordingRunner : IChatProcessRunner
    {
        private readonly Func<ChatProcessRequest, CancellationToken, Task<ChatProcessResult>> _impl;

        public ChatProcessRequest LastRequest { get; private set; }

        public RecordingRunner(Func<ChatProcessRequest, ChatProcessResult> impl)
            : this((r, _) => Task.FromResult(impl(r))) { }

        public RecordingRunner(Func<ChatProcessRequest, CancellationToken, Task<ChatProcessResult>> impl)
        {
            _impl = impl;
        }

        public async ValueTask<ChatProcessResult> RunAsync(ChatProcessRequest request, CancellationToken ct)
        {
            LastRequest = request;
            return await _impl(request, ct);
        }
    }
}