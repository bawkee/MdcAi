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

public class Permissions
{
    [JsonProperty("id")] public string Id { get; set; }
    [JsonProperty("object")] public string Object { get; set; }

    [JsonConverter(typeof(UnixDateTimeConverter))]
    [JsonProperty("created")]
    public DateTime? Created { get; set; }

    [JsonProperty("allow_create_engine")] public bool AllowCreateEngine { get; set; }
    [JsonProperty("allow_sampling")] public bool AllowSampling { get; set; }
    [JsonProperty("allow_logprobs")] public bool AllowLogProbs { get; set; }
    [JsonProperty("allow_search_indices")] public bool AllowSearchIndices { get; set; }
    [JsonProperty("allow_view")] public bool AllowView { get; set; }
    [JsonProperty("allow_fine_tuning")] public bool AllowFineTuning { get; set; }
    [JsonProperty("organization")] public string Organization { get; set; }
    [JsonProperty("group")] public string Group { get; set; }
    [JsonProperty("is_blocking")] public bool IsBlocking { get; set; }
}