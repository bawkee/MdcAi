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

using System.IO;
using System.Net.Http.Headers;

/// <summary>
/// Cancellation must reach the transport: pre-cancelled tokens abort before any network call
/// and a mid-stream cancellation stops the SSE read. The old TakeUntil-style "stop observing"
/// pattern was insufficient for an agent loop - see DSH_IMPLEMENTATION_PROPOSAL.md §6.1.
/// </summary>
public class CancellationTests
{
    private static readonly Uri BaseUri = new("https://openrouter.ai/api/v1/");

    [Fact]
    public async Task Pre_cancelled_non_streaming_token_aborts_before_any_request()
    {
        var handler = new TokenAwareHandler();
        using var client = new HttpClient(handler) { BaseAddress = BaseUri };
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        // The token reaches HttpClient.SendAsync: a pre-cancelled request surfaces as a
        // cancelled task rather than a response. The handler only records calls that were
        // actually delivered (not already cancelled), proving no request was processed.
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            client.RequestAsync<ChatResult>(
                new Uri("chat/completions", UriKind.Relative),
                HttpMethod.Post,
                new ChatRequest(),
                cts.Token));

        Assert.Empty(handler.Delivered);
    }

    [Fact]
    public async Task Pre_cancelled_streaming_token_aborts_before_any_request()
    {
        var handler = new TokenAwareHandler();
        using var client = new HttpClient(handler) { BaseAddress = BaseUri };
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await foreach (var _ in client.RequestStreamingAsync<ChatResult>(
                                new Uri("chat/completions", UriKind.Relative),
                                HttpMethod.Post,
                                new ChatRequest(),
                                cts.Token))
            {
            }
        });

        Assert.Empty(handler.Delivered);
    }

    /// <summary>
    /// A handler that records only deliveries that were actually allowed by the token - a
    /// cancelled token should prevent the request from being processed at all.
    /// </summary>
    private sealed class TokenAwareHandler : HttpMessageHandler
    {
        public List<HttpRequestMessage> Delivered { get; } = new();

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            if (cancellationToken.IsCancellationRequested)
                return Task.FromCanceled<HttpResponseMessage>(cancellationToken);

            Delivered.Add(request);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""
                                            {"id":"a","choices":[{"index":0,"message":{"role":"assistant","content":"hi"}}]}
                                            """, Encoding.UTF8, "application/json")
            });
        }
    }

    [Fact]
    public async Task Mid_stream_cancellation_stops_the_sse_read()
    {
        using var handler = new CancellableStreamHandler();
        using var client = new HttpClient(handler) { BaseAddress = BaseUri };
        using var cts = new CancellationTokenSource();

        var received = new List<ChatResult>();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await foreach (var chunk in client.RequestStreamingAsync<ChatResult>(
                                new Uri("chat/completions", UriKind.Relative),
                                HttpMethod.Post,
                                new ChatRequest(),
                                cts.Token))
            {
                received.Add(chunk);
                cts.Cancel();
            }
        });

        // We received the first chunk, then the token cancellation surfaced as the read error.
        Assert.Single(received);
        Assert.Equal("Hel", received[0].Choices[0].Delta.Content);
    }

    /// <summary>
    /// A handler whose SSE stream delivers one chunk and then blocks until the request token
    /// cancels - so a caller that stops mid-stream observes the cancellation instead of a hang.
    /// </summary>
    private sealed class CancellableStreamHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var stream = new CancellableChunkStream(cancellationToken);
            var content = new StreamContent(stream);
            content.Headers.ContentType = new MediaTypeHeaderValue("text/event-stream");
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content });
        }

        private sealed class CancellableChunkStream : Stream
        {
            private readonly CancellationToken _token;
            private int _phase;

            public CancellableChunkStream(CancellationToken token) => _token = token;

            public override bool CanRead => true;
            public override bool CanSeek => false;
            public override bool CanWrite => false;
            public override long Length => throw new NotSupportedException();
            public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

            public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
            {
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(_token, cancellationToken);

                if (_phase == 0)
                {
                    _phase = 1;
                    var data = Encoding.UTF8.GetBytes(
                        """data: {"id":"a","object":"chat.completion.chunk","choices":[{"index":0,"delta":{"content":"Hel"}}]}""" + "\n\n");
                    data.CopyTo(buffer, offset);
                    return data.Length;
                }

                await Task.Delay(Timeout.InfiniteTimeSpan, linked.Token);
                return 0;
            }

            public override void Flush() { }
            public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
            public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
            public override void SetLength(long value) => throw new NotSupportedException();
            public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        }
    }
}