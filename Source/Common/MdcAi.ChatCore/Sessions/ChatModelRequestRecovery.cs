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

namespace MdcAi.ChatCore.Sessions;

using MdcAi.OpenAiApi;

/// <summary>
/// Stable failure categories for provider-request recovery (DSH proposal §6.5). Retry only
/// transient categories; never retry auth, invalid request, quota exhaustion, context overflow,
/// cancellation, or any failure after an assistant delta was accepted.
/// </summary>
public static class ChatFailureClassifier
{
    public const string RateLimit = "rate_limit";
    public const string Server = "server";
    public const string Timeout = "timeout";
    public const string Transport = "transport";
    public const string Auth = "auth";
    public const string InvalidRequest = "invalid_request";
    public const string Quota = "quota";
    public const string ContextOverflow = "context_overflow";
    public const string Unknown = "unknown";

    public static string Classify(Exception ex) => ex switch
    {
        OpenAiInvalidApiKeyException => Auth,
        OpenAiApiAuthException => Auth,
        OpenAiApiQuotaException => Quota,
        TaskCanceledException => Timeout,
        HttpRequestException => Transport,
        OpenAiApiException api => ClassifyApi(api),
        _ => Unknown
    };

    private static string ClassifyApi(OpenAiApiException api)
    {
        var message = api.Message ?? string.Empty;

        if (Contains(message, "context length")
            || Contains(message, "maximum context")
            || Contains(message, "token limit"))
            return ContextOverflow;

        if (Contains(message, "429") || Contains(message, "rate limit"))
            return RateLimit;

        if (Contains(message, "500") || Contains(message, "502") || Contains(message, "503")
            || Contains(message, "internal server"))
            return Server;

        return InvalidRequest;
    }

    private static bool Contains(string haystack, string needle) =>
        haystack.Contains(needle, StringComparison.OrdinalIgnoreCase);

    /// <summary>Transient categories eligible for a bounded pre-delta retry.</summary>
    public static bool IsRetryable(string category) =>
        category is RateLimit or Server or Timeout or Transport;
}

/// <summary>
/// Conservative, provider-aware retry budget (DSH proposal §6.5): at most three attempts,
/// 500 ms initial delay, 10 s maximum, bounded exponential backoff with jitter.
/// </summary>
public sealed record ChatRetryPolicy(
    int MaxAttempts = 3,
    TimeSpan InitialDelay = default,
    TimeSpan MaxDelay = default)
{
    public static ChatRetryPolicy Default { get; } = new(
        MaxAttempts: 3,
        InitialDelay: TimeSpan.FromMilliseconds(500),
        MaxDelay: TimeSpan.FromSeconds(10));

    /// <summary>Deterministic bounded exponential backoff for the given retry number (1-based).</summary>
    public TimeSpan DelayForRetry(int retryNumber, TimeSpan? retryAfter = null)
    {
        if (retryAfter is { } header && header > TimeSpan.Zero)
            return TimeSpan.FromTicks(Math.Min(header.Ticks, MaxDelay.Ticks));

        var exponent = Math.Max(0, retryNumber - 1);
        var scaled = TimeSpan.FromTicks(InitialDelay.Ticks * (1L << Math.Min(exponent, 8)));
        return TimeSpan.FromTicks(Math.Min(scaled.Ticks, MaxDelay.Ticks));
    }
}

/// <summary>Injected, cancellable time source so retry backoff is deterministic in tests.</summary>
public interface IChatClock
{
    DateTimeOffset UtcNow { get; }
    Task DelayAsync(TimeSpan duration, CancellationToken ct);
}

public sealed class SystemChatClock : IChatClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;

    public Task DelayAsync(TimeSpan duration, CancellationToken ct) => Task.Delay(duration, ct);
}