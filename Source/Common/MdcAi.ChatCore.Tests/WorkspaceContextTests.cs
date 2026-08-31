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

using MdcAi.ChatCore.Context;

/// <summary>
/// P3-07: AGENTS.md discovery walks from the working dir toward the workspace root with bounded
/// size/count; a changed file yields a new snapshot hash; summary validity is driven by the
/// covered branch source hash; framed context is provenance-tagged.
/// </summary>
public class WorkspaceContextTests : IDisposable
{
    private readonly string _root;

    public WorkspaceContextTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "mdcai-ctx-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); }
        catch { }
    }

    private void Write(string relative, string content)
    {
        var full = Path.Combine(_root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(full));
        File.WriteAllText(full, content);
    }

    [Fact]
    public void Discovers_agents_md_walking_up_from_working_dir()
    {
        Write("AGENTS.md", "# Root instructions\nAlways read before editing.");
        Write("src/AGENTS.md", "# Nested instructions\nOnly for src.");

        var svc = new WorkspaceContextDiscoveryService();
        var found = svc.Discover(_root, "src");

        Assert.Equal(2, found.Count);
        // The nested search result (from the working dir) comes first, then the root one.
        Assert.Equal("src/AGENTS.md", found[0].SourcePath);
        Assert.Equal("AGENTS.md", found[1].SourcePath);
        Assert.False(string.IsNullOrEmpty(found[0].ContentHash));
        Assert.Contains("Nested instructions", found[0].Content);
    }

    [Fact]
    public void Discover_from_root_when_working_dir_outside()
    {
        Write("AGENTS.md", "# Root only");

        var svc = new WorkspaceContextDiscoveryService();
        var found = svc.Discover(_root, @"..\..\elsewhere");

        var single = Assert.Single(found);
        Assert.Equal("AGENTS.md", single.SourcePath);
    }

    [Fact]
    public void Large_file_is_bounded_and_marked_truncated()
    {
        Write("AGENTS.md", new string('z', 100 * 1024));

        var svc = new WorkspaceContextDiscoveryService();
        var snapshot = Assert.Single(svc.Discover(_root, "."));

        Assert.True(snapshot.Truncated);
        Assert.True(snapshot.Content.Length <= WorkspaceContextDiscoveryService.MaxInstructionsBytes);
    }

    [Fact]
    public void Changed_file_produces_a_new_hash()
    {
        Write("AGENTS.md", "v1");
        var svc = new WorkspaceContextDiscoveryService();
        var first = Assert.Single(svc.Discover(_root, "."));

        Write("AGENTS.md", "v2 changed");
        var second = Assert.Single(svc.Discover(_root, "."));

        Assert.NotEqual(first.ContentHash, second.ContentHash);
        Assert.Equal("v2 changed", second.Content.Trim());
    }

    [Fact]
    public void Frame_marks_context_as_untrusted_provenance()
    {
        var framed = WorkspaceContextDiscoveryService.Frame("workspace-instructions", "AGENTS.md", "abc123", "read-only data");

        Assert.Contains("<mdcai-context source=\"workspace-instructions\" path=\"AGENTS.md\" sha256=\"abc123\">", framed);
        Assert.Contains("read-only data", framed);
        Assert.EndsWith("</mdcai-context>", framed.TrimEnd());
    }

    [Fact]
    public void Summary_validity_tracks_the_covered_branch_hash()
    {
        // A summary is valid ONLY while the covered fork's source hash matches.
        Assert.True(SummaryValidity.IsValid("hashA", "hashA"));
        Assert.False(SummaryValidity.IsValid("hashA", "hashB"));
        Assert.False(SummaryValidity.IsValid(null, "hashB"));
        Assert.False(SummaryValidity.IsValid("", "hashB"));
    }
}