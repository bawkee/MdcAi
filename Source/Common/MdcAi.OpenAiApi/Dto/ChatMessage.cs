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

public class ChatMessage
{
    public ChatMessage() { }

    public ChatMessage(ChatMessageRole role, string content)
    {
        Role = role;
        Content = content;
    }

    public ChatMessage(ChatMessage basedOn)
    {
        if (basedOn == null)
            throw new ArgumentNullException();

        Role = basedOn.Role;
        Content = basedOn.Content;
        Name = basedOn.Name;
    }

    [JsonProperty("role")] public string Role { get; set; } = ChatMessageRole.User;
    [JsonProperty("content")] public string Content { get; set; }
    [JsonProperty("name")] public string Name { get; set; }
    [JsonProperty("tool_calls")] public ChatMessageToolCall[] ToolCalls { get; set; }
    [JsonProperty("tool_call_id")] public string ToolCallId { get; set; }
}

public class ChatMessageToolCall
{
    [JsonProperty("id")] public string Id { get; set; }
    [JsonProperty("type")] public string Type { get; set; }
    [JsonProperty("function")] public ChatMessageFunction Function { get; set; }
}

public class ChatMessageFunction
{
    [JsonProperty("arguments")] public string Arguments { get; set; }
    [JsonProperty("name")] public string Name { get; set; }
}