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

using LocalDal;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using OpenAiApi;

public static class ChatMessageVmExt
{
    /// <summary>Walks the CURRENT selected branch as ChatMessageVm nodes (Head → Tail).</summary>
    public static IEnumerable<ChatMessageVm> GetNextMessages(this ChatMessageVm head)
    {
        var message = head;
        while (message != null)
        {
            yield return message;
            message = message.Next?.Selector.Message;
        }
    }

    /// <summary>
    /// The exact protocol message for this node. This is the ONLY history source for the
    /// agentic loop: every request is derived from the accepted fork, never a private list.
    /// </summary>
    public static ChatMessage ToProtocolMessage(this ChatMessageVm m)
    {
        if (m == null)
            return null;

        return new ChatMessage
        {
            Role = m.Role,
            Content = m.Content,
            ReasoningContent = m.ReasoningContent,
            ReasoningRaw = m.ReasoningRaw?.DeepClone() as JToken,
            ReasoningDetails = m.ReasoningDetails?.DeepClone() as JArray,
            ToolCalls = m.ToolCalls?.Select(tc => new ChatMessageToolCall(tc)).ToArray(),
            ToolCallId = m.ToolCallId
        };
    }

    /// <summary>
    /// The current selected branch projected into protocol messages, excluding an optional
    /// in-flight assistant node (the driver holds one stable placeholder while it streams).
    /// Protocol order is the branch order; reasoning/tool continuity is preserved exactly.
    /// </summary>
    public static List<ChatMessage> ToProtocolBranch(this ConversationVm conversation, string excludeMessageId = null) =>
        conversation.Head?.Message
                    .GetNextMessages()
                    .Where(m => m.Id != excludeMessageId)
                    .Select(m => m.ToProtocolMessage())
                    .Where(m => m != null)
                    .ToList() ?? new List<ChatMessage>();

    public static WebViewChatMessageDto GetWebViewDto(this ChatMessageVm m)
    {
        if (m == null)
            return null;

        // The model that (re)generated this message, persisted on it when the completion
        // ran. User messages and legacy rows (persisted before per-message provenance
        // existed) carry null here — the renderer shows a generic label for those.
        var modelId = m.Model;
        var provider = modelId == null ? null : AiProviders.GetProviderForModelId(modelId).DisplayName;

        return new()
        {
            Id = m.Id,
            Role = m.Role,
            Content = m.HTMLContent ?? $"<p>{m.Content}</p>",
            Version = m.Selector.Version,
            VersionCount = m.Selector.Versions.Count,
            CreatedTs = m.CreatedTs,
            Model = modelId,
            Provider = provider,
            Effort = m.Effort,
            Reasoning = m.ReasoningHTMLContent ??
                        (string.IsNullOrEmpty(m.ReasoningContent) ? null : $"<p>{m.ReasoningContent}</p>"),
            ReasoningPreview = m.ReasoningPreview
        };
    }

    public static WebViewRequestDto CreateWebViewSetMessageRequest(this IEnumerable<ChatMessageVm> messages)
    {
        return new()
        {
            Name = "SetMessages",
            Data = new WebViewSetMessagesRequestDto { Messages = messages.Select(m => m.GetWebViewDto()).ToArray() }
        };
    }

    /// <summary>
    /// Flattens the fork tree into DbMessage rows with the FULL protocol surface. The wire
    /// arrays stay authoritative JSON; the renderable text and structured reasoning are kept
    /// apart so replay never depends on parsing prose.
    /// </summary>
    public static IEnumerable<DbMessage> ToDbMessages(this ChatMessageVm source, int idx = 0)
    {
        if (source == null)
            return Enumerable.Empty<DbMessage>();

        return source.Selector.Versions.SelectMany((m, v) =>
        {
            var msg = new DbMessage
            {
                IdMessage = m.Id,
                IdConversation = m.Conversation.Id,
                IdMessageParent = source.Previous?.Id,
                IsCurrentVersion = m.Selector.Message == m,
                Content = m.Content,
                Role = m.Role,
                CreatedTs = m.CreatedTs,
                Version = v + 1,
                Model = m.Model,
                Effort = m.Effort,
                Reasoning = m.ReasoningContent,
                ProviderKey = m.ProviderKey,
                Origin = m.Origin,
                CompletionState = m.CompletionState,
                FinishReason = m.FinishReason,
                ToolCallsJson = m.ToolCalls == null ? null : JsonConvert.SerializeObject(m.ToolCalls),
                ToolCallId = m.ToolCallId,
                ToolName = m.ToolName,
                ToolResultJson = m.ToolResultJson?.ToString(Formatting.None),
                ReasoningRawJson = m.ReasoningRaw?.ToString(Formatting.None),
                ReasoningDetailsJson = m.ReasoningDetails?.ToString(Formatting.None)
            };

            var children = m.Next?.ToDbMessages(idx + 1) ?? Enumerable.Empty<DbMessage>();

            return children.Append(msg);
        });
    }

    public static ChatMessageSelectorVm FromDbMessages(this IEnumerable<DbMessage> messages, ConversationVm convo, string headId = null)
    {
        var headDbMessages = messages.Where(m => m.IdMessageParent == headId)
                                     .OrderBy(m => m.Version)
                                     .ToArray();

        var firstDbHead = headDbMessages.FirstOrDefault();

        if (firstDbHead == null)
            return null;

        var firstHead = new ChatMessageVm(convo, firstDbHead.Role);

        SetMessage(firstDbHead, firstHead);

        var selector = firstHead.Selector;

        if (headDbMessages.Length > 1)
        {
            foreach (var otherDbMessage in headDbMessages[1..])
            {
                var otherMessage = new ChatMessageVm(convo, otherDbMessage.Role, selector);
                SetMessage(otherDbMessage, otherMessage);
                if (otherDbMessage.IsCurrentVersion)
                    selector.Message = otherMessage;
            }
        }

        return selector;

        void SetMessage(DbMessage dbMessage, ChatMessageVm message)
        {
            message.Id = dbMessage.IdMessage;
            message.CreatedTs = dbMessage.CreatedTs;
            message.Role = dbMessage.Role;
            message.Content = dbMessage.Content;
            message.Model = dbMessage.Model;
            message.Effort = dbMessage.Effort;
            message.ReasoningContent = dbMessage.Reasoning;
            message.ProviderKey = dbMessage.ProviderKey;
            message.Origin = dbMessage.Origin;
            message.CompletionState = dbMessage.CompletionState;
            message.FinishReason = dbMessage.FinishReason;
            message.ToolCallId = dbMessage.ToolCallId;
            message.ToolName = dbMessage.ToolName;
            message.ToolResultJson = string.IsNullOrEmpty(dbMessage.ToolResultJson)
                                         ? null
                                         : JToken.Parse(dbMessage.ToolResultJson);
            message.ToolCalls = string.IsNullOrEmpty(dbMessage.ToolCallsJson)
                                    ? null
                                    : JsonConvert.DeserializeObject<ChatMessageToolCall[]>(dbMessage.ToolCallsJson);
            message.ReasoningRaw = string.IsNullOrEmpty(dbMessage.ReasoningRawJson)
                                       ? null
                                       : JToken.Parse(dbMessage.ReasoningRawJson);
            message.ReasoningDetails = string.IsNullOrEmpty(dbMessage.ReasoningDetailsJson)
                                           ? null
                                           : JArray.Parse(dbMessage.ReasoningDetailsJson);

            SetNext(message);
        }

        void SetNext(ChatMessageVm message)
        {
            var nextSelector = FromDbMessages(messages, convo, message.Id);
            if (nextSelector == null)
                return;
            message.Next = nextSelector.Message;
            foreach (var nextMessages in nextSelector.Versions)
                nextMessages.Previous = message;
        }
    }
}