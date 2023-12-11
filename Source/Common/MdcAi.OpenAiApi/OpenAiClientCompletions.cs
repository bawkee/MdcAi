namespace MdcAi.OpenAiApi;

public partial class OpenAiClient
{
    internal const string ChatCompletionsUrl = "chat/completions";

    public Task<ChatResult> CreateChatCompletions(ChatRequest request)
    {
        request.Streaming = false;
        return Client.RequestAsync<ChatResult>(new RelativeUri(ChatCompletionsUrl), HttpMethod.Post, request);        
    }

    public IAsyncEnumerable<ChatResult> CreateChatCompletionsStream(ChatRequest request)
    {
        request.Streaming = true;
        return Client.RequestStreamingAsync<ChatResult>(new RelativeUri(ChatCompletionsUrl), HttpMethod.Post, request);
    }
}
