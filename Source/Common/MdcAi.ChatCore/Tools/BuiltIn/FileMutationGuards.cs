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
/// Shared mutation preconditions behind write_file/patch_file (DSH proposal §6.3 / §19.3-38):
/// a current complete-file read observation from an earlier model step is required, the hash is
/// rechecked immediately before IO, and the observation advances (or invalidates) after success.
/// </summary>
internal static class FileMutationGuards
{
    public const string ReadRequired = "read_required";
    public const string ReadRangeRequired = "read_range_required";
    public const string StaleRead = "stale_read";
    public const string ExpectedHashMismatch = "expected_sha256_mismatch";
    public const string MatchConflict = "match_conflict";
    public const string AlreadyExists = "already_exists";
    public const string CreateOnlyRequired = "create_only_required";

    /// <summary>
    /// Checks the prior-step observation for an existing file. Returns an error result, or null
    /// when the write may proceed. wholeFileRequired=true for whole-file replacement
    /// (write_file), false for patch (which needs only the replaced spans observed).
    /// </summary>
    public static ChatToolExecutionResult CheckExistingFileAuthority(
        string canonicalPath,
        string expectedSha256,
        bool wholeFileRequired,
        WorkspaceReadObservationSet readSet)
    {
        if (!readSet.TryGet(canonicalPath, out var observation))
            return ChatToolExecutionResult.Failure(
                ChatToolStatus.Failed, ReadRequired,
                $"Editing '{canonicalPath}' requires reading it first in an earlier step. Call read_file and retry.");

        if (wholeFileRequired && !observation.CoveredWholeFile)
            return ChatToolExecutionResult.Failure(
                ChatToolStatus.Failed, ReadRangeRequired,
                $"Replacing '{canonicalPath}' requires a COMPLETE-file read observation, but only lines " +
                $"{observation.StartLine}-{observation.EndLine} were observed. Read the whole file and retry.");

        if (expectedSha256 != null && !string.Equals(expectedSha256, observation.Sha256, StringComparison.OrdinalIgnoreCase))
            return ChatToolExecutionResult.Failure(
                ChatToolStatus.Failed, ExpectedHashMismatch,
                $"The supplied expected_sha256 does not match the observed hash of '{canonicalPath}'. Read the file and retry.");

        return null;
    }

    /// <summary>
    /// Rechecks the file hash against the observation IMMEDIATELY before IO; returns
    /// stale_read when the file changed since the read.
    /// </summary>
    public static ChatToolExecutionResult RecheckBeforeWrite(string canonicalPath, FileReadObservation observation)
    {
        if (!File.Exists(canonicalPath))
            return ChatToolExecutionResult.Failure(
                ChatToolStatus.Failed, StaleRead,
                $"'{canonicalPath}' no longer exists. Read again and retry.");

        string currentHash;
        try
        {
            currentHash = FileContentHelpers.Sha256Hex(File.ReadAllBytes(canonicalPath));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return ChatToolExecutionResult.Failure(ChatToolStatus.Failed, "read_failed",
                                                   $"Could not re-read '{canonicalPath}' before writing: {ex.Message}");
        }

        if (!string.Equals(currentHash, observation.Sha256, StringComparison.OrdinalIgnoreCase))
            return ChatToolExecutionResult.Failure(
                ChatToolStatus.Failed, StaleRead,
                $"'{canonicalPath}' changed since it was read (hash mismatch). Read the file again and retry - do not overwrite blind.");

        return null;
    }

    /// <summary>
    /// Atomically replaces (or creates) the target: write a temp file in the SAME directory,
    /// flush, then Replace/Move over the target so cancellation can never leave a half-written file.
    /// Returns (created, newSha, byteCount).
    /// </summary>
    public static (bool Created, string NewSha256, long ByteCount) AtomicWrite(string targetPath, byte[] content)
    {
        var dir = Path.GetDirectoryName(targetPath);
        var tempPath = Path.Combine(dir, $".{Path.GetFileName(targetPath)}.{Guid.NewGuid():N}.tmp");

        try
        {
            using (var fs = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                fs.Write(content, 0, content.Length);
                fs.Flush(flushToDisk: true);
            }

            var created = !File.Exists(targetPath);
            if (created)
                File.Move(tempPath, targetPath);
            else
                File.Replace(tempPath, targetPath, destinationBackupFileName: null);

            return (created, FileContentHelpers.Sha256Hex(content), content.Length);
        }
        finally
        {
            try { if (File.Exists(tempPath)) File.Delete(tempPath); }
            catch { /* best-effort temp cleanup */ }
        }
    }
}