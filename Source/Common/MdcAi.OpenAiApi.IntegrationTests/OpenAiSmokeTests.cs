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
/// Smoke tests against the live OpenAI endpoint. Skipped unless the "OpenAI:ApiKey" user
/// secret is configured (the user adds it locally):
///   dotnet user-secrets set "OpenAI:ApiKey" "sk-..." --project "<repo>\Source\Common\MdcAi.OpenAiApi.IntegrationTests\MdcAi.OpenAiApi.IntegrationTests.csproj"
/// </summary>
public class OpenAiSmokeTests
{
    private static OpenAiClient Client() => new(AiProviders.OpenAi, new AiProviderCredentials
    {
        ApiKey = TestSecrets.OpenAiApiKey
    });

    [Fact]
    public async Task Models_endpoint_returns_openai_catalog()
    {
        if (!TestSecrets.HasOpenAiKey) return;
        using var client = Client();

        var models = await client.GetModels();

        Assert.NotEmpty(models);
        Assert.All(models, m => Assert.Equal(AiProviders.OpenAiKey, m.ProviderKey));
        Assert.Contains(models, m => m.ModelID.StartsWith("gpt"));
    }

    [Fact]
    public async Task Non_streaming_completion_returns_content()
    {
        if (!TestSecrets.HasOpenAiKey) return;
        using var client = Client();

        var result = await client.CreateChatCompletions(new ChatRequest
        {
            Model = "gpt-4o-mini",
            Messages = new[]
            {
                new ChatMessage { Role = ChatMessageRole.User, Content = "Reply with the single word: pong." }
            },
            MaxTokens = 10
        });

        Assert.NotEmpty(result.Choices);
        Assert.NotNull(result.Choices[0].Message.Content);
        Assert.Contains("pong", result.Choices[0].Message.Content, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Streaming_completion_aggregates_tokens()
    {
        if (!TestSecrets.HasOpenAiKey) return;
        using var client = Client();

        var request = new ChatRequest
        {
            Model = "gpt-4o-mini",
            // Streaming is set internally by CreateChatCompletionsStream.
            Messages = new[]
            {
                new ChatMessage { Role = ChatMessageRole.User, Content = "Reply with the single word: stream." }
            },
            MaxTokens = 10
        };

        var content = string.Empty;
        await foreach (var chunk in client.CreateChatCompletionsStream(request))
        {
            if (chunk.Choices.Count > 0 && chunk.Choices[0].Delta?.Content != null)
                content += chunk.Choices[0].Delta.Content;
        }

        Assert.Contains("stream", content, StringComparison.OrdinalIgnoreCase);
    }
}