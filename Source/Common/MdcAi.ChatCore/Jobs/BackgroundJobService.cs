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

namespace MdcAi.ChatCore.Jobs;

/// <summary>
/// Defaults for the MdcAi job service (DSH proposal §8.1): per-conversation cap of 4 is lower
/// than a platform default; the app-wide cap keeps many conversations from exhausting the box.
/// </summary>
public sealed record JobServiceOptions(
    int MaxJobsPerConversation = 4,
    int MaxJobsAppWide = 64,
    int FastPathWaitMs = 1500)
{
    public static JobServiceOptions Default { get; } = new();
}

/// <summary>
/// Ownership-checked, bounded background-job service. Enforcement:
/// - per-conversation and app-wide concurrent caps (start refuses above them);
/// - first terminal result wins (a race sets the record once);
/// - one consuming output cursor per model poll, non-consuming snapshots for UI;
/// - cancellation stops the worker, terminal record is awaited before returning;
/// - listener/observer failures never affect job state.
/// </summary>
public sealed class BackgroundJobService : IBackgroundJobService
{
    private readonly JobServiceOptions _options;
    private readonly object _gate = new();
    private readonly Dictionary<string, JobRun> _jobs = new(StringComparer.Ordinal);
    private readonly HashSet<string> _stopping = new(StringComparer.Ordinal);
    private long _nextJobNumber;
    private bool _shutdown;

    public BackgroundJobService(JobServiceOptions options = null)
    {
        _options = options ?? JobServiceOptions.Default;
    }

    public Task<BackgroundJobStartResult> StartAsync(BackgroundJobStartRequest request, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (request?.Work == null)
            throw new ArgumentNullException(nameof(request.Work));

        lock (_gate)
        {
            if (_shutdown)
                throw new InvalidOperationException("The job service is shutting down.");

            var convoCount = _jobs.Values.Count(j =>
                j.OwnerConversationId == request.OwnerConversationId && !j.Record.IsTerminal);
            if (convoCount >= _options.MaxJobsPerConversation)
                throw new InvalidOperationException(
                    $"Too many concurrent jobs for this conversation (max {_options.MaxJobsPerConversation}). Wait for one to finish.");

            var totalActive = _jobs.Values.Count(j => !j.Record.IsTerminal);
            if (totalActive >= _options.MaxJobsAppWide)
                throw new InvalidOperationException("Too many concurrent jobs app-wide. Try again later.");

            var jobId = "job-" + Interlocked.Increment(ref _nextJobNumber).ToString("D6");
            var buffer = new BackgroundJobOutputBuffer(request.MaxRetainedOutputBytes);
            var run = new JobRun(jobId, request, buffer);
            _jobs[jobId] = run;

            var startResult = new BackgroundJobStartResult(jobId, run.Record);
            run.Start();

            return Task.FromResult(startResult);
        }
    }

    public async Task<BackgroundJobPollResult> PollAsync(
        string jobId, string ownerConversationId, long? fromCursor, int? waitMs, CancellationToken ct)
    {
        var run = GetOwned(jobId, ownerConversationId);

        var waitUntil = DateTime.UtcNow + TimeSpan.FromMilliseconds(Math.Min(waitMs ?? 0, 30_000));

        while (!ct.IsCancellationRequested)
        {
            var record = run.Record;
            var (text, nextCursor) = run.ReadSince(fromCursor ?? 0);

            if (record.IsTerminal || !string.IsNullOrEmpty(text) || DateTime.UtcNow >= waitUntil || waitMs <= 0)
                return new BackgroundJobPollResult(record, text, nextCursor);

            await Task.Delay(50, ct);
        }

        ct.ThrowIfCancellationRequested();
        throw new InvalidOperationException("unreachable");
    }

    public Task<BackgroundJobRecord> GetAsync(string jobId, string ownerConversationId, CancellationToken ct)
    {
        var run = GetOwned(jobId, ownerConversationId);
        return Task.FromResult(run.Record);
    }

    public async Task<BackgroundJobRecord> StopAsync(string jobId, string ownerConversationId, CancellationToken ct)
    {
        var run = GetOwned(jobId, ownerConversationId);

        lock (_gate)
        {
            if (run.Record.IsTerminal)
                return run.Record;

            _stopping.Add(jobId);
            // The status flip to stopping is authoritative; the worker re-reads it via StopRequested.
            run.MarkStopping();
        }

        await run.StopAndDrainAsync();

        lock (_gate)
        {
            _stopping.Remove(jobId);
            return run.Record;
        }
    }

    public async Task StopConversationJobsAsync(string ownerConversationId, CancellationToken ct)
    {
        string[] owned;
        lock (_gate)
        {
            owned = _jobs.Values
                         .Where(j => j.OwnerConversationId == ownerConversationId && !j.Record.IsTerminal)
                         .Select(j => j.JobId)
                         .ToArray();
        }

        foreach (var jobId in owned)
            await StopAsync(jobId, ownerConversationId, ct);
    }

    public async Task ShutdownAsync(CancellationToken ct)
    {
        string[] all;
        lock (_gate)
        {
            _shutdown = true;
            all = _jobs.Values.Where(j => !j.Record.IsTerminal).Select(j => j.JobId).ToArray();
        }

        foreach (var jobId in all)
        {
            JobRun run;
            lock (_gate)
            {
                run = _jobs.TryGetValue(jobId, out var r) ? r : null;
                if (run == null || run.Record.IsTerminal)
                    continue;
                run.MarkStopping();
            }

            await run.StopAndDrainAsync();
        }
    }

    public void Dispose() => ShutdownAsync(CancellationToken.None).GetAwaiter().GetResult();

    private JobRun GetOwned(string jobId, string ownerConversationId)
    {
        lock (_gate)
        {
            if (!_jobs.TryGetValue(jobId, out var run))
                throw new KeyNotFoundException($"Unknown job '{jobId}'.");

            if (!string.Equals(run.OwnerConversationId, ownerConversationId, StringComparison.Ordinal))
                throw new UnauthorizedAccessException(
                    $"Job '{jobId}' is not owned by conversation '{ownerConversationId}'.");

            return run;
        }
    }

    /// <summary>
    /// One job: owns the worker Task, the buffer, and the authoritative terminal record.
    /// First-terminal-result-wins: the worker sets status exactly once via TryTerminate.
    /// </summary>
    private sealed class JobRun
    {
        private readonly object _lock = new();
        private Task _worker;
        private CancellationTokenSource _cts;
        private BackgroundJobOutputWriter _writer;
        private BackgroundJobRecord _record;

        public string JobId { get; }
        public string OwnerConversationId { get; }

        private readonly string _kind;
        private readonly string _ownerTurnId;
        private readonly string _toolCallId;
        private readonly Func<BackgroundJobOutputWriter, CancellationToken, Task> _work;
        private readonly BackgroundJobOutputBuffer _buffer;

        public JobRun(string jobId, BackgroundJobStartRequest request, BackgroundJobOutputBuffer buffer)
        {
            JobId = jobId;
            OwnerConversationId = request.OwnerConversationId;
            _kind = request.Kind;
            _ownerTurnId = request.OwnerTurnId;
            _toolCallId = request.ToolCallId;
            _work = request.Work;
            _buffer = buffer;
            _record = new BackgroundJobRecord(jobId, _kind, OwnerConversationId, _ownerTurnId, _toolCallId,
                DateTime.UtcNow, null, BackgroundJobStatus.Running, null, null, 0, false, null);
        }

        public BackgroundJobRecord Record
        {
            get { lock (_lock) return _record; }
        }

        public void Start()
        {
            _cts = new CancellationTokenSource();
            _writer = new BackgroundJobOutputWriter(_buffer);
            _worker = RunWorkerAsync();
        }

        private async Task RunWorkerAsync()
        {
            try
            {
                await _work(_writer, _cts.Token);
                TryTerminate(BackgroundJobStatus.Completed, _writer.ExitCode, null);
            }
            catch (OperationCanceledException)
            {
                if (StopRequested())
                    TryTerminate(BackgroundJobStatus.Killed, null, "The job was stopped by the user or the app.");
                else
                    TryTerminate(BackgroundJobStatus.Failed, null, "The job was cancelled.");
            }
            catch (Exception ex)
            {
                TryTerminate(BackgroundJobStatus.Failed, null, ex.Message);
            }
        }

        /// <summary>Set the terminal status only if not already terminal - first terminal result wins.</summary>
        private void TryTerminate(string status, int? exitCode, string failureSummary)
        {
            lock (_lock)
            {
                if (_record.IsTerminal)
                    return;

                _record = _record with
                {
                    Status = status,
                    EndedUtc = DateTime.UtcNow,
                    ExitCode = exitCode,
                    FailureSummary = failureSummary,
                    TotalOutputBytes = _buffer.TotalBytes,
                    OutputTruncated = _buffer.Truncated,
                    ArtifactId = null
                };
            }
        }

        public void MarkStopping()
        {
            lock (_lock)
            {
                if (_record.IsTerminal)
                    return;
                _record = _record with { Status = BackgroundJobStatus.Stopping };
            }

            _cts?.Cancel();
        }

        public bool StopRequested()
        {
            lock (_lock)
                return _record.Status == BackgroundJobStatus.Stopping;
        }

        public async Task StopAndDrainAsync()
        {
            lock (_lock)
            {
                if (_record.IsTerminal && _worker?.IsCompleted == true)
                    return;
            }

            try
            {
                if (_worker != null)
                    await _worker;
            }
            catch
            {
                // the worker's own TryTerminate handled the failure classification
            }
        }

        public (string Text, long NextCursor) ReadSince(long cursor) => _buffer.ReadSince(cursor);
    }
}