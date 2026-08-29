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

public class SseParsingTests
{
    private static readonly Uri BaseUri = new("https://openrouter.ai/api/v1/");

    [Fact]
    public async Task Parses_openai_style_chunks_and_stops_at_done()
    {
        var handler = FakeHttpMessageHandler.OkStream(
            """data: {"id":"a","object":"chat.completion.chunk","choices":[{"index":0,"delta":{"role":"assistant"}}]}""",
            """data: {"id":"a","choices":[{"index":0,"delta":{"content":"Hel"}}]}""",
            """data: {"id":"a","choices":[{"index":0,"delta":{"content":"lo"}}]}""",
            "data: [DONE]",
            """data: {"id":"a","choices":[{"index":0,"delta":{"content":"ignored"}}]}""");

        using var client = handler.Client(BaseUri);

        var chunks = await client.RequestStreamingAsync<ChatResult>(
                          new Uri("chat/completions", UriKind.Relative),
                          HttpMethod.Post).CollectAsync();

        Assert.Equal(3, chunks.Count);
        Assert.Equal("Hel", chunks[1].Choices[0].Delta.Content);
        Assert.Equal("lo", chunks[2].Choices[0].Delta.Content);
    }

    [Fact]
    public async Task Skips_openrouter_processing_comments_and_blank_lines()
    {
        var handler = FakeHttpMessageHandler.OkStream(
            ": OPENROUTER PROCESSING",
            "",
            """data: {"id":"a","choices":[{"index":0,"delta":{"content":"x"}}]}""",
            ": OPENROUTER PROCESSING",
            "data: [DONE]");

        using var client = handler.Client(BaseUri);

        var chunks = await client.RequestStreamingAsync<ChatResult>(
                          new Uri("chat/completions", UriKind.Relative),
                          HttpMethod.Post).CollectAsync();

        var chunk = Assert.Single(chunks);
        Assert.Equal("x", chunk.Choices[0].Delta.Content);
    }

    [Fact]
    public async Task Emits_empty_choices_usage_chunk_without_crashing()
    {
        // OpenRouter sends usage in the final chunk with an EMPTY choices array.
        var handler = FakeHttpMessageHandler.OkStream(
            """data: {"id":"a","choices":[{"index":0,"delta":{"content":"hi"}}]}""",
            """data: {"id":"a","choices":[],"usage":{"prompt_tokens":10,"completion_tokens":5,"total_tokens":15,"cost":0.00005}}""",
            "data: [DONE]");

        using var client = handler.Client(BaseUri);

        var chunks = await client.RequestStreamingAsync<ChatResult>(
                          new Uri("chat/completions", UriKind.Relative),
                          HttpMethod.Post).CollectAsync();

        Assert.Equal(2, chunks.Count);
        Assert.Equal(0, chunks[1].Choices.Count);
        Assert.Equal(15, chunks[1].Usage.TotalTokens);
        Assert.Equal(0.00005m, chunks[1].Usage.Cost);
    }

    [Fact]
    public async Task Throws_on_mid_stream_error_event()
    {
        var handler = FakeHttpMessageHandler.OkStream(
            """data: {"id":"a","choices":[{"index":0,"delta":{"content":"partial"}}]}""",
            """data: {"id":"a","error":{"code":402,"message":"Insufficient credits to run this request.","metadata":{}},"choices":[{"index":0,"delta":{},"finish_reason":"error"}]}""");

        using var client = handler.Client(BaseUri);

        var ex = await Assert.ThrowsAnyAsync<OpenAiApiException>(async () =>
        {
            var chunks = new List<ChatResult>();
            await foreach (var chunk in client.RequestStreamingAsync<ChatResult>(
                               new Uri("chat/completions", UriKind.Relative),
                               HttpMethod.Post))
                chunks.Add(chunk);
        });

        Assert.Contains("Insufficient credits", ex.Message);
        Assert.IsType<OpenAiApiQuotaException>(ex);
    }

    [Fact]
    public async Task Reads_request_metadata_headers_once()
    {
        var handler = new FakeHttpMessageHandler(_ =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """data: {"id":"a","choices":[{"index":0,"delta":{"content":"hi"}}]}""" + "\n" + "data: [DONE]",
                    Encoding.UTF8, "text/event-stream")
            };
            response.Headers.Add("X-Generation-Id", "gen-123");
            response.Headers.Add("OpenAI-Processing-Ms", "42");
            return response;
        });

        using var client = handler.Client(BaseUri);

        var chunks = await client.RequestStreamingAsync<ChatResult>(
                          new Uri("chat/completions", UriKind.Relative),
                          HttpMethod.Post).CollectAsync();

        Assert.Equal("gen-123", chunks[0].RequestId);
        Assert.Equal(TimeSpan.FromMilliseconds(42), chunks[0].ProcessingTime);
    }

    [Fact]
    public async Task Captures_openrouter_reasoning_field_deltas()
    {
        // OpenRouter routes some hosts (e.g. Baidu-hosted DeepSeek) surface thinking in
        // delta.reasoning (incremental string) instead of reasoning_content - exactly the
        // shape seen on deepseek/deepseek-v4-flash-0731. Make sure ReasoningText picks it up.
        var handler = FakeHttpMessageHandler.OkStream(
            """data: {"id":"a","choices":[{"index":0,"delta":{"content":"","role":"assistant","reasoning":"The"}}]}""",
            """data: {"id":"a","choices":[{"index":0,"delta":{"content":"","reasoning":" user instructed"}}]}""",
            """data: {"id":"a","choices":[{"index":0,"delta":{"content":"","reasoning":" me"}}]}""",
            """data: {"id":"a","choices":[{"index":0,"delta":{"content":"Apple."}}]}""",
            "data: [DONE]");

        using var client = handler.Client(BaseUri);

        var chunks = await client.RequestStreamingAsync<ChatResult>(
                          new Uri("chat/completions", UriKind.Relative),
                          HttpMethod.Post).CollectAsync();

        Assert.Equal(4, chunks.Count);
        Assert.Equal("The", chunks[0].Choices[0].Delta.ReasoningText);
        Assert.Equal(" user instructed", chunks[1].Choices[0].Delta.ReasoningText);
        Assert.Equal(" me", chunks[2].Choices[0].Delta.ReasoningText);
        Assert.Null(chunks[3].Choices[0].Delta.ReasoningText); // answer chunk carries none
        Assert.Equal("Apple.", chunks[3].Choices[0].Delta.Content);
    }

    [Fact]
    public async Task Captures_reasoning_content_deltas_alongside_content()
    {
        // DeepSeek-style thinking-mode streams interleave reasoning_content (before any
        // answer text) with regular content deltas; the delta DTO must surface both.
        var handler = FakeHttpMessageHandler.OkStream(
            """data: {"id":"a","choices":[{"index":0,"delta":{"role":"assistant","content":null,"reasoning_content":""}}]}""",
            """data: {"id":"a","choices":[{"index":0,"delta":{"reasoning_content":"Let"}}]}""",
            """data: {"id":"a","choices":[{"index":0,"delta":{"reasoning_content":" me think"}}]}""",
            """data: {"id":"a","choices":[{"index":0,"delta":{"content":"The answer"}}]}""",
            "data: [DONE]");

        using var client = handler.Client(BaseUri);

        var chunks = await client.RequestStreamingAsync<ChatResult>(
                          new Uri("chat/completions", UriKind.Relative),
                          HttpMethod.Post).CollectAsync();

        Assert.Equal(4, chunks.Count);
        Assert.Equal("", chunks[0].Choices[0].Delta.ReasoningContent);
        Assert.Equal("Let", chunks[1].Choices[0].Delta.ReasoningContent);
        Assert.Equal(" me think", chunks[2].Choices[0].Delta.ReasoningContent);
        Assert.Null(chunks[3].Choices[0].Delta.ReasoningContent); // answer chunk carries none
        Assert.Equal("The answer", chunks[3].Choices[0].Delta.Content);
    }

    [Fact]
    public async Task Deserializes_full_non_streaming_chat_result()
    {
        var handler = FakeHttpMessageHandler.Ok("""
            {
              "id": "chatcmpl-123",
              "model": "gpt-4o",
              "choices": [
                { "index": 0, "message": { "role": "assistant", "content": "Hello!" }, "finish_reason": "stop" }
              ],
              "usage": { "prompt_tokens": 9, "completion_tokens": 2, "total_tokens": 11 }
            }
            """);

        using var client = handler.Client(BaseUri);

        var result = await client.RequestAsync<ChatResult>(new Uri("chat/completions", UriKind.Relative), HttpMethod.Post);

        Assert.Equal("chatcmpl-123", result.Id);
        Assert.Equal("Hello!", result.Choices[0].Message.Content);
        Assert.Equal("stop", result.Choices[0].FinishReason);
        Assert.Equal(11, result.Usage.TotalTokens);
    }
}