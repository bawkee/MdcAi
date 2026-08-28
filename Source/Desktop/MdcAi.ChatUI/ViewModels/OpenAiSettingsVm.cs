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

namespace MdcAi.ChatUI.ViewModels;

using OpenAiApi;

/// <summary>
/// OpenAI API-access settings. Shown as its own section in Settings; coexists with the other
/// providers' sections (no "current provider" selection).
/// </summary>
public class OpenAiSettingsVm : ProviderSettingsVm
{
    public override AiProvider Provider => AiProviders.OpenAi;

    [Reactive] public string OrganisationName { get; set; }

    public OpenAiSettingsVm(ICredsStore creds)
        : base(creds)
    {
        OrganisationName = GetCredential(ProviderCreds.OrganisationName);

        this.WhenAnyValue(vm => vm.OrganisationName)
            .Skip(1)
            .ObserveOnMainThread()
            .Do(v => SaveCredential(ProviderCreds.OrganisationName, v))
            .SubscribeSafe();
    }
}
