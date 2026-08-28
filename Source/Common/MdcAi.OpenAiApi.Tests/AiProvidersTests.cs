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

public class AiProvidersTests
{
    [Fact]
    public void Registry_conatins_openai_and_openrouter_with_expected_endpoints()
    {
        Assert.Equal(2, AiProviders.All.Count);

        var openAi = AiProviders.Get(AiProviders.OpenAiKey);
        Assert.Equal("OpenAI", openAi.DisplayName);
        Assert.Equal(new Uri("https://api.openai.com/v1/"), openAi.BaseUrl);
        Assert.Equal("gpt-4o", openAi.DefaultModel);

        var openRouter = AiProviders.Get(AiProviders.OpenRouterKey);
        Assert.Equal("OpenRouter", openRouter.DisplayName);
        Assert.Equal(new Uri("https://openrouter.ai/api/v1/"), openRouter.BaseUrl);
    }

    [Fact]
    public void Get_falls_back_to_default_provider_for_unknown_key()
    {
        Assert.Same(AiProviders.Default, AiProviders.Get("does-not-exist"));
    }

    [Theory]
    [InlineData("gpt-4o", true)]
    [InlineData("gpt-4-turbo", true)]
    [InlineData("chatgpt-4o-latest", true)]
    [InlineData("o1-mini", true)]
    [InlineData("o3-mini", true)]
    [InlineData("text-embedding-ada-002", false)]
    [InlineData("whisper-1", false)]
    public void OpenAi_conversational_classification(string id, bool expected) =>
        Assert.Equal(expected, AiProviders.OpenAi.IsConversationalModel(new AiModel(id)));

    [Theory]
    [InlineData("o1", true)]
    [InlineData("o1-mini", true)]
    [InlineData("o3-mini", true)]
    [InlineData("o4-mini", true)]
    [InlineData("gpt-4o", false)]
    public void OpenAi_reasoning_classification(string id, bool expected) =>
        Assert.Equal(expected, AiProviders.OpenAi.IsReasoningModel(new AiModel(id)));

    [Theory]
    [InlineData("anthropic/claude-3.5-sonnet", true)]
    [InlineData("meta-llama/llama-3.2-3b-instruct", true)]
    [InlineData("deepseek/deepseek-chat", true)]
    [InlineData("openai/gpt-4o", true)]
    [InlineData("openai/o1", true)]
    [InlineData("oleksiil/uform-gen2-dpo", true)] // image models are chat-capable too
    [InlineData("text-embedding-3-small", false)]  // bare id without provider, not routed here
    public void OpenRouter_conversational_classification(string id, bool expected) =>
        Assert.Equal(expected, AiProviders.OpenRouter.IsConversationalModel(new AiModel(id)));

    [Fact]
    public void OpenRouter_reasoning_uses_structured_metadata_when_present()
    {
        var mandatory = new AiModel("anthropic/claude-sonnet-4.5")
        {
            Reasoning = new AiModelReasoning { Mandatory = true }
        };
        Assert.True(AiProviders.OpenRouter.IsReasoningModel(mandatory));

        var plain = new AiModel("anthropic/claude-sonnet-4.5")
        {
            Reasoning = new AiModelReasoning { Mandatory = false, DefaultEnabled = false }
        };
        Assert.False(AiProviders.OpenRouter.IsReasoningModel(plain));
    }

    [Fact]
    public void OpenRouter_reasoning_falls_back_to_id_heuristics()
    {
        Assert.True(AiProviders.OpenRouter.IsReasoningModel(new AiModel("openai/o3-mini")));
        Assert.True(AiProviders.OpenRouter.IsReasoningModel(new AiModel("deepseek/deepseek-reasoner")));
        Assert.False(AiProviders.OpenRouter.IsReasoningModel(new AiModel("deepseek/deepseek-chat")));
    }

    [Fact]
    public void Model_routing_splats_openrouter_otherwise_openai()
    {
        Assert.Same(AiProviders.OpenRouter, AiProviders.GetProviderForModelId("anthropic/claude-3.5-sonnet"));
        Assert.Same(AiProviders.OpenRouter, AiProviders.GetProviderForModelId("meta-llama/llama-3.1-8b-instruct"));
        Assert.Same(AiProviders.OpenAi, AiProviders.GetProviderForModelId("gpt-4o"));
        Assert.Same(AiProviders.OpenAi, AiProviders.GetProviderForModelId("o1-mini"));
        Assert.Same(AiProviders.OpenAi, AiProviders.GetProviderForModelId(null));
    }

    [Fact]
    public void OpenRouter_model_grouping_uses_author()
    {
        Assert.Equal("anthropic", AiProviders.OpenRouter.ModelGroupKey(new AiModel("anthropic/claude-3-5-sonnet")));
        Assert.Equal("deepseek", AiProviders.OpenRouter.ModelGroupKey(new AiModel("deepseek/deepseek-chat")));
        Assert.Equal("openai", AiProviders.OpenRouter.ModelGroupKey(new AiModel("openai/gpt-4o-mini")));
    }

    [Fact]
    public void OpenAi_headers_set_bearer_and_org()
    {
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{}", Encoding.UTF8, "application/json")
        });
        using var client = new OpenAiClient(AiProviders.OpenAi, new AiProviderCredentials
        {
            ApiKey = "sk-test",
            Organisation = "org-123"
        }, handler.Client(AiProviders.OpenAi.BaseUrl));

        var auth = client.Client.DefaultRequestHeaders.Authorization;
        Assert.Equal("Bearer", auth.Scheme);
        Assert.Equal("sk-test", auth.Parameter);
        Assert.Contains("org-123", client.Client.DefaultRequestHeaders.GetValues("OpenAI-Organization"));
        Assert.Equal("sk-test", client.Client.DefaultRequestHeaders.GetValues("Api-Key").First());
    }

    [Fact]
    public void OpenRouter_headers_set_bearer_and_attribution()
    {
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{}", Encoding.UTF8, "application/json")
        });
        using var client = new OpenAiClient(AiProviders.OpenRouter, new AiProviderCredentials
        {
            ApiKey = "sk-or-v1-test",
            RefererUrl = "http://localhost:3431/",
            AppTitle = "MDC AI"
        }, handler.Client(AiProviders.OpenRouter.BaseUrl));

        var auth = client.Client.DefaultRequestHeaders.Authorization;
        Assert.Equal("Bearer", auth.Scheme);
        Assert.Equal("sk-or-v1-test", auth.Parameter);
        Assert.Equal("http://localhost:3431/", client.Client.DefaultRequestHeaders.GetValues("HTTP-Referer").First());
        Assert.Equal("MDC AI", client.Client.DefaultRequestHeaders.GetValues("X-OpenRouter-Title").First());
    }
}