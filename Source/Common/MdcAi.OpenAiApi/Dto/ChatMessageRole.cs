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

public class ChatMessageRole : IEquatable<ChatMessageRole>
{
    private ChatMessageRole(string value) { Value = value; }

    public static ChatMessageRole FromString(string roleName) =>
        roleName switch
        {
            "system" => System,
            "user" => User,
            "assistant" => Assistant,
            _ => null
        };

    private string Value { get; }

    public static ChatMessageRole System { get; } = new("system");
    public static ChatMessageRole User { get; } = new("user");
    public static ChatMessageRole Tool { get; } = new("tool");
    public static ChatMessageRole Assistant { get; } = new("assistant");
   
    public override bool Equals(object obj) => Value.Equals((obj as ChatMessageRole)?.Value);

    public bool Equals(ChatMessageRole other) => Value.Equals(other?.Value);

    public override int GetHashCode() => Value.GetHashCode();

    public static implicit operator string(ChatMessageRole value) { return value.Value; }

    public static implicit operator ChatMessageRole(string value) { return new(value); }
}