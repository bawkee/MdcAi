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

namespace MdcAi.ChatCore.Security;

/// <summary>
/// The workspace boundary for file tools (DSH proposal §6.4). Canonicalizes with
/// Path.GetFullPath, compares with Path.GetRelativePath, rejects rooted/UNC/device arguments
/// when the contract expects relative paths, and inspects existing ancestors for
/// symlinks/junctions so a workspace-relative path cannot escape through a reparse point.
/// This protects FILE tools only - PowerShell is outside it and must be described honestly.
/// </summary>
public sealed class WorkspacePathGuard
{
    private readonly string _workspaceRoot;

    public WorkspacePathGuard(string workspacePath)
    {
        _workspaceRoot = Canonicalize(workspacePath ?? throw new ArgumentNullException(nameof(workspacePath)));
    }

    public string WorkspaceRoot => _workspaceRoot;

    /// <summary>Full, normalized path without a trailing separator.</summary>
    public static string Canonicalize(string path)
    {
        var full = Path.GetFullPath(path);
        return full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    public static bool IsUncOrDevicePath(string path) =>
        path.StartsWith(@"\\", StringComparison.Ordinal) ||
        path.StartsWith(@"//", StringComparison.Ordinal) ||
        path.StartsWith(@"\\\\?\", StringComparison.Ordinal) ||
        path.StartsWith("\\\\.\\", StringComparison.Ordinal);

    /// <summary>
    /// Resolves a tool-supplied (expected-relative) path against the workspace. Returns the
    /// canonical absolute path when allowed, or null with a stable rejection code otherwise.
    /// </summary>
    public string TryResolveRelative(string relativePath, out string rejection)
    {
        rejection = null;

        if (string.IsNullOrWhiteSpace(relativePath))
        {
            rejection = "path_required";
            return null;
        }

        if (Path.IsPathRooted(relativePath) || (!string.IsNullOrEmpty(relativePath) &&
            (relativePath[0] == '/' || relativePath[0] == '\\')))
        {
            // UNC/device paths are rooted too, but deserve their own message (they imply a
            // different machine/namespace rather than a plain absolute path).
            if (IsUncOrDevicePath(relativePath))
            {
                rejection = "unc_device_path_not_allowed";
                return null;
            }

            rejection = "rooted_path_not_allowed";
            return null;
        }

        string candidate;
        try
        {
            candidate = Path.GetFullPath(Path.Combine(_workspaceRoot, relativePath));
        }
        catch (Exception ex) when (ex is ArgumentException or PathTooLongException or NotSupportedException)
        {
            rejection = "invalid_path";
            return null;
        }

        var rel = Path.GetRelativePath(_workspaceRoot, candidate);
        if (Path.IsPathRooted(rel) || rel == ".." || rel.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal))
        {
            rejection = "outside_workspace";
            return null;
        }

        // For a NEW file the nearest existing ancestor is what must be inside the boundary;
        // for an existing path that's the path itself. Refuse anything whose nearest existing
        // ancestor chain crosses a reparse point (junction/symlink) - that's an escape vector.
        var nearestExisting = FindNearestExistingAncestor(candidate);
        if (nearestExisting != null && HasReparsePointAncestor(nearestExisting))
        {
            rejection = "reparse_point_escape";
            return null;
        }

        return Canonicalize(candidate);
    }

    /// <summary>The nearest existing path walking up from a (possibly not-yet-created) target.</summary>
    private static string FindNearestExistingAncestor(string candidate)
    {
        var current = candidate;
        while (true)
        {
            if (Directory.Exists(current) || File.Exists(current))
                return current;

            var parent = Path.GetDirectoryName(current);
            if (string.IsNullOrEmpty(parent) || parent == current)
                return null;

            current = parent;
        }
    }

    /// <summary>True when the path itself or any ancestor up to the drive root is a reparse point.</summary>
    private static bool HasReparsePointAncestor(string path)
    {
        var current = path;
        var seen = 0;
        while (current != null && seen++ < 64)
        {
            if (IsReparsePoint(current))
                return true;

            var parent = Path.GetDirectoryName(current);
            if (string.IsNullOrEmpty(parent) || parent == current)
                return false;

            current = parent;
        }

        return false;
    }

    private static bool IsReparsePoint(string path)
    {
        try
        {
            var info = new DirectoryInfo(path);
            if (info.LinkTarget != null)
                return true;

            var attrs = File.GetAttributes(path);
            return (attrs & FileAttributes.ReparsePoint) != 0;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return false;
        }
    }
}