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

public class OpenAiSettingsVmTests
{
    public OpenAiSettingsVmTests() { TestRx.Init(); }

    [Fact]
    public void Loads_its_own_key_and_organisation_from_store()
    {
        var creds = new InMemoryCredsStore();
        creds.SetValue("openai:ApiKey", "sk-test-openai");
        creds.SetValue("openai:OrganisationName", "org-123");

        var vm = new OpenAiSettingsVm(creds);

        Assert.Equal(AiProviders.OpenAiKey, vm.Provider.Key);
        Assert.Equal("sk-test-openai", vm.ApiKey);
        Assert.Equal("org-123", vm.OrganisationName);
        Assert.True(vm.HasKey);
    }

    [Fact]
    public void Key_edits_save_to_its_own_slot_only()
    {
        var creds = new InMemoryCredsStore();
        var vm = new OpenAiSettingsVm(creds);

        vm.ApiKey = "sk-new";

        Assert.Equal("sk-new", creds.GetValue("openai:ApiKey"));
        Assert.Null(creds.GetValue("openrouter:ApiKey"));
    }

    [Fact]
    public void Organisation_edits_save_to_its_own_slot()
    {
        var creds = new InMemoryCredsStore();
        var vm = new OpenAiSettingsVm(creds);

        vm.OrganisationName = "org-new";

        Assert.Equal("org-new", creds.GetValue("openai:OrganisationName"));
    }

    [Fact]
    public void Clearing_the_key_removes_it_from_the_store()
    {
        var creds = new InMemoryCredsStore();
        creds.SetValue("openai:ApiKey", "sk-old");
        var vm = new OpenAiSettingsVm(creds);

        vm.ApiKey = null;

        Assert.Null(creds.GetValue("openai:ApiKey"));
        Assert.False(vm.HasKey);
    }

    [Fact]
    public void Leading_and_trailing_whitespace_is_trimmed()
    {
        var creds = new InMemoryCredsStore();
        var vm = new OpenAiSettingsVm(creds);

        vm.ApiKey = "  sk-trimmed  ";

        Assert.Equal("sk-trimmed", vm.ApiKey);
        Assert.Equal("sk-trimmed", creds.GetValue("openai:ApiKey"));
    }

    [Fact]
    public void Migrates_legacy_single_provider_key_into_the_openai_slot()
    {
        var creds = new InMemoryCredsStore();
        creds.SetValue(AppCredsManager.LegacyApiKeysName, "sk-legacy");
        creds.SetValue(AppCredsManager.LegacyOrganisationName, "org-legacy");

        ProviderCreds.MigrateLegacyOpenAiKey(creds);

        Assert.Equal("sk-legacy", creds.GetValue("openai:ApiKey"));
        Assert.Equal("org-legacy", creds.GetValue("openai:OrganisationName"));
    }

    [Fact]
    public void Migration_does_not_overwrite_an_existing_new_slot()
    {
        var creds = new InMemoryCredsStore();
        creds.SetValue("openai:ApiKey", "sk-modern");
        creds.SetValue(AppCredsManager.LegacyApiKeysName, "sk-legacy");

        ProviderCreds.MigrateLegacyOpenAiKey(creds);

        Assert.Equal("sk-modern", creds.GetValue("openai:ApiKey"));
    }
}