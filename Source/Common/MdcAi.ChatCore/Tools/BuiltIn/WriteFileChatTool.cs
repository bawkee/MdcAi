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
/// write_file: create-only for new files, read-before-write + stale-hash recheck for existing
/// files, atomic temp-file replace, and observation advance/invalidate after success.
/// No delete or recursive move in v1.
/// </summary>
public sealed class WriteFileChatTool : WorkspaceToolBase
{
    public const int MaxContentBytes = 512 * 1024;

    public override string Name => "write_file";
    public override string Description =>
        "Create or replace a workspace file atomically. New files require create_only:true; " +
        "replacing an existing file requires a prior complete-file read and survives a staleness recheck.";

    public override JObject ParametersSchema => JObject.Parse($$"""
        {
          "type": "object",
          "additionalProperties": false,
          "properties": {
            "path": { "type": "string" },
            "content": { "type": "string", "description": "Full new file content (UTF-8)." },
            "expected_sha256": { "type": "string", "description": "Optional: the observed hash the file must still have." },
            "create_only": { "type": "boolean", "description": "Must be true when creating a NEW file; fails if the target now exists." }
          },
          "required": ["path", "content"]
        }
        """);

    public override ChatToolExecutionMode ExecutionMode => ChatToolExecutionMode.Exclusive;
    public override ChatToolRisk Risk => ChatToolRisk.Write;

    protected override ValueTask<ChatToolExecutionResult> ExecuteWorkspaceAsync(
        JObject arguments,
        WorkspacePathGuard guard,
        ChatToolExecutionContext context,
        CancellationToken ct) =>
        new(ExecuteCore(arguments, guard, context, ct));

    private ChatToolExecutionResult ExecuteCore(
        JObject arguments,
        WorkspacePathGuard guard,
        ChatToolExecutionContext context,
        CancellationToken ct)
    {
        var path = (string)arguments["path"];
        var content = (string)arguments["content"] ?? string.Empty;
        var expectedSha = (string)arguments["expected_sha256"];
        var createOnly = arguments["create_only"]?.Value<bool?>() ?? false;

        if (Encoding.UTF8.GetByteCount(content) > MaxContentBytes)
            return Error("content_too_large", $"Content exceeds the {MaxContentBytes}-byte limit.");

        var targetPath = guard.TryResolveRelative(path, out var rejection);
        if (rejection != null)
            return Error(rejection, $"Path rejected ({rejection}): {path}");

        // Directory must exist (no implicit mkdir in v1).
        var dir = Path.GetDirectoryName(targetPath);
        if (!Directory.Exists(dir))
            return Error("parent_dir_missing", $"The parent directory of '{path}' does not exist.");

        ct.ThrowIfCancellationRequested();

        var exists = File.Exists(targetPath);

        if (exists && createOnly)
            return Error(FileMutationGuards.AlreadyExists, $"'{path}' already exists; create_only forbids overwriting it.");

        if (!exists && !createOnly)
            return Error(FileMutationGuards.CreateOnlyRequired,
                         $"'{path}' does not exist; creating a new file requires create_only:true.");

        if (exists)
        {
            var authority = FileMutationGuards.CheckExistingFileAuthority(
                targetPath, expectedSha, wholeFileRequired: true, context.ReadObservations);
            if (authority != null)
                return authority;

            if (!context.ReadObservations.TryGet(targetPath, out var observation))
                return Error(FileMutationGuards.ReadRequired, "Missing read observation.");

            // Recheck the hash immediately before IO - time spent can never turn approval stale.
            var recheck = FileMutationGuards.RecheckBeforeWrite(targetPath, observation);
            if (recheck != null)
                return recheck;
        }

        var bytes = Encoding.UTF8.GetBytes(content);

        var (created, newSha, byteCount) = FileMutationGuards.AtomicWrite(targetPath, bytes);

        // Advance/invalidate the observation: a whole-file write grants the new hash, so a later
        // deliberate edit in the same turn doesn't need a redundant read.
        context.ReadObservations.Record(new FileReadObservation(
            targetPath, newSha, byteCount, File.GetLastWriteTimeUtc(targetPath),
            1, null, CoveredWholeFile: true, context.StepNumber));

        var modelContent = created
                               ? $"created {path} ({byteCount} bytes) sha256:{newSha}"
                               : $"replaced {path} ({byteCount} bytes) sha256:{newSha}";

        return ChatToolExecutionResult.Success(new JObject
        {
            ["path"] = path,
            ["created"] = created,
            ["byte_count"] = byteCount,
            ["sha256"] = newSha
        }, modelContent);
    }

    public override ChatToolCallPresentation PresentCall(JObject arguments) =>
        ChatToolCallPresentation.Diff(
            (arguments["create_only"]?.Value<bool?>() ?? false) ? "Create file" : "Write file",
            $"Write · {arguments["path"]}",
            new JObject
            {
                ["path"] = arguments["path"],
                ["proposed"] = new JObject { ["new_text"] = arguments["content"] }
            });

    public override ChatToolResultPresentation PresentResult(JObject arguments, ChatToolExecutionResult result) =>
        new(1, ChatToolResultPresentationKind.Diff, "Write file",
            result.Ok ? $"Write · {arguments["path"]}" : "Write failed",
            new JObject { ["path"] = arguments["path"] });
}