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

using Properties;
using RxUIExt.Windsor;
using Windows.Storage;

[Singleton]
public class SettingsVm : ActivatableViewModel
{
    /// <summary>OpenAI API-access section (always visible).</summary>
    public OpenAiSettingsVm OpenAi { get; }

    /// <summary>OpenRouter API-access section (always visible).</summary>
    public OpenRouterSettingsVm OpenRouter { get; }

    /// <summary>
    /// True when at least one provider has a usable key - the app can generate. There is no
    /// "current provider": each conversation's model id decides which provider it uses.
    /// </summary>
    [Reactive] public bool IsAnyProviderConfigured { get; private set; }

    [Reactive] public bool ShowGettingStartedConvoTip { get; set; }
    public ReactiveCommand<Unit, Unit> ShowPrivacyStatementCmd { get; set; }
    public ReactiveCommand<Unit, Unit> ShowAboutCmd { get; set; }
    public ReactiveCommand<Unit, Unit> OpenAppStorageCmd { get; set; }

    public SettingsVm(OpenAiSettingsVm openAi, OpenRouterSettingsVm openRouter)
    {
        OpenAi = openAi;
        OpenRouter = openRouter;

        // The app is usable the moment ANY provider has a key. Re-evaluated whenever any key
        // changes (WhenAnyValue emits the current value immediately, so this is correct from
        // the start).
        Observable.Merge(
                      openAi.WhenAnyValue(vm => vm.ApiKey),
                      openRouter.WhenAnyValue(vm => vm.ApiKey))
                  .Select(_ => openAi.HasKey || openRouter.HasKey)
                  .DistinctUntilChanged()
                  .ObserveOnMainThread()
                  .Do(v => IsAnyProviderConfigured = v)
                  .SubscribeSafe();

        GlobalChatSettings.Default.WhenAnyValue(s => s.ShowGettingStartedConvoTip)
                          .ObserveOnMainThread()
                          .Do(v => ShowGettingStartedConvoTip = v)
                          .SubscribeSafe();

        this.WhenAnyValue(vm => vm.ShowGettingStartedConvoTip)
            .Skip(1)
            .Do(v =>
            {
                GlobalChatSettings.Default.ShowGettingStartedConvoTip = v;
                GlobalChatSettings.Default.Save();
            })
            .SubscribeSafe();

        OpenAppStorageCmd = ReactiveCommand.Create(() =>
        {
            ShellUtil.StartUrl(ApplicationData.Current.LocalFolder.Path);
        });
    }
}