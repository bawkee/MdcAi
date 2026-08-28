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
    public async Task SelectModel_sets_working_selection()
    {
        var (convo, _, _, chatSettings) = Make(("openai:ApiKey", "sk-oa"), ("openrouter:ApiKey", "sk-or"));

        // The working model is the conversation's business (independent of the persisted
        // default living on chat settings).
        await convo.LoadModelsCmd.Execute();

        convo.SelectModelCmd.Execute("anthropic/claude-3-5-sonnet").Subscribe();

        Assert.Equal("anthropic/claude-3-5-sonnet", convo.SelectedModel);
        Assert.Equal("OpenRouter · anthropic/claude-3-5-sonnet", convo.SelectedModelLabel);
    }

    [Fact]
    public async Task SelectedModelLabel_prettifies_when_catalog_arrives()
    {
        var (convo, _, _, _) = Make(("openai:ApiKey", "sk-oa"), ("openrouter:ApiKey", "sk-or"));

        // No catalog loaded yet: the label is honest, just not pretty.
        convo.SelectedModel = "anthropic/claude-3-5-sonnet";
        await Task.Yield();

        Assert.Equal("anthropic/claude-3-5-sonnet", convo.SelectedModelLabel);

        // Catalog arrives -> the same selection re-renders as provider · display name.
        await convo.LoadModelsCmd.Execute();
        await Task.Yield();

        Assert.Equal("OpenRouter · anthropic/claude-3-5-sonnet", convo.SelectedModelLabel);
    }

    [Fact]
    public void Providers_exposed_for_grouped_pickers()
    {
        var (convo, _, _, _) = Make();

        Assert.Equal(2, convo.Api.Providers.Count);
        Assert.Contains(convo.Api.Providers, p => p.Key == AiProviders.OpenAiKey);
        Assert.Contains(convo.Api.Providers, p => p.Key == AiProviders.OpenRouterKey);
    }

    [Theory]
    [InlineData(null, "google/gemma-4-31b-it", null, false, "google/gemma-4-31b-it")] // legacy + loaded -> category default
    [InlineData(null, "google/gemma-4-31b-it", "gpt-4o", false, "google/gemma-4-31b-it")] // legacy + loaded beats a provisional current
    [InlineData(null, null, null, false, null)] // legacy + not loaded yet -> nothing (don't cache placeholders)
    [InlineData("anthropic/claude-3-5-sonnet", "google/gemma-4-31b-it", null, false, "anthropic/claude-3-5-sonnet")] // last reply wins
    [InlineData("anthropic/claude-3-5-sonnet", "google/gemma-4-31b-it", "gpt-4o", true, "gpt-4o")] // user pick never stomped
    [InlineData(null, "google/gemma-4-31b-it", "gpt-4o", true, "gpt-4o")] // user pick wins over loaded default too
    public void ResolveWorkingModel_covers_load_scenarios(
        string lastReply, string categoryDefault, string current, bool userPicked, string expected)
    {
        Assert.Equal(expected, ConversationVm.ResolveWorkingModel(lastReply, categoryDefault, current, userPicked));
    }

    [Fact]
    public async Task Legacy_conversation_defaults_to_category_model_once_loaded()
    {
        var (convo, _, _, chatSettings) = Make(("openai:ApiKey", "sk-oa"));
        // Simulate the category's settings having loaded (alternatively the ctor placeholder
        // would be cached - that was the "picks the first one" bug).
        chatSettings.IdSettings = "general";
        chatSettings.Model = "google/gemma-4-31b-it";

        // Legacy: only a user message, no per-message model provenance anywhere.
        var user = new ChatMessageVm(convo, ChatMessageRole.User) { Content = "hi" };
        convo.Head = user.Selector;

        await Task.Delay(200); // let the 50ms apply throttle fire

        Assert.Equal("google/gemma-4-31b-it", convo.SelectedModel);
        Assert.Equal("google/gemma-4-31b-it", convo.SelectedModelLabel);
    }

    [Fact]
    public async Task Modern_conversation_defaults_to_last_reply_model()
    {
        var (convo, _, _, chatSettings) = Make(("openai:ApiKey", "sk-oa"));
        chatSettings.IdSettings = "general";
        chatSettings.Model = "google/gemma-4-31b-it"; // category default - must lose to the reply's model

        var user = new ChatMessageVm(convo, ChatMessageRole.User) { Content = "hi" };
        var assistant = new ChatMessageVm(convo, ChatMessageRole.Assistant)
        {
            Content = "hello",
            Model = "anthropic/claude-3-5-sonnet", // per-message provenance
            Previous = user
        };
        user.Next = assistant;
        convo.Head = user.Selector;

        await Task.Delay(200);

        Assert.Equal("anthropic/claude-3-5-sonnet", convo.SelectedModel);
    }

    [Fact]
    public async Task User_pick_is_never_overridden_by_load()
    {
        var (convo, _, _, chatSettings) = Make(("openai:ApiKey", "sk-oa"));
        chatSettings.IdSettings = "general";
        chatSettings.Model = "google/gemma-4-31b-it";

        await convo.LoadModelsCmd.Execute(); // so the picker label can prettify

        convo.SelectModelCmd.Execute("gpt-4o").Subscribe();

        var user = new ChatMessageVm(convo, ChatMessageRole.User) { Content = "hi" };
        convo.Head = user.Selector;

        await Task.Delay(200);

        Assert.Equal("gpt-4o", convo.SelectedModel);
        Assert.Equal("OpenAI · gpt-4o", convo.SelectedModelLabel);
    }
}