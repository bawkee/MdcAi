#region Copyright Notice
// Copyright (c) 2023 Bojan Sala
//   Licensed under the Apache License, Version 2.0 (the "License");
//   you may not use this file except in compliance with the License.
//   You may obtain a copy of the License at
//      http: www.apache.org/licenses/LICENSE-2.0
//   Unless required by applicable law or agreed to in writing, software
//   distributed under the License is distributed on an "AS IS" BASIS,
//   WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
//   See the License for the specific language governing permissions and
//   limitations under the License.
#endregion

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

    private readonly ArgumentBasedMemoize _mem = new();

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

        _mem.Clear();

        Client.DefaultRequestHeaders.Authorization = new("Bearer", ApiKey);
        Client.AddOrUpdateDefaultHeader("Api-Key", ApiKey);
        Client.AddOrUpdateDefaultHeader("OpenAI-Organization", Organisation);
    }

    public Task<AiModel[]> GetModels() =>
        _mem.GetMemoized(async () =>
        {
            var response = await Client.RequestAsync(new RelativeUri("models"), HttpMethod.Get);
            var responseStr = await response.Content.ReadAsStringAsync();
            var res = JsonConvert.DeserializeObject<AiModels>(responseStr);
            return res.Models;
        });

    public void Dispose() { Client.Dispose(); }
}