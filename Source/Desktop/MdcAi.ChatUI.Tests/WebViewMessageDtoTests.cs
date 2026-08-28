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
        var (convo, chatSettings) = Make();
        chatSettings.SelectedModel = modelId;

        var msg = new ChatMessageVm(convo, ChatMessageRole.Assistant)
        {
            Content = "Hello"
        };

        var dto = msg.GetWebViewDto();

        Assert.Equal(modelId, dto.Model);
        Assert.Equal(expectedProvider, dto.Provider);
        Assert.Equal("assistant", dto.Role);
    }

    [Fact]
    public void GetWebViewDto_falls_back_to_model_when_selected_is_null()
    {
        var (convo, chatSettings) = Make();
        chatSettings.SelectedModel = null;
        chatSettings.Model = "gpt-4o";

        var dto = new ChatMessageVm(convo, ChatMessageRole.Assistant).GetWebViewDto();

        Assert.Equal("gpt-4o", dto.Model);
        Assert.Equal("OpenAI", dto.Provider);
    }

    [Fact]
    public void GetWebViewDto_user_messages_carry_no_model_info()
    {
        var (convo, chatSettings) = Make();
        chatSettings.SelectedModel = "gpt-4o";

        var dto = new ChatMessageVm(convo, ChatMessageRole.User)
        {
            Content = "hi there"
        }.GetWebViewDto();

        // The payload still carries the model stamp (it's the conversation's model),
        // but the renderer ignores it for user role.
        Assert.Equal("user", dto.Role);
        Assert.Equal("gpt-4o", dto.Model);
        Assert.Equal("OpenAI", dto.Provider);
    }

    [Fact]
    public void GetWebViewDto_keeps_version_and_content_shape()
    {
        var (convo, chatSettings) = Make();
        chatSettings.SelectedModel = "gpt-4o";

        var msg = new ChatMessageVm(convo, ChatMessageRole.Assistant)
        {
            Content = "plain text"
        };

        var dto = msg.GetWebViewDto();

        // HTMLContent isn't rendered yet (throttled), so Content falls back to a <p> wrap.
        Assert.Equal("<p>plain text</p>", dto.Content);
        Assert.Equal(1, dto.VersionCount);
        Assert.NotNull(dto.Id);
        Assert.NotNull(dto.CreatedTs);
    }
}