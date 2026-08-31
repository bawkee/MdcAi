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

namespace MdcAi.ChatUI.Tests;

using MdcAi.ChatUI.ViewModels;
using OpenAiApi;
using ReactiveUI;

public sealed class InMemoryCredsStore : ICredsStore
{
    private readonly Dictionary<string, string> _values = new();

    public string GetValue(string name) => _values.TryGetValue(name, out var v) ? v : null;

    public void SetValue(string name, string value)
    {
        if (value == null)
            _values.Remove(name);
        else
            _values[name] = value;
    }
}

public sealed class FakeOpenAiApi : IOpenAiApi
{
    private readonly Dictionary<string, string> _keys = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, AiModel[]> _models = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Scripted streaming responses, one IAsyncEnumerable per request (agentic tests).</summary>
    public Queue<IAsyncEnumerable<ChatResult>> ScriptedStreams { get; } = new();

    public List<ChatRequest> Requests { get; } = new();

    public AiProvider ActiveProvider { get; set; } = AiProviders.Default;
    public IReadOnlyList<AiProvider> Providers => AiProviders.All;

    public FakeOpenAiApi()
    {
        _keys[AiProviders.OpenAiKey] = "sk-oa";
        _keys[AiProviders.OpenRouterKey] = "sk-or";

        _models[AiProviders.OpenAiKey] = new[]
        {
            Stamped("gpt-4o", AiProviders.OpenAi),
            Stamped("o1-mini", AiProviders.OpenAi)
        };
        _models[AiProviders.OpenRouterKey] = new[]
        {
            Stamped("anthropic/claude-3-5-sonnet", AiProviders.OpenRouter),
            Stamped("openai/gpt-4o-mini", AiProviders.OpenRouter),
            Stamped("deepseek/deepseek-chat", AiProviders.OpenRouter)
        };
    }

    private static AiModel Stamped(string id, AiProvider provider)
    {
        var model = new AiModel(id) { ProviderKey = provider.Key };
        model.GroupKey = provider.ModelGroupKey(model);
        return model;
    }

    public void SetKey(string providerKey, string key) => _keys[providerKey] = key;

    public void SetModels(string providerKey, AiModel[] models) => _models[providerKey] = models;

    public bool HasCredentials(string providerKey) => !string.IsNullOrEmpty(_keys.GetValueOrDefault(providerKey));

    public Task<AiModel[]> GetModels()
    {
        var models = _models.GetValueOrDefault(ActiveProvider.Key) ?? Array.Empty<AiModel>();
        return Task.FromResult(models);
    }

    public Task<AiModel[]> GetAllModels()
    {
        var all = new List<AiModel>();
        if (HasCredentials(AiProviders.OpenAiKey)) all.AddRange(_models[AiProviders.OpenAiKey]);
        if (HasCredentials(AiProviders.OpenRouterKey)) all.AddRange(_models[AiProviders.OpenRouterKey]);
        return Task.FromResult(all.ToArray());
    }

    public Task<ChatResult> CreateChatCompletions(ChatRequest request)
    {
        Requests.Add(request);
        return Task.FromResult(new ChatResult
        {
            Id = "chatcmpl-test",
            Model = request.Model,
            Choices = new[]
            {
                new ChatChoice
                {
                    Index = 0,
                    Message = new ChatMessage { Role = ChatMessageRole.Assistant, Content = "hello from " + AiProviders.GetProviderForModelId(request.Model).DisplayName }
                }
            }
        });
    }

    public Task<ChatResult> CreateChatCompletions(ChatRequest request, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return CreateChatCompletions(request);
    }

    public async IAsyncEnumerable<ChatResult> CreateChatCompletionsStream(ChatRequest request)
    {
        var result = await CreateChatCompletions(request);
        yield return result;
    }

    public IAsyncEnumerable<ChatResult> CreateChatCompletionsStream(ChatRequest request, CancellationToken ct)
    {
        Requests.Add(request);

        if (ScriptedStreams.Count > 0)
            return new CtAwareEnumerable(ScriptedStreams.Dequeue(), ct);

        return CreateChatCompletionsStream(request);
    }

    /// <summary>
    /// Wraps a scripted stream so a blocking MoveNextAsync observes the caller's token - the
    /// agentic loop must be stoppable mid-stream.
    /// </summary>
    private sealed class CtAwareEnumerable : IAsyncEnumerable<ChatResult>
    {
        private readonly IAsyncEnumerable<ChatResult> _inner;
        private readonly CancellationToken _token;

        public CtAwareEnumerable(IAsyncEnumerable<ChatResult> inner, CancellationToken token)
        {
            _inner = inner;
            _token = token;
        }

        public IAsyncEnumerator<ChatResult> GetAsyncEnumerator(CancellationToken ct) =>
            new Enumerator(_inner.GetAsyncEnumerator(ct), _token);

        private sealed class Enumerator : IAsyncEnumerator<ChatResult>
        {
            private readonly IAsyncEnumerator<ChatResult> _inner;
            private readonly CancellationToken _token;

            public Enumerator(IAsyncEnumerator<ChatResult> inner, CancellationToken token)
            {
                _inner = inner;
                _token = token;
            }

            public ChatResult Current => _inner.Current;

            public async ValueTask<bool> MoveNextAsync()
            {
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(_token);
                return await _inner.MoveNextAsync().AsTask().WaitAsync(linked.Token);
            }

            public ValueTask DisposeAsync() => _inner.DisposeAsync();
        }
    }
}

/// <summary>
/// ReactiveUI boots in headless tests: no WinUI dispatcher, so pin the main-thread scheduler
/// to the current thread and swallow exceptions that would otherwise hit the app handler.
/// </summary>
public static class TestRx
{
    public static void Init()
    {
        RxApp.MainThreadScheduler = CurrentThreadScheduler.Instance;
        RxApp.DefaultExceptionHandler = Observer.Create<Exception>(_ => { });
    }
}

/// <summary>Builds a SettingsVm with both provider sections backed by one in-memory store.</summary>
public static class TestSettings
{
    public static SettingsVm Build(InMemoryCredsStore creds) =>
        new(new OpenAiSettingsVm(creds), new OpenRouterSettingsVm(creds));
}