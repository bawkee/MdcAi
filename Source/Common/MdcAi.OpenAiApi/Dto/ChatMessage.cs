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

using Newtonsoft.Json.Linq;

/// <summary>
/// One OpenAI-compatible protocol message. This is BOTH a response/streaming delta shape and a
/// request-history shape. The agentic work depends on exact protocol continuity, so the copy
/// constructor deep-copies every field the continuation needs and raw reasoning holders are typed
/// JSON (JToken/JArray) rather than <c>object</c> - deep cloning and exact serialization must be
/// deterministic. See the implementation proposal §5.3 / §9.1.
/// </summary>
public class ChatMessage
{
    public ChatMessage() { }

    public ChatMessage(ChatMessageRole role, string content)
    {
        Role = role;
        Content = content;
    }

    /// <summary>Role-only message - e.g. an assistant node that carries tool_calls and no (or null) content.</summary>
    public ChatMessage(ChatMessageRole role)
    {
        Role = role;
    }

    /// <summary>
    /// Deep copy: reasoning, tool calls and protocol identity fields are preserved so a tool
    /// continuation never has to be reconstructed from display state.
    /// </summary>
    public ChatMessage(ChatMessage basedOn)
    {
        if (basedOn == null)
            throw new ArgumentNullException();

        Role = basedOn.Role;
        Content = basedOn.Content;
        Name = basedOn.Name;
        ReasoningContent = basedOn.ReasoningContent;
        ReasoningRaw = basedOn.ReasoningRaw?.DeepClone() as JToken;
        ReasoningDetails = basedOn.ReasoningDetails?.DeepClone() as JArray;
        ToolCalls = basedOn.ToolCalls?.Select(tc => new ChatMessageToolCall(tc)).ToArray();
        ToolCallId = basedOn.ToolCallId;

        ExtensionData = basedOn.ExtensionData == null
                            ? null
                            : new Dictionary<string, JToken>(
                                basedOn.ExtensionData.Select(kv =>
                                    new KeyValuePair<string, JToken>(kv.Key, kv.Value?.DeepClone() as JToken)));
    }

    // Content keeps an explicit-null presence because DeepSeek/OpenRouter distinguish
    // "assistant content is null" (tool-calling assistant, thinking completion) from an absent
    // property; the app's global request serializer ignores nulls, so this property opts back in.
    [JsonProperty("content", NullValueHandling = NullValueHandling.Include)]
    public string Content { get; set; }

    [JsonProperty("name")] public string Name { get; set; }

    [JsonProperty("role")] public string Role { get; set; } = ChatMessageRole.User;

    /// <summary>
    /// The reasoning/thinking tokens a model emitted before its answer (DeepSeek
    /// <c>reasoning_content</c>, OpenAI <c>reasoning_content</c> on <c>delta</c> chunks,
    /// OpenRouter's pass-through of the same). Present on completed responses and on
    /// streaming deltas; null when the provider sent none. DeepSeek thinking mode currently
    /// REQUIRES replaying this on assistant history messages when the request carries tools,
    /// so it is preserved by the copy constructor and serialized back when non-null.
    /// </summary>
    [JsonProperty("reasoning_content")] public string ReasoningContent { get; set; }

    /// <summary>
    /// OpenRouter's own reasoning field on <c>delta</c> (and some <c>message</c>) payloads.
    /// Some routes surface thinking here as an incremental STRING rather than in
    /// <c>reasoning_content</c>; Anthropic-style routes send an object instead. Kept as raw
    /// JSON so <see cref="ReasoningText"/> can normalize both without losing fidelity.
    /// </summary>
    [JsonProperty("reasoning")] public JToken ReasoningRaw { get; set; }

    /// <summary>
    /// Raw reasoning array OpenRouter attaches alongside the string (<c>reasoning_details</c>).
    /// Structured/signed/encrypted blocks must be preserved byte/sequence faithful for replay;
    /// it's a JArray (never reconstructed from display text) and deep-copied with the message.
    /// </summary>
    [JsonProperty("reasoning_details")] public JArray ReasoningDetails { get; set; }

    /// <summary>
    /// Normalized reasoning text for this message/delta - a DISPLAY projection only. Never use
    /// it to reconstruct provider protocol state; replay comes from the raw fields above.
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
                case JValue jv when jv.Type == JTokenType.String:
                    return (string)jv;
                case JObject obj when obj["summary"] is { } summary:
                    return summary.ToString().Trim('[', ']', '"');
                case JArray arr:
                    return string.Join("\n", arr
                                             .Where(t => t["text"] != null)
                                             .Select(t => t["text"].ToString()));
                default:
                    return ReasoningRaw.ToString();
            }
        }
    }

    [JsonProperty("tool_calls")] public ChatMessageToolCall[] ToolCalls { get; set; }

    [JsonProperty("tool_call_id")] public string ToolCallId { get; set; }

    /// <summary>
    /// Unknown protocol fields the app elects to preserve (Newtonsoft extension data). Keeping
    /// them means a message that carries a field this build doesn't model survives the
    /// request/response round trip instead of being silently dropped. Re-serialized verbatim.
    /// </summary>
    [JsonExtensionData] public IDictionary<string, JToken> ExtensionData { get; set; }
}

/// <summary>
/// A tool call inside an assistant message. Non-streaming responses carry it without an index
/// (array position is the order); streaming <c>delta.tool_calls[i]</c> chunks carry an
/// <c>index</c> so the assembler can accumulate fragmented calls by their stable slot.
/// </summary>
public class ChatMessageToolCall
{
    public ChatMessageToolCall() { }

    public ChatMessageToolCall(ChatMessageToolCall basedOn)
    {
        if (basedOn == null)
            throw new ArgumentNullException();

        Id = basedOn.Id;
        Type = basedOn.Type;
        Index = basedOn.Index;
        Function = basedOn.Function == null ? null : new ChatMessageFunction(basedOn.Function);
    }

    [JsonProperty("id")] public string Id { get; set; }
    [JsonProperty("type")] public string Type { get; set; }
    [JsonProperty("index")] public int? Index { get; set; }
    [JsonProperty("function")] public ChatMessageFunction Function { get; set; }
}

public class ChatMessageFunction
{
    public ChatMessageFunction() { }

    public ChatMessageFunction(ChatMessageFunction basedOn)
    {
        if (basedOn == null)
            throw new ArgumentNullException();

        Arguments = basedOn.Arguments;
        Name = basedOn.Name;
    }

    [JsonProperty("arguments")] public string Arguments { get; set; }
    [JsonProperty("name")] public string Name { get; set; }
}