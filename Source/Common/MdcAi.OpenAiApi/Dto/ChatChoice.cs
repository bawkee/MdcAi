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

public class ChatChoice
{
    [JsonProperty("index")] public int Index { get; set; }
    [JsonProperty("message")] public ChatMessage Message { get; set; }
    [JsonProperty("finish_reason")] public string FinishReason { get; set; }
    [JsonProperty("delta")] public ChatMessage Delta { get; set; }

    public override string ToString() => Message.Content;
}