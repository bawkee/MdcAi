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
using MdcAi.ChatCore.Tools.BuiltIn;
using MdcAi.OpenAiApi;

/// <summary>
/// P3-08 bounded parallel reads: contiguous ParallelSafe calls execute concurrently on a small
/// pool but are COMMITTED in model order; an Exclusive call is a barrier; the scheduler never
/// runs writes/shell concurrently merely because they were emitted together.
/// </summary>
public class ParallelSchedulingTests
{
    private readonly ChatToolRegistry _registry;
    private readonly ChatToolScheduler _scheduler;
    private readonly InMemorySink _sink;

    public ParallelSchedulingTests()
    {
        _registry = ChatToolRegistry.Build(new IChatTool[]
        {
            new ReadFileChatTool(),
            new ParallelTrackingTool("parallel_log"),
            new WriteFileChatTool() // Exclusive barrier
        });
        _scheduler = new ChatToolScheduler(_registry, parallelPool: 2);
        _sink = new InMemorySink();
    }

    private static ChatTurnRequest Turn(string ws) =>
        new("c1", "turn-1", "m1", "openrouter", "deepseek/deepseek-chat", null, null, ws,
            new[] { "read_file", "parallel_log", "write_file" }, ChatTurnOrigin.Human, null,
            ChatTurnLimits.Default);

    [Fact]
    public async Task Parallel_reads_overlap_but_commit_in_model_order()
    {
        var ws = Path.Combine(Path.GetTempPath(), "mdcai-para-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(ws);
        try
        {
            await File.WriteAllTextAsync(Path.Combine(ws, "a.txt"), "AAA - " + new string('x', 5000));
            await File.WriteAllTextAsync(Path.Combine(ws, "b.txt"), "BBB - " + new string('y', 5000));
            await File.WriteAllTextAsync(Path.Combine(ws, "c.txt"), "CCC - " + new string('z', 5000));

            var results = await _scheduler.ExecuteAsync(
                new[]
                {
                    Call("c1", "read_file", """{"path":"a.txt"}"""),
                    Call("c2", "read_file", """{"path":"b.txt"}"""),
                    Call("c3", "read_file", """{"path":"c.txt"}""")
                },
                Turn(ws), _sink, new WorkspaceReadObservationSet(), 1, CancellationToken.None);

            // Commit order == model order, even though execution overlapped.
            Assert.Equal(new[] { "c1", "c2", "c3" }, results.Select(r => r.ToolCallId).ToArray());
            Assert.Equal(3, _sink.ToolResults.Count);
            Assert.Equal(new[] { "c1", "c2", "c3" },
                         _sink.Messages.Where(m => m.Role == ChatMessageRole.Tool).Select(m => m.ToolCallId).ToArray());
        }
        finally
        {
            try { Directory.Delete(ws, recursive: true); } catch { }
        }
    }

    [Fact]
    public async Task Exclusive_call_is_a_barrier_surrounding_itself()
    {
        var seq = new List<char>();
        var registry = ChatToolRegistry.Build(new IChatTool[]
        {
            new LogicProbeTool("read_p", ChatToolExecutionMode.ParallelSafe, 'R', seq),
            new LogicProbeTool("write_x", ChatToolExecutionMode.Exclusive, 'W', seq),
            new LogicProbeTool("read_q", ChatToolExecutionMode.ParallelSafe, 'Q', seq)
        });
        var scheduler = new ChatToolScheduler(registry, parallelPool: 2);

        var results = await scheduler.ExecuteAsync(
            new[]
            {
                Call("c1", "read_p", "{}"),
                Call("c2", "read_p", "{}"),
                Call("c3", "write_x", "{}"),
                Call("c4", "read_q", "{}"),
                Call("c5", "read_q", "{}")
            },
            Turn(null) with { EnabledToolNames = new[] { "read_p", "write_x", "read_q" } },
            _sink, new WorkspaceReadObservationSet(), 1, CancellationToken.None);

        Assert.Equal(new[] { "c1", "c2", "c3", "c4", "c5" }, results.Select(r => r.ToolCallId).ToArray());

        // The exclusive marker W must never be adjacent to a concurrent pair boundary: the pool
        // is 2, so read_p/c1+c2 run together, then W alone, then read_q/c4+c5 together.
        var runs = string.Join("", seq);
        Assert.DoesNotContain("RW", runs); // no parallel call started right after W in the same batch
        Assert.DoesNotContain("WR", runs);
        Assert.Contains("RR", runs); // the two reads overlapped
        Assert.Contains("QQ", runs); // the post-barrier reads overlapped
    }

    [Fact]
    public async Task Writes_never_run_concurrently_even_when_emitted_together()
    {
        // Two distinct Exclusive tools prove writes never overlap. They are Write-risk, so the
        // turn must carry an approving approval service or they'd be denied before execution.
        var seq = new List<char>();
        var registry2 = ChatToolRegistry.Build(new IChatTool[]
        {
            new LogicProbeTool("write_a", ChatToolExecutionMode.Exclusive, 'A', seq),
            new LogicProbeTool("write_b", ChatToolExecutionMode.Exclusive, 'B', seq)
        });
        var scheduler = new ChatToolScheduler(registry2, parallelPool: 2);
        var approving = new FakeApprovalService { Decision = ChatApprovalDecision.Approved };

        await scheduler.ExecuteAsync(
            new[] { Call("w1", "write_a", "{}"), Call("w2", "write_b", "{}") },
            Turn(null) with
            {
                EnabledToolNames = new[] { "write_a", "write_b" },
                ApprovalService = approving
            },
            _sink, new WorkspaceReadObservationSet(), 1, CancellationToken.None);

        // A and B are Exclusive: they must never overlap (they run sequentially).
        var runs = string.Join("", seq);
        Assert.Equal("AB", runs);
    }

    private static ChatMessageToolCall Call(string id, string name, string args) => new()
    {
        Id = id,
        Type = "function",
        Function = new ChatMessageFunction { Name = name, Arguments = args }
    };

    /// <summary>Records its ordinal into a shared list so tests can assert overlap/order.</summary>
    private sealed class ParallelTrackingTool : IChatTool
    {
        public string Name { get; }
        public string Description => "parallel tracking";
        public JObject ParametersSchema => new JObject { ["type"] = "object" };
        public ChatToolExecutionMode ExecutionMode => ChatToolExecutionMode.ParallelSafe;
        public ChatToolRisk Risk => ChatToolRisk.ReadOnly;
        public TimeSpan Timeout => TimeSpan.FromSeconds(30);

        public ParallelTrackingTool(string name) => Name = name;

        public async ValueTask<ChatToolExecutionResult> ExecuteAsync(JObject arguments, ChatToolExecutionContext context, CancellationToken ct)
        {
            await Task.Delay(20, ct);
            return ChatToolExecutionResult.Success(new JValue("ok"), "ok");
        }

        public ChatToolCallPresentation PresentCall(JObject arguments) => ChatToolCallPresentation.Generic(Name, Name);
        public ChatToolResultPresentation PresentResult(JObject arguments, ChatToolExecutionResult result) =>
            ChatToolResultPresentation.Generic(Name, Name);
    }

    /// <summary>A probe tool that records its marker into a shared list (for barrier tests).</summary>
    private sealed class LogicProbeTool : IChatTool
    {
        private readonly char _marker;
        private readonly List<char> _seq;

        public string Name { get; }
        public string Description => "probe";
        public JObject ParametersSchema => new JObject { ["type"] = "object" };
        public ChatToolExecutionMode ExecutionMode { get; }
        public ChatToolRisk Risk =>
            ExecutionMode == MdcAi.ChatCore.Tools.ChatToolExecutionMode.Exclusive
                ? ChatToolRisk.Write
                : ChatToolRisk.ReadOnly;
        public TimeSpan Timeout => TimeSpan.FromSeconds(30);

        public LogicProbeTool(string name, ChatToolExecutionMode mode, char marker, List<char> seq)
        {
            Name = name;
            ExecutionMode = mode;
            _marker = marker;
            _seq = seq;
        }

        public async ValueTask<ChatToolExecutionResult> ExecuteAsync(JObject arguments, ChatToolExecutionContext context, CancellationToken ct)
        {
            lock (_seq)
                _seq.Add(_marker);

            // Slight delay so parallel siblings interleave.
            await Task.Delay(5, ct);

            return ChatToolExecutionResult.Success(new JValue("ok"), "ok");
        }

        public ChatToolCallPresentation PresentCall(JObject arguments) => ChatToolCallPresentation.Generic(Name, Name);
        public ChatToolResultPresentation PresentResult(JObject arguments, ChatToolExecutionResult result) =>
            ChatToolResultPresentation.Generic(Name, Name);
    }
}