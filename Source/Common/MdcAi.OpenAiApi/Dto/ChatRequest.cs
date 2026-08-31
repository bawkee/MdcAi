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

using Newtonsoft.Json.Linq;

public class ChatRequest
{
    [JsonProperty("model")] public string Model { get; set; } = AiModel.Gpt35Turbo;
    [JsonProperty("messages")] public IList<ChatMessage> Messages { get; set; }
    [JsonProperty("temperature")] public double? Temperature { get; set; }
    [JsonProperty("top_p")] public double? TopP { get; set; }
    [JsonProperty("n")] public int? NumChoicesPerMessage { get; set; }
    [JsonProperty("stream")] public bool Streaming { get; internal set; }

    /// <summary>
    /// This is only used for serializing the request into JSON, do not use it directly.
    /// </summary>
    [JsonProperty("stop")]
    internal object CompiledStop =>
        MultipleStopSequences?.Length switch
        {
            1 => StopSequence,
            > 0 => MultipleStopSequences,
            _ => null
        };

    /// <summary>
    /// One or more sequences where the API will stop generating further tokens. The returned text will not contain the stop sequence.
    /// </summary>
    [JsonIgnore] public string[] MultipleStopSequences { get; set; }

    /// <summary>
    /// The stop sequence where the API will stop generating further tokens. The returned text will not contain the stop sequence.  
    /// For convenience, if you are only requesting a single stop sequence, set it here
    /// </summary>
    [JsonIgnore]
    public string StopSequence
    {
        get => MultipleStopSequences?.FirstOrDefault();
        set
        {
            if (value != null)
                MultipleStopSequences = new[] { value };
        }
    }

    [JsonProperty("max_tokens")] public int? MaxTokens { get; set; } // This is for output tokens and max is 4096 for majority of models
    [JsonProperty("frequency_penalty")] public double? FrequencyPenalty { get; set; }
    [JsonProperty("presence_penalty")] public double? PresencePenalty { get; set; }
    [JsonProperty("logit_bias")] public IReadOnlyDictionary<string, float> LogitBias { get; set; }
    [JsonProperty("user")] public string User { get; set; }

    /// <summary>
    /// Reasoning effort for models that support it ("low"/"medium"/"high", or whatever the
    /// model advertises via <see cref="AiModel.SupportedEfforts"/>). Omitted from the JSON
    /// when null (NullValueHandling.Ignore) - the app guarantees null for models without
    /// effort support, so this parameter is never sent to those.
    /// </summary>
    [JsonProperty("reasoning_effort", NullValueHandling = NullValueHandling.Ignore)]
    public string ReasoningEffort { get; set; }

    /// <summary>
    /// Adapter-owned provider reasoning request options. This is the wire shape for providers
    /// that take a NESTED reasoning object (e.g. OpenRouter's <c>"reasoning": { "effort": ...,
    /// "max_tokens": ... }</c>). VMs never decide which form a provider wants; the provider
    /// capability adapter populates either this or <see cref="ReasoningEffort"/>. Null omits the
    /// property entirely.
    /// </summary>
    [JsonProperty("reasoning", NullValueHandling = NullValueHandling.Ignore)]
    public JObject ReasoningOptions { get; set; }

    [JsonProperty("tools")] public ChatTool[] Tools { get; set; }

    /// <summary>
    /// Which tool selection the API should apply: a plain string ("auto"/"none"/"required") or an
    /// explicit function object. Kept as raw JSON so both forms round trip exactly; providers that
    /// don't support a form simply don't fill it.
    /// </summary>
    [JsonProperty("tool_choice", NullValueHandling = NullValueHandling.Ignore)]
    public JToken ToolChoice { get; set; }

    /// <summary>Whether the model may issue several tool calls in one response (OpenAI-style).</summary>
    [JsonProperty("parallel_tool_calls", NullValueHandling = NullValueHandling.Ignore)]
    public bool? ParallelToolCalls { get; set; }

    /// <summary>
    /// Explicit provider routing hint. The router prefers this key; the legacy model-id heuristic
    /// (slash => OpenRouter) is only a fallback for old callers that never stamp it.
    /// </summary>
    [JsonIgnore] public string ProviderKey { get; set; }

    public ChatRequest() { }

    public ChatRequest(ChatRequest basedOn)
    {
        if (basedOn == null)
            return;

        Model = basedOn.Model;
        Messages = basedOn.Messages.Select(m => new ChatMessage(m)).ToList();
        Temperature = basedOn.Temperature;
        TopP = basedOn.TopP;
        NumChoicesPerMessage = basedOn.NumChoicesPerMessage;
        MultipleStopSequences = basedOn.MultipleStopSequences;
        MaxTokens = basedOn.MaxTokens;
        FrequencyPenalty = basedOn.FrequencyPenalty;
        PresencePenalty = basedOn.PresencePenalty;
        LogitBias = basedOn.LogitBias;
        ReasoningEffort = basedOn.ReasoningEffort;
        ReasoningOptions = basedOn.ReasoningOptions?.DeepClone() as JObject;
        User = basedOn.User;
        Tools = basedOn.Tools;
        ToolChoice = basedOn.ToolChoice?.DeepClone();
        ParallelToolCalls = basedOn.ParallelToolCalls;
        ProviderKey = basedOn.ProviderKey;
    }
}

public class ChatTool
{
    [JsonProperty("type")] public string Type { get; set; }
    [JsonProperty("function")] public FunctionTool Function { get; set; }
}

public class FunctionTool
{
    [JsonProperty("name")] public string Name { get; set; }
    [JsonProperty("description")] public string Description { get; set; }

    /// <summary>
    /// The authoritative JSON Schema for this tool's arguments. Any rich schema is expressible
    /// (nested objects, arrays, enums, anyOf, constraints, additionalProperties:false) - unlike
    /// the legacy shallow <see cref="FunctionToolParams"/>.
    /// </summary>
    [JsonProperty("parameters")] public JObject Parameters { get; set; }

    /// <summary>Optional strict-schema flag, controlled by provider capability (DeepSeek beta strict mode must not be enabled blindly).</summary>
    [JsonProperty("strict", NullValueHandling = NullValueHandling.Ignore)]
    public bool? Strict { get; set; }

    /// <summary>Builds a tool from the legacy shallow param shape; parameters become a plain object schema.</summary>
    public static FunctionTool FromLegacy(string name, string description, FunctionToolParams parameters)
    {
        return new FunctionTool
        {
            Name = name,
            Description = description,
            Parameters = parameters?.ToJObject()
        };
    }
}

/// <summary>
/// Legacy shallow parameter schema, kept for source compatibility. New tool definitions should
/// use a JObject JSON Schema via <see cref="FunctionTool.Parameters"/>; this shape cannot
/// describe nested objects, arrays, enums or reusable definitions.
/// </summary>
public class FunctionToolParams
{
    [JsonProperty("type")] public string Type { get; set; }
    [JsonProperty("properties")] public Dictionary<string, FunctionToolParamProperty> Properties { get; set; }
    [JsonProperty("required")] public string[] Required { get; set; }
    [JsonProperty("additionalProperties")] public bool AdditionalProperties { get; set; }

    public JObject ToJObject()
    {
        var jObj = new JObject { ["type"] = Type ?? "object" };

        if (Properties is { Count: > 0 })
        {
            var props = new JObject();
            foreach (var kv in Properties)
                props[kv.Key] = kv.Value?.ToJObject() ?? new JObject();
            jObj["properties"] = props;
        }

        if (Required is { Length: > 0 })
            jObj["required"] = JArray.FromObject(Required);

        if (AdditionalProperties)
            jObj["additionalProperties"] = true;

        return jObj;
    }
}

public class FunctionToolParamProperty
{
    [JsonProperty("type")] public string Type { get; set; }
    [JsonProperty("description")] public string Description { get; set; }

    public JObject ToJObject()
    {
        var jObj = new JObject();
        if (!string.IsNullOrEmpty(Type))
            jObj["type"] = Type;
        if (!string.IsNullOrEmpty(Description))
            jObj["description"] = Description;
        return jObj;
    }
}