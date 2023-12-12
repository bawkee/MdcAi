namespace MdcAi.ChatUI.ViewModels;

public class WebViewChatMessageDto
{
    public string Id { get; set; }
    public string Role { get; set; }
    public string Content { get; set; }
    public int Version { get; set; }
    public int VersionCount { get; set; }
    public DateTime? CreatedTs { get; set; }
}