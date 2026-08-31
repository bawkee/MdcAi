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

namespace MdcAi.ChatCore.Tools.BuiltIn;

using System.Text;
using System.Text.RegularExpressions;
using MdcAi.ChatCore.Security;

/// <summary>
/// grep: a MANAGED search over the workspace (no rg.exe dependency). Skips binary files and
/// generated/hidden directories by default, enforces a finite regex timeout and result caps,
/// checks cancellation, and returns deterministic ordering (file path, then line number).
/// </summary>
public sealed class GrepChatTool : WorkspaceToolBase
{
    public const int MaxResults = 200;
    public const int MaxFileBytes = 8 * 1024 * 1024;
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(3);

    public override string Name => "grep";
    public override string Description =>
        "Search file contents inside the workspace. Managed implementation; returns line matches grouped by file.";

    public override JObject ParametersSchema => JObject.Parse($$"""
        {
          "type": "object",
          "additionalProperties": false,
          "properties": {
            "query": { "type": "string", "description": "Literal text (or regex when is_regex is true)." },
            "path": { "type": "string", "description": "Workspace-relative directory to search; omit for the whole workspace." },
            "glob": { "type": "string", "description": "Optional file-name filter (e.g. *.cs)." },
            "is_regex": { "type": "boolean" },
            "case_sensitive": { "type": "boolean" },
            "max_results": { "type": "integer", "minimum": 1, "maximum": {{MaxResults}} },
            "include_generated": { "type": "boolean" }
          },
          "required": ["query"]
        }
        """);

    protected override ValueTask<ChatToolExecutionResult> ExecuteWorkspaceAsync(
        JObject arguments,
        WorkspacePathGuard guard,
        ChatToolExecutionContext context,
        CancellationToken ct)
    {
        var query = (string)arguments["query"];
        if (string.IsNullOrWhiteSpace(query))
            return new ValueTask<ChatToolExecutionResult>(Error("query_required", "grep requires a non-empty query."));

        var path = (string)arguments["path"] ?? ".";
        var isRegex = arguments["is_regex"]?.Value<bool?>() ?? false;
        var caseSensitive = arguments["case_sensitive"]?.Value<bool?>() ?? false;
        var maxResults = arguments["max_results"]?.Value<int?>() ?? MaxResults;
        maxResults = Math.Min(maxResults, MaxResults);
        var includeGenerated = arguments["include_generated"]?.Value<bool?>() ?? false;
        var glob = (string)arguments["glob"];

        var root = guard.TryResolveRelative(path, out var rejection);
        if (rejection != null)
            return new ValueTask<ChatToolExecutionResult>(Error(rejection, $"Path rejected ({rejection}): {path}"));
        if (!Directory.Exists(root))
            return new ValueTask<ChatToolExecutionResult>(Error("dir_not_found", $"Directory not found: {path}"));

        Regex matcher;
        try
        {
            var options = caseSensitive ? RegexOptions.None : RegexOptions.IgnoreCase;
            matcher = isRegex
                          ? new Regex(query, options, RegexTimeout)
                          : new Regex(Regex.Escape(query), options, RegexTimeout);
        }
        catch (ArgumentException ex)
        {
            return new ValueTask<ChatToolExecutionResult>(Error("invalid_regex",
                                                                $"The query is not a valid regular expression: {ex.Message}"));
        }

        var nameFilter = glob != null
                             ? new Regex("^" + Regex.Escape(glob).Replace("\\*", ".*") + "$",
                                         RegexOptions.IgnoreCase, RegexTimeout)
                             : null;

        var matches = new List<JObject>();
        var totalFound = 0;

        ScanDir(guard, root, matcher, nameFilter, includeGenerated, maxResults, matches,
                ref totalFound, ct);

        var groups = matches.GroupBy(m => (string)m["path"])
                            .OrderBy(g => g.Key, StringComparer.Ordinal)
                            .ToArray();

        var builder = new StringBuilder();
        builder.Append($"grep '{query}': {totalFound} matches in {groups.Length} files");
        foreach (var group in groups)
        {
            builder.Append($"\n{group.Key}:");
            foreach (var m in group)
                builder.Append($"\n  {m["line_number"]} | {m["text"]}");
        }

        var value = new JObject
        {
            ["query"] = query,
            ["total_matches"] = totalFound,
            ["files"] = new JArray(groups.Select(g =>
                new JObject
                {
                    ["path"] = g.Key,
                    ["matches"] = new JArray(g.Select(m => m.DeepClone()))
                }))
        };

        if (totalFound > maxResults)
            builder.Append($"\n... ({totalFound - maxResults} more matches; raise max_results to see them)");

        return new ValueTask<ChatToolExecutionResult>(ChatToolExecutionResult.Success(value, builder.ToString()));
    }

    private static void ScanDir(
        WorkspacePathGuard guard,
        string dir,
        Regex matcher,
        Regex nameFilter,
        bool includeGenerated,
        int maxResults,
        List<JObject> matches,
        ref int totalFound,
        CancellationToken ct)
    {
        if (totalFound >= maxResults)
            return;

        ct.ThrowIfCancellationRequested();

        IEnumerable<string> files;
        IEnumerable<string> dirs;
        try
        {
            files = Directory.EnumerateFiles(dir).OrderBy(f => f, StringComparer.OrdinalIgnoreCase);
            dirs = Directory.EnumerateDirectories(dir).OrderBy(d => d, StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return;
        }

        foreach (var file in files)
        {
            if (totalFound >= maxResults)
                return;

            ct.ThrowIfCancellationRequested();

            var name = Path.GetFileName(file);
            if (nameFilter != null && !nameFilter.IsMatch(name))
                continue;

            ScanFile(guard, file, matcher, maxResults, matches, ref totalFound, ct);
        }

        foreach (var sub in dirs)
        {
            if (totalFound >= maxResults)
                return;

            DirectoryInfo info;
            try { info = new DirectoryInfo(sub); }
            catch { continue; }

            if ((info.Attributes & FileAttributes.ReparsePoint) != 0)
                continue;

            if (!includeGenerated && FileContentHelpers.IsGeneratedOrHidden(info.Name))
                continue;

            ScanDir(guard, sub, matcher, nameFilter, includeGenerated, maxResults, matches,
                    ref totalFound, ct);
        }
    }

    private static void ScanFile(
        WorkspacePathGuard guard,
        string file,
        Regex matcher,
        int maxResults,
        List<JObject> matches,
        ref int totalFound,
        CancellationToken ct)
    {
        byte[] bytes;
        try
        {
            var info = new FileInfo(file);
            if (info.Length > MaxFileBytes)
                return;
            bytes = File.ReadAllBytes(file);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return;
        }

        if (FileContentHelpers.IsLikelyBinary(bytes))
            return;

        var text = FileContentHelpers.DecodeUtf8(bytes);
        var lines = FileContentHelpers.SplitLines(text);
        var displayPath = FileContentHelpers.WorkspaceRelative(guard, file);

        for (var i = 0; i < lines.Length && totalFound < maxResults; i++)
        {
            ct.ThrowIfCancellationRequested();

            if (!matcher.IsMatch(lines[i]))
                continue;

            totalFound++;
            matches.Add(new JObject
            {
                ["path"] = displayPath,
                ["line_number"] = i + 1,
                ["text"] = lines[i]
            });
        }
    }

    public override ChatToolCallPresentation PresentCall(JObject arguments) =>
        ChatToolCallPresentation.Generic("Search", $"Grep · {arguments["query"]}",
                                         new JObject { ["query"] = arguments["query"] });

    public override ChatToolResultPresentation PresentResult(JObject arguments, ChatToolExecutionResult result) =>
        new(1, ChatToolResultPresentationKind.Search, "Search",
            result.Ok ? $"Grep · {arguments["query"]}" : "Search failed",
            new JObject { ["query"] = arguments["query"] });
}