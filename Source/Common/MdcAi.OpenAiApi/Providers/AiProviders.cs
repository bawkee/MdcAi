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

namespace MdcAi.OpenAiApi;

/// <summary>
/// The catalog of providers the app ships with. Each provider is a pure descriptor:
/// base url, credential/header scheme, model classification + grouping rules.
/// Adding a provider = adding one more descriptor here.
/// </summary>
public static class AiProviders
{
    public const string OpenAiKey = "openai";
    public const string OpenRouterKey = "openrouter";

    public static readonly AiProvider OpenAi = new()
    {
        Key = OpenAiKey,
        DisplayName = "OpenAI",
        ApiVersion = "v1",
        BaseUrl = new("https://api.openai.com/v1/"),
        DefaultModel = "gpt-4o",
        IsConversationalModel = IsOpenAiConversational,
        IsReasoningModel = IsOpenAiReasoning,
        ModelGroupKey = static _ => "OpenAI",
        ConfigureDefaultHeaders = (client, creds) =>
        {
            client.AddOrUpdateDefaultHeader("Authorization", $"Bearer {creds.ApiKey}");
            // Legacy header OpenAI used to check; harmless to keep sending.
            client.AddOrUpdateDefaultHeader("Api-Key", creds.ApiKey);
            client.AddOrUpdateDefaultHeader("OpenAI-Organization", creds.Organisation);
        }
    };

    public static readonly AiProvider OpenRouter = new()
    {
        Key = OpenRouterKey,
        DisplayName = "OpenRouter",
        ApiVersion = "v1",
        BaseUrl = new("https://openrouter.ai/api/v1/"),
        // OpenRouter is an OpenAI-compatible gateway; every listed model is chat-capable.
        DefaultModel = "openai/gpt-4o-mini",
        IsConversationalModel = IsOpenRouterConversational,
        IsReasoningModel = IsOpenRouterReasoning,
        ModelGroupKey = static m => m.Author ?? "Other",
        ConfigureDefaultHeaders = (client, creds) =>
        {
            client.AddOrUpdateDefaultHeader("Authorization", $"Bearer {creds.ApiKey}");
            // https://openrouter.ai/docs/app-attribution - rankings + app identity.
            client.AddOrUpdateDefaultHeader("HTTP-Referer", creds.RefererUrl);
            client.AddOrUpdateDefaultHeader("X-OpenRouter-Title", creds.AppTitle);
        }
    };

    public static IReadOnlyList<AiProvider> All { get; } = new[] { OpenAi, OpenRouter };

    /// <summary>First provider a fresh install should default to.</summary>
    public static AiProvider Default => OpenAi;

    public static AiProvider Get(string key) => All.FirstOrDefault(p => p.Key == key) ?? Default;

    public static bool IsKnown(string key) => All.Any(p => p.Key == key);

    /// <summary>
    /// Picks the provider a bare model id belongs to: OpenRouter ids always carry an
    /// "{author}/{model}" slash, everything else is treated as OpenAI's id space.
    /// </summary>
    public static AiProvider GetProviderForModelId(string modelId) =>
        !string.IsNullOrEmpty(modelId) && modelId.Contains('/') ? OpenRouter : OpenAi;

    public static bool IsReasoningId(string modelId) =>
        !string.IsNullOrEmpty(modelId) &&
        (IsOpenAiReasoningId(modelId) || IsOpenRouterReasoningId(modelId));

    /// <summary>
    /// Whether a model id belongs to an effort-capable family. Used only for OpenAI-provider
    /// models (OpenRouter models are decided by their fetched <c>supported_efforts</c>
    /// metadata instead): o1*/o3*/o4*/gpt-5* all take a reasoning_effort parameter.
    /// </summary>
    public static bool IsEffortCapableId(string modelId) =>
        !string.IsNullOrEmpty(modelId) &&
        (IsOpenAiReasoningId(modelId) || IsGpt5FamilyId(modelId));

    private static bool IsGpt5FamilyId(string id) =>
        id.StartsWith("gpt-5", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Best-effort "is this id a chat model" heuristic used when no provider is stamped.
    /// </summary>
    public static bool IsConversationalId(string modelId) =>
        !string.IsNullOrEmpty(modelId) &&
        (IsOpenAiConversationalId(modelId) || IsOpenRouterConversationalId(modelId));

    #region OpenAI classification

    private static bool IsOpenAiConversational(AiModel m) => IsOpenAiConversationalId(m?.ModelID);

    private static bool IsOpenAiReasoning(AiModel m) => IsOpenAiReasoningId(m?.ModelID);

    private static bool IsOpenAiConversationalId(string id) =>
        id.StartsWith("gpt", StringComparison.OrdinalIgnoreCase) ||
        id.StartsWith("chatgpt", StringComparison.OrdinalIgnoreCase) ||
        IsOpenAiReasoningId(id);

    private static bool IsOpenAiReasoningId(string id) =>
        id.StartsWith("o1", StringComparison.OrdinalIgnoreCase) ||
        id.StartsWith("o3", StringComparison.OrdinalIgnoreCase) ||
        id.StartsWith("o4", StringComparison.OrdinalIgnoreCase);

    #endregion

    #region OpenRouter classification

    private static bool IsOpenRouterConversational(AiModel m)
    {
        var id = m?.ModelID;
        if (string.IsNullOrEmpty(id))
            return false;

        // Non-chat modalities that appear in the OpenRouter catalog: embeddings, rerank,
        // moderation, speech/audio/image generation...
        return !id.Contains("embedding", StringComparison.OrdinalIgnoreCase) &&
               !id.Contains("rerank", StringComparison.OrdinalIgnoreCase) &&
               !id.Contains("moderation", StringComparison.OrdinalIgnoreCase) &&
               !id.Contains("whisper", StringComparison.OrdinalIgnoreCase) &&
               !id.Contains("tts", StringComparison.OrdinalIgnoreCase) &&
               !id.Contains("dall-e", StringComparison.OrdinalIgnoreCase) &&
               !id.Contains("audio", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsOpenRouterReasoning(AiModel m)
    {
        // Structured metadata is authoritative when present.
        if (m?.Reasoning != null)
            return m.Reasoning.Mandatory || m.Reasoning.DefaultEnabled ||
                   m.Reasoning.SupportedEfforts?.Length > 0;

        return IsOpenRouterReasoningId(m?.ModelID);
    }

    private static bool IsOpenRouterReasoningId(string id)
    {
        if (string.IsNullOrEmpty(id))
            return false;

        // openai/o1, openai/o3, openai/o4 ...
        if (id.StartsWith("openai/o", StringComparison.OrdinalIgnoreCase))
            return true;

        var slug = id.Contains('/') ? id[(id.LastIndexOf('/') + 1)..] : id;

        return slug.StartsWith("o1", StringComparison.OrdinalIgnoreCase) ||
               slug.StartsWith("o3", StringComparison.OrdinalIgnoreCase) ||
               slug.StartsWith("o4", StringComparison.OrdinalIgnoreCase) ||
               slug.Contains("reasoner", StringComparison.OrdinalIgnoreCase) ||
               slug.Contains("reasoning", StringComparison.OrdinalIgnoreCase) ||
               slug.Contains("thinking", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsOpenRouterConversationalId(string id) =>
        IsOpenRouterConversational(new AiModel(id));

    #endregion
}