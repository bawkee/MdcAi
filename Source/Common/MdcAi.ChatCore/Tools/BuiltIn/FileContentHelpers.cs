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

using System.Security.Cryptography;
using System.Text;

/// <summary>
/// Shared byte/encoding/hash helpers for file tools (DSH proposal §6.3). Content is never logged;
/// only hashes, lengths and normalized relative paths flow into diagnostics.
/// </summary>
internal static class FileContentHelpers
{
    /// <summary>SHA-256 hex of a byte blob, lower-case.</summary>
    public static string Sha256Hex(byte[] bytes) =>
        Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    /// <summary>
    /// A cheap binary sniff: too many NUL/control bytes or invalid UTF-8 => treat as binary.
    /// </summary>
    public static bool IsLikelyBinary(byte[] bytes)
    {
        if (bytes.Length == 0)
            return false;

        var controlCount = 0;
        var sample = Math.Min(bytes.Length, 4096);
        for (var i = 0; i < sample; i++)
        {
            if (bytes[i] == 0)
                return true;
            if (bytes[i] < 0x09 || (bytes[i] > 0x0d && bytes[i] < 0x20))
            {
                if (++controlCount > 4)
                    return true;
            }
        }

        // Reject invalid UTF-8 (binary or a bizarre legacy encoding).
        return !IsValidUtf8(bytes);
    }

    private static bool IsValidUtf8(byte[] bytes)
    {
        var i = 0;
        var n = bytes.Length;
        while (i < n)
        {
            var c = bytes[i];
            int continuation;
            if (c <= 0x7f)
            {
                i++;
                continue;
            }
            if ((c & 0xe0) == 0xc0) { continuation = 1; }
            else if ((c & 0xf0) == 0xe0) { continuation = 2; }
            else if ((c & 0xf8) == 0xf0) { continuation = 3; }
            else return false;

            if (i + continuation >= n)
                return false;
            for (var k = 1; k <= continuation; k++)
                if ((bytes[i + k] & 0xc0) != 0x80)
                    return false;

            // Surrogate/overlong guard (loose, enough for a binary sniff).
            if (continuation == 2 && c == 0xe0 && (bytes[i + 1] & 0xe0) == 0x80)
                return false;
            if (continuation == 3 && c == 0xed && (bytes[i + 1] & 0xe0) == 0xa0)
                return false;

            i += continuation + 1;
        }

        return true;
    }

    /// <summary>Decode UTF-8 (honoring a BOM) and report the detected encoding label.</summary>
    public static string DecodeUtf8(byte[] bytes)
    {
        if (bytes.Length >= 3 && bytes[0] == 0xef && bytes[1] == 0xbb && bytes[2] == 0xbf)
            return Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);

        // Strip a UTF-16 BOM defensively, decoding as UTF-16.
        if (bytes.Length >= 2 && ((bytes[0] == 0xff && bytes[1] == 0xfe) || (bytes[0] == 0xfe && bytes[1] == 0xff)))
            return new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetString(bytes);

        return new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetString(bytes);
    }

    public static string DetectEncodingLabel(byte[] bytes)
    {
        if (bytes.Length >= 3 && bytes[0] == 0xef && bytes[1] == 0xbb && bytes[2] == 0xbf)
            return "utf-8-bom";
        if (bytes.Length >= 2 && (bytes[0] == 0xfe || bytes[1] == 0xfe))
            return "utf-16";
        return "utf-8";
    }

    /// <summary>Split decoded text into lines, preserving a trailing newline as an empty final line only when present.</summary>
    public static string[] SplitLines(string text)
    {
        return text.Replace("\r\n", "\n").Split('\n');
    }

    /// <summary>Reducer/finalizer for reading files, keyed by exact path.</summary>
    public static readonly HashSet<string> GeneratedOrHidden = new(StringComparer.OrdinalIgnoreCase)
    {
        "node_modules", ".git", ".hg", ".svn", "bin", "obj", "build", "dist", "out",
        ".vs", ".vscode", ".idea", "packages", ".gradle", "__pycache__", ".pytest_cache"
    };

    public static bool IsGeneratedOrHidden(string name) =>
        name.StartsWith(".", StringComparison.Ordinal) || GeneratedOrHidden.Contains(name);

    /// <summary>Forward-slashed, workspace-relative path for model/UI display.</summary>
    public static string WorkspaceRelative(WorkspacePathGuard guard, string fullPath)
    {
        var rel = Path.GetRelativePath(guard.WorkspaceRoot, fullPath);
        return rel.Replace('\\', '/');
    }
}

/// <summary>
/// Base class for workspace-scoped read-only file tools: resolves the workspace boundary and
/// rejects calls made without a selected workspace.
/// </summary>
public abstract class WorkspaceToolBase : IChatTool
{
    public abstract string Name { get; }
    public abstract string Description { get; }
    public abstract JObject ParametersSchema { get; }
    public virtual ChatToolExecutionMode ExecutionMode => ChatToolExecutionMode.ParallelSafe;
    public virtual ChatToolRisk Risk => ChatToolRisk.ReadOnly;
    public virtual TimeSpan Timeout => TimeSpan.FromSeconds(60);

    protected abstract ValueTask<ChatToolExecutionResult> ExecuteWorkspaceAsync(
        JObject arguments,
        WorkspacePathGuard guard,
        ChatToolExecutionContext context,
        CancellationToken ct);

    public async ValueTask<ChatToolExecutionResult> ExecuteAsync(
        JObject arguments,
        ChatToolExecutionContext context,
        CancellationToken ct)
    {
        if (string.IsNullOrEmpty(context.WorkspacePath))
            return Error("workspace_not_configured",
                         "No workspace is selected for this conversation, so file tools are disabled.");

        return await ExecuteWorkspaceAsync(arguments, new WorkspacePathGuard(context.WorkspacePath), context, ct);
    }

    public abstract ChatToolCallPresentation PresentCall(JObject arguments);
    public abstract ChatToolResultPresentation PresentResult(JObject arguments, ChatToolExecutionResult result);

    protected static ChatToolExecutionResult Error(string code, string summary) =>
        ChatToolExecutionResult.Failure(ChatToolStatus.Failed, code, summary);
}