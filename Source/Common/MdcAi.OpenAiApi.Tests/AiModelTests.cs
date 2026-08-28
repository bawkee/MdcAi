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

public class AiModelTests
{
    [Fact]
    public void Author_splits_openrouter_id()
    {
        Assert.Equal("anthropic", new AiModel("anthropic/claude-3-5-sonnet").Author);
        Assert.Null(new AiModel("gpt-4o").Author);
    }

    [Fact]
    public void DisplayLabel_prefers_name_over_id()
    {
        var model = new AiModel("anthropic/claude-3-5-sonnet") { Name = "Claude 3.5 Sonnet" };
        Assert.Equal("Claude 3.5 Sonnet", model.DisplayLabel);

        var plain = new AiModel("gpt-4o");
        Assert.Equal("gpt-4o", plain.DisplayLabel);
    }

    [Fact]
    public void Pricing_parses_per_token_to_per_million()
    {
        var pricing = new AiModelPricing
        {
            Prompt = "0.0000002574",
            Completion = "0.0000010287"
        };

        Assert.Equal(0.2574m, pricing.PromptPerMTokens);
        Assert.Equal(1.0287m, pricing.CompletionPerMTokens);
    }

    [Fact]
    public void Pricing_returns_null_for_missing_values()
    {
        var pricing = new AiModelPricing();
        Assert.Null(pricing.PromptPerMTokens);
        Assert.Null(pricing.CompletionPerMTokens);
    }

    [Fact]
    public void OpenRouter_models_json_deserializes_extensions()
    {
        const string json = """
            {
              "data": [
                {
                  "id": "deepseek/deepseek-chat",
                  "name": "DeepSeek",
                  "context_length": 65536,
                  "pricing": { "prompt": "0.0000002574", "completion": "0.0000010287" },
                  "reasoning": { "supported_efforts": ["low", "high"], "default_enabled": false }
                }
              ],
              "total_count": 1
            }
            """;

        var res = JsonConvert.DeserializeObject<AiModels>(json);

        var model = Assert.Single(res.Models);
        Assert.Equal("deepseek/deepseek-chat", model.ModelID);
        Assert.Equal("DeepSeek", model.Name);
        Assert.Equal(65536, model.ContextLength);
        Assert.Equal(0.2574m, model.Pricing.PromptPerMTokens);
        Assert.Equal(new[] { "low", "high" }, model.Reasoning.SupportedEfforts);
    }

    [Fact]
    public void Client_stamps_models_with_provider_and_group()
    {
        var handler = FakeHttpMessageHandler.Ok("""
            {
              "data": [
                { "id": "anthropic/claude-3-5-sonnet", "name": "Claude" },
                { "id": "openai/gpt-4o-mini" }
              ]
            }
            """);

        using var client = new OpenAiClient(AiProviders.OpenRouter, new AiProviderCredentials
        {
            ApiKey = "sk-or-v1-test"
        }, handler.Client(AiProviders.OpenRouter.BaseUrl));

        var models = client.GetModels().GetAwaiter().GetResult();

        Assert.Equal(2, models.Length);
        Assert.All(models, m => Assert.Equal(AiProviders.OpenRouterKey, m.ProviderKey));
        Assert.Equal("anthropic", models[0].GroupKey);
        Assert.Equal("openai", models[1].GroupKey);
        Assert.True(models[0].IsConversational);
    }

    [Fact]
    public void GetModels_hits_provider_models_endpoint()
    {
        var handler = FakeHttpMessageHandler.Ok("""{ "data": [] }""");
        using var client = new OpenAiClient(AiProviders.OpenRouter, new AiProviderCredentials
        {
            ApiKey = "sk-or-v1-test"
        }, handler.Client(AiProviders.OpenRouter.BaseUrl));

        client.GetModels().GetAwaiter().GetResult();

        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.Equal(new Uri("https://openrouter.ai/api/v1/models"), request.RequestUri);
    }

    [Fact]
    public void Reasoning_property_defaults_to_id_heuristics_when_unstamped()
    {
        Assert.True(new AiModel("o1-mini").IsReasoning);
        Assert.False(new AiModel("gpt-4o").IsReasoning);
        Assert.True(new AiModel("anthropic/claude-3-5-sonnet").IsConversational);
    }
}