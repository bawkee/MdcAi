namespace Mdc.OpenAiApi;

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
    }

    [JsonProperty("role")] public string Role { get; set; } = ChatMessageRole.User;
    [JsonProperty("content")] public string Content { get; set; }
    [JsonProperty("name")] public string Name { get; set; }
}