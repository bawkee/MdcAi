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

namespace MdcAi.ChatUI.LocalDal;

using System.ComponentModel.DataAnnotations;

/// <summary>
/// Durable record of a background job (DSH proposal §8.1 persistence). After an app restart a
/// running/stopping row is reconciled to killed/interrupted - Windows processes are never
/// reattached in this scope, and the card says it cannot be resumed.
/// </summary>
public class DbBackgroundJob
{
    [Key] public string IdJob { get; set; }
    public string OwnerConversationId { get; set; }
    public string OwnerTurnId { get; set; }
    public string ToolCallId { get; set; }
    public string OwnerToolName { get; set; }

    /// <summary>powershell | ... (future kinds).</summary>
    public string Kind { get; set; }

    /// <summary>running | stopping | completed | killed | failed</summary>
    public string Status { get; set; }

    public DateTime StartedUtc { get; set; }
    public DateTime? EndedUtc { get; set; }

    /// <summary>Redacted command presentation (never the raw script body at normal levels; a hash is enough for diagnosis).</summary>
    public string CommandPresentationHash { get; set; }

    public int? ExitCode { get; set; }
    public long? OutputBytes { get; set; }
    public bool OutputTruncated { get; set; }

    /// <summary>Artifact metadata reference (id only, never an arbitrary absolute path).</summary>
    public string ArtifactId { get; set; }

    /// <summary>Sanitized failure summary; null when ok.</summary>
    public string FailureSummary { get; set; }
}