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

using MdcAi.ChatCore.Process;
using MdcAi.ChatCore.Security;
using MdcAi.ChatCore.Tools;
using MdcAi.ChatCore.Tools.BuiltIn;

/// <summary>
/// Read-before-write / stale-hash enforcement for write_file and patch_file (DSH proposal §6.3,
/// P1-09): preconditions fail predictably with NO bytes changed, complete-file mutations advance
/// the observation, partial-range mutations invalidate it, and PowerShell stays behind approval.
/// </summary>
public class BuiltInMutationToolsTests : IDisposable
{
    private readonly string _root;
    private readonly WorkspaceReadObservationSet _readSet = new();

    public BuiltInMutationToolsTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "mdcai-mut-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); }
        catch { /* best-effort cleanup */ }
    }

    private ChatToolExecutionContext Ctx(FakeApprovalService approval = null, int step = 3) =>
        new("c1", "turn-1", step, "call_1", _root, _readSet, approval ?? new FakeApprovalService());

    private async Task ReadWholeFile(string relPath)
    {
        var tool = new ReadFileChatTool();
        var result = await tool.ExecuteAsync(JObject.Parse($$"""{"path":"{{relPath}}"}"""), Ctx(), CancellationToken.None);
        Assert.True(result.Ok, result.ModelContent);
        Assert.NotNull(result.Observation);
        _readSet.Record(result.Observation);
    }

    private string FullPath(string rel) => Path.GetFullPath(Path.Combine(_root, rel));

    [Fact]
    public async Task Write_file_new_file_requires_create_only()
    {
        var tool = new WriteFileChatTool();
        var result = await tool.ExecuteAsync(JObject.Parse("""{"path":"new.txt","content":"hello"}"""),
                                             Ctx(), CancellationToken.None);
        Assert.Equal("create_only_required", result.ErrorCode);
        Assert.False(File.Exists(FullPath("new.txt")));
    }

    [Fact]
    public async Task Write_file_create_only_creates_and_records_observation()
    {
        var tool = new WriteFileChatTool();
        var result = await tool.ExecuteAsync(JObject.Parse("""{"path":"new.txt","content":"hello","create_only":true}"""),
                                             Ctx(), CancellationToken.None);

        Assert.True(result.Ok);
        Assert.StartsWith("created new.txt", result.ModelContent);
        Assert.Equal("hello", await File.ReadAllTextAsync(FullPath("new.txt")));
        Assert.True(_readSet.TryGet(FullPath("new.txt"), out var obs));
        Assert.True(obs.CoveredWholeFile);

        // Second create_only attempt on the existing file fails.
        var second = await tool.ExecuteAsync(JObject.Parse("""{"path":"new.txt","content":"x","create_only":true}"""),
                                             Ctx(), CancellationToken.None);
        Assert.Equal("already_exists", second.ErrorCode);
    }

    [Fact]
    public async Task Write_file_existing_without_read_is_refused_no_bytes_change()
    {
        await File.WriteAllTextAsync(FullPath("a.txt"), "original");
        var tool = new WriteFileChatTool();

        var result = await tool.ExecuteAsync(JObject.Parse("""{"path":"a.txt","content":"replaced"}"""),
                                             Ctx(), CancellationToken.None);

        Assert.Equal("read_required", result.ErrorCode);
        Assert.Equal("original", await File.ReadAllTextAsync(FullPath("a.txt")));
    }

    [Fact]
    public async Task Write_file_partial_observation_cannot_replace_whole_file()
    {
        await File.WriteAllTextAsync(FullPath("a.txt"), string.Join("\n", Enumerable.Range(1, 10).Select(i => $"l{i}")));
        // Only lines 2-4 observed (partial window).
        _readSet.Record(new FileReadObservation(FullPath("a.txt"), HashOf(FullPath("a.txt")),
                                                new FileInfo(FullPath("a.txt")).Length,
                                                File.GetLastWriteTimeUtc(FullPath("a.txt")),
                                                2, 4, CoveredWholeFile: false, 1));

        var tool = new WriteFileChatTool();
        var result = await tool.ExecuteAsync(JObject.Parse("""{"path":"a.txt","content":"replaced"}"""),
                                             Ctx(), CancellationToken.None);

        Assert.Equal("read_range_required", result.ErrorCode);
        Assert.Equal(string.Join("\n", Enumerable.Range(1, 10).Select(i => $"l{i}")),
                     await File.ReadAllTextAsync(FullPath("a.txt")));
    }

    [Fact]
    public async Task Write_file_external_change_after_read_yields_stale_read_no_overwrite()
    {
        await File.WriteAllTextAsync(FullPath("a.txt"), "v1");
        await ReadWholeFile("a.txt");
        await File.WriteAllTextAsync(FullPath("a.txt"), "v2-external");

        var tool = new WriteFileChatTool();
        var result = await tool.ExecuteAsync(JObject.Parse("""{"path":"a.txt","content":"replaced"}"""),
                                             Ctx(), CancellationToken.None);

        Assert.Equal("stale_read", result.ErrorCode);
        Assert.Equal("v2-external", await File.ReadAllTextAsync(FullPath("a.txt")));
    }

    [Fact]
    public async Task Write_file_valid_full_observation_replaces_and_advances_hash()
    {
        await File.WriteAllTextAsync(FullPath("a.txt"), "v1");
        await ReadWholeFile("a.txt");
        var oldHash = HashOf(FullPath("a.txt"));

        var tool = new WriteFileChatTool();
        var result = await tool.ExecuteAsync(JObject.Parse("""{"path":"a.txt","content":"v1 plus more"}"""),
                                             Ctx(), CancellationToken.None);

        Assert.True(result.Ok);
        Assert.Equal("v1 plus more", await File.ReadAllTextAsync(FullPath("a.txt")));
        Assert.True(_readSet.TryGet(FullPath("a.txt"), out var advanced));
        Assert.NotEqual(oldHash, advanced.Sha256);
        Assert.True(advanced.CoveredWholeFile);
    }

    [Fact]
    public async Task Write_file_expected_sha256_mismatch_is_refused()
    {
        await File.WriteAllTextAsync(FullPath("a.txt"), "v1");
        await ReadWholeFile("a.txt");

        var tool = new WriteFileChatTool();
        var result = await tool.ExecuteAsync(
            JObject.Parse("""{"path":"a.txt","content":"x","expected_sha256":"deadbeef"}"""),
            Ctx(), CancellationToken.None);

        Assert.Equal("expected_sha256_mismatch", result.ErrorCode);
    }

    [Fact]
    public async Task Patch_file_match_conflict_writes_nothing()
    {
        await File.WriteAllTextAsync(FullPath("a.txt"), "x x x");
        await ReadWholeFile("a.txt");

        var tool = new PatchFileChatTool();
        var result = await tool.ExecuteAsync(JObject.Parse(
            """{"path":"a.txt","replacements":[{"old_text":"x","new_text":"y","expected_occurrences":1}]}"""),
            Ctx(), CancellationToken.None);

        Assert.Equal("match_conflict", result.ErrorCode);
        Assert.Equal("x x x", await File.ReadAllTextAsync(FullPath("a.txt")));
    }

    [Fact]
    public async Task Patch_file_replacement_outside_observed_range_is_refused()
    {
        var content = string.Join("\n", Enumerable.Range(1, 10).Select(i => $"line {i}"));
        await File.WriteAllTextAsync(FullPath("a.txt"), content + "\n");
        _readSet.Record(new FileReadObservation(FullPath("a.txt"), HashOf(FullPath("a.txt")),
                                                new FileInfo(FullPath("a.txt")).Length,
                                                File.GetLastWriteTimeUtc(FullPath("a.txt")),
                                                1, 3, CoveredWholeFile: false, 1));

        var tool = new PatchFileChatTool();
        // "line 7" spans line 7, outside the observed 1-3 window.
        var result = await tool.ExecuteAsync(JObject.Parse(
            """{"path":"a.txt","replacements":[{"old_text":"line 7","new_text":"line 7 changed"}]}"""),
            Ctx(), CancellationToken.None);

        Assert.Equal("read_range_required", result.ErrorCode);
        Assert.Contains("line 7", await File.ReadAllTextAsync(FullPath("a.txt")));
        Assert.DoesNotContain("line 7 changed", await File.ReadAllTextAsync(FullPath("a.txt")));
    }

    [Fact]
    public async Task Patch_file_multiple_replacements_apply_in_one_atomic_write()
    {
        await File.WriteAllTextAsync(FullPath("a.txt"), "alpha\nbeta\ngamma\n");
        await ReadWholeFile("a.txt");
        var oldHash = HashOf(FullPath("a.txt"));

        var tool = new PatchFileChatTool();
        var result = await tool.ExecuteAsync(JObject.Parse(
            """{"path":"a.txt","replacements":[{"old_text":"alpha","new_text":"A"},{"old_text":"gamma","new_text":"G"}]}"""),
            Ctx(), CancellationToken.None);

        Assert.True(result.Ok);
        Assert.Equal("A\nbeta\nG\n", await File.ReadAllTextAsync(FullPath("a.txt")));
        Assert.True(_readSet.TryGet(FullPath("a.txt"), out var advanced));
        Assert.NotEqual(oldHash, advanced.Sha256);
    }

    [Fact]
    public async Task Patch_file_stale_content_refused_before_write()
    {
        await File.WriteAllTextAsync(FullPath("a.txt"), "original");
        await ReadWholeFile("a.txt");
        await File.WriteAllTextAsync(FullPath("a.txt"), "changed-outside");

        var tool = new PatchFileChatTool();
        var result = await tool.ExecuteAsync(JObject.Parse(
            """{"path":"a.txt","replacements":[{"old_text":"original","new_text":"patched"}]}"""),
            Ctx(), CancellationToken.None);

        Assert.Equal("stale_read", result.ErrorCode);
        Assert.Equal("changed-outside", await File.ReadAllTextAsync(FullPath("a.txt")));
    }

    [Fact]
    public async Task Patch_file_partial_window_success_invalidates_observation()
    {
        await File.WriteAllTextAsync(FullPath("a.txt"), "a\nbb\nccc\n");
        _readSet.Record(new FileReadObservation(FullPath("a.txt"), HashOf(FullPath("a.txt")),
                                                new FileInfo(FullPath("a.txt")).Length,
                                                File.GetLastWriteTimeUtc(FullPath("a.txt")),
                                                1, 2, CoveredWholeFile: false, 1));

        var tool = new PatchFileChatTool();
        var result = await tool.ExecuteAsync(JObject.Parse(
            """{"path":"a.txt","replacements":[{"old_text":"bb","new_text":"B"}]}"""),
            Ctx(), CancellationToken.None);

        Assert.True(result.Ok);
        // Partial-range success invalidates the observation: the model must read again.
        Assert.False(_readSet.TryGet(FullPath("a.txt"), out _));
    }

    [Fact]
    public async Task PowerShell_nonzero_exit_is_a_completed_failure_result()
    {
        var runner = new FakeProcessRunner(new ChatProcessResult(1, "", "boom", TimeSpan.FromMilliseconds(50),
                                                                 false, false, 0, 5, false, false));
        var tool = new RunPowerShellChatTool(runner, () => @"C:\pwsh\pwsh.exe");

        var result = await tool.ExecuteAsync(JObject.Parse("""{"script":"throw 'boom'"}"""),
                                             Ctx(), CancellationToken.None);

        Assert.False(result.Ok);
        Assert.Equal("nonzero_exit", result.ErrorCode);
        Assert.Equal(ChatToolStatus.Completed, result.Status);
        Assert.Contains("exit code 1", result.ModelContent);
        Assert.Contains("boom", result.ModelContent);
    }

    [Fact]
    public async Task PowerShell_timeout_reports_timed_out_state()
    {
        var runner = new FakeProcessRunner(new ChatProcessResult(-1, "", "", TimeSpan.FromSeconds(10),
                                                                 TimedOut: true, Cancelled: false, 0, 0, false, false));
        var tool = new RunPowerShellChatTool(runner, () => @"C:\pwsh\pwsh.exe");

        var result = await tool.ExecuteAsync(JObject.Parse("""{"script":"Start-Sleep 99"}"""),
                                             Ctx(), CancellationToken.None);

        Assert.False(result.Ok);
        Assert.Contains("timed out", result.ModelContent);
        Assert.True((bool)result.Value["timed_out"]);
    }

    [Fact]
    public async Task PowerShell_without_pwsh_returns_clear_unavailable_error()
    {
        var tool = new RunPowerShellChatTool(new FakeProcessRunner(null), () => null);

        var result = await tool.ExecuteAsync(JObject.Parse("""{"script":"Get-Date"}"""),
                                             Ctx(), CancellationToken.None);

        Assert.Equal("pwsh_unavailable", result.ErrorCode);
    }

    [Fact]
    public async Task PowerShell_script_is_passed_via_stdin_not_command_line()
    {
        ChatProcessRequest seen = null;
        var runner = new ScriptRecordingRunner(r =>
        {
            seen = r;
            return new ChatProcessResult(0, "ok", "", TimeSpan.Zero, false, false, 0, 0, false, false);
        });
        var tool = new RunPowerShellChatTool(runner, () => @"C:\pwsh\pwsh.exe");

        var result = await tool.ExecuteAsync(
            JObject.Parse("""{"script":"Write-Output 'hi'","working_directory":"."}"""),
            Ctx(), CancellationToken.None);

        Assert.True(result.Ok);
        Assert.NotNull(seen);
        Assert.Equal("Write-Output 'hi'", seen.StandardInputScript);
        Assert.Equal("-Command", seen.Arguments[^2]); // script goes through stdin, not -Command text
        Assert.Equal("-", seen.Arguments[^1]);
    }

    private string HashOf(string path) => FileContentHelpersHelper.Sha256(path);

    private sealed class FakeProcessRunner : IChatProcessRunner
    {
        private readonly ChatProcessResult _result;
        public FakeProcessRunner(ChatProcessResult result) => _result = result;

        public ValueTask<ChatProcessResult> RunAsync(ChatProcessRequest request, CancellationToken ct) =>
            new(_result);
    }

    private sealed class ScriptRecordingRunner : IChatProcessRunner
    {
        private readonly Func<ChatProcessRequest, ChatProcessResult> _impl;
        public ScriptRecordingRunner(Func<ChatProcessRequest, ChatProcessResult> impl) => _impl = impl;

        public ValueTask<ChatProcessResult> RunAsync(ChatProcessRequest request, CancellationToken ct) =>
            new(_impl(request));
    }
}

/// <summary>Small shim so the test doesn't reach into internals.</summary>
internal static class FileContentHelpersHelper
{
    public static string Sha256(string path) =>
        Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();
}