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
/// Persisted conversation summary (DSH proposal §8.4): covers through message/turn ids and a
/// source hash so it INVALIDATES when the covered fork selection changes. Only completed turns
/// are summarized; the summarizer prompt/version is persisted so behavior drift is diagnosable.
/// </summary>
public class DbConversationSummary
{
    [Key] public string IdSummary { get; set; }
    public string IdConversation { get; set; }
    public string BranchAnchorMessageId { get; set; }
    public string CoveredThroughMessageId { get; set; }
    public string SourceHash { get; set; }
    public string SummaryText { get; set; }
    public string Model { get; set; }
    public string ProviderKey { get; set; }
    public string SummarizerPromptVersion { get; set; }
    public long? TokenEstimate { get; set; }
    public DateTime CreatedUtc { get; set; }
    /// <summary>valid | superseded | invalidated</summary>
    public string Status { get; set; }
    public string SupersedesSummaryId { get; set; }
}

/// <summary>
/// Workspace instruction/context snapshot (DSH proposal §8.4/§7.3 context surface): exact
/// model-visible content with source path/hash so replay is honest. A changed file creates a NEW
/// snapshot; it never rewrites what an earlier turn saw.
/// </summary>
public class DbWorkspaceContext
{
    [Key] public string IdWorkspaceContext { get; set; }
    public string IdConversation { get; set; }
    /// <summary>premise | workspace_instructions | summary | goal_reminder | runtime_snapshot | tool_catalog</summary>
    public string SourceKind { get; set; }
    /// <summary>Workspace-relative path or producer label (never an arbitrary absolute path).</summary>
    public string SourcePath { get; set; }
    public string ContentHash { get; set; }
    public string Content { get; set; }
    public DateTime DiscoveredUtc { get; set; }
    /// <summary>active | superseded</summary>
    public string State { get; set; }
    public string FirstTurnId { get; set; }
    public string LastTurnId { get; set; }
}

/// <summary>Durable record of one artifact (DSH proposal §11.2); never an arbitrary absolute path.</summary>
public class DbArtifact
{
    [Key] public string IdArtifact { get; set; }
    public string OwnerConversationId { get; set; }
    public string OwnerTurnId { get; set; }
    public string OwnerToolCallId { get; set; }
    public string OwnerJobId { get; set; }
    /// <summary>Generated storage name (not model-controlled).</summary>
    public string StorageName { get; set; }
    public string Kind { get; set; }
    public long SizeBytes { get; set; }
    public string Sha256 { get; set; }
    public string MimeType { get; set; }
    public DateTime CreatedUtc { get; set; }
    public DateTime? ExpiryUtc { get; set; }
}