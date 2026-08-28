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

public class ConversationVmTests
{
    public ConversationVmTests() { TestRx.Init(); }

    private static (ConversationVm convo, SettingsVm settings, FakeOpenAiApi api, ChatSettingsVm chatSettings) Make(
        params (string name, string value)[] creds)
    {
        var api = new FakeOpenAiApi();

        var store = new InMemoryCredsStore();
        foreach (var (name, value) in creds)
            store.SetValue(name, value);
        var settings = TestSettings.Build(store);
        var chatSettings = new ChatSettingsVm(api);
        var convo = new ConversationVm(api, settings, chatSettings);
        return (convo, settings, api, chatSettings);
    }

    [Fact]
    public void IsAIReady_true_when_any_provider_has_a_key()
    {
        var (convo, _, _, _) = Make(("openai:ApiKey", "sk-oa"));

        Assert.True(convo.IsAIReady);
    }

    [Fact]
    public async Task IsAIReady_false_when_no_provider_has_a_key()
    {
        var (convo, settings, _, _) = Make();

        settings.OpenAi.ApiKey = null;
        await Task.Yield();

        Assert.False(convo.IsAIReady);
    }

    [Fact]
    public async Task IsAIReady_true_when_another_provider_gains_a_key()
    {
        var (convo, settings, _, _) = Make();
        Assert.False(convo.IsAIReady);

        settings.OpenRouter.ApiKey = "sk-or-new";
        await Task.Yield();

        // No "current provider" concept - the app is usable the moment ANY provider is set.
        Assert.True(convo.IsAIReady);
    }

    [Fact]
    public async Task IsAIReady_false_after_last_key_removed()
    {
        var (convo, settings, _, _) = Make(("openai:ApiKey", "sk-oa"), ("openrouter:ApiKey", "sk-or"));
        Assert.True(convo.IsAIReady);

        settings.OpenAi.ApiKey = null;
        await Task.Yield();
        Assert.True(convo.IsAIReady); // openrouter still has a key

        settings.OpenRouter.ApiKey = null;
        await Task.Yield();
        Assert.False(convo.IsAIReady);
    }

    [Fact]
    public async Task LoadModels_stamps_provider_and_group_keys()
    {
        var (convo, _, _, _) = Make(("openai:ApiKey", "sk-oa"), ("openrouter:ApiKey", "sk-or"));

        await convo.LoadModelsCmd.Execute();

        Assert.Equal(5, convo.Models.Length);
        var claude = convo.Models.First(m => m.ModelID == "anthropic/claude-3-5-sonnet");
        Assert.Equal(AiProviders.OpenRouterKey, claude.ProviderKey);
        Assert.Equal("anthropic", claude.GroupKey);
    }

    [Fact]
    public async Task LoadModels_still_returns_openrouter_models_without_openai_key()
    {
        var (convo, _, api, _) = Make(("openrouter:ApiKey", "sk-or"));
        api.SetKey(AiProviders.OpenAiKey, null); // OpenAI API not configured

        await convo.LoadModelsCmd.Execute();

        Assert.All(convo.Models, m => Assert.Equal(AiProviders.OpenRouterKey, m.ProviderKey));
        Assert.Equal(3, convo.Models.Length);
    }

    [Fact]
    public async Task SelectModel_copies_into_settings_working_selection()
    {
        var (convo, _, _, chatSettings) = Make(("openai:ApiKey", "sk-oa"), ("openrouter:ApiKey", "sk-or"));

        convo.SelectModelCmd.Execute("gpt-4o").Subscribe();

        Assert.Equal("gpt-4o", chatSettings.SelectedModel);
    }

    [Fact]
    public void Providers_exposed_for_grouped_pickers()
    {
        var (convo, _, _, _) = Make();

        Assert.Equal(2, convo.Api.Providers.Count);
        Assert.Contains(convo.Api.Providers, p => p.Key == AiProviders.OpenAiKey);
        Assert.Contains(convo.Api.Providers, p => p.Key == AiProviders.OpenRouterKey);
    }
}