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

using MdcAi.ChatCore.Sessions;
using MdcAi.ChatCore.Tools;
using MdcAi.OpenAiApi;

/// <summary>
/// The step-loop vertical slice (DSH proposal §6.2): a scripted fake model performs
/// read_file → tool result → final answer through two model requests; multiple/messy calls and
/// guard outcomes are exercised through the same driver.
/// </summary>
public class ChatSessionServiceTests
{
    private readonly ScriptedFakeApi _api = new();
    private readonly FakeReadTool _readTool = new();
    private readonly ChatToolRegistry _registry;
    private readonly ChatSessionService _service;

    public ChatSessionServiceTests()
    {
        _registry = ChatToolRegistry.Build(new IChatTool[] { _readTool });
        _service = new ChatSessionService(_api, _registry);
    }

    private static InMemorySink SinkWithUserMessage(string text = "inspect the file") =>
        new() { Messages = { new ChatMessage(ChatMessageRole.User, text) } };

    private ChatTurnRequest Turn(string workspace = @"C:\ws") =>
        new("c1", "turn-1", "msg-1", AiProviders.OpenRouterKey, "deepseek/deepseek-chat", "medium",
            "You are a helpful assistant.", workspace, new[] { "read_file" },
            ChatTurnOrigin.Human, null, ChatTurnLimits.Default);

    [Fact]
    public async Task Read_then_answer_vertical_slice_runs_two_steps()
    {
        _api.EnqueueStream(req =>
            {
                // Step 1: tools advertised, history has user + system, provider/model stamped.
                Assert.Equal("deepseek/deepseek-chat", req.Model);
                Assert.Equal(AiProviders.OpenRouterKey, req.ProviderKey);
                Assert.NotNull(req.Tools);
                Assert.Contains(req.Messages, m => m.Role == ChatMessageRole.System);
            },
            FakeChunks.Reasoning("I should read the file."),
            FakeChunks.ToolCallChunk(0, id: "call_1", name: "read_file", args: "{\"path\":\"a.txt\"}"),
            FakeChunks.Finish("tool_calls"));

        _api.EnqueueStream(req =>
            {
                // Step 2: history now contains the assistant tool call and the tool result.
                Assert.Collection(req.Messages.Where(m => m.Role == ChatMessageRole.Tool),
                                  m => Assert.Equal("call_1", m.ToolCallId));
                Assert.Contains(req.Messages, m =>
                    m.Role == ChatMessageRole.Assistant && m.ToolCalls is { Length: 1 });
            },
            FakeChunks.Content("The file contains: "),
            FakeChunks.Content("hello"),
            FakeChunks.Finish());

        _readTool.OnRead = p => ChatToolExecutionResult.Success(new JValue("hello"), "file a.txt: hello");

        var sink = SinkWithUserMessage();
        var result = await _service.RunTurnAsync(Turn(), sink, CancellationToken.None);

        Assert.Equal(ChatTurnOutcome.Completed, result.Outcome);
        Assert.Equal(2, result.StepCount);
        Assert.Equal(1, result.TotalToolCalls);
        Assert.Equal(2, _api.Requests.Count);

        // Transcript: user, assistant(tool), tool result, assistant(final).
        var roles = sink.Messages.Select(m => (string)m.Role).ToArray();
        Assert.Equal(new[] { "user", "assistant", "tool", "assistant" }, roles);
        Assert.Equal("The file contains: hello", sink.Messages[^1].Content);
        Assert.Equal("I should read the file.", sink.Messages[1].ReasoningContent);
        Assert.Equal("file a.txt: hello", sink.Messages[2].Content);
        Assert.Equal("call_1", sink.Messages[2].ToolCallId);
    }

    [Fact]
    public async Task Two_tool_calls_in_one_message_get_two_ordered_results()
    {
        _api.EnqueueStream(
            FakeChunks.ToolCallChunk(0, id: "call_1", name: "read_file", args: "{\"path\":\"a.txt\"}"),
            FakeChunks.ToolCallChunk(1, id: "call_2", name: "read_file", args: "{\"path\":\"b.txt\"}"),
            FakeChunks.Finish("tool_calls"));

        _api.EnqueueStream(FakeChunks.Content("both read"), FakeChunks.Finish());

        _readTool.OnRead = p => ChatToolExecutionResult.Success(new JValue(p), "content of " + p);

        var sink = SinkWithUserMessage();
        var result = await _service.RunTurnAsync(Turn(), sink, CancellationToken.None);

        Assert.Equal(ChatTurnOutcome.Completed, result.Outcome);
        Assert.Collection(sink.ToolResults,
                          r => Assert.Equal("call_1", r.ToolCallId),
                          r => Assert.Equal("call_2", r.ToolCallId));
        // Protocol tool results in model order
        Assert.Equal("call_1", sink.Messages[2].ToolCallId);
        Assert.Equal("content of a.txt", sink.Messages[2].Content);
        Assert.Equal("call_2", sink.Messages[3].ToolCallId);
    }

    [Fact]
    public async Task Unknown_tool_becomes_structured_result_and_turn_continues()
    {
        _api.EnqueueStream(
            FakeChunks.ToolCallChunk(0, id: "call_1", name: "does_not_exist", args: "{}"),
            FakeChunks.Finish("tool_calls"));
        _api.EnqueueStream(FakeChunks.Content("recovered"), FakeChunks.Finish());

        var sink = SinkWithUserMessage();
        var result = await _service.RunTurnAsync(Turn(), sink, CancellationToken.None);

        Assert.Equal(ChatTurnOutcome.Completed, result.Outcome);
        var toolMsg = sink.Messages[2];
        Assert.Equal(ChatMessageRole.Tool, toolMsg.Role);
        Assert.Contains("unknown_tool", toolMsg.Content);
        Assert.Equal("recovered", sink.Messages[^1].Content);
    }

    [Fact]
    public async Task Finish_reason_length_with_partial_tool_call_executes_nothing()
    {
        _api.EnqueueStream(
            FakeChunks.ToolCallChunk(0, id: "call_1", name: "read_file", args: "{\"path\":"),
            FakeChunks.Finish("length"));

        _readTool.OnRead = p => throw new InvalidOperationException("must not execute");

        var sink = SinkWithUserMessage();
        var result = await _service.RunTurnAsync(Turn(), sink, CancellationToken.None);

        Assert.Equal(ChatTurnOutcome.MaxTokens, result.Outcome);
        Assert.Empty(sink.ToolResults);
    }

    [Fact]
    public async Task MaxTokens_without_tools_is_sticky_outcome()
    {
        _api.EnqueueStream(FakeChunks.Content("partial answer"), FakeChunks.Finish("length"));

        var sink = SinkWithUserMessage();
        var result = await _service.RunTurnAsync(Turn(), sink, CancellationToken.None);

        Assert.Equal(ChatTurnOutcome.MaxTokens, result.Outcome);
        Assert.Equal("partial answer", sink.Messages[^1].Content);
        Assert.Contains(sink.Checkpoints, c => c.Status == "max_tokens");
    }

    [Fact]
    public async Task MaxSteps_limit_ends_loop_with_explicit_outcome()
    {
        // Every step ends by asking for another tool call -> the step guard must terminate.
        for (var i = 0; i < 3; i++)
        {
            _api.EnqueueStream(
                FakeChunks.ToolCallChunk(0, id: "call_" + i, name: "read_file", args: "{\"path\":\"a.txt\"}"),
                FakeChunks.Finish("tool_calls"));
        }

        _readTool.OnRead = p => ChatToolExecutionResult.Success(new JValue("x"), "x");

        var turn = Turn() with { Limits = new ChatTurnLimits(MaxSteps: 2) };
        var sink = SinkWithUserMessage();
        var result = await _service.RunTurnAsync(turn, sink, CancellationToken.None);

        Assert.Equal(ChatTurnOutcome.MaxSteps, result.Outcome);
        Assert.Equal(2, result.StepCount);
    }

    [Fact]
    public async Task Tools_disabled_sends_no_tools_and_no_workspace_context()
    {
        _api.EnqueueStream(req =>
            {
                Assert.Null(req.Tools);
                var system = req.Messages.First(m => m.Role == ChatMessageRole.System);
                Assert.DoesNotContain("Workspace", system.Content);
            },
            FakeChunks.Content("plain chat"),
            FakeChunks.Finish());

        var turn = Turn() with { EnabledToolNames = Array.Empty<string>(), WorkspacePath = null, Effort = null };
        var sink = SinkWithUserMessage();
        var result = await _service.RunTurnAsync(turn, sink, CancellationToken.None);

        Assert.Equal(ChatTurnOutcome.Completed, result.Outcome);
        Assert.Equal("plain chat", sink.Messages[^1].Content);
    }

    [Fact]
    public async Task Cancellation_during_streaming_persists_interrupted_prefix()
    {
        using var cts = new CancellationTokenSource();

        // First response delivers a prefix chunk, then hangs until cancelled.
        _api.EnqueueHanging(FakeChunks.Content("prefix "));

        var sink = SinkWithUserMessage();
        var run = _service.RunTurnAsync(Turn(), sink, cts.Token);

        // Let the prefix flow, then cancel mid-stream.
        await Task.Delay(150);
        cts.Cancel();

        var result = await run;

        Assert.Equal(ChatTurnOutcome.Cancelled, result.Outcome);
        // The delivered prefix stays as an honest interrupted assistant message.
        var assistant = sink.Messages.Last(m => m.Role == ChatMessageRole.Assistant);
        Assert.Equal("prefix ", assistant.Content);
        Assert.Contains(sink.Abandoned, a => a.KeepPrefix);
        Assert.Empty(sink.ToolResults);
    }

    [Fact]
    public async Task Tool_result_above_model_visible_cap_is_truncated_with_flag()
    {
        var huge = new string('x', 40 * 1024);
        _api.EnqueueStream(
            FakeChunks.ToolCallChunk(0, id: "call_1", name: "read_file", args: "{\"path\":\"a.txt\"}"),
            FakeChunks.Finish("tool_calls"));
        _api.EnqueueStream(FakeChunks.Content("done"), FakeChunks.Finish());

        _readTool.OnRead = p => ChatToolExecutionResult.Success(new JValue(huge), huge);

        var sink = SinkWithUserMessage();
        var result = await _service.RunTurnAsync(Turn(), sink, CancellationToken.None);

        Assert.Equal(ChatTurnOutcome.Completed, result.Outcome);
        var toolMsg = sink.Messages[2];
        Assert.True(toolMsg.Content.Length <= 32 * 1024);
        Assert.Contains("truncated", toolMsg.Content);
        Assert.True(sink.ToolResults[0].Result.Truncated);
    }
}