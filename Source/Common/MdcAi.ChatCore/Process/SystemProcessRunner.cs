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

using System.Diagnostics;
using System.Text;

/// <summary>
/// Real process runner: drafts stdout/stderr CONCURRENTLY (no pipe deadlocks), bounds retained
/// bytes with an artifact spill note, and on timeout/cancellation kills the entire process tree
/// then awaits stream drains.
/// </summary>
public sealed class SystemProcessRunner : IChatProcessRunner
{
    public async ValueTask<ChatProcessResult> RunAsync(ChatProcessRequest request, CancellationToken ct)
    {
        var psi = new ProcessStartInfo
        {
            FileName = request.FilePath,
            WorkingDirectory = request.WorkingDirectory ?? Environment.CurrentDirectory,
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        foreach (var arg in request.Arguments)
            psi.ArgumentList.Add(arg);

        var started = DateTime.UtcNow;
        using var process = new Process { StartInfo = psi };

        try
        {
            process.Start();

            // Feed the script through stdin, then close it so the child sees EOF.
            if (!string.IsNullOrEmpty(request.StandardInputScript))
            {
                await process.StandardInput.WriteAsync(request.StandardInputScript.AsMemory(), ct);
                process.StandardInput.Close();
            }
            else
            {
                process.StandardInput.Close();
            }

            var stdout = new BoundedBuffer(request.MaxRetainedOutputBytes);
            var stderr = new BoundedBuffer(request.MaxRetainedOutputBytes);

            var drainOut = DrainAsync(process.StandardOutput, stdout, ct);
            var drainErr = DrainAsync(process.StandardError, stderr, ct);

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            if (request.TimeoutMs is { } ms)
                timeoutCts.CancelAfter(ms);

            try
            {
                await process.WaitForExitAsync(timeoutCts.Token);
            }
            catch (OperationCanceledException)
            {
                KillTree(process);
                await SafeAwaitBothAsync(drainOut, drainErr);
                var timedOut = !ct.IsCancellationRequested;
                return new ChatProcessResult(
                    -1, stdout.ToString(), stderr.ToString(), DateTime.UtcNow - started,
                    timedOut, !timedOut,
                    stdout.TotalBytes, stderr.TotalBytes, stdout.Truncated, stderr.Truncated);
            }

            // Ensure both drains have read everything the child produced before exiting.
            await SafeAwaitBothAsync(drainOut, drainErr);

            return new ChatProcessResult(
                process.ExitCode, stdout.ToString(), stderr.ToString(), DateTime.UtcNow - started,
                false, false,
                stdout.TotalBytes, stderr.TotalBytes, stdout.Truncated, stderr.Truncated);
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return new ChatProcessResult(
                -1, string.Empty, $"Failed to start '{request.FilePath}': {ex.Message}",
                DateTime.UtcNow - started, false, false, 0, 0, false, false);
        }
    }

    private static async Task DrainAsync(StreamReader reader, BoundedBuffer buffer, CancellationToken ct)
    {
        var chunk = new char[4096];
        while (true)
        {
            var read = await reader.ReadAsync(chunk.AsMemory(), ct);
            if (read == 0)
                return;
            buffer.Append(chunk, read);
        }
    }

    private static async Task SafeAwaitBothAsync(Task a, Task b)
    {
        try { await a; } catch { /* drain cancelled with the process */ }
        try { await b; } catch { /* drain cancelled with the process */ }
    }

    private static void KillTree(Process process)
    {
        try
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException)
        {
            // already exited
        }
    }

    private sealed class BoundedBuffer
    {
        private readonly StringBuilder _builder = new();
        private readonly int _maxBytes;

        public long TotalBytes { get; private set; }
        public bool Truncated { get; private set; }

        public BoundedBuffer(int maxBytes) => _maxBytes = maxBytes;

        public void Append(char[] chunk, int length)
        {
            TotalBytes += length;
            if (_builder.Length >= _maxBytes)
            {
                Truncated = true;
                return;
            }

            var room = _maxBytes - _builder.Length;
            var take = Math.Min(room, length);
            _builder.Append(chunk, 0, take);
            if (take < length)
                Truncated = true;
        }

        public override string ToString() => _builder.ToString();
    }
}