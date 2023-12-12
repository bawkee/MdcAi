namespace MdcAi.OpenAiApi;

using SalaTools.Core;

public interface IOpenAiApi
{
    string ApiKey { get; }
    string Organisation { get; }
    string ApiVersion { get; }
    Task<AiModel[]> GetModels();
    Task<ChatResult> CreateChatCompletions(ChatRequest request);
    IAsyncEnumerable<ChatResult> CreateChatCompletionsStream(ChatRequest request);
}

public partial class OpenAiClient : IOpenAiApi, IDisposable
{
    public string ApiKey { get; private set; }
    public string Organisation { get; private set; }
    public string ApiVersion { get; }
    public HttpClient Client { get; }

    public OpenAiClient(string apiKey = null,
                     string organisation = null,
                     string apiVersion = "v1",
                     HttpClient client = null)
    {        
        ApiVersion = apiVersion;

        Client = client ?? new SafeHttpClient
        {
            BaseAddress = new($"https://api.openai.com/{ApiVersion}/")
        };

        SetCredentials(apiKey, organisation);
        
        Client.DefaultRequestHeaders.Add("User-Agent", "MdcAi");        
    }

    public void SetCredentials(string apiKey, string organisation)
    {
        ApiKey = apiKey;
        Organisation = organisation;

        Client.DefaultRequestHeaders.Authorization = new("Bearer", ApiKey);
        Client.AddOrUpdateDefaultHeader("Api-Key", ApiKey);
        Client.AddOrUpdateDefaultHeader("OpenAI-Organization", Organisation);
    }

    public async Task<AiModel[]> GetModels()
    {        
        var response = await Client.RequestAsync(new RelativeUri("models"), HttpMethod.Get);
        var responseStr = await response.Content.ReadAsStringAsync();
        var res = JsonConvert.DeserializeObject<AiModels>(responseStr);
        return res.Models;
    }

    public void Dispose() { Client.Dispose(); }
}