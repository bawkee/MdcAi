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

namespace MdcAi.OpenAiApi;

public class ChatMessage
{
    public ChatMessage() { }

    public ChatMessage(ChatMessageRole role, string content)
    {
        Role = role;
        Content = content;
    }

    public ChatMessage(ChatMessage basedOn)
    {
        if (basedOn == null)
            throw new ArgumentNullException();

        Role = basedOn.Role;
        Content = basedOn.Content;
        Name = basedOn.Name;
        ReasoningContent = basedOn.ReasoningContent;
        ReasoningRaw = basedOn.ReasoningRaw;
    }

    [JsonProperty("role")] public string Role { get; set; } = ChatMessageRole.User;
    [JsonProperty("content")] public string Content { get; set; }
    [JsonProperty("name")] public string Name { get; set; }

    /// <summary>
    /// The reasoning/thinking tokens a model emitted before its answer (DeepSeek
    /// <c>reasoning_content</c>, OpenAI <c>reasoning_content</c> on <c>delta</c> chunks,
    /// OpenRouter's pass-through of the same). Present on completed responses and on
    /// streaming deltas; null when the provider sent none. Never serialized back into
    /// request histories (assistant messages) by default — repsonses only.
    /// </summary>
    [JsonProperty("reasoning_content")] public string ReasoningContent { get; set; }

    /// <summary>
    /// OpenRouter's own reasoning field on <c>delta</c> (and some <c>message</c>) payloads.
    /// Some routes (e.g. Baidu-hosted DeepSeek on OpenRouter) surface thinking here as an
    /// incremental STRING rather than in <c>reasoning_content</c>; Anthropic-style routes
    /// send an object instead. Kept raw so <see cref="ReasoningText"/> can normalize both.
    /// </summary>
    [JsonProperty("reasoning")] public object ReasoningRaw { get; set; }

    /// <summary>
    /// Normalized reasoning text for this message/delta: <c>reasoning_content</c> wins,
    /// else <c>reasoning</c> coerced to a string (string as-is; object via its summary /
    /// text parts). Null when there's no reasoning.
    /// </summary>
    [JsonIgnore]
    public string ReasoningText
    {
        get
        {
            if (!string.IsNullOrEmpty(ReasoningContent))
                return ReasoningContent;

            switch (ReasoningRaw)
            {
                case null:
                    return null;
                case string s:
                    return s;
                case Newtonsoft.Json.Linq.JObject obj when obj["summary"] is { } summary:
                    return summary.ToString().Trim('[', ']', '"');
                case Newtonsoft.Json.Linq.JArray arr:
                    return string.Join("\n", arr
                                             .Where(t => t["text"] != null)
                                             .Select(t => t["text"].ToString()));
                default:
                    return ReasoningRaw.ToString();
            }
        }
    }

    /// <summary>
    /// Raw reasoning array OpenRouter attaches alongside the string (reasoning_details),
    /// kept for completeness but not needed for rendering.
    /// </summary>
    [JsonProperty("reasoning_details")] public object ReasoningDetails { get; set; }
    [JsonProperty("tool_calls")] public ChatMessageToolCall[] ToolCalls { get; set; }
    [JsonProperty("tool_call_id")] public string ToolCallId { get; set; }
}

public class ChatMessageToolCall
{
    [JsonProperty("id")] public string Id { get; set; }
    [JsonProperty("type")] public string Type { get; set; }
    [JsonProperty("function")] public ChatMessageFunction Function { get; set; }
}

public class ChatMessageFunction
{
    [JsonProperty("arguments")] public string Arguments { get; set; }
    [JsonProperty("name")] public string Name { get; set; }
}