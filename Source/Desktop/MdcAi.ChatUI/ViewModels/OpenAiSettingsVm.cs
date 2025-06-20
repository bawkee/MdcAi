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

public class OpenAiSettingsVm : ActivatableViewModel
{
    private string _apiKey;

    public string ApiKey
    {
        get => _apiKey;
        set
        {
            this.RaiseAndSetIfChanged(ref _apiKey, value?.Trim());
        }
    }

    [Reactive] public string OrganisationName { get; set; }

    public OpenAiSettingsVm()
    {
        ApiKey = AppCredsManager.GetValue("ApiKeys");
        OrganisationName = AppCredsManager.GetValue("OrganisationName");

        this.WhenAnyValue(vm => vm.ApiKey)
            .Skip(1)
            .ObserveOnMainThread()
            .Do(v => AppCredsManager.SetValue("ApiKeys", v))
            .SubscribeSafe();

        this.WhenAnyValue(vm => vm.OrganisationName)
            .Skip(1)
            .ObserveOnMainThread()
            .Do(v => AppCredsManager.SetValue("OrganisationName", v))
            .SubscribeSafe();
    }
}