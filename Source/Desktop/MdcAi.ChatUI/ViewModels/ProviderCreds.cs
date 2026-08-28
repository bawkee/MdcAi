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
/// Names the credential-store entries per provider (dsh-style per-provider key refs).
/// "openrouter:ApiKey", "openai:ApiKey", "openai:OrganisationName", ...
/// </summary>
public static class ProviderCreds
{
    public const string ApiKeyName = "ApiKey";
    public const string OrganisationName = "OrganisationName";
    public const string RefererUrlName = "RefererUrl";
    public const string AppTitleName = "AppTitle";

    public static string Name(AiProvider provider, string slot) => $"{provider.Key}:{slot}";

    public static string GetApiKey(ICredsStore store, AiProvider provider) => store.GetValue(Name(provider, ApiKeyName));

    public static string GetOrganisation(ICredsStore store, AiProvider provider) => store.GetValue(Name(provider, OrganisationName));

    public static string GetRefererUrl(ICredsStore store, AiProvider provider) => store.GetValue(Name(provider, RefererUrlName));

    public static string GetAppTitle(ICredsStore store, AiProvider provider) => store.GetValue(Name(provider, AppTitleName));

    /// <summary>
    /// Builds the credentials object the client needs for a provider, reading the live vault.
    /// OpenRouter attribution defaults: localhost referer + app title so the request works
    /// even when the user leaves the (optional) fields empty.
    /// </summary>
    public static AiProviderCredentials Build(ICredsStore store, AiProvider provider)
    {
        var creds = new AiProviderCredentials
        {
            ApiKey = GetApiKey(store, provider),
            Organisation = GetOrganisation(store, provider),
            RefererUrl = GetRefererUrl(store, provider),
            AppTitle = GetAppTitle(store, provider)
        };

        if (provider.Key == AiProviders.OpenRouterKey)
        {
            creds.RefererUrl ??= "http://localhost:3431/";
            creds.AppTitle ??= "MDC AI";
        }

        return creds;
    }

    /// <summary>
    /// Migrates the v1 OpenAI key ("ApiKeys" / "OrganisationName" vault entries) into the new
    /// per-provider slots on first run, so existing users don't lose their key.
    /// </summary>
    public static void MigrateLegacyOpenAiKey(ICredsStore store)
    {
        var provider = AiProviders.OpenAi;

        if (string.IsNullOrEmpty(GetApiKey(store, provider)))
        {
            var legacyKey = store.GetValue(AppCredsManager.LegacyApiKeysName);
            if (!string.IsNullOrEmpty(legacyKey))
                store.SetValue(Name(provider, ApiKeyName), legacyKey);
        }

        if (string.IsNullOrEmpty(GetOrganisation(store, provider)))
        {
            var legacyOrg = store.GetValue(AppCredsManager.LegacyOrganisationName);
            if (!string.IsNullOrEmpty(legacyOrg))
                store.SetValue(Name(provider, OrganisationName), legacyOrg);
        }
    }
}