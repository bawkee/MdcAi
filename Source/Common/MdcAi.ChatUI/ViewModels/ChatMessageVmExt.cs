namespace MdcAi.ChatUI.ViewModels;

public static class ChatMessageVmExt
{
    public static IEnumerable<ChatMessageVm> GetNextMessages(this ChatMessageVm head)
    {
        var message = head.Selector.Message;
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
}