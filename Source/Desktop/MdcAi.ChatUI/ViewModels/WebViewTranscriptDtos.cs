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

namespace MdcAi.ChatUI.ViewModels;

/// <summary>
/// Phase 2 versioned transcript contract (DSH proposal §7.1/§7.2): the payload is an ordered
/// transcript of discriminated items - messages, activities (thinking/tool/context/retry/...)
/// and turn summaries - inside the same snapshot protocol. <see cref="Revision"/> is a
/// monotonically increasing per-conversation counter so the renderer can reject stale deltas.
/// </summary>
public class WebViewTranscriptSnapshotDto
{
    public int ContractVersion { get; set; }
    public string ConversationId { get; set; }
    public long Revision { get; set; }
    public WebViewTranscriptItemDto[] Items { get; set; }
}

/// <summary>
/// One transcript item. Kind discriminates message | activity | turn_summary; exactly one of
/// <see cref="Message"/>/<see cref="Activity"/>/<see cref="TurnSummary"/> is populated.
/// </summary>
public class WebViewTranscriptItemDto
{
    /// <summary>Stable deterministic id, e.g. message:{IdMessage} / tool:{IdToolCall} / thinking:{IdMessage}.</summary>
    public string Id { get; set; }
    public string Kind { get; set; }
    public string TurnId { get; set; }
    public int? StepNumber { get; set; }
    public long Revision { get; set; }
    public WebViewChatMessageDto Message { get; set; }
    public WebViewActivityDto Activity { get; set; }
    public WebViewTurnSummaryDto TurnSummary { get; set; }
}

/// <summary>
/// A compact activity row: one-line flow chrome with a bounded, tool-specific detail surface.
/// The renderer switches ONLY on the closed <see cref="PresentationKind"/> vocabulary; unknown
/// or older presentation kinds degrade to a generic bounded text/JSON view (never break).
/// </summary>
public class WebViewActivityDto
{
    public string ActivityKind { get; set; }   // thinking | tool | context | retry | plan | job | helper | goal | notice
    public string PresentationKind { get; set; } // generic | terminal | read | search | diff | context | retry | plan | ...
    public string Status { get; set; }           // running | completed | denied | failed | timed_out | cancelled | ...
    public string Title { get; set; }
    public string Summary { get; set; }
    public string SourceMessageId { get; set; }

    /// <summary>Exact wire tool_call_id of the underlying (hidden by default) tool result pairing.</summary>
    public string ToolCallId { get; set; }
    public string ArgumentHash { get; set; }

    /// <summary>Optional suffix pill text ("exit code 1", "3/5", "+2 active"). Non-shrinking, right-aligned.</summary>
    public string Pill { get; set; }

    /// <summary>The one typed payload (tagged container; see <see cref="WebViewActivityDetailsDto"/>).</summary>
    public WebViewActivityDetailsDto Details { get; set; }
}

/// <summary>
/// Explicitly tagged detail container: <see cref="Kind"/> selects exactly one typed payload.
/// No TypeNameHandling / $type / reflection across the WebView boundary (DSH proposal §7.2).
/// </summary>
public class WebViewActivityDetailsDto
{
    public int Version { get; set; }
    public string Kind { get; set; }
    public WebViewGenericDetailsDto Generic { get; set; }
    public WebViewReadDetailsDto Read { get; set; }
    public WebViewSearchDetailsDto Search { get; set; }
    public WebViewTerminalDetailsDto Terminal { get; set; }
    public WebViewDiffDetailsDto Diff { get; set; }
    public WebViewContextDetailsDto Context { get; set; }
    public WebViewRetryDetailsDto Retry { get; set; }
}

public class WebViewGenericDetailsDto
{
    public string Input { get; set; }
    public string Output { get; set; }
}

/// <summary>Read card: exact line numbers/totals so replay never regex-parses prose.</summary>
public class WebViewReadDetailsDto
{
    public string LocationId { get; set; }
    public string Path { get; set; }
    public int Offset { get; set; }
    public WebViewLineDto[] Lines { get; set; }
    public int RetainedLineCount { get; set; }
    public int TotalLineCount { get; set; }
    public string Language { get; set; }
    public bool Truncated { get; set; }
    public string ArtifactId { get; set; }
}

public class WebViewLineDto
{
    public int Number { get; set; }
    public string Text { get; set; }
}

/// <summary>Grep/glob card: match groups per file + pre-cap totals.</summary>
public class WebViewSearchDetailsDto
{
    public string Query { get; set; }
    public int TotalMatches { get; set; }
    public WebViewSearchFileGroupDto[] Files { get; set; }
    public bool Truncated { get; set; }
    public string ArtifactId { get; set; }
}

public class WebViewSearchFileGroupDto
{
    public string Path { get; set; }
    public WebViewSearchMatchDto[] Matches { get; set; }
}

public class WebViewSearchMatchDto
{
    public int LineNumber { get; set; }
    public string Text { get; set; }
}

/// <summary>Terminal card: exact script, cwd, running flag, separate stdout/stderr, exit code/signal, duration, truncation.</summary>
public class WebViewTerminalDetailsDto
{
    public string Script { get; set; }
    public string WorkingDirectory { get; set; }
    public bool Running { get; set; }
    public string Stdout { get; set; }
    public string Stderr { get; set; }
    public bool Combined { get; set; }
    public int? ExitCode { get; set; }
    public bool TimedOut { get; set; }
    public bool Cancelled { get; set; }
    public long? DurationMs { get; set; }
    public bool Truncated { get; set; }
    public string ArtifactId { get; set; }
}

/// <summary>
/// Diff card with explicit state: proposed (pre-approval, derived from validated args) vs
/// applied (post-success, captured from the tool result). A failed precondition never carries
/// an Applied label (DSH proposal §7.3).
/// </summary>
public class WebViewDiffDetailsDto
{
    public string State { get; set; } // proposed | applied | failed
    public WebViewDiffFileDto[] Diffs { get; set; }
    public int FilesAdded { get; set; }
    public int FilesRemoved { get; set; }
    public int FilesModified { get; set; }
    public string PreconditionError { get; set; }
}

public class WebViewDiffFileDto
{
    public string Path { get; set; }
    public string OldText { get; set; }
    public string NewText { get; set; }
}

/// <summary>Context card: exactly the bytes the model received - never a cleaner paraphrase.</summary>
public class WebViewContextDetailsDto
{
    public string SourceKind { get; set; }
    public string SourcePath { get; set; }
    public string Content { get; set; }
    public string Hash { get; set; }
    public string State { get; set; } // loaded | replaced | removed | baseline | delta
}

/// <summary>Request-attempt retry card: durable timestamps/state authoritative; countdown is render-time.</summary>
public class WebViewRetryDetailsDto
{
    public int AttemptNumber { get; set; }
    public int MaxAttempts { get; set; }
    public string DelaySource { get; set; } // retry-after | local-backoff
    public string FailureCategory { get; set; }
    public string Status { get; set; } // scheduled | started | cancelled | completed
    public string Reason { get; set; }
}

/// <summary>Per-turn usage disclosure derived from durable step/call/usage records.</summary>
public class WebViewTurnSummaryDto
{
    public string TurnId { get; set; }
    public string Status { get; set; }
    public string Outcome { get; set; }
    public int StepCount { get; set; }
    public int ToolCallCount { get; set; }
    public string ProviderModel { get; set; }
    public long? PromptTokens { get; set; }
    public long? CompletionTokens { get; set; }
    public long? ReasoningTokens { get; set; }
    public long? PromptCacheReadTokens { get; set; }
    public long? PromptCacheWriteTokens { get; set; }
    public decimal? Cost { get; set; }
    public long? WallTimeMs { get; set; }
}