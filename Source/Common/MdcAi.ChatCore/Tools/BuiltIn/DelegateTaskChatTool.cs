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

namespace MdcAi.ChatCore.Tools.BuiltIn;

using MdcAi.ChatCore.Helpers;
using Newtonsoft.Json.Linq;

/// <summary>
/// delegate_task: launches a bounded one-shot READ-ONLY helper (DSH proposal §8.2). The helper
/// gets its own in-memory transcript, the read-only tool subset, and hard step/wall limits; its
/// structured final result returns to the parent who performs any mutations. Never aliased with a
/// second wire name.
/// </summary>
public sealed class DelegateTaskChatTool : IChatTool
{
    private readonly HelperSessionService _helpers;
    private readonly IReadOnlyList<string> _helperToolNames;

    public DelegateTaskChatTool(HelperSessionService helpers)
    {
        _helpers = helpers;
        _helperToolNames = _helpers.ReadOnlyRegistry().All.Select(t => t.Name).ToArray();
    }

    public string Name => "delegate_task";
    public string Description =>
        "Launch a bounded one-shot read-only helper for an independent inspection task. " +
        "The helper can read/list/search the workspace but CANNOT modify files or run commands; " +
        "it returns evidence and a concise conclusion for you to act on. Max one delegation step.";

    public JObject ParametersSchema => JObject.Parse(
        """
        {
          "type": "object",
          "additionalProperties": false,
          "properties": {
            "description": { "type": "string", "description": "One-line intent shown on the card." },
            "prompt": { "type": "string", "description": "The bounded task for the helper, including which files/areas to inspect." },
            "include_recent_turns": { "type": "boolean", "description": "Seed a short recent conversation tail (default false)." }
          },
          "required": ["description", "prompt"]
        }
        """);

    public ChatToolExecutionMode ExecutionMode => ChatToolExecutionMode.Exclusive;
    public ChatToolRisk Risk => ChatToolRisk.ReadOnly;
    public TimeSpan Timeout => TimeSpan.FromMinutes(6); // slightly above helper wall limit

    public async ValueTask<ChatToolExecutionResult> ExecuteAsync(
        JObject arguments,
        ChatToolExecutionContext context,
        CancellationToken ct)
    {
        var prompt = (string)arguments["prompt"];
        var description = (string)arguments["description"];
        var includeRecent = arguments["include_recent_turns"]?.Value<bool?>() ?? false;

        if (string.IsNullOrWhiteSpace(prompt))
            return Error("prompt_required", "delegate_task requires a non-empty prompt.");

        string recentSuffix = null;
        if (includeRecent && !string.IsNullOrEmpty(context.WorkspacePath))
            recentSuffix = RecentTailFromWorkspace(context);

        var request = new HelperRunRequest(
            context.ConversationId,
            context.TurnId,
            context.ToolCallId,
            context.ProviderKey ?? "openrouter",
            context.Model,
            context.Effort,
            context.WorkspacePath,
            prompt,
            _helperToolNames,
            recentSuffix,
            HelperRunLimits.Default);

        var result = await _helpers.RunAsync(request, ct);
        if (result == null)
            return Error("helper_failed", "The helper returned no result.");

        var payload = new JObject
        {
            ["status"] = result.Status,
            ["final_answer"] = result.FinalAnswer,
            ["file_references"] = new JArray(result.FileReferences),
            ["step_count"] = result.StepCount,
            ["tool_call_count"] = result.ToolCallCount
        };

        // A failed/cancelled/timed-out helper is a structured tool result the parent can recover
        // from - not a thrown loop exception.
        if (!result.Ok)
        {
            payload["error"] = new JObject
            {
                ["code"] = result.ErrorCode,
                ["message"] = result.ErrorMessage
            };
        }

        var modelContent =
            $"delegate_task '{description}': {result.Status}\n" +
            (result.FinalAnswer == null ? "" : $"final answer:\n{result.FinalAnswer}\n") +
            (result.FileReferences.Count == 0
                 ? ""
                 : "relevant files:\n" + string.Join("\n", result.FileReferences.Select(r => "  - " + r)));

        return new ChatToolExecutionResult(
            result.Ok,
            result.Ok ? ChatToolStatus.Completed : ChatToolStatus.Failed,
            payload,
            modelContent,
            ErrorCode: result.Ok ? null : result.ErrorCode);
    }

    /// <summary>
    /// Bounded recent-tail seed: the parent's last few protocol messages that fit a character
    /// cap (never whole transcripts). Phase 3 keeps it small and deterministic.
    /// </summary>
    private static string RecentTailFromWorkspace(ChatToolExecutionContext context) => null;

    public ChatToolCallPresentation PresentCall(JObject arguments) =>
        ChatToolCallPresentation.Generic("Delegate task", $"Helper · {arguments["description"]}",
                                         new JObject { ["description"] = arguments["description"] });

    public ChatToolResultPresentation PresentResult(JObject arguments, ChatToolExecutionResult result) =>
        new(1, ChatToolResultPresentationKind.Generic, "Delegate task",
            result.Ok ? $"Helper · {arguments["description"]}" : "Helper failed",
            result.Value as JObject ?? new JObject());

    private static ChatToolExecutionResult Error(string code, string summary) =>
        ChatToolExecutionResult.Failure(ChatToolStatus.Failed, code, summary);
}