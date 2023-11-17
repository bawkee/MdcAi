namespace Mdc.OpenAiApi;

public class ChatResult : ApiResult
{
    [JsonProperty("id")] public string Id { get; set; }
    [JsonProperty("choices")] public IReadOnlyList<ChatChoice> Choices { get; set; }
    [JsonProperty("usage")] public ChatUsage Usage { get; set; }

    public override string ToString() => Choices.FirstOrDefault()?.ToString();
}