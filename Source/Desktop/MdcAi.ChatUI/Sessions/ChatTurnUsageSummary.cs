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

namespace MdcAi.ChatUI.Sessions;

/// <summary>
/// Whole-turn usage aggregated from the durable step records while a turn runs (DSH proposal
/// §7.3 "Turn usage"). Unavailable metrics stay null - never displayed as zero.
/// </summary>
public sealed record ChatTurnUsageSummary(
    string TurnId,
    string ProviderModel,
    int StepCount,
    int ToolCallCount,
    long? PromptTokens,
    long? CompletionTokens,
    long? ReasoningTokens,
    long? PromptCacheReadTokens,
    long? PromptCacheWriteTokens,
    decimal? Cost,
    long? WallTimeMs,
    string Outcome)
{
    public static ChatTurnUsageSummary Empty(string turnId, string providerModel, string outcome = null) =>
        new(turnId, providerModel, 0, 0, null, null, null, null, null, null, null, outcome);
}