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

using Windows.Security.Credentials;

/// <summary>
/// Where per-provider secrets (and the non-secret bits that travel with them) live. The real
/// implementation is the Windows PasswordVault; tests swap in an in-memory store.
/// </summary>
public interface ICredsStore
{
    string GetValue(string name);
    void SetValue(string name, string value);
}

/// <summary>
/// PasswordVault-backed credential store. Stored under one resource name ("MdcAi") with
/// per-provider value names, e.g. "openai:ApiKey", "openrouter:ApiKey".
/// </summary>
public class PasswordVaultCredsStore : ICredsStore
{
    private readonly PasswordVault _vault;

    public PasswordVaultCredsStore() { _vault = new PasswordVault(); }

    public static readonly string ResourceName = "MdcAi";

    public void SetValue(string name, string value)
    {
        var credential = string.IsNullOrEmpty(value) ? null : new PasswordCredential(ResourceName, name, value);

        // Remove the old credential if it exists
        try
        {
            var oldCredential = _vault.Retrieve(ResourceName, name);
            _vault.Remove(oldCredential);
        }
        catch
        {
            /* Ignored if there's no existing credential */
        }

        if (credential != null)
            // Add the new credential
            _vault.Add(credential);
    }

    public string GetValue(string name)
    {
        try
        {
            var credential = _vault.Retrieve(ResourceName, name);
            credential.RetrievePassword();
            return credential.Password;
        }
        catch
        {
            return null;
        }
    }
}

/// <summary>
/// Back-compat static facade the app used to call directly. The VM layer now goes through
/// <see cref="ICredsStore"/>; this keeps old call sites working.
/// </summary>
public static class AppCredsManager
{
    public static readonly string ResourceName = PasswordVaultCredsStore.ResourceName;

    private static readonly ICredsStore _store = new PasswordVaultCredsStore();

    public static ICredsStore Store => _store;

    public static void SetValue(string name, string value) => _store.SetValue(name, value);

    public static string GetValue(string name) => _store.GetValue(name);

    // Legacy value names kept for migration of the v1 OpenAI key.
    public const string LegacyApiKeysName = "ApiKeys";
    public const string LegacyOrganisationName = "OrganisationName";
}