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

public class ChatSettingsVmTests
{
    public ChatSettingsVmTests() { TestRx.Init(); }

    private static SettingsVm MakeSettings(params (string name, string value)[] creds)
    {
        var store = new InMemoryCredsStore();
        foreach (var (name, value) in creds)
            store.SetValue(name, value);
        return TestSettings.Build(store);
    }

    [Fact]
    public async Task LoadModels_filters_non_conversational_models()
    {
        var api = new FakeOpenAiApi();
        api.SetModels(AiProviders.OpenAiKey, new[]
        {
            Stamped("gpt-4o", AiProviders.OpenAi),
            Stamped("text-embedding-3-small", AiProviders.OpenAi)
        });
        api.SetModels(AiProviders.OpenRouterKey, new[]
        {
            Stamped("anthropic/claude-3-5-sonnet", AiProviders.OpenRouter)
        });
        var vm = new ChatSettingsVm(api);

        await vm.LoadModelsCmd.Execute();

        Assert.DoesNotContain(vm.Models, m => m.ModelID == "text-embedding-3-small");
    }

    [Fact]
    public async Task LoadModels_covers_both_providers_when_both_configured()
    {
        var api = new FakeOpenAiApi();
        api.SetModels(AiProviders.OpenAiKey, new[]
        {
            Stamped("gpt-4o", AiProviders.OpenAi),
            Stamped("text-embedding-3-small", AiProviders.OpenAi)
        });
        api.SetModels(AiProviders.OpenRouterKey, new[]
        {
            Stamped("anthropic/claude-3-5-sonnet", AiProviders.OpenRouter)
        });
        var vm = new ChatSettingsVm(api);

        await vm.LoadModelsCmd.Execute();

        Assert.Equal(2, vm.Models.Length);
        Assert.Contains(vm.Models, m => m.ModelID == "gpt-4o");
        Assert.Contains(vm.Models, m => m.ModelID == "anthropic/claude-3-5-sonnet");
        Assert.Contains(vm.Models, m => m.ProviderKey == AiProviders.OpenAiKey);
        Assert.Contains(vm.Models, m => m.ProviderKey == AiProviders.OpenRouterKey);
    }

    [Fact]
    public async Task Stored_model_outside_catalog_falls_back_to_its_provider_default()
    {
        var api = new FakeOpenAiApi();
        var vm = new ChatSettingsVm(api) { Model = "gpt-4.5-preview" };

        await vm.LoadModelsCmd.Execute();

        // "gpt-4.5-preview" routes to OpenAI and OpenAI is available -> its default.
        Assert.Equal(AiProviders.OpenAi.DefaultModel, vm.Model);
    }

    [Fact]
    public async Task Stored_model_from_unconfigured_provider_falls_back_to_first_available_model()
    {
        var api = new FakeOpenAiApi();
        api.SetKey(AiProviders.OpenAiKey, null); // OpenAI NOT configured
        var vm = new ChatSettingsVm(api) { Model = "gpt-4o" };

        await vm.LoadModelsCmd.Execute();

        // gpt-4o would route to OpenAI, but OpenAI has no key -> use the first usable model
        // from the (OpenRouter-only) catalog.
        Assert.NotEqual("gpt-4o", vm.Model);
        Assert.NotEmpty(vm.Models);
        Assert.Contains(vm.Models, m => m.ModelID == vm.Model);
    }

    [Fact]
    public async Task Stored_model_still_in_catalog_is_kept()
    {
        var api = new FakeOpenAiApi();
        var vm = new ChatSettingsVm(api) { Model = "gpt-4o" };

        await vm.LoadModelsCmd.Execute();

        Assert.Equal("gpt-4o", vm.Model);
    }

    [Fact]
    public async Task SelectedModel_marks_reasoning_flag_from_stamped_metadata()
    {
        var api = new FakeOpenAiApi();
        var vm = new ChatSettingsVm(api);

        vm.SelectedModel = "o1-mini";
        await Task.Yield(); // let the reactive chain tick

        Assert.True(vm.IsReasoningModel);

        vm.SelectedModel = "gpt-4o";
        await Task.Yield();

        Assert.False(vm.IsReasoningModel);
    }

    private static AiModel Stamped(string id, AiProvider provider)
    {
        var model = new AiModel(id) { ProviderKey = provider.Key };
        model.GroupKey = provider.ModelGroupKey(model);
        return model;
    }
}