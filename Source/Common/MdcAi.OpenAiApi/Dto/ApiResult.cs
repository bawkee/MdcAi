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

public class ApiResult
{
    [JsonConverter(typeof(UnixDateTimeConverter))]
    [JsonProperty("created")]
    public DateTime? Created { get; set; }

    [JsonProperty("model")] public string Model { get; set; }
    [JsonProperty("object")] public string Object { get; set; }
    [JsonIgnore] public string Organization { get; internal set; }
    [JsonIgnore] public TimeSpan ProcessingTime { get; internal set; }
    [JsonIgnore] public string RequestId { get; internal set; }
    [JsonIgnore] public string OpenaiVersion { get; internal set; }
}