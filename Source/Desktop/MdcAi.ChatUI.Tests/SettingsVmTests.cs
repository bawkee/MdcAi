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

namespace MdcAi.ChatUI.Tests;

using MdcAi.ChatUI.ViewModels;
using OpenAiApi;

public class SettingsVmTests
{
    public SettingsVmTests() { TestRx.Init(); }

    [Fact]
    public void IsAnyProviderConfigured_false_with_no_keys()
    {
        var creds = new InMemoryCredsStore();
        var vm = TestSettings.Build(creds);

        Assert.False(vm.IsAnyProviderConfigured);
    }

    [Fact]
    public void IsAnyProviderConfigured_true_when_any_provider_has_a_key()
    {
        var creds = new InMemoryCredsStore();
        var vm = TestSettings.Build(creds);

        vm.OpenRouter.ApiKey = "sk-or";

        Assert.True(vm.IsAnyProviderConfigured);
    }

    [Fact]
    public void IsAnyProviderConfigured_false_after_last_key_removed()
    {
        var creds = new InMemoryCredsStore();
        creds.SetValue("openai:ApiKey", "sk-oa");
        var vm = TestSettings.Build(creds);

        Assert.True(vm.IsAnyProviderConfigured);

        vm.OpenAi.ApiKey = null;

        Assert.False(vm.IsAnyProviderConfigured);
    }

    [Fact]
    public void Both_provider_sections_coexist_and_keep_their_own_credentials()
    {
        var creds = new InMemoryCredsStore();
        creds.SetValue("openai:ApiKey", "sk-oa");
        creds.SetValue("openrouter:ApiKey", "sk-or");
        var vm = TestSettings.Build(creds);

        Assert.Equal("sk-oa", vm.OpenAi.ApiKey);
        Assert.Equal("sk-or", vm.OpenRouter.ApiKey);

        // Editing one does not touch the other.
        vm.OpenAi.ApiKey = "sk-oa-2";
        Assert.Equal("sk-oa-2", creds.GetValue("openai:ApiKey"));
        Assert.Equal("sk-or", creds.GetValue("openrouter:ApiKey"));
    }
}