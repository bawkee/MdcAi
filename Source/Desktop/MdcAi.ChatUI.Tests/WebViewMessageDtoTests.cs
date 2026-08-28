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

/// <summary>
/// Covers the C# -> JS wire payload (WebViewChatMessageDto): the provider/model
/// stamping added so the React renderer can show who produced each message.
/// Model is per-message provenance (persisted on the message itself), not the
/// conversation's current selection — legacy messages carry null.
/// </summary>
public class WebViewMessageDtoTests
{
    public WebViewMessageDtoTests() { TestRx.Init(); }

    private static (ConversationVm convo, ChatSettingsVm chatSettings) Make()
    {
        var api = new FakeOpenAiApi();
        var store = new InMemoryCredsStore();
        var settings = TestSettings.Build(store);
        var chatSettings = new ChatSettingsVm(api);
        var convo = new ConversationVm(api, settings, chatSettings);
        return (convo, chatSettings);
    }

    [Theory]
    [InlineData("gpt-4o", "OpenAI")]
    [InlineData("anthropic/claude-3-5-sonnet", "OpenRouter")]
    [InlineData("openai/gpt-4o-mini", "OpenRouter")]
    public void GetWebViewDto_stamps_model_and_provider(string modelId, string expectedProvider)
    {
        var (convo, _) = Make();

        var msg = new ChatMessageVm(convo, ChatMessageRole.Assistant)
        {
            Content = "Hello",
            Model = modelId
        };

        var dto = msg.GetWebViewDto();

        Assert.Equal(modelId, dto.Model);
        Assert.Equal(expectedProvider, dto.Provider);
        Assert.Equal("assistant", dto.Role);
    }

    [Fact]
    public void GetWebViewDto_uses_per_message_model_not_conversation_selection()
    {
        var (convo, _) = Make();
        convo.SelectedModel = "gpt-4o";

        // The conversation picker says gpt-4o right now, but this message was produced
        // by a different model — provenance comes from the message itself.
        var msg = new ChatMessageVm(convo, ChatMessageRole.Assistant)
        {
            Content = "Hello",
            Model = "anthropic/claude-3-5-sonnet"
        };

        var dto = msg.GetWebViewDto();

        Assert.Equal("anthropic/claude-3-5-sonnet", dto.Model);
        Assert.Equal("OpenRouter", dto.Provider);
    }

    [Fact]
    public void GetWebViewDto_legacy_assistant_messages_carry_no_model_info()
    {
        var (convo, _) = Make();
        convo.SelectedModel = "gpt-4o";

        // No Model ever stamped on legacy rows: the renderer falls back to a generic label.
        var dto = new ChatMessageVm(convo, ChatMessageRole.Assistant).GetWebViewDto();

        Assert.Null(dto.Model);
        Assert.Null(dto.Provider);
    }

    [Fact]
    public void GetWebViewDto_user_messages_carry_no_model_info()
    {
        var (convo, _) = Make();
        convo.SelectedModel = "gpt-4o";

        var dto = new ChatMessageVm(convo, ChatMessageRole.User)
        {
            Content = "hi there"
        }.GetWebViewDto();

        // User messages are never stamped with a model (only completions are); the
        // renderer labels them "You" regardless.
        Assert.Equal("user", dto.Role);
        Assert.Null(dto.Model);
        Assert.Null(dto.Provider);
    }

    [Fact]
    public void GetWebViewDto_keeps_version_and_content_shape()
    {
        var (convo, _) = Make();
        convo.SelectedModel = "gpt-4o";

        var msg = new ChatMessageVm(convo, ChatMessageRole.Assistant)
        {
            Content = "plain text",
            Model = "gpt-4o"
        };

        var dto = msg.GetWebViewDto();

        // HTMLContent isn't rendered yet (throttled), so Content falls back to a <p> wrap.
        Assert.Equal("<p>plain text</p>", dto.Content);
        Assert.Equal(1, dto.VersionCount);
        Assert.NotNull(dto.Id);
        Assert.NotNull(dto.CreatedTs);
    }
}