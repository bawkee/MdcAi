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

using System.Text;

/// <summary>
/// Bounded in-memory output buffer (DSH proposal §8.1). Keeps TotalBytes always accurate,
/// retains at most <c>maxBytes</c> of the TAIL, and marks Truncated once anything was spilled.
/// Supports one consuming cursor (the model's last-read position - consumed output is never
/// repeated) and non-consuming snapshots (UI). Not thread-safe against concurrent writers; the
/// job service serializes writes per job.
/// </summary>
public sealed class BackgroundJobOutputBuffer
{
    private readonly StringBuilder _tail = new();
    private readonly long _maxBytes;

    /// <summary>Total UTF-8 bytes ever written (before cap).</summary>
    public long TotalBytes { get; private set; }

    /// <summary>Total characters ever written - the unit the consuming cursor counts in.</summary>
    public long TotalChars { get; private set; }

    /// <summary>True once any bytes were spilled beyond the retained cap.</summary>
    public bool Truncated { get; private set; }

    public BackgroundJobOutputBuffer(long maxBytes)
    {
        _maxBytes = Math.Max(1, maxBytes);
    }

    public ValueTask WriteAsync(string text, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        if (string.IsNullOrEmpty(text))
            return ValueTask.CompletedTask;

        TotalBytes += Encoding.UTF8.GetByteCount(text);
        TotalChars += text.Length;

        _tail.Append(text);

        // Keep the retained tail within the byte cap (approximated by char count to avoid
        // per-write encoding; the cap is a memory guard, exactness matters only for spill notes).
        var maxChars = Math.Max(8, _maxBytes);
        if (_tail.Length > maxChars)
        {
            Truncated = true;
            _tail.Remove(0, (int)(_tail.Length - maxChars));
        }

        return ValueTask.CompletedTask;
    }

    /// <summary>Non-consuming snapshot of the retained tail (UI replay).</summary>
    public string Snapshot() => _tail.ToString();

    /// <summary>
    /// Consuming read: returns the retained tail AFTER the cursor and advances the cursor to the
    /// end. If the consumed part was already spilled by the ring buffer, the whole retained tail
    /// is returned (the terminal record's Truncated flag already told the model).
    /// </summary>
    public (string Text, long NextCursor) ReadSince(long cursor)
    {
        var tail = _tail.ToString();
        if (TotalChars == 0 || cursor >= TotalChars)
            return ("", TotalChars);

        var tailStartChar = TotalChars - tail.Length;
        var offset = (int)Math.Max(0, cursor - tailStartChar);
        var text = tail[Math.Min(offset, tail.Length)..];

        return (text, TotalChars);
    }
}