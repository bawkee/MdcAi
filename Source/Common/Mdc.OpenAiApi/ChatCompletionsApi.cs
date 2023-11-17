namespace Mdc.OpenAiApi;

public partial class OpenAIApi
{
    internal const string ChatCompletionsUrl = "chat/completions";

    public async Task<ChatResult> CreateChatCompletions(ChatRequest request)
    {
        request.Streaming = false;
        return await Client.RequestAsync<ChatResult>(new RelativeUri(ChatCompletionsUrl), HttpMethod.Post, request);        
    }

    public IAsyncEnumerable<ChatResult> CreateChatCompletionsStream(ChatRequest request)
    {
        request.Streaming = true;
        return Client.RequestStreamingAsync<ChatResult>(new RelativeUri(ChatCompletionsUrl), HttpMethod.Post, request);
    }
}
