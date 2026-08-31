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

namespace MdcAi.ChatCore.Process;

/// <summary>
/// A bounded, cancellable process invocation (DSH proposal §6.3 run_powershell). The tool layer
/// depends on this interface so tests use a fake; the real implementation spawns pwsh with
/// redirected stdio, drains stdout/stderr concurrently, bounds retained output, and kills the
/// whole process tree on timeout/cancellation.
/// </summary>
public sealed record ChatProcessRequest(
    string FilePath,
    IReadOnlyList<string> Arguments,
    string WorkingDirectory,
    string StandardInputScript,
    int MaxRetainedOutputBytes = 96 * 1024,
    int? TimeoutMs = null,
    Action<string, CancellationToken> OnStdoutChunk = null,
    Action<string, CancellationToken> OnStderrChunk = null);

public sealed record ChatProcessResult(
    int ExitCode,
    string Stdout,
    string Stderr,
    TimeSpan Duration,
    bool TimedOut,
    bool Cancelled,
    long TotalStdoutBytes,
    long TotalStderrBytes,
    bool StdoutTruncated,
    bool StderrTruncated,
    string ArtifactId = null)
{
    public bool RanToCompletion => !TimedOut && !Cancelled;
    public bool Success => RanToCompletion && ExitCode == 0;
}

public interface IChatProcessRunner
{
    ValueTask<ChatProcessResult> RunAsync(ChatProcessRequest request, CancellationToken ct);
}