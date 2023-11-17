namespace MdcAi.ChatUI.ViewModels;

public class WebViewRequestDto
{
    public string Name { get; set; }
    public object Data { get; set; }
}

public class WebViewSetMessagesRequestDto
{
    public WebViewChatMessageDto[] Messages { get; set; }
}
