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
/// Durable-visible status vocabulary for one background job (DSH proposal §8.1).
/// "stopping" is the in-between state after Stop was requested and before the worker drained.
/// </summary>
public static class BackgroundJobStatus
{
    public const string Running = "running";
    public const string Stopping = "stopping";
    public const string Completed = "completed";
    public const string Killed = "killed";
    public const string Failed = "failed";
}

/// <summary>
/// How to start a background job. The work function pushes UTF-8 text into the output writer
/// and is expected to observe cancellation - the service kills/spills as needed.
/// </summary>
public sealed record BackgroundJobStartRequest(
    string Kind,
    string OwnerConversationId,
    string OwnerTurnId,
    string ToolCallId,
    Func<BackgroundJobOutputWriter, CancellationToken, Task> Work,
    int MaxRetainedOutputBytes = 96 * 1024);

/// <summary>Result of starting a job; always succeeds or surfaces an immediate start failure.</summary>
public sealed record BackgroundJobStartResult(
    string JobId,
    BackgroundJobRecord Record);

/// <summary>
/// The full, current snapshot of a job. Status is authoritative; the UI may render a
/// non-consuming snapshot while the model polls with its own cursor.
/// </summary>
public sealed record BackgroundJobRecord(
    string JobId,
    string Kind,
    string OwnerConversationId,
    string OwnerTurnId,
    string ToolCallId,
    DateTime StartedUtc,
    DateTime? EndedUtc,
    string Status,
    int? ExitCode,
    string FailureSummary,
    long TotalOutputBytes,
    bool OutputTruncated,
    string ArtifactId)
{
    public bool IsTerminal => Status is BackgroundJobStatus.Completed or BackgroundJobStatus.Killed or BackgroundJobStatus.Failed;
    public TimeSpan? Duration => EndedUtc.HasValue ? EndedUtc - StartedUtc : null;
}

/// <summary>
/// One model-facing poll: returns ONLY the new output since the cursor plus the next cursor.
/// Consuming semantics - the same output is never repeated to the model.
/// </summary>
public sealed record BackgroundJobPollResult(
    BackgroundJobRecord Record,
    string NewOutput,
    long NextCursor)
{
    /// <summary>True when the job reached a terminal state (completed/killed/failed).</summary>
    public bool IsTerminal => Record.IsTerminal;
}

/// <summary>
/// The output sink a worker pushes text into. Bound by a ring buffer; a larger retained
/// artifact is optional and surfaced via ArtifactId on the terminal record. A worker may also
/// record the process exit code so the terminal record carries it (PowerShell's nonzero exit).
/// </summary>
public sealed class BackgroundJobOutputWriter
{
    private readonly BackgroundJobOutputBuffer _buffer;

    internal BackgroundJobOutputWriter(BackgroundJobOutputBuffer buffer) => _buffer = buffer;

    public ValueTask WriteAsync(string text, CancellationToken ct) => _buffer.WriteAsync(text, ct);

    /// <summary>Nominal exit code the worker observed; carried onto the terminal record.</summary>
    public int? ExitCode { get; private set; }

    /// <summary>Record the process exit code (null = not set by the worker).</summary>
    public void SetExitCode(int? exitCode) => ExitCode = exitCode;
}

/// <summary>
/// Ownership-checked, bounded background-job service (DSH proposal §8.1): per-conversation and
/// app-wide caps, first-terminal-result-wins, one consuming output cursor per model poll, and
/// cancellation of owned jobs on shutdown. Testable with scripted work functions.
/// </summary>
public interface IBackgroundJobService : IDisposable
{
    Task<BackgroundJobStartResult> StartAsync(BackgroundJobStartRequest request, CancellationToken ct);

    /// <summary>Model-facing poll: new output since the cursor + next cursor; boundedly waits for progress.</summary>
    Task<BackgroundJobPollResult> PollAsync(string jobId, string ownerConversationId, long? fromCursor, int? waitMs, CancellationToken ct);

    /// <summary>Ownership-checked terminal snapshot; requesting stop first is optional.</summary>
    Task<BackgroundJobRecord> GetAsync(string jobId, string ownerConversationId, CancellationToken ct);

    /// <summary>Ownership-checked stop: kills the worker, lets it drain, returns the terminal snapshot.</summary>
    Task<BackgroundJobRecord> StopAsync(string jobId, string ownerConversationId, CancellationToken ct);

    /// <summary>Stops every job owned by a conversation (conversation disposal).</summary>
    Task StopConversationJobsAsync(string ownerConversationId, CancellationToken ct);

    /// <summary>Stops every job (app shutdown).</summary>
    Task ShutdownAsync(CancellationToken ct);
}