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

using MdcAi.ChatCore.Security;
using MdcAi.ChatCore.Sessions;
using MdcAi.ChatCore.Tools;
using MdcAi.ChatCore.Tools.BuiltIn;
using MdcAi.OpenAiApi;

public class BuiltInFileToolsTests : IDisposable
{
    private readonly string _root;

    public BuiltInFileToolsTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "mdcai-tools-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); }
        catch { /* best-effort cleanup */ }
    }

    private ChatToolExecutionContext Ctx(string toolCallId = "call_1", int step = 1) =>
        new("c1", "turn-1", step, toolCallId, _root, new WorkspaceReadObservationSet(), null);

    private static async Task<ChatToolExecutionResult> Run(IChatTool tool, string argsJson, ChatToolExecutionContext ctx = null) =>
        await tool.ExecuteAsync(JObject.Parse(argsJson), ctx ?? new ChatToolExecutionContext(
            "c1", "turn-1", 1, "call_1", null, new WorkspaceReadObservationSet(), null), CancellationToken.None);

    [Fact]
    public async Task Read_file_full_read_registers_whole_file_observation()
    {
        var path = Path.Combine(_root, "a.txt");
        await File.WriteAllTextAsync(path, "line1\nline2\nline3\n");

        var tool = new ReadFileChatTool();
        var ctx = Ctx();
        var result = await tool.ExecuteAsync(JObject.Parse("""{"path":"a.txt"}"""), ctx, CancellationToken.None);

        Assert.True(result.Ok);
        Assert.Contains("sha256:", result.ModelContent);
        Assert.Contains("lines 1-3 of 3", result.ModelContent);
        Assert.NotNull(result.Observation);
        Assert.True(result.Observation.CoveredWholeFile);
        Assert.Equal(1, result.Observation.StartLine);
        Assert.Equal(3, result.Observation.EndLine);

        // The scheduler registers the observation after commit - verify via the scheduler path.
        var readSet = new WorkspaceReadObservationSet();
        var registry = ChatToolRegistry.Build(new IChatTool[] { tool });
        var scheduler = new ChatToolScheduler(registry);
        var sink = new InMemorySink();
        var turn = new ChatTurnRequest("c1", "turn-1", "m1", "openrouter", "deepseek/deepseek-chat", null,
                                       null, _root, new[] { "read_file" }, ChatTurnOrigin.Human,
                                       null, ChatTurnLimits.Default);

        await scheduler.ExecuteAsync(
            new[]
            {
                new ChatMessageToolCall
                {
                    Id = "call_1", Function = new ChatMessageFunction { Name = "read_file", Arguments = """{"path":"a.txt"}""" }
                }
            },
            turn, sink, readSet, 1, CancellationToken.None);

        Assert.True(readSet.TryGet(Path.GetFullPath(path), out var obs));
        Assert.True(obs.CoveredWholeFile);
        Assert.Equal(1, readSet.Count);
    }

    [Fact]
    public async Task Read_file_with_range_marks_partial_observation()
    {
        var path = Path.Combine(_root, "a.txt");
        await File.WriteAllTextAsync(path, string.Join("\n", Enumerable.Range(1, 10).Select(i => $"line{i}")) + "\n");

        var tool = new ReadFileChatTool();
        var result = await tool.ExecuteAsync(JObject.Parse("""{"path":"a.txt","start_line":2,"line_count":3}"""),
                                             Ctx(), CancellationToken.None);

        Assert.True(result.Ok);
        Assert.Contains("2 | line2", result.ModelContent);
        Assert.Contains("lines 2-4 of 10", result.ModelContent);
        Assert.False(result.Observation.CoveredWholeFile);
        Assert.Equal(2, result.Observation.StartLine);
        Assert.Equal(4, result.Observation.EndLine);

        // Next-range hint included for the model.
        Assert.Contains("next: start_line=5", result.ModelContent);
    }

    [Fact]
    public async Task Read_file_rejects_binary_missing_directory_and_escapes()
    {
        var binary = Path.Combine(_root, "bin.dat");
        await File.WriteAllBytesAsync(binary, new byte[] { 0, 1, 2, 0, 5, 0 });
        await File.WriteAllTextAsync(Path.Combine(_root, "dirfile.txt"), "x");
        Directory.CreateDirectory(Path.Combine(_root, "somedir"));

        var tool = new ReadFileChatTool();
        Assert.Equal("binary_file", (await Run(tool, """{"path":"bin.dat"}""", Ctx())).ErrorCode);
        Assert.Equal("file_not_found", (await Run(tool, """{"path":"nope.txt"}""", Ctx())).ErrorCode);
        Assert.Equal("is_directory", (await Run(tool, """{"path":"somedir"}""", Ctx())).ErrorCode);
        Assert.Equal("rooted_path_not_allowed", (await Run(tool, """{"path":"C:\\x.txt"}""", Ctx())).ErrorCode);
        Assert.Equal("outside_workspace", (await Run(tool, """{"path":"..\\..\\x"}""", Ctx())).ErrorCode);
    }

    [Fact]
    public async Task Read_file_without_workspace_is_disabled()
    {
        var result = await Run(new ReadFileChatTool(), """{"path":"a.txt"}""",
                               new ChatToolExecutionContext("c1", "turn-1", 1, "call_1", null,
                                                            new WorkspaceReadObservationSet(), null));
        Assert.Equal("workspace_not_configured", result.ErrorCode);
    }

    [Fact]
    public async Task List_dir_defaults_to_depth_one_and_skips_generated()
    {
        Directory.CreateDirectory(Path.Combine(_root, "src"));
        Directory.CreateDirectory(Path.Combine(_root, "node_modules"));
        Directory.CreateDirectory(Path.Combine(_root, "src", "nested"));
        await File.WriteAllTextAsync(Path.Combine(_root, "top.cs"), "x");
        await File.WriteAllTextAsync(Path.Combine(_root, "src", "a.cs"), "x");

        var tool = new ListDirChatTool();
        var result = await tool.ExecuteAsync(JObject.Parse("{}"), Ctx(), CancellationToken.None);

        Assert.True(result.Ok);
        var paths = ((JArray)result.Value["entries"]).Select(e => (string)e["path"]).ToArray();
        // Depth 1: top.cs and src only; node_modules skipped; nested not reached.
        Assert.Contains("top.cs", paths);
        Assert.Contains("src", paths);
        Assert.DoesNotContain("node_modules", paths);
        Assert.DoesNotContain("src/nested", paths);

        // Explicit include of generated + deeper depth.
        var deep = await tool.ExecuteAsync(JObject.Parse("""{"depth":3,"include_generated":true}"""),
                                           Ctx(), CancellationToken.None);
        var deepPaths = ((JArray)deep.Value["entries"]).Select(e => (string)e["path"]).ToArray();
        Assert.Contains("node_modules", deepPaths);
        Assert.Contains("src/nested", deepPaths);
    }

    [Fact]
    public async Task List_dir_never_descends_into_reparse_points()
    {
        // A junction inside the workspace must neither be listed nor traversed.
        var outside = Path.Combine(Path.GetTempPath(), "mdcai-tools-out-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(outside);
        await File.WriteAllTextAsync(Path.Combine(outside, "secret.txt"), "s");
        var junction = Path.Combine(_root, "link");
        var linked = TryCreateJunction(junction, outside);

        try
        {
            var tool = new ListDirChatTool();
            var result = await tool.ExecuteAsync(JObject.Parse("""{"depth":3}"""), Ctx(), CancellationToken.None);

            Assert.True(result.Ok);
            var paths = ((JArray)result.Value["entries"]).Select(e => (string)e["path"]).ToArray();
            Assert.DoesNotContain("link", paths);
            Assert.DoesNotContain("link/secret.txt", paths);
        }
        finally
        {
            if (linked)
            {
                try { Directory.Delete(junction); } catch { }
                try { Directory.Delete(outside, recursive: true); } catch { }
            }
        }
    }

    private static bool TryCreateJunction(string junctionPath, string target)
    {
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo("cmd.exe", $"/c mklink /J \"{junctionPath}\" \"{target}\"")
            {
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            using var p = System.Diagnostics.Process.Start(psi);
            p.WaitForExit(10_000);
            return p.ExitCode == 0 && Directory.Exists(junctionPath);
        }
        catch
        {
            return false;
        }
    }

    [Fact]
    public async Task Grep_finds_literal_and_regex_matches_grouped_by_file()
    {
        Directory.CreateDirectory(Path.Combine(_root, "src"));
        await File.WriteAllTextAsync(Path.Combine(_root, "src", "a.cs"), "using System;\nvar x = 1; // TODO fix\n");
        await File.WriteAllTextAsync(Path.Combine(_root, "src", "b.cs"), "var y = 2; // todo later\nno match here\n");
        await File.WriteAllTextAsync(Path.Combine(_root, "notes.txt"), "no code here\n");

        var tool = new GrepChatTool();

        var literal = await tool.ExecuteAsync(JObject.Parse("""{"query":"TODO","path":"src","glob":"*.cs"}"""),
                                              Ctx(), CancellationToken.None);
        Assert.True(literal.Ok);
        Assert.Contains("grep 'TODO': 2 matches in 2 files", literal.ModelContent);
        var files = (JArray)literal.Value["files"];
        Assert.Equal(2, files.Count);
        var first = (JObject)files[0];
        Assert.Equal(2, (int)first["matches"][0]["line_number"]);
    }

    [Fact]
    public async Task Grep_regex_and_case_sensitivity_and_binary_skip()
    {
        await File.WriteAllTextAsync(Path.Combine(_root, "a.txt"), "Alpha\nbat\n");
        await File.WriteAllBytesAsync(Path.Combine(_root, "b.dat"), new byte[] { 0, 66, 0, 65, 0 });

        var tool = new GrepChatTool();

        var caseSensitive = await tool.ExecuteAsync(JObject.Parse("""{"query":"ALPHA","case_sensitive":true}"""),
                                                    Ctx(), CancellationToken.None);
        Assert.DoesNotContain("1 match", caseSensitive.ModelContent);

        var insensitive = await tool.ExecuteAsync(JObject.Parse("""{"query":"alpha"}"""), Ctx(), CancellationToken.None);
        Assert.Contains("1 match", insensitive.ModelContent);

        var regex = await tool.ExecuteAsync(JObject.Parse("""{"query":"^b.t$","is_regex":true}"""), Ctx(), CancellationToken.None);
        Assert.Contains("1 match", regex.ModelContent);

        // Binary file must never poison results.
        Assert.DoesNotContain("b.dat", insensitive.ModelContent);
    }

    [Fact]
    public async Task Grep_invalid_regex_returns_repairable_error()
    {
        var tool = new GrepChatTool();
        var result = await tool.ExecuteAsync(JObject.Parse("""{"query":"[unclosed","is_regex":true}"""),
                                             Ctx(), CancellationToken.None);
        Assert.Equal("invalid_regex", result.ErrorCode);
    }

    [Fact]
    public void Presenters_are_pure_and_bounded()
    {
        var tool = new ReadFileChatTool();
        var call = tool.PresentCall(JObject.Parse("""{"path":"a.txt"}"""));
        var result = tool.PresentResult(JObject.Parse("""{"path":"a.txt"}"""),
                                        ChatToolExecutionResult.Success(new JObject(), "ok"));

        Assert.Equal("Read file", call.Title);
        Assert.Equal(ChatToolResultPresentationKind.Read, result.Kind);
        Assert.Equal("a.txt", (string)result.Payload["path"]);
    }
}