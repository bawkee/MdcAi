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

namespace MdcAi.OpenAiApi.IntegrationTests;

/// <summary>
/// Real-network smoke tests against OpenRouter. They verify the pieces the app depends on:
///  1. the models catalog is fetchable + stamped/grouped,
///  2. non-streaming completions work (the app's non-stream path),
///  3. streaming works end-to-end incl. OpenRouter's `: OPENROUTER PROCESSING` comments and the
///     final usage-only chunk (the app's streaming path).
/// Tests early-return (count as passed) if the OpenRouter key secret isn't configured.
/// </summary>
public class OpenRouterSmokeTests
{
    private static OpenAiClient Client() => new(AiProviders.OpenRouter, new AiProviderCredentials
    {
        ApiKey = TestSecrets.OpenRouterApiKey,
        RefererUrl = "http://localhost:3431/",
        AppTitle = "MDC AI"
    });

    [Fact]
    public async Task Models_endpoint_returns_stamped_and_grouped_catalog()
    {
        if (!TestSecrets.HasOpenRouterKey) return;
        using var client = Client();

        var models = await client.GetModels();

        Assert.NotEmpty(models);
        Assert.All(models, m => Assert.Equal(AiProviders.OpenRouterKey, m.ProviderKey));
        Assert.All(models, m => Assert.False(string.IsNullOrEmpty(m.GroupKey)));
        Assert.Contains(models, m => m.ModelID.Contains('/'));
        Assert.Contains(models, m => !string.IsNullOrEmpty(m.Name) || m.ContextLength > 0 || m.Pricing != null);
    }

    [Fact]
    public async Task Non_streaming_completion_returns_content()
    {
        if (!TestSecrets.HasOpenRouterKey) return;
        using var client = Client();

        var result = await client.CreateChatCompletions(new ChatRequest
        {
            Model = "openai/gpt-4o-mini",
            Messages = new[]
            {
                new ChatMessage { Role = ChatMessageRole.System, Content = "You are a terse assistant. Answer in at most one sentence." },
                new ChatMessage { Role = ChatMessageRole.User, Content = "What is 2+2? Reply with only the number." }
            },
            MaxTokens = 20
        });

        Assert.NotEmpty(result.Choices);
        Assert.NotNull(result.Choices[0].Message.Content);
        Assert.Contains("4", result.Choices[0].Message.Content, StringComparison.OrdinalIgnoreCase);
        Assert.NotNull(result.Id);
        Assert.NotNull(result.Usage);
        Assert.True(result.Usage.TotalTokens > 0);
    }

    [Fact]
    public async Task Streaming_completion_aggregates_tokens_including_final_usage_chunk()
    {
        if (!TestSecrets.HasOpenRouterKey) return;
        using var client = Client();

        var request = new ChatRequest
        {
            Model = "openai/gpt-4o-mini",
            // Streaming is set internally by CreateChatCompletionsStream.
            Messages = new[]
            {
                new ChatMessage { Role = ChatMessageRole.System, Content = "You are a terse assistant. Answer in at most one sentence." },
                new ChatMessage { Role = ChatMessageRole.User, Content = "Say 'hello world' exactly." }
            },
            MaxTokens = 20
        };

        var chunks = new List<ChatResult>();
        var content = string.Empty;
        ChatUsage usage = null;

        await foreach (var chunk in client.CreateChatCompletionsStream(request))
        {
            chunks.Add(chunk);
            if (chunk.Choices.Count > 0 && chunk.Choices[0].Delta?.Content != null)
                content += chunk.Choices[0].Delta.Content;
            if (chunk.Usage != null)
                usage = chunk.Usage;
        }

        Assert.NotEmpty(chunks);
        Assert.Contains("hello", content, StringComparison.OrdinalIgnoreCase);
        Assert.NotNull(usage);
        Assert.True(usage.TotalTokens > 0);

        // The provider sends usage exactly once, near the end of the stream.
        Assert.Contains(chunks, c => c.Usage != null);
    }

    [Fact]
    public async Task Unknown_model_throws_typed_exception()
    {
        if (!TestSecrets.HasOpenRouterKey) return;
        using var client = Client();

        var ex = await Assert.ThrowsAnyAsync<OpenAiApiException>(() => client.CreateChatCompletions(new ChatRequest
        {
            Model = "this/model-definitely-does-not-exist",
            Messages = new[]
            {
                new ChatMessage { Role = ChatMessageRole.User, Content = "ping" }
            },
            MaxTokens = 5
        }));

        Assert.False(string.IsNullOrEmpty(ex.Message));
    }

    [Fact]
    public async Task Router_aggregates_openrouter_catalog_without_openai_key()
    {
        if (!TestSecrets.HasOpenRouterKey) return;
        using var router = new ChatApiRouter(p => p.Key == AiProviders.OpenRouterKey
            ? new AiProviderCredentials
            {
                ApiKey = TestSecrets.OpenRouterApiKey,
                RefererUrl = "http://localhost:3431/",
                AppTitle = "MDC AI"
            }
            : new AiProviderCredentials());

        var models = await router.GetAllModels();

        Assert.NotEmpty(models);
        Assert.All(models, m => Assert.Equal(AiProviders.OpenRouterKey, m.ProviderKey));
    }
}