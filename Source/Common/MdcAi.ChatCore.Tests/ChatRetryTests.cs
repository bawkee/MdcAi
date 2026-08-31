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

namespace MdcAi.ChatCore.Tests;

using MdcAi.ChatCore.Sessions;
using MdcAi.ChatCore.Tools;
using MdcAi.OpenAiApi;

/// <summary>
/// Durable provider-request recovery (DSH proposal §6.5 / P1-11): eligible transient failures
/// before any delta retry the EXACT frozen request after cancellable backoff without duplicate
/// assistant nodes; ineligible failures, failures after a delta, and budget exhaustion never
/// retry; cancellation during backoff never dispatches.
/// </summary>
public class ChatRetryTests
{
    private readonly ScriptedFakeApi _api = new();
    private readonly ScriptedFakeApi.FakeClock _clock = new();

    private ChatSessionService BuildService(ChatRetryPolicy policy) =>
        new(_api, ChatToolRegistry.Build(Array.Empty<IChatTool>()), retryPolicy: policy, clock: _clock);

    private static ChatTurnRequest Turn() =>
        new("c1", "turn-1", "msg-1", AiProviders.OpenRouterKey, "deepseek/deepseek-chat", null,
            "You are helpful.", null, Array.Empty<string>(), ChatTurnOrigin.Human, null,
            ChatTurnLimits.Default);

    private static InMemorySink Sink() =>
        new() { Messages = { new ChatMessage(ChatMessageRole.User, "hi") } };

    [Fact]
    public async Task Eligible_transient_failure_before_delta_retries_exact_request_and_succeeds()
    {
        _api.EnqueueThrows(new OpenAiApiException("HTTP 500 Internal Server Error"));
        CallChunks(inspect: req =>
            {
                Assert.Null(req.Tools);
            },
            FakeChunks.Content("recovered answer"), FakeChunks.Finish());

        var sink = Sink();
        var result = await BuildService(ChatRetryPolicy.Default).RunTurnAsync(Turn(), sink, CancellationToken.None);

        Assert.Equal(ChatTurnOutcome.Completed, result.Outcome);
        Assert.Equal("recovered answer", sink.Messages[^1].Content);

        // Two identical requests went out; the retried attempt is the same frozen request.
        Assert.Equal(2, _api.Requests.Count);
        Assert.Equal(_api.Requests[0].ProviderKey, _api.Requests[1].ProviderKey);
        Assert.Equal(_api.Requests[0].Messages.Count, _api.Requests[1].Messages.Count);

        // Attempt lifecycle: started -> failed/scheduled -> started -> completed. No duplicate
        // assistant nodes in the transcript (only the final answer node committed).
        var attempts = sink.Attempts.Select(a => (a.AttemptNumber, a.Status, a.RetryDisposition)).ToArray();
        Assert.Contains(attempts, a => a == (1, "started", "none"));
        Assert.Contains(attempts, a => a == (1, "failed", "scheduled"));
        Assert.Contains(attempts, a => a == (2, "completed", "none"));
        Assert.Single(sink.Committed);
        Assert.Single(_clock.Delays);
        Assert.Equal(TimeSpan.FromMilliseconds(500), _clock.Delays[0]); // policy initial delay
    }

    [Fact]
    public async Task Ineligible_auth_failure_never_retries()
    {
        _api.EnqueueThrows(new OpenAiInvalidApiKeyException("invalid api key"));

        var sink = Sink();
        var result = await BuildService(ChatRetryPolicy.Default).RunTurnAsync(Turn(), sink, CancellationToken.None);

        Assert.Equal(ChatTurnOutcome.Failed, result.Outcome);
        Assert.Equal("invalid_api_key", result.ErrorCode);
        Assert.Single(_api.Requests);
        Assert.Empty(_clock.Delays);
        Assert.DoesNotContain(sink.Attempts, a => a.RetryDisposition == "scheduled");
    }

    [Fact]
    public async Task Failure_after_accepted_delta_never_retries()
    {
        // Delivers a content prefix, then the SAME stream fails mid-flight.
        _api.EnqueueThenThrow(new OpenAiApiException("HTTP 500 Internal Server Error"),
                              FakeChunks.Content("prefix "));

        var sink = Sink();
        var result = await BuildService(ChatRetryPolicy.Default).RunTurnAsync(Turn(), sink, CancellationToken.None);

        // The failure after a delivered prefix finalizes that prefix as failed - no retry.
        Assert.Equal(ChatTurnOutcome.Failed, result.Outcome);
        Assert.Single(_api.Requests);
        Assert.Empty(_clock.Delays);
        Assert.Contains(sink.Abandoned, a => a.KeepPrefix);
    }

    [Fact]
    public async Task Budget_exhaustion_fails_without_further_attempts()
    {
        var policy = new ChatRetryPolicy(MaxAttempts: 2, InitialDelay: TimeSpan.FromMilliseconds(50),
                                         MaxDelay: TimeSpan.FromMilliseconds(50));
        _api.EnqueueThrows(new OpenAiApiException("HTTP 500 Internal Server Error"));
        _api.EnqueueThrows(new OpenAiApiException("HTTP 500 Internal Server Error"));

        var sink = Sink();
        var result = await BuildService(policy).RunTurnAsync(Turn(), sink, CancellationToken.None);

        Assert.Equal(ChatTurnOutcome.Failed, result.Outcome);
        Assert.Equal(2, _api.Requests.Count);
        Assert.Equal(1, _clock.Delays.Count); // one backoff between the two attempts
    }

    [Fact]
    public async Task Cancellation_during_backoff_never_dispatches_retry()
    {
        // A long backoff so cancellation lands inside the wait.
        var policy = new ChatRetryPolicy(MaxAttempts: 3, InitialDelay: TimeSpan.FromSeconds(30),
                                         MaxDelay: TimeSpan.FromSeconds(30));
        _api.EnqueueThrows(new OpenAiApiException("HTTP 500 Internal Server Error"));
        _api.EnqueueStream(FakeChunks.Content("must not be consumed"), FakeChunks.Finish());

        using var cts = new CancellationTokenSource();
        var sink = Sink();
        var run = BuildService(policy).RunTurnAsync(Turn(), sink, cts.Token);

        // Wait for the scheduled disposition to be persisted, then cancel during backoff.
        await WaitUntilAsync(() => sink.Attempts.Any(a => a.RetryDisposition == "scheduled"));
        cts.Cancel();

        var result = await run;

        Assert.Equal(ChatTurnOutcome.Cancelled, result.Outcome);
        Assert.Single(_api.Requests); // the retry was never dispatched
        Assert.DoesNotContain(sink.Attempts, a => a.AttemptNumber == 2 && a.Status == "started");
    }

    private void CallChunks(Action<ChatRequest> inspect, params ChatResult[] chunks) =>
        _api.EnqueueStream(inspect, chunks);

    private static async Task WaitUntilAsync(Func<bool> condition, int timeoutMs = 5000)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (!condition())
        {
            if (sw.ElapsedMilliseconds > timeoutMs)
                throw new TimeoutException("Condition was not met in time.");
            await Task.Delay(20);
        }
    }
}