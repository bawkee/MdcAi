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

internal class AiModels
{
    [JsonProperty("data")] public AiModel[] Models { get; set; }
}

public class AiModel
{
    [JsonProperty("id")] public string ModelID { get; set; }
    [JsonProperty("owned_by")] public string OwnedBy { get; set; }
    [JsonProperty("object")] public string Object { get; set; }

    [JsonConverter(typeof(UnixDateTimeConverter))]
    [JsonProperty("created")]
    public DateTime? Created { get; set; }

    [JsonProperty("permission")] public Permissions[] Permission { get; set; }

    public static implicit operator string(AiModel model) => model?.ModelID;
    public static implicit operator AiModel(string name) => new(name);

    public AiModel(string name) { ModelID = name; }

    public static AiModel AdaTextEmbedding => new AiModel("text-embedding-ada-002") { OwnedBy = "openai" };
    public static AiModel Gpt35Turbo => new AiModel("gpt-3.5-turbo-1106") { OwnedBy = "openai" };
    public static AiModel Gpt4Turbo => new AiModel("gpt-4-1106-preview") { OwnedBy = "openai" };
    public static AiModel Gpt4 => new AiModel("gpt-4") { OwnedBy = "openai" };
    public static AiModel Gpt4o => new AiModel("gpt-4o") { OwnedBy = "openai" };

    public override string ToString() => ModelID;
}