namespace MdcAi.ChatUI.ViewModels;

using LocalDal;

public static class ConversationVmExt
{
    public static DbConversation ToDbConversation(this ConversationVm source)
    {
        if (source == null)
            return null;

        var messages = source.Head?.Message.ToDbMessages() ?? Enumerable.Empty<DbMessage>();

        var convo = new DbConversation
        {
            IdConversation = source.Id,
            Name = source.Name,
            CreatedTs = source.CreatedTs,
            Messages = messages.ToList(),
            IdCategory = source.IdCategory,
            IsTrash = source.IsTrash,
            IdSettingsOverride = source.IdSettingsOverride
        };

        return convo;
    }

}