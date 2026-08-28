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

/// <summary>
/// The stable seam the UI consumes. Talks to whichever provider is currently active; the
/// concrete implementation is either an <see cref="OpenAiClient"/> (single provider) or a
/// <see cref="ChatApiRouter"/> (switches between providers at runtime).
/// </summary>
public interface IOpenAiApi
{
    /// <summary>Provider the next request will actually go to.</summary>
    AiProvider ActiveProvider { get; }

    /// <summary>All providers this app knows about (for pickers).</summary>
    IReadOnlyList<AiProvider> Providers { get; }

    /// <summary>Whether the app currently has a usable key for the given provider key.</summary>
    bool HasCredentials(string providerKey);

    /// <summary>Fetch the active provider's model catalog.</summary>
    Task<AiModel[]> GetModels();

    /// <summary>
    /// Fetch every reachable provider's catalog (falling back to id heuristics per provider),
    /// stamped with Provider/Group keys. Used by the grouped model pickers.
    /// </summary>
    Task<AiModel[]> GetAllModels();

    Task<ChatResult> CreateChatCompletions(ChatRequest request);
    IAsyncEnumerable<ChatResult> CreateChatCompletionsStream(ChatRequest request);
}

/// <summary>
/// An OpenAI-compatible chat client configured for one provider. The endpoint, auth scheme and
/// model classification all come from the provider descriptor, so the same class speaks to
/// OpenAI, OpenRouter and any other OpenAI-compatible endpoint.
/// </summary>
public partial class OpenAiClient : IOpenAiApi, IDisposable
{
    public AiProvider Provider { get; }
    public IReadOnlyList<AiProvider> Providers => new[] { Provider };
    public AiProvider ActiveProvider => Provider;

    public bool HasCredentials(string providerKey) =>
        providerKey == Provider.Key && !string.IsNullOrEmpty(ApiKey);

    public string ApiKey { get; private set; }
    public string Organisation { get; private set; }
    public string ApiVersion => Provider.ApiVersion;
    public HttpClient Client { get; }

    private readonly ArgumentBasedMemoize _mem = new();

    public OpenAiClient(AiProvider provider = null,
                        AiProviderCredentials credentials = null,
                        HttpClient client = null)
    {
        Provider = provider ?? AiProviders.Default;

        Client = client ?? new SafeHttpClient
        {
            BaseAddress = Provider.BaseUrl
        };

        Client.DefaultRequestHeaders.Add("User-Agent", "MdcAi");

        SetCredentials(credentials ?? new AiProviderCredentials());
    }

    public void SetCredentials(AiProviderCredentials credentials)
    {
        credentials ??= new AiProviderCredentials();

        ApiKey = credentials.ApiKey;
        Organisation = credentials.Organisation;

        _mem.Clear();

        Provider.ConfigureDefaultHeaders(Client, credentials);
    }

    public Task<AiModel[]> GetModels() =>
        _mem.GetMemoized(async () =>
        {
            var response = await Client.RequestAsync(new RelativeUri("models"), HttpMethod.Get);
            var responseStr = await response.Content.ReadAsStringAsync();
            var res = JsonConvert.DeserializeObject<AiModels>(responseStr);

            foreach (var model in res.Models)
                StampModel(model);

            return res.Models;
        });

    /// <summary>Single-provider clients just return their own catalog.</summary>
    public Task<AiModel[]> GetAllModels() => GetModels();

    /// <summary>
    /// Applies the provider's classification + grouping metadata to a fetched model.
    /// </summary>
    internal void StampModel(AiModel model)
    {
        model.ProviderKey = Provider.Key;
        model.GroupKey = Provider.ModelGroupKey(model);
    }

    public void Dispose() { Client.Dispose(); }
}