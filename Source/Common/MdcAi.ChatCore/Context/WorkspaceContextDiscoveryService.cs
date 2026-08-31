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

namespace MdcAi.ChatCore.Context;

using System.Security.Cryptography;
using System.Text;

/// <summary>
/// Workspace instructions (AGENTS.md etc.) discovery (DSH proposal §8.4): walks from the
/// selected workspace root toward the working directory (bounded), reads files it finds, and
/// reports them as snapshots with hash + bounded content. Loading is explicit/visible; the
/// snapshot is persisted so model-visible input is replayable.
/// </summary>
public sealed class WorkspaceContextDiscoveryService
{
    public const int MaxInstructionsBytes = 48 * 1024;
    public const int MaxResultCount = 2;

    /// <summary>One discovered snapshot.</summary>
    public sealed record WorkspaceSnapshot(
        string SourcePath,      // workspace-relative path, forward slashes
        string FullPath,
        string ContentHash,
        string Content,         // bounded model-visible bytes
        bool Truncated);

    public IReadOnlyList<WorkspaceSnapshot> Discover(string workspaceRoot, string workingRelativePath)
    {
        if (string.IsNullOrWhiteSpace(workspaceRoot) || !Directory.Exists(workspaceRoot))
            return Array.Empty<WorkspaceSnapshot>();

        var candidates = new List<WorkspaceSnapshot>();

        // From the working dir (if inside the workspace) up to the root; else from the root.
        var start = workspaceRoot;
        if (!string.IsNullOrEmpty(workingRelativePath))
        {
            var full = Path.GetFullPath(Path.Combine(workspaceRoot, workingRelativePath));
            if (full.StartsWith(workspaceRoot, StringComparison.OrdinalIgnoreCase))
                start = Directory.Exists(full) ? full : Path.GetDirectoryName(full);
        }

        var dir = start;
        var hops = 0;
        while (dir != null && hops++ < 8 && candidates.Count < MaxResultCount)
        {
            foreach (var name in new[] { "AGENTS.md", "AGENTS.txt" })
            {
                var candidate = Path.Combine(dir, name);
                if (!File.Exists(candidate))
                    continue;

                var snapshot = ReadSnapshot(workspaceRoot, candidate);
                if (snapshot != null)
                    candidates.Add(snapshot);

                if (candidates.Count >= MaxResultCount)
                    break;
            }

            var parent = Path.GetDirectoryName(dir);
            if (string.IsNullOrEmpty(parent) || parent == dir)
                break;
            dir = parent;
        }

        return candidates;
    }

    private static WorkspaceSnapshot ReadSnapshot(string root, string fullPath)
    {
        byte[] bytes;
        try
        {
            var info = new FileInfo(fullPath);
            if (info.Length > MaxInstructionsBytes)
            {
                // Read a bounded prefix so the snapshot still reflects reality without unbounded memory.
                using var fs = File.OpenRead(fullPath);
                bytes = new byte[MaxInstructionsBytes];
                var read = fs.Read(bytes, 0, MaxInstructionsBytes);
                Array.Resize(ref bytes, read);
            }
            else
            {
                bytes = File.ReadAllBytes(fullPath);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }

        var hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        var content = Encoding.UTF8.GetString(bytes);
        var relative = Path.GetRelativePath(root, fullPath).Replace('\\', '/');

        return new WorkspaceSnapshot(relative, fullPath, hash, content,
                                     bytes.Length == MaxInstructionsBytes);
    }

    /// <summary>Frames injected context with provenance (DSH proposal §8.4: untrusted data, never authority).</summary>
    public static string Frame(string sourceKind, string relativePath, string sha256, string content)
    {
        var sb = new StringBuilder();
        sb.Append($"<mdcai-context source=\"{sourceKind}\" path=\"{relativePath}\" sha256=\"{sha256}\">\n");
        sb.Append(content);
        sb.Append("\n</mdcai-context>");
        return sb.ToString();
    }
}

/// <summary>
/// Durable summary store seam (DSH proposal §8.4). Adapters persist; validity is driven by the
/// covered fork's source hash - a changed selection invalidates the summary.
/// </summary>
public interface IConversationSummaryStore
{
    Task<string> GetLatestValidAsync(string conversationId, string branchSourceHash, CancellationToken ct);
    Task SaveAsync(string conversationId, string anchor, string through, string sourceHash,
                   string summaryText, string model, string summarizerVersion, CancellationToken ct);
    Task InvalidateAsync(string conversationId, string sourceHash, CancellationToken ct);
}

/// <summary>Pure invalidation rules: a summary is valid only for the exact covered source hash.</summary>
public static class SummaryValidity
{
    public static bool IsValid(string persistedHash, string currentBranchHash) =>
        !string.IsNullOrEmpty(persistedHash)
        && string.Equals(persistedHash, currentBranchHash, StringComparison.Ordinal);
}