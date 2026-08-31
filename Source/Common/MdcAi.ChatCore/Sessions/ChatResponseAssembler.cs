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

namespace MdcAi.ChatCore.Sessions;

using System.Text;
using MdcAi.OpenAiApi;
using Newtonsoft.Json.Linq;

/// <summary>
/// A first-class, tested streaming assembler - NOT string concatenation inside a view model
/// (DSH proposal §6.2 "Streaming assembler"). Accumulates one assistant message from SSE chunks:
///
/// - content and reasoning_content are appended independently;
/// - raw delta.reasoning (string or structured) is preserved;
/// - reasoning_details blocks are kept byte/sequence faithful (signed/encrypted blocks must not
///   be reordered);
/// - tool calls accumulate by delta.tool_calls[i].index, not by the current chunk array position;
/// - the first non-empty id/type is retained and fragmented names/arguments are appended;
/// - usage-only empty-choices chunks retain usage;
/// - finish_reason / request id / model are recorded.
/// </summary>
public sealed class ChatResponseAssembler
{
    private readonly StringBuilder _content = new();
    private readonly StringBuilder _reasoningContent = new();
    private readonly SortedDictionary<int, AssembledToolCall> _toolCallsByIndex = new();
    private JToken _reasoning;
    private JArray _reasoningDetails;
    private string _requestId;
    private string _id;

    /// <summary>True once any content, reasoning or tool-call delta was accepted.</summary>
    public bool HasAcceptedDelta { get; private set; }

    public string FinishReason { get; private set; }
    public string Model { get; private set; }
    public ChatUsage Usage { get; private set; }

    public bool IsMaxTokens => string.Equals(FinishReason, "length", StringComparison.OrdinalIgnoreCase);

    public string Content => _content.ToString();
    public string ReasoningContent => _reasoningContent.ToString();
    public JToken ReasoningRaw => _reasoning;
    public JArray ReasoningDetails => _reasoningDetails;

    /// <summary>Completed tool calls in model order (by their index).</summary>
    public IReadOnlyList<ChatMessageToolCall> ToolCalls { get; private set; } = Array.Empty<ChatMessageToolCall>();

    /// <summary>
    /// Every accumulated tool call has an id, a name and arguments that parse as complete JSON.
    /// Partial argument JSON (because finish_reason:length cut it off) makes this false.
    /// </summary>
    public bool HasCompleteToolArguments { get; private set; } = true;

    /// <summary>Accumulate one SSE chunk. Usage-only empty-choices chunks retain usage and return.</summary>
    public void Accept(ChatResult chunk)
    {
        _requestId ??= chunk.RequestId;
        _id ??= chunk.Id;
        Model ??= chunk.Model;

        if (chunk.Usage != null)
            Usage = chunk.Usage;

        if (chunk.Choices == null || chunk.Choices.Count == 0)
            return; // usage-only final chunk

        // The app forces n=1 for tool sessions; take the first choice.
        var choice = chunk.Choices[0];

        if (!string.IsNullOrEmpty(choice.FinishReason))
            FinishReason = choice.FinishReason;

        var delta = choice.Delta;
        if (delta == null)
            return;

        var acceptedAny = false;

        if (!string.IsNullOrEmpty(delta.Content))
        {
            _content.Append(delta.Content);
            acceptedAny = true;
        }

        if (!string.IsNullOrEmpty(delta.ReasoningContent))
        {
            _reasoningContent.Append(delta.ReasoningContent);
            acceptedAny = true;
        }

        if (delta.ReasoningRaw != null)
        {
            _reasoning = MergeReasoning(_reasoning, delta.ReasoningRaw);
            acceptedAny = true;
        }

        if (delta.ReasoningDetails is { Count: > 0 })
        {
            _reasoningDetails ??= new JArray();
            foreach (var block in delta.ReasoningDetails)
                _reasoningDetails.Add(block.DeepClone());
            acceptedAny = true;
        }

        if (delta.ToolCalls is { Length: > 0 })
        {
            foreach (var deltaCall in delta.ToolCalls)
                AccumulateToolCall(deltaCall);
            acceptedAny = true;
        }

        if (acceptedAny)
            HasAcceptedDelta = true;
    }

    private void AccumulateToolCall(ChatMessageToolCall deltaCall)
    {
        var index = deltaCall.Index ?? 0;

        if (!_toolCallsByIndex.TryGetValue(index, out var existing))
        {
            existing = new AssembledToolCall();
            _toolCallsByIndex[index] = existing;
        }

        existing.Id ??= deltaCall.Id;
        existing.Type ??= deltaCall.Type;

        if (deltaCall.Function != null)
        {
            existing.Function ??= new ChatMessageFunction();
            existing.Function.Name ??= deltaCall.Function.Name;
            if (!string.IsNullOrEmpty(deltaCall.Function.Arguments))
                existing.Function.Arguments += deltaCall.Function.Arguments;
        }
    }

    /// <summary>Merge an incoming raw reasoning value into the accumulated one.</summary>
    private static JToken MergeReasoning(JToken current, JToken incoming)
    {
        // String reasoning is assembled (incremental text), structured stays last-wins or appended.
        if (incoming is JValue jv && jv.Type == JTokenType.String)
        {
            var text = (string)jv;
            if (current is JValue currentStr && currentStr.Type == JTokenType.String)
                return new JValue((string)currentStr + text);
            return current == null ? new JValue(text) : current;
        }

        return incoming.DeepClone();
    }

    /// <summary>
    /// The current aggregate streaming state, pushed to the sink while the stream runs. The sink
    /// treats this as the LATEST state of the single in-flight placeholder node (an aggregate
    /// update, not an append) - commit, not this, carries the canonical message. Adapters that
    /// need real token cadence sample/coalesce; the core sources of truth remain this + commit.
    /// </summary>
    public ChatAssistantDelta BuildCurrentDelta() => new(
        _content.Length == 0 ? null : _content.ToString(),
        _reasoningContent.Length == 0 ? null : _reasoningContent.ToString(),
        _reasoning,
        _reasoningDetails,
        MaterializeToolCalls());

    private IReadOnlyList<ChatMessageToolCall> MaterializeToolCalls()
    {
        if (_toolCallsByIndex.Count == 0)
            return Array.Empty<ChatMessageToolCall>();

        var list = new List<ChatMessageToolCall>();
        foreach (var kv in _toolCallsByIndex)
        {
            var ts = kv.Value;
            list.Add(new ChatMessageToolCall
            {
                Id = ts.Id,
                Type = ts.Type ?? "function",
                Function = ts.Function == null
                               ? null
                               : new ChatMessageFunction { Name = ts.Function.Name, Arguments = ts.Function.Arguments }
            });
        }

        return list;
    }

    /// <summary>Freeze the tool calls and argument-completeness flags once the stream ends.</summary>
    public void Seal()
    {
        if (_toolCallsByIndex.Count == 0)
        {
            ToolCalls = Array.Empty<ChatMessageToolCall>();
            HasCompleteToolArguments = true;
            return;
        }

        var list = new List<ChatMessageToolCall>();
        foreach (var kv in _toolCallsByIndex)
        {
            var ts = kv.Value;
            var complete = !string.IsNullOrEmpty(ts.Id) &&
                           !string.IsNullOrEmpty(ts.Function?.Name) &&
                           ts.Function.Arguments != null &&
                           IsCompleteJson(ts.Function.Arguments);
            if (!complete)
                HasCompleteToolArguments = false;

            list.Add(new ChatMessageToolCall
            {
                Id = ts.Id,
                Type = ts.Type ?? "function",
                Function = ts.Function == null
                               ? null
                               : new ChatMessageFunction { Name = ts.Function.Name, Arguments = ts.Function.Arguments }
            });
        }

        ToolCalls = list;
    }

    private static bool IsCompleteJson(string arguments)
    {
        if (string.IsNullOrWhiteSpace(arguments))
            return false;
        try
        {
            JToken.Parse(arguments);
            return true;
        }
        catch (JsonReaderException)
        {
            return false;
        }
    }

    /// <summary>
    /// Builds the final assistant protocol message. Content is null when the model returned none
    /// (a pure tool-call step) - the explicit-null presence is preserved for replay.
    /// </summary>
    public ChatMessage BuildAssistantMessage()
    {
        Seal();

        var message = new ChatMessage(ChatMessageRole.Assistant)
        {
            Content = _content.Length == 0 ? null : _content.ToString(),
            ReasoningContent = _reasoningContent.Length == 0 ? null : _reasoningContent.ToString(),
            ReasoningRaw = _reasoning,
            ReasoningDetails = _reasoningDetails,
            ToolCalls = ToolCalls.Count == 0 ? null : ToolCalls.ToArray()
        };

        return message;
    }

    /// <summary>
    /// Builds a <see cref="ChatAssistantRecord"/> for the driver. When IsMaxTokens or a partial
    /// tool call happened, the record flags the incomplete state so the driver never executes
    /// half-assembled arguments.
    /// </summary>
    public ChatAssistantRecord BuildRecord()
    {
        var message = BuildAssistantMessage();
        return new ChatAssistantRecord(
            message,
            IsMaxTokens,
            HasCompleteToolArguments,
            FinishReason,
            _requestId ?? _id,
            Usage);
    }

    private sealed class AssembledToolCall
    {
        public string Id { get; set; }
        public string Type { get; set; }
        public ChatMessageFunction Function { get; set; }
    }
}