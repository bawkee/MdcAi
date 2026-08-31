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

namespace MdcAi.ChatCore.Security;

/// <summary>
/// One committed prior-step file read observation. Existing-file writes/patches require a current
/// observation for the exact canonical path from an EARLIER model step and a recheck of the hash
/// immediately before the atomic write - the optimistic-concurrency guard that prevents blind or
/// stale edits (DSH proposal §6.3, §19.3 item 38).
/// </summary>
public sealed record FileReadObservation(
    string CanonicalPath,
    string Sha256,
    long Length,
    DateTimeOffset LastWriteTimeUtc,
    int? StartLine,
    int? EndLine,
    bool CoveredWholeFile,
    int SourceStep);

/// <summary>
/// Turn-scoped, in-memory set of committed read observations. Dies with the turn; a resumed/new
/// turn must read again. A successful complete-file mutation advances the observation; a
/// partial-range-based success invalidates it so the model reads again.
/// </summary>
public sealed class WorkspaceReadObservationSet
{
    private readonly Dictionary<string, FileReadObservation> _observations = new(StringComparer.OrdinalIgnoreCase);

    public void Record(FileReadObservation observation) => _observations[observation.CanonicalPath] = observation;

    public bool TryGet(string canonicalPath, out FileReadObservation observation) =>
        _observations.TryGetValue(canonicalPath, out observation);

    public void Remove(string canonicalPath) => _observations.Remove(canonicalPath);

    public IReadOnlyCollection<FileReadObservation> All => _observations.Values;

    public int Count => _observations.Count;
}