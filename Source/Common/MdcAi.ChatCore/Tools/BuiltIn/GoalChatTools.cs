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

using MdcAi.ChatCore.Goals;
using Newtonsoft.Json.Linq;

/// <summary>
/// complete_goal: transitions the matching active goal revision to complete and CONCLUDES the
/// turn (host-only ConcludesTurn flag so the scheduler skips later calls with protocol-valid
/// skipped_goal_terminal results - DSH proposal §8.3 point on terminal goal tools).
/// </summary>
public sealed class CompleteGoalChatTool : IChatTool
{
    private readonly Func<CancellationToken, Task<GoalState>> _loadActive;
    private readonly Func<GoalState, GoalState, CancellationToken, Task<bool>> _update;

    public CompleteGoalChatTool(
        Func<CancellationToken, Task<GoalState>> loadActive,
        Func<GoalState, GoalState, CancellationToken, Task<bool>> update)
    {
        _loadActive = loadActive;
        _update = update;
    }

    public string Name => "complete_goal";
    public string Description =>
        "Mark the active goal complete with a summary and evidence. Concludes the current turn; " +
        "calls after this one are not executed.";

    public JObject ParametersSchema => JObject.Parse(
        """
        {
          "type": "object",
          "additionalProperties": false,
          "properties": {
            "summary": { "type": "string" },
            "evidence": { "type": "array", "items": { "type": "string" } }
          },
          "required": ["summary"]
        }
        """);

    public ChatToolExecutionMode ExecutionMode => ChatToolExecutionMode.Exclusive;
    public ChatToolRisk Risk => ChatToolRisk.Write;
    public TimeSpan Timeout => TimeSpan.FromSeconds(30);

    public async ValueTask<ChatToolExecutionResult> ExecuteAsync(
        JObject arguments,
        ChatToolExecutionContext context,
        CancellationToken ct)
    {
        var goal = await _loadActive(ct);
        if (goal == null)
            return Error("no_active_goal", "There is no active goal to complete.");

        var completed = goal.Complete(
            (string)arguments["summary"],
            arguments["evidence"]?.ToString(Formatting.None));

        if (!await _update(goal, completed, ct))
            return Error("goal_revision_mismatch", "The goal changed concurrently; retry.");

        return new ChatToolExecutionResult(
            true, ChatToolStatus.Completed,
            new JObject { ["status"] = "complete", ["goal_id"] = goal.IdGoal },
            """{"status":"complete","summary":"goal marked complete"}""",
            ConcludesTurn: true);
    }

    public ChatToolCallPresentation PresentCall(JObject arguments) =>
        ChatToolCallPresentation.Generic("Complete goal", "Mark the active goal complete",
                                         new JObject { ["summary"] = arguments["summary"] });

    public ChatToolResultPresentation PresentResult(JObject arguments, ChatToolExecutionResult result) =>
        ChatToolResultPresentation.Generic("Complete goal", result.Ok ? "Goal complete" : "Complete failed");

    private static ChatToolExecutionResult Error(string code, string summary) =>
        ChatToolExecutionResult.Failure(ChatToolStatus.Failed, code, summary);
}

/// <summary>
/// block_goal: transitions the active goal to blocked with a specific code/reason/required input
/// and concludes the turn (DSH proposal §8.3).
/// </summary>
public sealed class BlockGoalChatTool : IChatTool
{
    private readonly Func<CancellationToken, Task<GoalState>> _loadActive;
    private readonly Func<GoalState, GoalState, CancellationToken, Task<bool>> _update;

    public BlockGoalChatTool(
        Func<CancellationToken, Task<GoalState>> loadActive,
        Func<GoalState, GoalState, CancellationToken, Task<bool>> update)
    {
        _loadActive = loadActive;
        _update = update;
    }

    public string Name => "block_goal";
    public string Description =>
        "Mark the active goal blocked with a specific reason and the input/external change required to unblock. Concludes the turn.";

    public JObject ParametersSchema => JObject.Parse(
        """
        {
          "type": "object",
          "additionalProperties": false,
          "properties": {
            "code": { "type": "string" },
            "reason": { "type": "string" },
            "required_input": { "type": "string" }
          },
          "required": ["code", "reason"]
        }
        """);

    public ChatToolExecutionMode ExecutionMode => ChatToolExecutionMode.Exclusive;
    public ChatToolRisk Risk => ChatToolRisk.Write;
    public TimeSpan Timeout => TimeSpan.FromSeconds(30);

    public async ValueTask<ChatToolExecutionResult> ExecuteAsync(
        JObject arguments,
        ChatToolExecutionContext context,
        CancellationToken ct)
    {
        var goal = await _loadActive(ct);
        if (goal == null)
            return Error("no_active_goal", "There is no active goal to block.");

        var code = (string)arguments["code"];
        var reason = (string)arguments["reason"];
        var required = (string)arguments["required_input"];

        var blocked = goal.Block(code, string.IsNullOrEmpty(required) ? reason : reason + " | Required input: " + required);

        if (!await _update(goal, blocked, ct))
            return Error("goal_revision_mismatch", "The goal changed concurrently; retry.");

        return new ChatToolExecutionResult(
            true, ChatToolStatus.Completed,
            new JObject { ["status"] = "blocked", ["goal_id"] = goal.IdGoal },
            """{"status":"blocked"}""",
            ConcludesTurn: true);
    }

    public ChatToolCallPresentation PresentCall(JObject arguments) =>
        ChatToolCallPresentation.Generic("Block goal", "Mark the active goal blocked",
                                         new JObject { ["code"] = arguments["code"] });

    public ChatToolResultPresentation PresentResult(JObject arguments, ChatToolExecutionResult result) =>
        ChatToolResultPresentation.Generic("Block goal", result.Ok ? "Goal blocked" : "Block failed");

    private static ChatToolExecutionResult Error(string code, string summary) =>
        ChatToolExecutionResult.Failure(ChatToolStatus.Failed, code, summary);
}