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

public class ChatUsage
{
    [JsonProperty("completion_tokens")] public int CompletionTokens { get; set; }
    [JsonProperty("prompt_tokens")] public int PromptTokens { get; set; }
    [JsonProperty("total_tokens")] public int TotalTokens { get; set; }

    // --- OpenRouter/mid-stream extras (absent/zero on OpenAI responses) ---

    [JsonProperty("prompt_tokens_details")] public TokenDetails PromptDetails { get; set; }
    [JsonProperty("completion_tokens_details")] public TokenDetails CompletionDetails { get; set; }

    /// <summary>OpenRouter: cost of the request in credits (USD).</summary>
    [JsonProperty("cost")] public decimal? Cost { get; set; }

    /// <summary>OpenRouter: whether the request was billed under BYOK (bring your own key).</summary>
    [JsonProperty("is_byok")] public bool IsByok { get; set; }
}

public class TokenDetails
{
    [JsonProperty("cached_tokens")] public int? CachedTokens { get; set; }
    [JsonProperty("reasoning_tokens")] public int? ReasoningTokens { get; set; }
    [JsonProperty("audio_tokens")] public int? AudioTokens { get; set; }
}