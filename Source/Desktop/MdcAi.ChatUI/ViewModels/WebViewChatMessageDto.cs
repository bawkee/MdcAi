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

public class WebViewChatMessageDto
{
    public string Id { get; set; }
    public string Role { get; set; }
    public string Content { get; set; }
    public int Version { get; set; }
    public int VersionCount { get; set; }
    public DateTime? CreatedTs { get; set; }

    /// <summary>Model id that produced this message ("gpt-4o", "anthropic/claude-3-5-sonnet", ...). Null on legacy/user messages.</summary>
    public string Model { get; set; }

    /// <summary>Display name of the provider that served the model ("OpenAI", "OpenRouter", ...). Null on legacy/user messages.</summary>
    public string Provider { get; set; }

    /// <summary>Reasoning effort that produced this message ("low"/"medium"/"high", ...). Null on legacy/user messages and effort-less models.</summary>
    public string Effort { get; set; }

    /// <summary>Pre-rendered HTML of the model's thinking/reasoning ("reasoning_content"), what
    /// the renderer shows inside the expanded thinking block. Null when the message has none.</summary>
    public string Reasoning { get; set; }

    /// <summary>One-liner collapsed label for the thinking block: derived from the reasoning
    /// text's last line via <c>ChatMessageVm.ReasoningPreview</c> (recomputed while streaming,
    /// on the same throttle as the reasoning HTML). Can be null transiently (fresh message);
    /// the renderer falls back to a plain "Thinking" label in that case.</summary>
    public string ReasoningPreview { get; set; }
}