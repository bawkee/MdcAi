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

/// <summary>
/// list_dir: deterministic workspace directory listing. Default depth 1 (hard cap), skips
/// generated/hidden directories unless explicitly included, never descends into reparse points
/// (junctions/symlinks), and returns normalized workspace-relative paths with type/size/time.
/// </summary>
public sealed class ListDirChatTool : WorkspaceToolBase
{
    public const int MaxDepth = 4;
    public const int MaxEntries = 500;

    public override string Name => "list_dir";
    public override string Description =>
        "List files and folders inside a workspace directory (default depth 1, deterministic order).";

    public override JObject ParametersSchema => JObject.Parse($$"""
        {
          "type": "object",
          "additionalProperties": false,
          "properties": {
            "path": { "type": "string", "description": "Workspace-relative directory; omit for the workspace root." },
            "depth": { "type": "integer", "minimum": 1, "maximum": {{MaxDepth}}, "description": "Recursion depth (1 = immediate children)." },
            "glob": { "type": "string", "description": "Optional name filter (e.g. *.cs)." },
            "max_entries": { "type": "integer", "minimum": 1, "maximum": {{MaxEntries}} },
            "include_generated": { "type": "boolean", "description": "Also list node_modules/.git/bin/obj/etc." }
          },
          "required": []
        }
        """);

    protected override ValueTask<ChatToolExecutionResult> ExecuteWorkspaceAsync(
        JObject arguments,
        WorkspacePathGuard guard,
        ChatToolExecutionContext context,
        CancellationToken ct)
    {
        var path = (string)arguments["path"] ?? ".";
        var depth = arguments["depth"]?.Value<int?>() ?? 1;
        var glob = (string)arguments["glob"];
        var maxEntries = arguments["max_entries"]?.Value<int?>() ?? MaxEntries;
        maxEntries = Math.Min(maxEntries, MaxEntries);
        var includeGenerated = arguments["include_generated"]?.Value<bool?>() ?? false;

        var root = guard.TryResolveRelative(path, out var rejection);
        if (rejection != null)
            return new ValueTask<ChatToolExecutionResult>(Error(rejection, $"Path rejected ({rejection}): {path}"));
        if (!Directory.Exists(root))
            return new ValueTask<ChatToolExecutionResult>(Error("dir_not_found", $"Directory not found: {path}"));

        var matcher = glob != null
                          ? new System.Text.RegularExpressions.Regex(
                                "^" + System.Text.RegularExpressions.Regex.Escape(glob).Replace("\\*", ".*") + "$",
                                System.Text.RegularExpressions.RegexOptions.IgnoreCase,
                                TimeSpan.FromSeconds(2))
                          : null;

        var entries = new List<JObject>();
        Walk(guard, root, 1, depth, matcher, includeGenerated, maxEntries, entries, ct);

        entries.Sort((a, b) => string.CompareOrdinal((string)a["path"], (string)b["path"]));

        var model = new System.Text.StringBuilder();
        model.Append($"listing {path} ({entries.Count} entries)");
        foreach (var e in entries)
            model.Append('\n').Append(e["type"]).Append(' ').Append(e["path"]);

        var value = new JObject
        {
            ["path"] = path,
            ["entries"] = new JArray(entries)
        };

        return new ValueTask<ChatToolExecutionResult>(ChatToolExecutionResult.Success(value, model.ToString()));
    }

    private static void Walk(
        WorkspacePathGuard guard,
        string dir,
        int currentDepth,
        int maxDepth,
        System.Text.RegularExpressions.Regex matcher,
        bool includeGenerated,
        int maxEntries,
        List<JObject> entries,
        CancellationToken ct)
    {
        if (entries.Count >= maxEntries)
            return;

        ct.ThrowIfCancellationRequested();

        IEnumerable<string> rawFiles;
        IEnumerable<string> rawDirs;
        try
        {
            rawFiles = Directory.EnumerateFiles(dir);
            rawDirs = Directory.EnumerateDirectories(dir);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return; // unreadable folder: skip silently, keep the listing deterministic
        }

        foreach (var file in rawFiles.OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
        {
            if (entries.Count >= maxEntries)
                return;

            var name = Path.GetFileName(file);
            if (matcher != null && !matcher.IsMatch(name))
                continue;

            FileInfo info;
            try { info = new FileInfo(file); }
            catch { continue; }

            entries.Add(new JObject
            {
                ["type"] = "file",
                ["path"] = FileContentHelpers.WorkspaceRelative(guard, file),
                ["size"] = info.Length,
                ["modified"] = info.LastWriteTimeUtc.ToString("O")
            });
        }

        if (currentDepth > maxDepth)
            return;

        foreach (var sub in rawDirs.OrderBy(d => d, StringComparer.OrdinalIgnoreCase))
        {
            if (entries.Count >= maxEntries)
                return;

            DirectoryInfo info;
            try { info = new DirectoryInfo(sub); }
            catch { continue; }

            // Never descend into reparse points (junctions/symlinks) - escape vector + loops.
            if ((info.Attributes & FileAttributes.ReparsePoint) != 0)
                continue;

            var name = info.Name;
            if (!includeGenerated && FileContentHelpers.IsGeneratedOrHidden(name))
                continue;

            if (matcher != null && !matcher.IsMatch(name))
                continue;

            entries.Add(new JObject
            {
                ["type"] = "dir",
                ["path"] = FileContentHelpers.WorkspaceRelative(guard, sub),
                ["size"] = 0,
                ["modified"] = info.LastWriteTimeUtc.ToString("O")
            });

            if (currentDepth < maxDepth)
                Walk(guard, sub, currentDepth + 1, maxDepth, matcher, includeGenerated, maxEntries, entries, ct);
        }
    }

    public override ChatToolCallPresentation PresentCall(JObject arguments)
    {
        var path = (string)arguments["path"] ?? ".";
        return ChatToolCallPresentation.Generic("List directory", $"List · {path}",
                                                new JObject { ["path"] = path });
    }

    public override ChatToolResultPresentation PresentResult(JObject arguments, ChatToolExecutionResult result) =>
        new(1, ChatToolResultPresentationKind.Generic, "List directory",
            result.Ok ? $"Listed {(string)arguments["path"] ?? "."}" : "List failed",
            new JObject { ["path"] = arguments["path"] });
}