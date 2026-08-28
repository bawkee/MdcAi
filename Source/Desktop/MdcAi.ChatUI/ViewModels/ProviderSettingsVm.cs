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
/// One provider's API-access settings, shown as its own section in the Settings page.
/// All providers' sections are visible at the same time - there is no "current provider",
/// any chat can use any configured provider (the model id decides). Credentials are stored
/// per provider in the credential store under "&lt;providerKey&gt;:..." names.
/// 
/// Derived VMs add their provider-specific fields (OpenAI organization, OpenRouter
/// attribution) and declare which <see cref="AiProvider"/> they configure.
/// </summary>
public abstract class ProviderSettingsVm : ActivatableViewModel
{
    private string _apiKey;

    /// <summary>The provider this VM configures.</summary>
    public abstract AiProvider Provider { get; }

    /// <summary>Credential store read at startup and written on change.</summary>
    protected ICredsStore Creds { get; }

    /// <summary>The provider's API key (Bearer token for OpenAI and OpenRouter).</summary>
    public string ApiKey
    {
        get => _apiKey;
        set => this.RaiseAndSetIfChanged(ref _apiKey, value?.Trim());
    }

    public bool HasKey => !string.IsNullOrEmpty(ApiKey);

    protected ProviderSettingsVm(ICredsStore creds)
    {
        Creds = creds;

        ApiKey = ProviderCreds.GetApiKey(creds, Provider);

        // Mirror key edits back into the credential store under this provider's slot.
        this.WhenAnyValue(vm => vm.ApiKey)
            .Skip(1)
            .ObserveOnMainThread()
            .Do(v => Creds.SetValue(ProviderCreds.Name(Provider, ProviderCreds.ApiKeyName), v))
            .SubscribeSafe();
    }

    /// <summary>Loads this provider's stored credential into the matching property.</summary>
    protected string GetCredential(string slot) => Creds.GetValue(ProviderCreds.Name(Provider, slot));

    /// <summary>Saves a field back into this provider's credential slot.</summary>
    protected void SaveCredential(string slot, string value) =>
        Creds.SetValue(ProviderCreds.Name(Provider, slot), value);
}