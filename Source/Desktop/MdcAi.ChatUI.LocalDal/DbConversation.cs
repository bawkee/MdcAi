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

public class DbConversation
{
    [Key] public string IdConversation { get; set; }
    public string IdCategory { get; set; }
    public string IdSettingsOverride { get; set; }
    public string Name { get; set; }
    public bool IsTrash { get; set; }
    public DateTime CreatedTs { get; set; }

    public DbCategory Category { get; set; }
    public List<DbMessage> Messages { get; set; } = new();
    public DbChatSettings SettingsOverride { get; set; }
}