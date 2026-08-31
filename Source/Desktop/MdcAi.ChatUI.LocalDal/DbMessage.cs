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

public class DbMessage
{
    [Key] public string IdMessage { get; set; }
    public string IdMessageParent { get; set; }
    public string IdConversation { get; set; }
    public int Version { get; set; }
    public bool IsCurrentVersion { get; set; }
    public DateTime CreatedTs { get; set; }
    public string Role { get; set; }
    public string Content { get; set; }

    /// <summary>Model id that (re)generated this message ("gpt-4o", "anthropic/claude-3-5-sonnet", ...).
    /// Null on user messages and on legacy rows that predate per-message provenance.</summary>
    public string Model { get; set; }

    /// <summary>Reasoning effort that (re)generated this message ("low"/"medium"/"high", ...).
    /// Null on user messages, on effort-less models, and on legacy rows that predate
    /// per-message effort provenance - same convention as <see cref="Model"/>.</summary>
    public string Effort { get; set; }

    /// <summary>Raw reasoning/thinking text the model emitted before its answer
    /// ("reasoning_content"). Null on user messages and on models that never think.</summary>
    public string Reasoning { get; set; }

    public bool IsTrash { get; set; }

    // --- Phase 1 agentic columns (nullable; legacy rows load untouched) ---

    /// <summary>Owning turn/step for intermediate nodes; null on ordinary chat rows.</summary>
    public string IdTurn { get; set; }
    public string IdStep { get; set; }
    public int? SequenceInStep { get; set; }

    /// <summary>Which provider produced this message ("openai", "openrouter", ...); null on legacy rows.</summary>
    public string ProviderKey { get; set; }

    /// <summary>Provenance, not display: human | model | tool | goal | job | workspace_context | summary | subagent.</summary>
    public string Origin { get; set; }

    /// <summary>pending | streaming | completed | interrupted | failed. Null on legacy rows.</summary>
    public string CompletionState { get; set; }

    /// <summary>Provider finish_reason ("stop", "length", "tool_calls", ...). Null on legacy rows.</summary>
    public string FinishReason { get; set; }

    /// <summary>Exact ordered assistant tool-call array (wire JSON). Authoritative model-visible truth.</summary>
    public string ToolCallsJson { get; set; }

    /// <summary>Wire tool_call_id on a role:"tool" result message.</summary>
    public string ToolCallId { get; set; }

    /// <summary>Tool name on a role:"tool" result message.</summary>
    public string ToolName { get; set; }

    /// <summary>Canonical structured tool result (replay/presentation); Content stays the exact bounded model-visible string.</summary>
    public string ToolResultJson { get; set; }

    /// <summary>Raw reasoning JSON (OpenRouter `reasoning`) preserved for protocol replay, independent of display text.</summary>
    public string ReasoningRawJson { get; set; }

    /// <summary>Raw ordered `reasoning_details` JSON (may be signed/encrypted); byte/sequence faithful.</summary>
    public string ReasoningDetailsJson { get; set; }

    public DbConversation Conversation { get; set; }
    public DbChatTurn Turn { get; set; }
    public DbChatStep Step { get; set; }
}