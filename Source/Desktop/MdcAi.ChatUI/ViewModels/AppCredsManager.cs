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

public static class AppCredsManager
{
    private static readonly PasswordVault _vault;
    public static readonly string ResourceName = "MdcAi";

    static AppCredsManager() { _vault = new PasswordVault(); }

    public static void SetValue(string name, string value)
    {
        var resName = "MdcAi";
        var credential = string.IsNullOrEmpty(value) ? null : new PasswordCredential(resName, name, value);

        // Remove the old credential if it exists
        try
        {
            var oldCredential = _vault.Retrieve(resName, name);
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

    public static string GetValue(string name)
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