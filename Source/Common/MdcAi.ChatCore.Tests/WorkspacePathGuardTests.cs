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

using System.Diagnostics;
using MdcAi.ChatCore.Security;

public class WorkspacePathGuardTests : IDisposable
{
    private readonly string _root;

    public WorkspacePathGuardTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "mdcai-guard-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); }
        catch { /* best-effort cleanup */ }
    }

    private WorkspacePathGuard Guard() => new(_root);

    [Fact]
    public void Resolves_relative_paths_inside_workspace()
    {
        Directory.CreateDirectory(Path.Combine(_root, "sub"));
        File.WriteAllText(Path.Combine(_root, "sub", "a.txt"), "x");

        var guard = Guard();
        var resolved = guard.TryResolveRelative(@"sub\a.txt", out var rejection);

        Assert.Null(rejection);
        Assert.Equal(Path.GetFullPath(Path.Combine(_root, "sub", "a.txt")), resolved);
        Assert.Equal(Path.Combine("sub", "a.txt"), Path.GetRelativePath(_root, resolved));
    }

    [Fact]
    public void Rejects_paths_outside_workspace_via_parent_escape()
    {
        var guard = Guard();

        var resolved = guard.TryResolveRelative(@"..\..\Windows\win.ini", out var rejection);
        Assert.Null(resolved);
        Assert.Equal("outside_workspace", rejection);
    }

    [Fact]
    public void Rejects_rooted_and_unc_device_paths()
    {
        var guard = Guard();

        foreach (var (path, expected) in new[]
                 {
                     (@"C:\temp\a.txt", "rooted_path_not_allowed"),
                     (@"/etc/passwd", "rooted_path_not_allowed"),
                     (@"\\server\share\x", "unc_device_path_not_allowed"),
                     (@"\\?\C:\x", "unc_device_path_not_allowed"),
                     ("", "path_required"),
                     ("   ", "path_required")
                 })
        {
            var resolved = guard.TryResolveRelative(path, out var rejection);
            Assert.Null(resolved);
            Assert.Equal(expected, rejection);
        }
    }

    [Fact]
    public void Rejects_slash_normalized_parent_escape()
    {
        var guard = Guard();

        var resolved = guard.TryResolveRelative("x/../../outside", out var rejection);
        Assert.Null(resolved);
        Assert.Equal("outside_workspace", rejection);
    }

    [Fact]
    public void Rejects_junction_escape_through_a_link_inside_workspace()
    {
        var outside = Path.Combine(Path.GetTempPath(), "mdcai-guard-out-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(outside);
        File.WriteAllText(Path.Combine(outside, "secret.txt"), "secret");

        var junction = Path.Combine(_root, "link");
        if (!TryCreateJunction(junction, outside))
            return; // junction creation unavailable (CI without mklink) - skip

        try
        {
            var guard = Guard();
            var resolved = guard.TryResolveRelative(@"link\secret.txt", out var rejection);
            Assert.Null(resolved);
            Assert.Equal("reparse_point_escape", rejection);
        }
        finally
        {
            try { Directory.Delete(outside, recursive: true); } catch { }
            try { Directory.Delete(junction); } catch { }
        }
    }

    private static bool TryCreateJunction(string junctionPath, string target)
    {
        try
        {
            var psi = new ProcessStartInfo("cmd.exe", $"/c mklink /J \"{junctionPath}\" \"{target}\"")
            {
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            using var p = Process.Start(psi);
            p.WaitForExit(10_000);
            return p.ExitCode == 0 && Directory.Exists(junctionPath);
        }
        catch
        {
            return false;
        }
    }
}