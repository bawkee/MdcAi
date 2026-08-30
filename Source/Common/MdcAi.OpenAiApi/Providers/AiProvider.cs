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
/// Describes one LLM provider the app can talk to - an OpenAI-compatible chat endpoint plus
/// the little bits of behaviour that differ between providers (auth headers, model
/// classification/grouping, default model). Mirrors dsh's provider-route concept: one
/// implementation speaks "OpenAI-compatible chat" against a configurable endpoint.
/// </summary>
public sealed class AiProvider
{
    /// <summary>Stable machine key ("openai", "openrouter", ...). Never change after shipping.</summary>
    public string Key { get; init; }

    /// <summary>Human-readable name for pickers ("OpenAI", "OpenRouter", ...).</summary>
    public string DisplayName { get; init; }

    /// <summary>API version path segment (OpenAI uses "v1", OpenRouter exposes "/api/v1").</summary>
    public string ApiVersion { get; init; } = "v1";

    /// <summary>Full endpoint base, including trailing slash and api version.</summary>
    public Uri BaseUrl { get; init; }

    /// <summary>Model used when a category has no model chosen for this provider yet.</summary>
    public string DefaultModel { get; init; }

    /// <summary>Whether a model id is a chat model for this provider.</summary>
    public Func<AiModel, bool> IsConversationalModel { get; init; } = static _ => true;

    /// <summary>Whether a model id is a reasoning model for this provider (reasoning-effort support, reasoning output handling).</summary>
    public Func<AiModel, bool> IsReasoningModel { get; init; } = static _ => false;

    /// <summary>Heading models of this provider are grouped under in pickers.</summary>
    public Func<AiModel, string> ModelGroupKey { get; init; } = static _ => "Models";

    /// <summary>Applies provider-specific default request headers (auth, attribution, ...).</summary>
    public Action<HttpClient, AiProviderCredentials> ConfigureDefaultHeaders { get; init; } = static (_, _) => { };

    public override string ToString() => DisplayName ?? Key;
}

/// <summary>
/// Credentials + optional metadata headers of one provider, all optional.
/// Feed into <see cref="OpenAiClient.SetCredentials"/>.
/// </summary>
public sealed class AiProviderCredentials
{
    /// <summary>API key / Bearer token.</summary>
    public string ApiKey { get; set; }

    /// <summary>OpenAI organisation, optional.</summary>
    public string Organisation { get; set; }

    /// <summary>App url sent as HTTP-Referer (OpenRouter attribution/rankings).</summary>
    public string RefererUrl { get; set; }

    /// <summary>App title sent as X-OpenRouter-Title (OpenRouter attribution).</summary>
    public string AppTitle { get; set; }

    public bool HasKey => !string.IsNullOrEmpty(ApiKey);
}