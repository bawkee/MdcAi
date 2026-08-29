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

using Newtonsoft.Json.Converters;

public class AiModels
{
    [JsonProperty("data")] public AiModel[] Models { get; set; }
}

/// <summary>
/// One model reported by a provider. The wire shapes differ per provider: OpenAI sends
/// <c>owned_by</c>/<c>permission</c>, OpenRouter sends <c>name</c>/<c>context_length</c>/
/// <c>pricing</c>/<c>reasoning</c>. All of those stay optional and null where absent.
/// 
/// <see cref="ProviderKey"/> and <see cref="GroupKey"/> are stamped by the client after
/// fetching, using the active provider's descriptors - the UI groups and classifies against
/// those. When no provider is set (e.g. `new AiModel("gpt-4o")` in app code) classification
/// falls back to id heuristics so existing call sites keep working.
/// </summary>
public class AiModel
{
    [JsonProperty("id")] public string ModelID { get; set; }
    [JsonProperty("owned_by")] public string OwnedBy { get; set; }
    [JsonProperty("object")] public string Object { get; set; }

    [JsonConverter(typeof(UnixDateTimeConverter))]
    [JsonProperty("created")]
    public DateTime? Created { get; set; }

    [JsonProperty("permission")] public Permissions[] Permission { get; set; }

    // --- OpenRouter extensions (null/absent on OpenAI) ---
    [JsonProperty("name")] public string Name { get; set; }
    [JsonProperty("context_length")] public int? ContextLength { get; set; }
    [JsonProperty("pricing")] public AiModelPricing Pricing { get; set; }
    [JsonProperty("reasoning")] public AiModelReasoning Reasoning { get; set; }

    /// <summary>Which provider found this model ("openai", "openrouter", ...). Stamped by the client.</summary>
    [JsonIgnore] public string ProviderKey { get; set; }

    /// <summary>Heading to group this model under in pickers (author for OpenRouter).</summary>
    [JsonIgnore] public string GroupKey { get; set; }

    public static implicit operator string(AiModel model) => model?.ModelID;
    public static implicit operator AiModel(string name) => new(name);

    public AiModel() { }

    public AiModel(string name) { ModelID = name; }

    public AiModel(string name, string providerKey) { ModelID = name; ProviderKey = providerKey; }

    /// <summary>Author part of an OpenRouter id ("anthropic" from "anthropic/claude-..."), null otherwise.</summary>
    public string Author =>
        ModelID?.IndexOf('/') is { } i && i > 0 ? ModelID[..i] : null;

    /// <summary>
    /// Nice label for pickers: OpenRouter display name when present, else the model id.
    /// OpenRouter names carry a redundant "{Author}: " prefix (e.g. "DeepSeek: DeepSeek V4")
    /// that the grouped pickers already express via the group header, so it's dropped here —
    /// the picker button ends up "OpenRouter · DeepSeek V4", not "OpenRouter · DeepSeek: DeepSeek V4".
    /// </summary>
    public string DisplayLabel
    {
        get
        {
            var label = !string.IsNullOrEmpty(Name) && !string.Equals(Name, ModelID, StringComparison.OrdinalIgnoreCase)
                            ? Name
                            : ModelID;
            return StripGroupPrefix(label);
        }
    }

    /// <summary>
    /// Drops a leading "{group}: " from an OpenRouter-style name ("Anthropic: Claude 3.5 Sonnet"
    /// -> "Claude 3.5 Sonnet") when the prefix is just the author/group the pickers already group
    /// by. Case-insensitive; anything without that exact prefix passes through untouched.
    /// </summary>
    private string StripGroupPrefix(string label)
    {
        var group = GroupKey ?? Author;
        if (string.IsNullOrEmpty(group))
            return label;

        var prefix = group + ":";
        if (label.Length > prefix.Length
            && label.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            && label[prefix.Length] == ' ')
            return label[(prefix.Length + 1)..];

        return label;
    }

    /// <summary>
    /// Whether this is a chat-capable model. Provider-aware when stamped, id-heuristic otherwise.
    /// </summary>
    [JsonIgnore]
    public bool IsConversational =>
        ProviderKey == null
            ? AiProviders.IsConversationalId(ModelID)
            : AiProviders.Get(ProviderKey).IsConversationalModel(this);

    /// <summary>
    /// Whether this is a reasoning model (premise is skipped for those, etc).
    /// Provider-aware when stamped, id-heuristic otherwise.
    /// </summary>
    [JsonIgnore]
    public bool IsReasoning =>
        ProviderKey == null
            ? AiProviders.IsReasoningId(ModelID)
            : AiProviders.Get(ProviderKey).IsReasoningModel(this);

    /// <summary>
    /// The reasoning-effort levels this model supports ("low", "medium", "high", ...), or null
    /// when the model has no effort support at all. Effort-capable models:
    ///   - OpenRouter models follow the fetched <c>reasoning.supported_efforts</c> metadata
    ///     (authoritative - no id guessing for unknown authors),
    ///   - OpenAI-provider models get the standard level set by id family (o1*/o3*/o4*/gpt-5*).
    /// Everything else reports null: no effort UI, and the <c>reasoning_effort</c> request
    /// parameter is never sent for them.
    /// </summary>
    [JsonIgnore]
    public string[] SupportedEfforts =>
        ProviderKey == AiProviders.OpenRouterKey
            ? Reasoning?.SupportedEfforts is { Length: > 0 } efforts
                  ? efforts
                  : null
            : AiProviders.IsEffortCapableId(ModelID) ? AiEffort.Levels : null;

    public static AiModel AdaTextEmbedding => new("text-embedding-ada-002") { OwnedBy = "openai" };
    public static AiModel Gpt35Turbo => new("gpt-3.5-turbo-1106") { OwnedBy = "openai" };
    public static AiModel Gpt4Turbo => new("gpt-4-1106-preview") { OwnedBy = "openai" };
    public static AiModel Gpt4 => new("gpt-4") { OwnedBy = "openai" };
    public static AiModel Gpt4o => new("gpt-4o") { OwnedBy = "openai" };

    public override string ToString() => ModelID;
}

/// <summary>
/// OpenRouter per-model pricing, in USD *per token* on the wire (e.g. "0.0000002574").
/// Parsed helpers give per-million-token prices for display.
/// </summary>
public class AiModelPricing
{
    [JsonProperty("prompt")] public string Prompt { get; set; }
    [JsonProperty("completion")] public string Completion { get; set; }
    [JsonProperty("request")] public string Request { get; set; }
    [JsonProperty("image")] public string Image { get; set; }
    [JsonProperty("internal_reasoning")] public string InternalReasoning { get; set; }
    [JsonProperty("input_cache_read")] public string InputCacheRead { get; set; }
    [JsonProperty("input_cache_write")] public string InputCacheWrite { get; set; }

    public decimal? PromptPerMTokens => ParsePerM(Prompt);
    public decimal? CompletionPerMTokens => ParsePerM(Completion);
    public decimal? RequestPerRequest => ParsePerM(Request);

    private static decimal? ParsePerM(string perToken)
    {
        if (string.IsNullOrWhiteSpace(perToken))
            return null;
        return decimal.TryParse(perToken, System.Globalization.NumberStyles.Float,
                                System.Globalization.CultureInfo.InvariantCulture, out var v)
            ? v * 1_000_000m
            : null;
    }
}

/// <summary>
/// Reasoning-effort vocabulary. The de-facto standard level set is OpenAI's
/// low/medium/high, which is what OpenRouter's reasoning metadata reports too; models may
/// advertise arbitrary strings though, so the app never hard-filters - display and send
/// whatever the model declares.
/// </summary>
public static class AiEffort
{
    public const string Low = "low";
    public const string Medium = "medium";
    public const string High = "high";

    /// <summary>Standard level set, in display order.</summary>
    public static readonly string[] Levels = { Low, Medium, High };

    /// <summary>
    /// Picks the level closest to "medium" - the default the app auto-selects for models
    /// with effort support and for categories without a stored effort. Prefers an exact
    /// "medium", then "low" (the cheaper neighbor when medium isn't offered), then any known
    /// level, then the first declared one for exotic sets.
    /// </summary>
    public static string ClosestToMedium(IEnumerable<string> efforts)
    {
        if (efforts == null)
            return null;

        var set = efforts.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (set.Length == 0)
            return null;

        foreach (var want in PreferenceOrder)
            if (set.FirstOrDefault(e => string.Equals(e, want, StringComparison.OrdinalIgnoreCase)) is { } match)
                return match;

        return set[0];
    }

    private static readonly string[] PreferenceOrder = { Medium, Low, High };
}

/// <summary>
/// OpenRouter `reasoning` object: presence + flags tell us a model emits reasoning tokens.
/// </summary>
public class AiModelReasoning
{
    [JsonProperty("supported_efforts")] public string[] SupportedEfforts { get; set; }
    [JsonProperty("default_effort")] public string DefaultEffort { get; set; }
    [JsonProperty("default_enabled")] public bool DefaultEnabled { get; set; }
    [JsonProperty("mandatory")] public bool Mandatory { get; set; }
    [JsonProperty("supports_max_tokens")] public bool SupportsMaxTokens { get; set; }
}