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

using MdcAi.ChatCore.Jobs;
using Newtonsoft.Json.Linq;

/// <summary>Shared model-facing payload builders for job tools.</summary>
internal static class JobToolPayloads
{
    public static JObject RunningPayload(string jobId, string newOutput, long nextCursor) => new()
    {
        ["status"] = "running",
        ["job_id"] = jobId,
        ["new_output"] = newOutput ?? "",
        ["next_cursor"] = nextCursor,
        ["message"] = "Still running. Poll with get_job(job_id, cursor=next_cursor, wait_ms) until status is terminal. Do NOT start a duplicate command while a job is running."
    };

    public static JObject TerminalPayload(BackgroundJobRecord record, string newOutput, long nextCursor) => new()
    {
        ["status"] = record.Status,
        ["job_id"] = record.JobId,
        ["exit_code"] = record.ExitCode,
        ["new_output"] = newOutput ?? "",
        ["next_cursor"] = nextCursor,
        ["summary"] = record.FailureSummary
    };
}

/// <summary>
/// get_job: polls a running job with a consuming output cursor (DSH proposal §8.1/8.2). Returns
/// only NEW output since the cursor plus the next cursor; ownership is enforced by the service.
/// </summary>
public sealed class GetJobChatTool : IChatTool
{
    public string Name => "get_job";
    public string Description =>
        "Poll a background job (job_id from run_powershell). Pass the previous next_cursor to receive " +
        "only new output; set wait_ms to block briefly for progress. Keep polling until status is terminal " +
        "before claiming success.";

    public JObject ParametersSchema => JObject.Parse(
        """
        {
          "type": "object",
          "additionalProperties": false,
          "properties": {
            "job_id": { "type": "string" },
            "cursor": { "type": "integer", "minimum": 0 },
            "wait_ms": { "type": "integer", "minimum": 0, "maximum": 30000 }
          },
          "required": ["job_id"]
        }
        """);

    public ChatToolExecutionMode ExecutionMode => ChatToolExecutionMode.ParallelSafe;
    public ChatToolRisk Risk => ChatToolRisk.ReadOnly;
    public TimeSpan Timeout => TimeSpan.FromSeconds(60);

    public async ValueTask<ChatToolExecutionResult> ExecuteAsync(
        JObject arguments,
        ChatToolExecutionContext context,
        CancellationToken ct)
    {
        var jobId = (string)arguments["job_id"];
        var cursor = arguments["cursor"]?.Value<long?>() ?? 0;
        var waitMs = arguments["wait_ms"]?.Value<int?>();

        if (context.JobService == null)
            return Error("job_service_unavailable", "Background jobs are not available in this conversation.");

        try
        {
            var poll = await context.JobService.PollAsync(
                jobId, context.ConversationId, cursor, waitMs, ct);

            var payload = poll.IsTerminal
                              ? JobToolPayloads.TerminalPayload(poll.Record, poll.NewOutput, poll.NextCursor)
                              : JobToolPayloads.RunningPayload(poll.Record.JobId, poll.NewOutput, poll.NextCursor);

            return ChatToolExecutionResult.Success(payload, payload.ToString(Formatting.None));
        }
        catch (UnauthorizedAccessException ex)
        {
            return Error("job_ownership_mismatch", ex.Message);
        }
        catch (KeyNotFoundException ex)
        {
            return Error("job_not_found", ex.Message);
        }
    }

    public ChatToolCallPresentation PresentCall(JObject arguments) =>
        ChatToolCallPresentation.Generic("Poll job", $"Get job · {arguments["job_id"]}",
                                         new JObject { ["job_id"] = arguments["job_id"] });

    public ChatToolResultPresentation PresentResult(JObject arguments, ChatToolExecutionResult result) =>
        ChatToolResultPresentation.Generic("Poll job", result.Ok ? "Poll ok" : "Poll failed",
                                           new JObject { ["job_id"] = arguments["job_id"] });

    private static ChatToolExecutionResult Error(string code, string summary) =>
        ChatToolExecutionResult.Failure(ChatToolStatus.Failed, code, summary);
}

/// <summary>
/// stop_job: ownership-checked cancellation of a running job; returns the final killed/failed
/// snapshot after cleanup (DSH proposal §8.1 point 6).
/// </summary>
public sealed class StopJobChatTool : IChatTool
{
    public string Name => "stop_job";
    public string Description => "Stop a running background job that this conversation owns; returns the final killed snapshot.";

    public JObject ParametersSchema => JObject.Parse(
        """
        {
          "type": "object",
          "additionalProperties": false,
          "properties": {
            "job_id": { "type": "string" }
          },
          "required": ["job_id"]
        }
        """);

    public ChatToolExecutionMode ExecutionMode => ChatToolExecutionMode.Exclusive;
    public ChatToolRisk Risk => ChatToolRisk.Process;
    public TimeSpan Timeout => TimeSpan.FromSeconds(60);

    public async ValueTask<ChatToolExecutionResult> ExecuteAsync(
        JObject arguments,
        ChatToolExecutionContext context,
        CancellationToken ct)
    {
        var jobId = (string)arguments["job_id"];

        if (context.JobService == null)
            return Error("job_service_unavailable", "Background jobs are not available in this conversation.");

        try
        {
            var record = await context.JobService.StopAsync(jobId, context.ConversationId, ct);
            var payload = JobToolPayloads.TerminalPayload(record, "", 0);
            return ChatToolExecutionResult.Success(payload, payload.ToString(Formatting.None));
        }
        catch (UnauthorizedAccessException ex)
        {
            return Error("job_ownership_mismatch", ex.Message);
        }
        catch (KeyNotFoundException ex)
        {
            return Error("job_not_found", ex.Message);
        }
    }

    public ChatToolCallPresentation PresentCall(JObject arguments) =>
        ChatToolCallPresentation.Generic("Stop job", $"Stop job · {arguments["job_id"]}",
                                         new JObject { ["job_id"] = arguments["job_id"] });

    public ChatToolResultPresentation PresentResult(JObject arguments, ChatToolExecutionResult result) =>
        ChatToolResultPresentation.Generic("Stop job", result.Ok ? "Stopped" : "Stop failed",
                                           new JObject { ["job_id"] = arguments["job_id"] });

    private static ChatToolExecutionResult Error(string code, string summary) =>
        ChatToolExecutionResult.Failure(ChatToolStatus.Failed, code, summary);
}