namespace Mdc.OpenAiApi;

public class ChatChoice
{
    [JsonProperty("index")] public int Index { get; set; }
    [JsonProperty("message")] public ChatMessage Message { get; set; }
    [JsonProperty("finish_reason")] public string FinishReason { get; set; }
    [JsonProperty("delta")] public ChatMessage Delta { get; set; }

    public override string ToString() => Message.Content;
}