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

using Microsoft.Extensions.Logging;
using SalaTools.Core;

/// <summary>
/// dsh-style provider router: one <see cref="IOpenAiApi"/> that owns one <see cref="OpenAiClient"/>
/// per known provider and delegates calls. Which provider a request goes to is derived from the
/// model id: OpenRouter ids always carry an "{author}/{model}" slash, OpenAI ids don't, so we can
/// route per-request without the UI having to remember "which provider" separately from the model.
/// 
/// The active provider is the one the UI shows/should default to; both catalogs can be live at the
/// same time (OpenRouter's /models endpoint is public, OpenAI's needs a key).
/// </summary>
public class ChatApiRouter : IOpenAiApi, IDisposable
{
    private readonly Func<AiProvider, AiProviderCredentials> _credentialsProvider;
    private readonly Func<AiProvider, bool> _hasCredentials;
    private readonly Func<AiProvider, HttpClient> _clientFactory;
    private readonly Dictionary<string, OpenAiClient> _clients = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _lock = new();

    /// <summary>App-level catalog fetch. Shared by every conversation; invalidated by
    /// <see cref="RefreshCredentials"/> (i.e. whenever any credential changes) so it never
    /// goes stale, but a conversation open never repeats the fetch or the vault reads.</summary>
    private Task<AiModel[]> _allModelsFetch;

    public AiProvider ActiveProvider { get; private set; } = AiProviders.Default;
    public IReadOnlyList<AiProvider> Providers => AiProviders.All;

    public bool HasCredentials(string providerKey) => _hasCredentials(AiProviders.Get(providerKey));

    public ChatApiRouter(Func<AiProvider, AiProviderCredentials> credentialsProvider = null,
                         Func<AiProvider, HttpClient> clientFactory = null,
                         Func<AiProvider, bool> hasCredentials = null)
    {
        _credentialsProvider = credentialsProvider ?? (_ => new AiProviderCredentials());
        _clientFactory = clientFactory;
        // Default matches the old behavior (full credential read). The app wires in a
        // one-slot check instead: whether a provider can be talked to is decided by its
        // API key alone, and reading the other (optional) slots here would be vault work
        // for a question that doesn't need it.
        _hasCredentials = hasCredentials ?? (p => _credentialsProvider(p).HasKey);
    }

    /// <summary>Switches the app-level active provider (used as default / shown in pickers).</summary>
    public void SetActiveProvider(AiProvider provider)
    {
        ActiveProvider = provider ?? AiProviders.Default;
        _ = ClientFor(ActiveProvider);
    }

    /// <summary>
    /// Drops the cached clients AND the catalog so the next request re-reads credentials from
    /// the provider function. Call after the user edits any credential.
    /// </summary>
    public void RefreshCredentials()
    {
        lock (_lock)
        {
            foreach (var client in _clients.Values)
                client.Dispose();
            _clients.Clear();
            _allModelsFetch = null;
        }
    }

    public bool IsConfigured => HasCredentials(ActiveProvider.Key);

    /// <summary>The provider a model id routes to: "/" => OpenRouter, otherwise OpenAI.</summary>
    public AiProvider ResolveProviderForModel(string modelId) => AiProviders.GetProviderForModelId(modelId);

    public Task<AiModel[]> GetModels() => ClientFor(ActiveProvider).GetModels();

    /// <summary>
    /// Composite catalog: every provider that can respond (OpenRouter has a public models list,
    /// OpenAI needs a key). Providers that fail auth are skipped - the picker should never show
    /// "OpenAI" models the user can't actually run.
    /// 
    /// Fetched once and shared by every consumer (each conversation's LoadModelsCmd, the
    /// category editor, ...); <see cref="RefreshCredentials"/> clears it, so a credential
    /// change still re-fetches. The Task-based cache also means concurrent callers coalesce
    /// onto a single fetch instead of stampeding the endpoints.
    /// </summary>
    public Task<AiModel[]> GetAllModels()
    {
        lock (_lock)
        {
            return _allModelsFetch ??= FetchAllModelsAsync();
        }
    }

    private async Task<AiModel[]> FetchAllModelsAsync()
    {
        var all = new List<AiModel>();

        foreach (var provider in AiProviders.All)
        {
            // OpenAI's /models requires auth; without a key there is nothing to show.
            if (!HasCredentials(provider.Key) && provider.Key == AiProviders.OpenAiKey)
                continue;

            try
            {
                all.AddRange(await ClientFor(provider).GetModels());
            }
            catch (OpenAiApiAuthException)
            {
                // Not configured or the key is bad - skip this provider's section.
            }
            catch (HttpRequestException)
            {
                // Provider unreachable - don't kill the whole catalog.
            }
            catch (ObjectDisposedException)
            {
                // A credential refresh disposed the client mid-flight; the next call after
                // RefreshCredentials rebuilds the cache from scratch.
            }
        }

        return all.ToArray();
    }

    public Task<ChatResult> CreateChatCompletions(ChatRequest request)
    {
        var provider = ResolveProviderForModel(request.Model);
        return ClientFor(provider).CreateChatCompletions(request);
    }

    public IAsyncEnumerable<ChatResult> CreateChatCompletionsStream(ChatRequest request)
    {
        var provider = ResolveProviderForModel(request.Model);
        return ClientFor(provider).CreateChatCompletionsStream(request);
    }

    private OpenAiClient ClientFor(AiProvider provider)
    {
        lock (_lock)
        {
            if (_clients.TryGetValue(provider.Key, out var existing))
                return existing;

            HttpClient http = null;
            if (_clientFactory != null)
                http = _clientFactory(provider) ?? new SafeHttpClient { BaseAddress = provider.BaseUrl };

            var client = _clientFactory == null
                ? new OpenAiClient(provider, _credentialsProvider(provider))
                : new OpenAiClient(provider, _credentialsProvider(provider), http);
            _clients[provider.Key] = client;
            return client;
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            foreach (var client in _clients.Values)
                client.Dispose();
            _clients.Clear();
            _allModelsFetch = null;
        }
    }
}