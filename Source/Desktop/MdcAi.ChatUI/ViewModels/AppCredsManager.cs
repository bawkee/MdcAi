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
/// 
/// Reads come from an in-memory snapshot of the whole vault (taken once at construction and
/// kept fresh by every write). This matters: <see cref="PasswordVault.Retrieve"/> *throws*
/// when an entry doesn't exist, so the old per-lookup implementation threw a COMException
/// ("Cannot get credential from Vault") for every optional slot that was never set - which
/// the debugger surfaces as exception spam on every conversation open. Snapshotting turns a
/// lookup into a dictionary hit: no throw, no vault round-trip.
/// </summary>
public class PasswordVaultCredsStore : ICredsStore
{
    private readonly PasswordVault _vault;
    private readonly object _lock = new();
    private readonly Dictionary<string, string> _cache;

    public static readonly string ResourceName = "MdcAi";

    public PasswordVaultCredsStore()
    {
        _vault = new PasswordVault();
        _cache = LoadAll();
    }

    /// <summary>
    /// Reads every credential the app can see, keeps only the ones stored under our resource,
    /// and keys the cache by user name ("openai:ApiKey", ...). Entries that can't be decrypted
    /// for this identity (e.g. vault namespace differs between packaged/unpackaged runs) are
    /// treated as absent - same outcome the old throwing Retrieve had, just quiet about it.
    /// </summary>
    private Dictionary<string, string> LoadAll()
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        try
        {
            foreach (var credential in _vault.RetrieveAll())
            {
                if (!string.Equals(credential.Resource, ResourceName, StringComparison.Ordinal))
                    continue;

                try
                {
                    credential.RetrievePassword();
                    result[credential.UserName] = credential.Password;
                }
                catch
                {
                    /* Not decryptable for this identity - treat as absent */
                }
            }
        }
        catch
        {
            /* Vault entirely unavailable - cache stays empty, reads return null */
        }

        return result;
    }

    public void SetValue(string name, string value)
    {
        lock (_lock)
        {
            // Remove the old credential if it exists. The cache already tells us whether one
            // does, so we never probe the vault blindly (a Retrieve on a missing entry throws).
            if (_cache.ContainsKey(name))
            {
                try
                {
                    var oldCredential = _vault.Retrieve(ResourceName, name);
                    _vault.Remove(oldCredential);
                }
                catch
                {
                    /* Entry disappeared out from under us - the cache write below still wins */
                }
            }

            if (string.IsNullOrEmpty(value))
                _cache.Remove(name);
            else
            {
                // Add the new credential
                _vault.Add(new PasswordCredential(ResourceName, name, value));
                _cache[name] = value;
            }
        }
    }

    public string GetValue(string name)
    {
        lock (_lock)
        {
            return _cache.TryGetValue(name, out var value) ? value : null;
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