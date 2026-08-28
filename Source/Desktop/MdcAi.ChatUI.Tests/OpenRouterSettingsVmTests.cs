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

public class OpenRouterSettingsVmTests
{
    public OpenRouterSettingsVmTests() { TestRx.Init(); }

    [Fact]
    public void Loads_its_own_key_and_attribution_fields_from_store()
    {
        var creds = new InMemoryCredsStore();
        creds.SetValue("openrouter:ApiKey", "sk-or-test");
        creds.SetValue("openrouter:RefererUrl", "http://localhost:3431/");
        creds.SetValue("openrouter:AppTitle", "MDC AI");

        var vm = new OpenRouterSettingsVm(creds);

        Assert.Equal(AiProviders.OpenRouterKey, vm.Provider.Key);
        Assert.Equal("sk-or-test", vm.ApiKey);
        Assert.Equal("http://localhost:3431/", vm.RefererUrl);
        Assert.Equal("MDC AI", vm.AppTitle);
        Assert.True(vm.HasKey);
    }

    [Fact]
    public void Key_edits_save_to_its_own_slot_only()
    {
        var creds = new InMemoryCredsStore();
        var vm = new OpenRouterSettingsVm(creds);

        vm.ApiKey = "sk-or-new";

        Assert.Equal("sk-or-new", creds.GetValue("openrouter:ApiKey"));
        Assert.Null(creds.GetValue("openai:ApiKey"));
    }

    [Fact]
    public void Attribution_edits_save_to_their_slots()
    {
        var creds = new InMemoryCredsStore();
        var vm = new OpenRouterSettingsVm(creds);

        vm.RefererUrl = "https://example.com/";
        vm.AppTitle = "My App";

        Assert.Equal("https://example.com/", creds.GetValue("openrouter:RefererUrl"));
        Assert.Equal("My App", creds.GetValue("openrouter:AppTitle"));
    }

    [Fact]
    public void Build_credentials_defaults_openrouter_attribution()
    {
        var creds = new InMemoryCredsStore();
        creds.SetValue("openrouter:ApiKey", "sk-or");

        var built = ProviderCreds.Build(creds, AiProviders.OpenRouter);

        Assert.Equal("sk-or", built.ApiKey);
        Assert.False(string.IsNullOrEmpty(built.RefererUrl));
        Assert.False(string.IsNullOrEmpty(built.AppTitle));
    }
}