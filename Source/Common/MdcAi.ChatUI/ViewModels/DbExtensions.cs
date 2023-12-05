namespace MdcAi.ChatUI.ViewModels;

using LocalDal;

public static class DbExtensions
{
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
                Index = idx
            };

            var children = m.Next?.ToDbMessages(idx + 1) ?? Enumerable.Empty<DbMessage>();

            return children.Append(msg);
        });
    }

    public static DbConversation ToDbConversation(this ConversationVm source)
    {
        if (source == null)
            return null;

        var messages = source.Head?.Message.ToDbMessages() ?? Enumerable.Empty<DbMessage>();

        var convo = new DbConversation()
        {
            IdConversation = source.Id,
            Name = source.Name,
            CreatedTs = source.CreatedTs,
            Messages = messages.ToList(),
            Category = source.Category,
            IsTrash = source.IsTrash
        };

        return convo;
    }

    // TODO: Remove?
    public static ConversationVm FromDbConversation(this DbConversation source)
    {
        var convo = Services.Container.Resolve<ConversationVm>();

        using var _ = convo.SuppressChangeNotifications();

        convo.Id = source.IdConversation;
        convo.Name = source.Name;
        convo.CreatedTs = source.CreatedTs;        

        convo.Head = source.Messages.FromDbMessages(convo);

        return convo;
    }

    /*
     * public class DbMessage
       {
       [Key] public string IdMessage { get; set; }
       public string IdMessageParent { get; set; }
       public string IdConversation { get; set; }
       public int Version { get; set; }
       public bool IsCurrentVersion { get; set; }
       public int Index { get; set; }
       public DateTime CreatedTs { get; set; }
       public string Role { get; set; }
       public string Content { get; set; }
       public string HTMLContent { get; set; }
       }
     */

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