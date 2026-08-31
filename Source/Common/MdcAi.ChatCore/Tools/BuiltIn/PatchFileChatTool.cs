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
/// patch_file: deterministic exact-replacement contract (DSH proposal §6.3). All preconditions
/// (read observation, range coverage, occurrence counts, freshness) are verified against the
/// unchanged preimage BEFORE any write; one atomic write applies all replacements. On any
/// precondition failure NOTHING is written and the model gets a repairable error code.
/// </summary>
public sealed class PatchFileChatTool : WorkspaceToolBase
{
    public override string Name => "patch_file";
    public override string Description =>
        "Apply exact text replacements to a workspace file atomically. " +
        "Requires a prior read; each old_text must match the expected occurrence count and its " +
        "line span must have been observed. If the file changed since the read, nothing is written.";

    public override JObject ParametersSchema => JObject.Parse($$"""
        {
          "type": "object",
          "additionalProperties": false,
          "properties": {
            "path": { "type": "string" },
            "expected_sha256": { "type": "string" },
            "replacements": {
              "type": "array",
              "items": {
                "type": "object",
                "additionalProperties": false,
                "properties": {
                  "old_text": { "type": "string" },
                  "new_text": { "type": "string" },
                  "expected_occurrences": { "type": "integer", "minimum": 1, "maximum": 100 }
                },
                "required": ["old_text", "new_text"]
              },
              "maxItems": 100
            }
          },
          "required": ["path", "replacements"]
        }
        """);

    public override ChatToolExecutionMode ExecutionMode => ChatToolExecutionMode.Exclusive;
    public override ChatToolRisk Risk => ChatToolRisk.Write;

    protected override async ValueTask<ChatToolExecutionResult> ExecuteWorkspaceAsync(
        JObject arguments,
        WorkspacePathGuard guard,
        ChatToolExecutionContext context,
        CancellationToken ct)
    {
        var path = (string)arguments["path"];
        var expectedSha = (string)arguments["expected_sha256"];

        var replacements = (arguments["replacements"] as JArray)?
            .Select(r => new PatchReplacement(
                (string)r["old_text"],
                (string)r["new_text"],
                r["expected_occurrences"]?.Value<int?>() ?? 1))
            .ToArray() ?? Array.Empty<PatchReplacement>();

        if (replacements.Length == 0)
            return Error("no_replacements", "patch_file requires at least one replacement.");

        if (replacements.Any(r => string.IsNullOrEmpty(r.OldText)))
            return Error("empty_old_text", "A replacement's old_text must not be empty.");

        var targetPath = guard.TryResolveRelative(path, out var rejection);
        if (rejection != null)
            return Error(rejection, $"Path rejected ({rejection}): {path}");

        if (!File.Exists(targetPath))
            return Error("file_not_found", $"File not found: {path}");

        // Authority: a current observation is required; only the replaced SPANS must be observed.
        var authority = FileMutationGuards.CheckExistingFileAuthority(
            targetPath, expectedSha, wholeFileRequired: false, context.ReadObservations);
        if (authority != null)
            return authority;

        if (!context.ReadObservations.TryGet(targetPath, out var observation))
            return Error(FileMutationGuards.ReadRequired, "Missing read observation.");

        // Freshness recheck on the unchanged preimage.
        byte[] bytes;
        try
        {
            bytes = await File.ReadAllBytesAsync(targetPath, ct);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Error("read_failed", $"Could not read '{path}': {ex.Message}");
        }

        var currentHash = FileContentHelpers.Sha256Hex(bytes);
        if (!string.Equals(currentHash, observation.Sha256, StringComparison.OrdinalIgnoreCase))
            return Error(FileMutationGuards.StaleRead,
                         $"'{path}' changed since it was read. Read it again and retry - nothing was written.");

        var text = FileContentHelpers.DecodeUtf8(bytes);

        // Find every match span FIRST; verify counts and range coverage against the preimage.
        var spans = new List<PatchSpan>();

        foreach (var r in replacements)
        {
            var occurrences = CountOccurrences(text, r.OldText);
            if (occurrences != r.ExpectedOccurrences)
                return Error(FileMutationGuards.MatchConflict,
                             $"Replacement '{TruncateForError(r.OldText)}' matched {occurrences} times but " +
                             $"{r.ExpectedOccurrences} were expected. Make old_text unique/exact, then retry.");

            var start = 0;
            for (var i = 0; i < occurrences; i++)
            {
                var idx = text.IndexOf(r.OldText, start, StringComparison.Ordinal);
                spans.Add(new PatchSpan(idx, r.OldText.Length, r.NewText));
                start = idx + r.OldText.Length;
            }
        }

        foreach (var span in spans)
        {
            var (startLine, endLine) = LineSpan(text, span.Start, span.Length);
            if (!observation.CoveredWholeFile &&
                !(observation.StartLine <= startLine && endLine <= (observation.EndLine ?? int.MaxValue)))
            {
                return Error(FileMutationGuards.ReadRangeRequired,
                             $"Replacement spans lines {startLine}-{endLine} but only lines " +
                             $"{observation.StartLine}-{observation.EndLine} were observed. Read the relevant range and retry.");
            }
        }

        // All preconditions hold: apply replacements back-to-front so earlier spans stay valid.
        var sb = new StringBuilder(text);
        foreach (var span in spans.OrderByDescending(s => s.Start))
            sb.Remove(span.Start, span.Length).Insert(span.Start, span.NewText);

        var newBytes = EncodeWithOriginalBom(sb.ToString(), bytes);
        var (_, newSha, byteCount) = FileMutationGuards.AtomicWrite(targetPath, newBytes);

        // A complete-file patch advances the observation; a partial-window patch invalidates it.
        if (observation.CoveredWholeFile)
            context.ReadObservations.Record(new FileReadObservation(
                targetPath, newSha, byteCount, File.GetLastWriteTimeUtc(targetPath),
                1, null, CoveredWholeFile: true, context.StepNumber));
        else
            context.ReadObservations.Remove(targetPath);

        return ChatToolExecutionResult.Success(new JObject
        {
            ["path"] = path,
            ["replacement_count"] = spans.Count,
            ["sha256"] = newSha,
            ["byte_count"] = byteCount
        }, $"patched {path}: {spans.Count} replacement{(spans.Count == 1 ? "" : "s")} applied · sha256:{newSha}");
    }

    private static byte[] EncodeWithOriginalBom(string text, byte[] original)
    {
        var hasBom = FileContentHelpers.DetectEncodingLabel(original) == "utf-8-bom";
        var body = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(text);
        if (!hasBom)
            return body;

        var withBom = new byte[body.Length + 3];
        withBom[0] = 0xef;
        withBom[1] = 0xbb;
        withBom[2] = 0xbf;
        Buffer.BlockCopy(body, 0, withBom, 3, body.Length);
        return withBom;
    }

    private static int CountOccurrences(string text, string needle)
    {
        var count = 0;
        var start = 0;
        while ((start = text.IndexOf(needle, start, StringComparison.Ordinal)) >= 0)
        {
            count++;
            start += needle.Length;
            if (start >= text.Length)
                break;
        }

        return count;
    }

    /// <summary>1-based line numbers of a span (lines split on '\n').</summary>
    private static (int StartLine, int EndLine) LineSpan(string text, int start, int length)
    {
        var startLine = 1;
        for (var i = 0; i < start; i++)
            if (text[i] == '\n')
                startLine++;

        var endLine = startLine;
        for (var i = start; i < start + length && i < text.Length; i++)
            if (text[i] == '\n')
                endLine++;

        return (startLine, endLine);
    }

    private static string TruncateForError(string s)
    {
        const int max = 48;
        if (s.Length <= max)
        {
            return "'" + s.Replace("\n", "\\n") + "'";
        }

        return "'" + s[..max].Replace("\n", "\\n") + "...'";
    }

    public override ChatToolCallPresentation PresentCall(JObject arguments) =>
        ChatToolCallPresentation.Diff("Edit file", $"Edit · {arguments["path"]}",
                                      new JObject { ["path"] = arguments["path"] });

    public override ChatToolResultPresentation PresentResult(JObject arguments, ChatToolExecutionResult result) =>
        new(1, ChatToolResultPresentationKind.Diff, "Edit file",
            result.Ok ? $"Edit · {arguments["path"]}" : "Edit failed",
            new JObject { ["path"] = arguments["path"] });

    private sealed record PatchReplacement(string OldText, string NewText, int ExpectedOccurrences);
    private sealed record PatchSpan(int Start, int Length, string NewText);
}