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

namespace MdcAi.OpenAiApi.Tests;

public class ChatApiRouterTests
{
    private static readonly string[] OpenRouterModels =
    {
        "anthropic/claude-3-5-sonnet",
        "openai/gpt-4o-mini"
    };

    private static readonly string[] OpenAiModels =
    {
        "gpt-4o",
        "o1-mini"
    };

    private static FakeHttpMessageHandler AllModelsHandler() =>
        new(request =>
        {
            if (request.RequestUri.ToString().EndsWith("/models"))
            {
                var ids = request.RequestUri.Host == "openrouter.ai" ? OpenRouterModels : OpenAiModels;
                var data = string.Join(",", ids.Select(id => "{\"id\":\"" + id + "\"}"));
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent($"{{\"data\":[{data}]}}", Encoding.UTF8, "application/json")
                };
            }

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""
                    {
                      "id": "chatcmpl-test",
                      "model": "gpt-4o",
                      "choices": [{ "index": 0, "message": { "role": "assistant", "content": "hi" } }]
                    }
                    """, Encoding.UTF8, "application/json")
            };
        });

    private static ChatApiRouter Router(FakeHttpMessageHandler handler, bool openAiKey = true, bool openRouterKey = true) =>
        new(
            p => new AiProviderCredentials { ApiKey = p.Key == AiProviders.OpenAiKey && openAiKey ? "sk-oa" : p.Key == AiProviders.OpenRouterKey && openRouterKey ? "sk-or" : null },
            _ => handler.Client(_.BaseUrl));

    [Fact]
    public void Routes_completion_by_model_id()
    {
        var handler = AllModelsHandler();
        using var router = Router(handler);

        var result = router.CreateChatCompletions(new ChatRequest { Model = "anthropic/claude-3-5-sonnet" })
                           .GetAwaiter().GetResult();

        var request = Assert.Single(handler.Requests);
        Assert.Equal("openrouter.ai", request.RequestUri.Host);
        Assert.EndsWith("/chat/completions", request.RequestUri.AbsolutePath);
        Assert.Contains("""model":"anthropic/claude-3-5-sonnet""" ,
                        request.JsonBody());
    }

    [Fact]
    public void Routes_openai_model_to_openai_client()
    {
        var handler = AllModelsHandler();
        using var router = Router(handler);

        router.CreateChatCompletions(new ChatRequest { Model = "gpt-4o" }).GetAwaiter().GetResult();

        var request = Assert.Single(handler.Requests);
        Assert.Equal("api.openai.com", request.RequestUri.Host);
    }

    [Fact]
    public void Routes_by_explicit_provider_key_over_heuristic()
    {
        var handler = AllModelsHandler();
        using var router = Router(handler);

        // A bare OpenAI-looking id explicitly routed to OpenRouter must hit OpenRouter.
        router.CreateChatCompletions(new ChatRequest
                                     {
                                         Model = "gpt-4o",
                                         ProviderKey = AiProviders.OpenRouterKey
                                     })
               .GetAwaiter().GetResult();

        var request = Assert.Single(handler.Requests);
        Assert.Equal("openrouter.ai", request.RequestUri.Host);
    }

    [Fact]
    public void ResolveProviderForRequest_prefers_known_key_and_falls_back_to_heuristic()
    {
        using var router = Router(new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)));

        Assert.Equal(AiProviders.OpenRouter,
                     router.ResolveProviderForRequest(new ChatRequest { Model = "gpt-4o", ProviderKey = AiProviders.OpenRouterKey }));
        Assert.Equal(AiProviders.OpenAi,
                     router.ResolveProviderForRequest(new ChatRequest { Model = "gpt-4o" }));
        Assert.Equal(AiProviders.OpenRouter,
                     router.ResolveProviderForRequest(new ChatRequest { Model = "anthropic/claude-3-5-sonnet" }));
        // Unknown provider key degrades to the legacy heuristic instead of throwing.
        Assert.Equal(AiProviders.OpenAi,
                     router.ResolveProviderForRequest(new ChatRequest { Model = "gpt-4o", ProviderKey = "deepseek" }));
    }

    [Fact]
    public void GetAllModels_skips_openai_when_no_key()
    {
        var handler = AllModelsHandler();
        using var router = Router(handler, openAiKey: false, openRouterKey: true);

        var models = router.GetAllModels().GetAwaiter().GetResult();

        Assert.Equal(OpenRouterModels, models.Select(m => m.ModelID).ToArray());
        Assert.All(models, m => Assert.Equal(AiProviders.OpenRouterKey, m.ProviderKey));
    }

    [Fact]
    public void GetAllModels_collects_both_when_both_configured()
    {
        var handler = AllModelsHandler();
        using var router = Router(handler);

        var models = router.GetAllModels().GetAwaiter().GetResult();

        Assert.Equal(OpenRouterModels.Concat(OpenAiModels).OrderBy(x => x), models.Select(m => m.ModelID).OrderBy(x => x));
    }

    [Fact]
    public void HasCredentials_reflects_provider_keys()
    {
        using var router = Router(new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)),
                                  openAiKey: false, openRouterKey: true);

        Assert.False(router.HasCredentials(AiProviders.OpenAiKey));
        Assert.True(router.HasCredentials(AiProviders.OpenRouterKey));
        Assert.False(router.IsConfigured); // active provider defaults to OpenAI
    }

    [Fact]
    public void ActiveProvider_switches_and_survives_unknown()
    {
        using var router = Router(new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)));

        router.SetActiveProvider(AiProviders.OpenRouter);
        Assert.Equal(AiProviders.OpenRouter, router.ActiveProvider);

        router.SetActiveProvider(null);
        Assert.Equal(AiProviders.Default, router.ActiveProvider);
    }

    [Fact]
    public void RefreshCredentials_recreates_clients()
    {
        var handler = AllModelsHandler();
        using var router = Router(handler, openAiKey: false, openRouterKey: true);

        router.GetAllModels().GetAwaiter().GetResult();

        // User adds an OpenAI key later.
        var reRouter = Router(handler, openAiKey: true, openRouterKey: true);
        reRouter.RefreshCredentials();
        var models = reRouter.GetAllModels().GetAwaiter().GetResult();

        Assert.Equal(OpenRouterModels.Concat(OpenAiModels).OrderBy(x => x), models.Select(m => m.ModelID).OrderBy(x => x));
    }
}