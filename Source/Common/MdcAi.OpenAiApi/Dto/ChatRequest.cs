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

    [JsonProperty("tools")] public ChatTool[] Tools { get; set; }

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
    [JsonProperty("parameters")] public FunctionToolParams Parameters { get; set; }
}

public class FunctionToolParams
{
    [JsonProperty("type")] public string Type { get; set; }
    [JsonProperty("properties")] public Dictionary<string, FunctionToolParamProperty> Properties { get; set; }
    [JsonProperty("required")] public string[] Required { get; set; }
    [JsonProperty("additionalProperties")] public bool AdditionalProperties { get; set; }
}

public class FunctionToolParamProperty
{
    [JsonProperty("type")] public string Type { get; set; }
    [JsonProperty("description")] public string Description { get; set; }
}