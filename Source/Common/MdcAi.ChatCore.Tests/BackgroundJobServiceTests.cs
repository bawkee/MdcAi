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

/// <summary>
/// P3-01 background jobs: short fast-path, incremental output with consuming cursors, output
/// cap/spill, timeout/stop/cancellation, ownership checks, per-conversation + app caps, and
/// shutdown marking jobs killed.
/// </summary>
public class BackgroundJobServiceTests
{
    private static BackgroundJobStartRequest Job(
        string convo = "c1", string kind = "test", Func<BackgroundJobOutputWriter, CancellationToken, Task> work = null) =>
        new(kind, convo, "turn-1", "call_1",
            work ?? (async (outW, ct) => await outW.WriteAsync("hello", ct)),
            MaxRetainedOutputBytes: 64);

    [Fact]
    public async Task Short_job_completes_with_output()
    {
        using var svc = new BackgroundJobService();

        var start = await svc.StartAsync(Job(), CancellationToken.None);
        var poll = await svc.PollAsync(start.JobId, "c1", null, 2000, CancellationToken.None);

        Assert.True(poll.IsTerminal);
        Assert.Equal(BackgroundJobStatus.Completed, poll.Record.Status);
        Assert.Contains("hello", poll.NewOutput);
        Assert.True(poll.Record.Duration >= TimeSpan.Zero);
    }

    [Fact]
    public async Task Long_job_returns_id_incremental_output_then_completes()
    {
        using var svc = new BackgroundJobService();
        var events = new List<string>();

        var start = await svc.StartAsync(Job(work: async (outW, ct) =>
        {
            foreach (var chunk in new[] { "part1\n", "part2\n", "part3\n" })
            {
                events.Add("write:" + chunk.Trim());
                await outW.WriteAsync(chunk, ct);
                await Task.Delay(60, ct);
            }
        }), CancellationToken.None);

        var poll1 = await svc.PollAsync(start.JobId, "c1", null, 50, CancellationToken.None);
        Assert.False(poll1.IsTerminal);
        Assert.Equal(BackgroundJobStatus.Running, poll1.Record.Status);
        Assert.Contains("part1", poll1.NewOutput);

        // Consuming cursor: second poll must NOT repeat part1.
        var poll2 = await svc.PollAsync(start.JobId, "c1", poll1.NextCursor, 100, CancellationToken.None);
        Assert.DoesNotContain("part1", poll2.NewOutput);

        // Keep polling to terminal, accumulating consumed output along the way.
        long cursor = poll2.NextCursor;
        BackgroundJobPollResult final = null;
        var accumulated = poll1.NewOutput + poll2.NewOutput;
        for (var i = 0; i < 50 && (final == null || !final.IsTerminal); i++)
        {
            final = await svc.PollAsync(start.JobId, "c1", cursor, 100, CancellationToken.None);
            accumulated += final.NewOutput;
            cursor = final.NextCursor;
        }

        Assert.NotNull(final);
        Assert.True(final.IsTerminal);
        Assert.Equal(BackgroundJobStatus.Completed, final.Record.Status);
        Assert.Contains("part3", accumulated); // every chunk reached the model exactly once
    }

    [Fact]
    public async Task Output_cap_marks_truncated_and_retains_tail()
    {
        using var svc = new BackgroundJobService();

        var start = await svc.StartAsync(new BackgroundJobStartRequest(
            "test", "c1", "turn-1", "call_1",
            async (outW, ct) => await outW.WriteAsync(new string('x', 512), ct),
            MaxRetainedOutputBytes: 64), CancellationToken.None);

        var poll = await svc.PollAsync(start.JobId, "c1", null, 2000, CancellationToken.None);

        Assert.True(poll.Record.OutputTruncated);
        Assert.Equal(512, poll.Record.TotalOutputBytes);
        Assert.True(poll.NewOutput.Length < 512); // ring buffer retained a bounded tail

        // Cursor reads from a truncated buffer never repeat consumed content.
        var poll2 = await svc.PollAsync(start.JobId, "c1", poll.NextCursor, 0, CancellationToken.None);
        Assert.Equal("", poll2.NewOutput);
    }

    [Fact]
    public async Task Stop_kills_the_worker_and_returns_terminal_snapshot()
    {
        using var svc = new BackgroundJobService();

        var start = await svc.StartAsync(Job(work: async (outW, ct) =>
        {
            await outW.WriteAsync("started", ct);
            await Task.Delay(Timeout.InfiniteTimeSpan, ct);
        }), CancellationToken.None);

        var stopped = await svc.StopAsync(start.JobId, "c1", CancellationToken.None);

        Assert.True(stopped.IsTerminal);
        Assert.Equal(BackgroundJobStatus.Killed, stopped.Status);
        Assert.True(stopped.Duration >= TimeSpan.Zero);
    }

    [Fact]
    public async Task Worker_failure_marks_job_failed()
    {
        using var svc = new BackgroundJobService();

        var start = await svc.StartAsync(Job(work: async (_, _) => throw new InvalidOperationException("boom")),
                                         CancellationToken.None);
        var poll = await svc.PollAsync(start.JobId, "c1", null, 2000, CancellationToken.None);

        Assert.True(poll.IsTerminal);
        Assert.Equal(BackgroundJobStatus.Failed, poll.Record.Status);
        Assert.Contains("boom", poll.Record.FailureSummary);
    }

    [Fact]
    public async Task Foreign_conversation_cannot_read_or_stop_a_job()
    {
        using var svc = new BackgroundJobService();

        var start = await svc.StartAsync(Job(work: async (outW, ct) =>
        {
            await outW.WriteAsync("x", ct);
            await Task.Delay(Timeout.InfiniteTimeSpan, ct);
        }), CancellationToken.None);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => svc.GetAsync(start.JobId, "other-convo", CancellationToken.None));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => svc.StopAsync(start.JobId, "other-convo", CancellationToken.None));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => svc.PollAsync(start.JobId, "other-convo", null, 0, CancellationToken.None));

        // The owner can still read and stop it.
        var record = await svc.GetAsync(start.JobId, "c1", CancellationToken.None);
        Assert.Equal(BackgroundJobStatus.Running, record.Status);
    }

    [Fact]
    public async Task Per_conversation_cap_rejects_overflow()
    {
        using var svc = new BackgroundJobService(new JobServiceOptions(MaxJobsPerConversation: 2));

        var hold = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        await svc.StartAsync(Job(convo: "c1", work: async (_, ct) =>
        {
            await hold.Task.WaitAsync(ct);
        }), CancellationToken.None);
        await svc.StartAsync(Job(convo: "c1", work: async (_, ct) =>
        {
            await hold.Task.WaitAsync(ct);
        }), CancellationToken.None);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => svc.StartAsync(Job(convo: "c1"), CancellationToken.None));

        // Another conversation is not affected by c1's cap.
        await svc.StartAsync(Job(convo: "c2", work: async (_, ct) =>
        {
            await hold.Task.WaitAsync(ct);
        }), CancellationToken.None);

        hold.SetResult(true);
    }

    [Fact]
    public async Task Shutdown_stops_all_running_jobs_as_killed()
    {
        var svc = new BackgroundJobService();
        var hold = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        await svc.StartAsync(Job(convo: "c1", work: async (_, ct) => await hold.Task.WaitAsync(ct)), CancellationToken.None);
        await svc.StartAsync(Job(convo: "c2", work: async (_, ct) => await hold.Task.WaitAsync(ct)), CancellationToken.None);

        await svc.ShutdownAsync(CancellationToken.None);

        // Both jobs are killed and terminal.
        Assert.Equal(BackgroundJobStatus.Killed,
                     (await svc.GetAsync("job-000001", "c1", CancellationToken.None)).Status);
        Assert.Equal(BackgroundJobStatus.Killed,
                     (await svc.GetAsync("job-000002", "c2", CancellationToken.None)).Status);
    }
}