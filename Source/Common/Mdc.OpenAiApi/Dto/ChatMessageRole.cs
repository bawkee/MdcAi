namespace Mdc.OpenAiApi;

public class ChatMessageRole : IEquatable<ChatMessageRole>
{
    private ChatMessageRole(string value) { Value = value; }

    public static ChatMessageRole FromString(string roleName) =>
        roleName switch
        {
            "system" => System,
            "user" => User,
            "assistant" => Assistant,
            _ => null
        };

    private string Value { get; }

    public static ChatMessageRole System { get; } = new("system");
    public static ChatMessageRole User { get; } = new("user");
    public static ChatMessageRole Assistant { get; } = new("assistant");
   
    public override bool Equals(object obj) => Value.Equals((obj as ChatMessageRole)?.Value);

    public bool Equals(ChatMessageRole other) => Value.Equals(other?.Value);

    public override int GetHashCode() => Value.GetHashCode();

    public static implicit operator string(ChatMessageRole value) { return value.Value; }

    public static implicit operator ChatMessageRole(string value) { return new(value); }
}