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

namespace MdcAi.ChatCore.Tests;

using MdcAi.ChatCore.Security;
using MdcAi.ChatCore.Sessions;
using MdcAi.ChatCore.Tools;
using MdcAi.OpenAiApi;

public class ChatToolSchedulerTests
{
    private readonly ChatToolRegistry _registry;
    private readonly ChatToolScheduler _scheduler;
    private readonly InMemorySink _sink;
    private readonly FakeReadTool _readTool = new();
    private readonly FakeApprovalService _approval = new();

    public ChatToolSchedulerTests()
    {
        _registry = ChatToolRegistry.Build(new IChatTool[] { _readTool });
        _scheduler = new ChatToolScheduler(_registry);
        _sink = new InMemorySink();
    }

    private ChatTurnRequest Turn(string workspace = null, IChatToolApprovalService approval = null) =>
        new("c1", "turn-1", "msg-1", "openrouter", "deepseek/deepseek-chat", null, null, workspace,
            new[] { "read_file" }, ChatTurnOrigin.Human, approval, ChatTurnLimits.Default);

    private async Task<IReadOnlyList<ChatToolResultRecord>> Run(params ChatMessageToolCall[] calls) =>
        await _scheduler.ExecuteAsync(calls, Turn(), _sink, new WorkspaceReadObservationSet(), 1, CancellationToken.None);

    [Fact]
    public async Task Returns_results_in_model_order_and_commits_each()
    {
        _readTool.OnRead = p => ChatToolExecutionResult.Success(new JValue("content of " + p), "content of " + p);

        var results = await Run(
            Call("call_1", "read_file", """{"path":"a.txt"}"""),
            Call("call_2", "read_file", """{"path":"b.txt"}"""));

        Assert.Equal(2, results.Count);
        Assert.Equal("call_1", results[0].ToolCallId);
        Assert.Equal("call_2", results[1].ToolCallId);
        Assert.Equal(2, _sink.ToolResults.Count);
        Assert.Equal(2, _sink.Messages.Count(m => m.Role == ChatMessageRole.Tool));
        Assert.Equal("content of a.txt", _sink.Messages[^2].Content);
        Assert.Equal("call_1", _sink.Messages[^2].ToolCallId);
    }

    [Fact]
    public async Task Unknown_tool_becomes_structured_failure()
    {
        var results = await Run(Call("call_1", "nope", "{}"));

        var r = Assert.Single(results);
        Assert.False(r.Result.Ok);
        Assert.Equal("unknown_tool", r.Result.ErrorCode);
        Assert.Contains("nope", r.Result.ModelContent);
    }

    [Fact]
    public async Task Invalid_json_arguments_become_structured_failure()
    {
        var results = await Run(new ChatMessageToolCall
        {
            Id = "call_1",
            Function = new ChatMessageFunction { Name = "read_file", Arguments = "{\"path\":" }
        });

        var r = Assert.Single(results);
        Assert.Equal("invalid_json_arguments", r.Result.ErrorCode);
    }

    [Fact]
    public async Task Schema_violating_arguments_become_structured_failure()
    {
        var results = await Run(Call("call_1", "read_file", """{"path":42}"""));

        var r = Assert.Single(results);
        Assert.Equal("type_mismatch", r.Result.ErrorCode);
    }

    [Fact]
    public async Task Denied_approval_denies_write_tool()
    {
        _approval.Decision = ChatApprovalDecision.Denied;
        var registry = ChatToolRegistry.Build(new IChatTool[] { new FakeWriteTool() });
        var scheduler = new ChatToolScheduler(registry);
        var sink = new InMemorySink();

        var results = await scheduler.ExecuteAsync(
            new[] { Call("call_1", "write_file", """{"path":"a.txt","content":"x"}""") },
            Turn(approval: _approval), sink, new WorkspaceReadObservationSet(), 1, CancellationToken.None);

        var r = Assert.Single(results);
        Assert.Equal("approval_denied", r.Result.ErrorCode);
        Assert.Equal(ChatToolStatus.Denied, r.Result.Status);
    }

    [Fact]
    public async Task No_approval_service_denies_mutating_tool()
    {
        var registry = ChatToolRegistry.Build(new IChatTool[] { new FakeWriteTool() });
        var scheduler = new ChatToolScheduler(registry);
        var sink = new InMemorySink();

        var results = await scheduler.ExecuteAsync(
            new[] { Call("call_1", "write_file", """{"path":"a.txt","content":"x"}""") },
            Turn(approval: null), sink, new WorkspaceReadObservationSet(), 1, CancellationToken.None);

        var r = Assert.Single(results);
        Assert.Equal("approval_denied", r.Result.ErrorCode);
    }

    [Fact]
    public async Task Approved_write_runs()
    {
        var registry = ChatToolRegistry.Build(new IChatTool[] { new FakeWriteTool() });
        var scheduler = new ChatToolScheduler(registry);
        var sink = new InMemorySink();
        var approval = new FakeApprovalService { Decision = ChatApprovalDecision.Approved };

        var results = await scheduler.ExecuteAsync(
            new[] { Call("call_1", "write_file", """{"path":"a.txt","content":"x"}""") },
            Turn(approval: approval), sink, new WorkspaceReadObservationSet(), 1, CancellationToken.None);

        var r = Assert.Single(results);
        Assert.True(r.Result.Ok);
        Assert.Equal("wrote a.txt", r.Result.ModelContent);
    }

    [Fact]
    public async Task Tool_exception_becomes_sanitized_failure()
    {
        _readTool.OnRead = _ => throw new InvalidOperationException("boom");
        var results = await Run(Call("call_1", "read_file", """{"path":"a.txt"}"""));

        var r = Assert.Single(results);
        Assert.Equal("tool_exception", r.Result.ErrorCode);
        Assert.Contains("boom", r.Result.ModelContent);
    }

    [Fact]
    public async Task Timeout_becomes_timed_out_result()
    {
        var registry = ChatToolRegistry.Build(new IChatTool[] { new SlowTool(TimeSpan.FromSeconds(10)) });
        var scheduler = new ChatToolScheduler(registry);
        var sink = new InMemorySink();

        var results = await scheduler.ExecuteAsync(
            new[] { Call("call_1", "slow_tool", "{}") },
            Turn(), sink, new WorkspaceReadObservationSet(), 1, CancellationToken.None);

        var r = Assert.Single(results);
        Assert.Equal(ChatToolStatus.TimedOut, r.Result.Status);
        Assert.Equal("timed_out", r.Result.ErrorCode);
    }

    [Fact]
    public async Task Repeated_identical_read_without_progress_hits_loop_guard()
    {
        _readTool.OnRead = p => ChatToolExecutionResult.Success(new JValue("same"), "same");

        var results = await Run(
            Call("call_1", "read_file", """{"path":"a.txt"}"""),
            Call("call_2", "read_file", """{"path":"a.txt"}"""),
            Call("call_3", "read_file", """{"path":"a.txt"}"""));

        var last = results[^1];
        Assert.Equal("probable_loop", last.Result.ErrorCode);
    }

    [Fact]
    public async Task Terminal_goal_result_skips_later_calls_with_protocol_valid_results()
    {
        var registry = ChatToolRegistry.Build(new IChatTool[] { new TerminatingGoalTool("complete_goal") });
        var scheduler = new ChatToolScheduler(registry);
        var sink = new InMemorySink();

        var results = await scheduler.ExecuteAsync(
            new[]
            {
                Call("call_1", "complete_goal", """{"summary":"done"}"""),
                Call("call_2", "complete_goal", """{"summary":"after"}""")
            },
            Turn(approval: _approval), sink, new WorkspaceReadObservationSet(), 1, CancellationToken.None);

        Assert.Equal(2, results.Count);
        Assert.True(results[0].Result.Ok);
        Assert.Equal("skipped_goal_terminal", results[1].Result.ErrorCode);
        Assert.Equal(ChatToolStatus.Skipped, results[1].Result.Status);
    }

    private static ChatMessageToolCall Call(string id, string name, string args) => new()
    {
        Id = id,
        Type = "function",
        Function = new ChatMessageFunction { Name = name, Arguments = args }
    };

    private sealed class FakeWriteTool : IChatTool
    {
        public string Name => "write_file";
        public string Description => "Writes a file.";
        public JObject ParametersSchema => JObject.Parse(
            """{"type":"object","properties":{"path":{"type":"string"},"content":{"type":"string"}},"required":["path","content"]}""");
        public ChatToolExecutionMode ExecutionMode => ChatToolExecutionMode.Exclusive;
        public ChatToolRisk Risk => ChatToolRisk.Write;
        public TimeSpan Timeout => TimeSpan.FromSeconds(30);

        public ValueTask<ChatToolExecutionResult> ExecuteAsync(JObject arguments, ChatToolExecutionContext context, CancellationToken ct) =>
            new(ChatToolExecutionResult.Success(new JValue("ok"), "wrote " + arguments["path"]));

        public ChatToolCallPresentation PresentCall(JObject arguments) =>
            ChatToolCallPresentation.Generic("Write", $"Write · {arguments["path"]}");
        public ChatToolResultPresentation PresentResult(JObject arguments, ChatToolExecutionResult result) =>
            ChatToolResultPresentation.Generic("Write", result.Ok ? "ok" : "failed");
    }

    private sealed class SlowTool : IChatTool
    {
        public string Name => "slow_tool";
        public string Description => "Never finishes in time.";
        public JObject ParametersSchema => new JObject { ["type"] = "object" };
        public ChatToolExecutionMode ExecutionMode => ChatToolExecutionMode.Exclusive;
        public ChatToolRisk Risk => ChatToolRisk.ReadOnly;
        public TimeSpan Timeout => TimeSpan.FromMilliseconds(50);

        private readonly TimeSpan _wait;

        public SlowTool(TimeSpan wait) => _wait = wait;

        public async ValueTask<ChatToolExecutionResult> ExecuteAsync(JObject arguments, ChatToolExecutionContext context, CancellationToken ct)
        {
            await Task.Delay(_wait, ct);
            return ChatToolExecutionResult.Success(new JValue("late"), "late");
        }

        public ChatToolCallPresentation PresentCall(JObject arguments) => ChatToolCallPresentation.Generic("Slow", "Slow");
        public ChatToolResultPresentation PresentResult(JObject arguments, ChatToolExecutionResult result) =>
            ChatToolResultPresentation.Generic("Slow", "Slow");
    }

    private sealed class TerminatingGoalTool : IChatTool
    {
        public string Name { get; }
        public string Description => "Terminal goal tool.";
        public JObject ParametersSchema => new JObject { ["type"] = "object" };
        public ChatToolExecutionMode ExecutionMode => ChatToolExecutionMode.Exclusive;
        public ChatToolRisk Risk => ChatToolRisk.Write;
        public TimeSpan Timeout => TimeSpan.FromSeconds(30);

        public TerminatingGoalTool(string name) => Name = name;

        public ValueTask<ChatToolExecutionResult> ExecuteAsync(JObject arguments, ChatToolExecutionContext context, CancellationToken ct) =>
            new(new ChatToolExecutionResult(true, ChatToolStatus.Completed, new JValue("done"), "goal complete", ConcludesTurn: true));

        public ChatToolCallPresentation PresentCall(JObject arguments) => ChatToolCallPresentation.Generic("Complete goal", "Complete");
        public ChatToolResultPresentation PresentResult(JObject arguments, ChatToolExecutionResult result) =>
            ChatToolResultPresentation.Generic("Complete goal", "Complete");
    }
}