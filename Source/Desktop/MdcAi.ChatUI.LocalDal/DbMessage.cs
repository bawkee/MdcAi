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

namespace MdcAi.ChatUI.LocalDal;

using System.ComponentModel.DataAnnotations;

public class DbMessage
{
    [Key] public string IdMessage { get; set; }
    public string IdMessageParent { get; set; }
    public string IdConversation { get; set; }
    public int Version { get; set; }
    public bool IsCurrentVersion { get; set; }
    public DateTime CreatedTs { get; set; }
    public string Role { get; set; }
    public string Content { get; set; }

    /// <summary>Model id that (re)generated this message ("gpt-4o", "anthropic/claude-3-5-sonnet", ...).
    /// Null on user messages and on legacy rows that predate per-message provenance.</summary>
    public string Model { get; set; }

    /// <summary>Reasoning effort that (re)generated this message ("low"/"medium"/"high", ...).
    /// Null on user messages, on effort-less models, and on legacy rows that predate
    /// per-message effort provenance - same convention as <see cref="Model"/>.</summary>
    public string Effort { get; set; }

    /// <summary>Raw reasoning/thinking text the model emitted before its answer
    /// ("reasoning_content"). Null on user messages and on models that never think.</summary>
    public string Reasoning { get; set; }

    public bool IsTrash { get; set; }

    public DbConversation Conversation { get; set; }
}