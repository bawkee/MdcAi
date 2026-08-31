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
using MdcAi.ChatCore.Security;

/// <summary>
/// read_file: resolves the path inside the workspace, rejects binary/oversized files, returns
/// line-numbered content with a stable full-file SHA-256, and (only after the result is
/// committed) registers the prior-step read observation that authorizes later edits.
/// </summary>
public sealed class ReadFileChatTool : WorkspaceToolBase
{
    public const int DefaultMaxChars = 128 * 1024;
    public const int HardMaxChars = 512 * 1024;

    public override string Name => "read_file";
    public override string Description =>
        "Read lines from a workspace-relative file. Returns line numbers and a stable file hash. " +
        "Reading a file in a prior step is REQUIRED before editing it.";

    public override JObject ParametersSchema => JObject.Parse($$"""
        {
          "type": "object",
          "additionalProperties": false,
          "properties": {
            "path": { "type": "string", "description": "Workspace-relative path (forward slashes ok)." },
            "start_line": { "type": "integer", "minimum": 1, "description": "1-based first line to return." },
            "line_count": { "type": "integer", "minimum": 1, "maximum": 500, "description": "How many lines to return." },
            "max_chars": { "type": "integer", "minimum": 1, "maximum": {{HardMaxChars}}, "description": "Output character cap." }
          },
          "required": ["path"]
        }
        """);

    protected override async ValueTask<ChatToolExecutionResult> ExecuteWorkspaceAsync(
        JObject arguments,
        WorkspacePathGuard guard,
        ChatToolExecutionContext context,
        CancellationToken ct)
    {
        var path = (string)arguments["path"];
        var startLine = arguments["start_line"]?.Value<int?>() ?? 1;
        var lineCount = arguments["line_count"]?.Value<int?>();
        var maxChars = arguments["max_chars"]?.Value<int?>() ?? DefaultMaxChars;
        maxChars = Math.Min(maxChars, HardMaxChars);

        var fullPath = guard.TryResolveRelative(path, out var rejection);
        if (rejection != null)
            return Error(rejection, $"Path rejected ({rejection}): {path}");

        if (Directory.Exists(fullPath))
            return Error("is_directory", $"'{path}' is a directory; use list_dir instead.");

        if (!File.Exists(fullPath))
            return Error("file_not_found", $"File not found: {path}");

        byte[] bytes;
        try
        {
            bytes = await File.ReadAllBytesAsync(fullPath, ct);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Error("read_failed", $"Could not read '{path}': {ex.Message}");
        }

        if (FileContentHelpers.IsLikelyBinary(bytes))
            return Error("binary_file", $"'{path}' looks like a binary file and was not read.");

        var sha256 = FileContentHelpers.Sha256Hex(bytes);
        var encoding = FileContentHelpers.DetectEncodingLabel(bytes);
        var text = FileContentHelpers.DecodeUtf8(bytes);
        var lines = FileContentHelpers.SplitLines(text);

        var totalLines = lines.Length;

        // Trim a trailing empty line produced by a final newline (text files usually end with one).
        if (totalLines > 0 && lines[totalLines - 1].Length == 0)
            totalLines--;

        var from = Math.Max(1, startLine);
        var to = lineCount is { } count ? Math.Min(totalLines, from + count - 1) : totalLines;
        if (from > to)
            return Error("range_out_of_bounds",
                         $"Start line {from} is beyond the file's {totalLines} lines.");

        var coveredWholeFile = from == 1 && to >= totalLines;

        var builder = new StringBuilder();
        var truncated = false;
        for (var i = from; i <= to; i++)
        {
            if (builder.Length > maxChars)
            {
                truncated = true;
                break;
            }

            var line = lines[i - 1];
            builder.Append(i).Append(" | ").Append(line).Append('\n');
        }

        var content = builder.ToString().TrimEnd('\n');
        if (truncated)
            content += $"\n... (read truncated at {maxChars} chars; request the next range)";

        var nextRangeHint = truncated || to < totalLines
                                ? $"\nnext: start_line={to + 1}"
                                : string.Empty;

        var modelContent =
            $"path: {FileContentHelpers.WorkspaceRelative(guard, fullPath)}\n" +
            $"encoding: {encoding}\n" +
            $"{content}\n" +
            $"lines {from}-{(truncated ? "?" : to.ToString())} of {totalLines} · sha256:{sha256}" +
            nextRangeHint;

        var value = new JObject
        {
            ["path"] = path,
            ["canonical_path"] = fullPath,
            ["sha256"] = sha256,
            ["encoding"] = encoding,
            ["length"] = bytes.Length,
            ["start_line"] = from,
            ["end_line"] = truncated ? (int?)null : to,
            ["total_line_count"] = totalLines,
            ["truncated"] = truncated
        };

        var observation = new FileReadObservation(
            fullPath, sha256, bytes.Length, File.GetLastWriteTimeUtc(fullPath),
            from, truncated ? (int?)null : to, coveredWholeFile, context.StepNumber);

        return ChatToolExecutionResult.Success(value, modelContent, observation);
    }

    public override ChatToolCallPresentation PresentCall(JObject arguments)
    {
        var path = (string)arguments["path"];
        return ChatToolCallPresentation.Generic("Read file", $"Read · {path}",
                                                new JObject { ["path"] = path });
    }

    public override ChatToolResultPresentation PresentResult(JObject arguments, ChatToolExecutionResult result)
    {
        var payload = new JObject();
        if (result.Value is JObject v)
            payload = (JObject)v.DeepClone();
        payload["path"] = arguments["path"];

        return new ChatToolResultPresentation(
            1, ChatToolResultPresentationKind.Read,
            "Read file", result.Ok ? $"Read · {arguments["path"]}" : "Read failed",
            payload);
    }
}