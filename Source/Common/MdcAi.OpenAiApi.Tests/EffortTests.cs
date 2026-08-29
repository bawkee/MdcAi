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

namespace MdcAi.OpenAiApi.Tests;

public class EffortTests
{
    #region AiEffort.ClosestToMedium

    [Fact]
    public void ClosestToMedium_prefers_an_exact_medium()
    {
        Assert.Equal("medium", AiEffort.ClosestToMedium(new[] { "low", "medium", "high" }));
    }

    [Fact]
    public void ClosestToMedium_picks_low_when_medium_is_not_offered()
    {
        // The cheaper neighbor; deterministic tie-break for sets like ["low","high"].
        Assert.Equal("low", AiEffort.ClosestToMedium(new[] { "low", "high" }));
    }

    [Fact]
    public void ClosestToMedium_keeps_the_declared_casing()
    {
        Assert.Equal("MEDIUM", AiEffort.ClosestToMedium(new[] { "LOW", "MEDIUM" }));
    }

    [Fact]
    public void ClosestToMedium_falls_back_to_first_for_exotic_sets()
    {
        Assert.Equal("hardcore", AiEffort.ClosestToMedium(new[] { "hardcore", "full" }));
    }

    [Fact]
    public void ClosestToMedium_handles_empty_and_null()
    {
        Assert.Null(AiEffort.ClosestToMedium(null));
        Assert.Null(AiEffort.ClosestToMedium(new string[0]));
    }

    #endregion

    #region AiModel.SupportedEfforts

    [Fact]
    public void SupportedEfforts_comes_from_metadata_for_openrouter_models()
    {
        var model = new AiModel("deepseek/deepseek-reasoner", AiProviders.OpenRouterKey)
        {
            Reasoning = new AiModelReasoning { SupportedEfforts = new[] { "low", "high" } }
        };

        Assert.Equal(new[] { "low", "high" }, model.SupportedEfforts);
    }

    [Fact]
    public void SupportedEfforts_is_null_for_openrouter_models_without_metadata()
    {
        // The metadata is authoritative: no id guessing for unknown OpenRouter authors (and
        // even known families go metadata-only on OpenRouter).
        var claude = new AiModel("anthropic/claude-3-5-sonnet", AiProviders.OpenRouterKey);
        var openaiO3OnOr = new AiModel("openai/o3", AiProviders.OpenRouterKey);

        Assert.Null(claude.SupportedEfforts);
        Assert.Null(openaiO3OnOr.SupportedEfforts);
    }

    [Fact]
    public void SupportedEfforts_uses_id_family_for_openai_provider_models()
    {
        Assert.Equal(AiEffort.Levels, new AiModel("o1", AiProviders.OpenAiKey).SupportedEfforts);
        Assert.Equal(AiEffort.Levels, new AiModel("o3-mini", AiProviders.OpenAiKey).SupportedEfforts);
        Assert.Equal(AiEffort.Levels, new AiModel("o4-mini", AiProviders.OpenAiKey).SupportedEfforts);
        Assert.Equal(AiEffort.Levels, new AiModel("gpt-5", AiProviders.OpenAiKey).SupportedEfforts);
        Assert.Equal(AiEffort.Levels, new AiModel("gpt-5-mini", AiProviders.OpenAiKey).SupportedEfforts);
    }

    [Fact]
    public void SupportedEfforts_is_null_for_plain_models()
    {
        Assert.Null(new AiModel("gpt-4o", AiProviders.OpenAiKey).SupportedEfforts);
        Assert.Null(new AiModel("gpt-3.5-turbo").SupportedEfforts); // unstamped, not in the family
    }

    #endregion

    #region ChatRequest.reasoning_effort

    [Fact]
    public void ChatRequest_serializes_reasoning_effort_when_set()
    {
        var json = JsonConvert.SerializeObject(new ChatRequest { ReasoningEffort = "medium" });

        Assert.Contains("\"reasoning_effort\":\"medium\"", json);
    }

    [Fact]
    public void ChatRequest_omits_reasoning_effort_when_null()
    {
        // This is what lets the app guarantee the parameter is never sent to effort-less models.
        var json = JsonConvert.SerializeObject(new ChatRequest { Model = "gpt-4o" });

        Assert.DoesNotContain("reasoning_effort", json);
    }

    #endregion
}