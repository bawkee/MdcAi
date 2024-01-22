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

namespace MdcAi.ViewModels;

using MdcAi.ChatUI.ViewModels;

// MPV:
// ...

// TODO: Use gpt4 turbo for name generation, try it at least

// Other
// TODO: Localisation

[Singleton]
public class MainVm : ActivatableViewModel
{
    public ConversationsVm Conversations { get; }
    public SettingsVm Settings { get; }

    [Reactive] public string Foo { get; set; }

    public MainVm(ConversationsVm conversations, SettingsVm settings)
    {
        Conversations = conversations;
        Settings = settings;

        Activator.Activated
                 .InvokeCommand(Conversations.LoadItems);
    }
}