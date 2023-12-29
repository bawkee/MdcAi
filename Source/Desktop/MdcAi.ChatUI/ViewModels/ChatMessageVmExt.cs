namespace MdcAi.ChatUI.ViewModels;

using MdcAi.ChatUI.LocalDal;

public static class ChatMessageVmExt
{
    public static IEnumerable<ChatMessageVm> GetNextMessages(this ChatMessageVm head)
    {
        var message = head;
        while (message != null)
        {
            yield return message;
            message = message.Next?.Selector.Message;
        }
    }

    public static WebViewChatMessageDto GetWebViewDto(this ChatMessageVm m)
    {
        if (m == null)
            return null;

        return new()
        {
            Id = m.Id,
            Role = m.Role,
            Content = m.HTMLContent ?? $"<p>{m.Content}</p>",
            Version = m.Selector.Version,
            VersionCount = m.Selector.Versions.Count,
            CreatedTs = m.CreatedTs
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
                Version = v + 1
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