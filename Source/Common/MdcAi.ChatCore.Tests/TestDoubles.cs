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

/// <summary>
/// A scripted IOpenAiApi: the test script enqueues one chunk-sequence per expected request; the
/// driver's streaming call dequeues the next script item. All requests are recorded for asserts.
/// </summary>
public sealed class ScriptedFakeApi : IOpenAiApi
{
    private readonly Queue<IAsyncEnumerable<ChatResult>> _script = new();
    private readonly Queue<Action<ChatRequest>> _inspectors = new();

    public List<ChatRequest> Requests { get; } = new();

    public AiProvider ActiveProvider => AiProviders.Default;
    public IReadOnlyList<AiProvider> Providers => AiProviders.All;
    public bool HasCredentials(string providerKey) => true;
    public Task<AiModel[]> GetModels() => Task.FromResult(Array.Empty<AiModel>());
    public Task<AiModel[]> GetAllModels() => Task.FromResult(Array.Empty<AiModel>());

    /// <summary>Enqueues one streaming response made of the given chunks.</summary>
    public void EnqueueStream(Action<ChatRequest> inspect, params ChatResult[] chunks)
    {
        _inspectors.Enqueue(inspect);
        _script.Enqueue(Chunks(chunks));
    }

    public void EnqueueStream(params ChatResult[] chunks) =>
        EnqueueStream(_ => { }, chunks);

    public void EnqueueStream(Action<ChatRequest> inspect, IAsyncEnumerable<ChatResult> enumerable)
    {
        _inspectors.Enqueue(inspect);
        _script.Enqueue(enumerable);
    }

    /// <summary>Enqueues a response that delivers the given prefix chunks and then never completes.</summary>
    public void EnqueueHanging(params ChatResult[] prefixChunks)
    {
        _inspectors.Enqueue(_ => { });
        _script.Enqueue(new HangingStream(prefixChunks));
    }

    private sealed class HangingStream : IAsyncEnumerable<ChatResult>
    {
        private readonly ChatResult[] _prefix;

        public HangingStream(ChatResult[] prefix) => _prefix = prefix;

        public IAsyncEnumerator<ChatResult> GetAsyncEnumerator(CancellationToken ct) => new Enumerator(_prefix);

        private sealed class Enumerator : IAsyncEnumerator<ChatResult>
        {
            private readonly ChatResult[] _prefix;
            private int _phase;

            public Enumerator(ChatResult[] prefix) => _prefix = prefix;

            public ChatResult Current { get; private set; }

            public ValueTask<bool> MoveNextAsync()
            {
                if (_phase < _prefix.Length)
                {
                    Current = _prefix[_phase++];
                    return new ValueTask<bool>(true);
                }

                // Never completes; the consumer's WithCancellation wrapper observes its token.
                var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                return new ValueTask<bool>(tcs.Task);
            }

            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }

    private static async IAsyncEnumerable<ChatResult> Chunks(ChatResult[] chunks)
    {
        foreach (var c in chunks)
            yield return c;
        await Task.CompletedTask;
    }

    public Task<ChatResult> CreateChatCompletions(ChatRequest request) =>
        CreateChatCompletions(request, CancellationToken.None);

    public Task<ChatResult> CreateChatCompletions(ChatRequest request, CancellationToken ct) =>
        throw new NotSupportedException("The step driver uses the streaming path in Phase 1.");

    public IAsyncEnumerable<ChatResult> CreateChatCompletionsStream(ChatRequest request) =>
        CreateChatCompletionsStream(request, CancellationToken.None);

    public IAsyncEnumerable<ChatResult> CreateChatCompletionsStream(ChatRequest request, CancellationToken ct)
    {
        Requests.Add(request);
        if (_script.Count == 0)
            throw new InvalidOperationException("Scripted fake ran out of responses.");

        var inspect = _inspectors.Count > 0 ? _inspectors.Dequeue() : (Action<ChatRequest>)(_ => { });
        inspect(request);

        var item = _script.Dequeue();
        return ct == CancellationToken.None ? item : new CtAwareEnumerable(item, ct);
    }

    /// <summary>Wraps an inner enumerable so a blocking MoveNextAsync observes the caller's token.</summary>
    private sealed class CtAwareEnumerable : IAsyncEnumerable<ChatResult>
    {
        private readonly IAsyncEnumerable<ChatResult> _inner;
        private readonly CancellationToken _token;

        public CtAwareEnumerable(IAsyncEnumerable<ChatResult> inner, CancellationToken token)
        {
            _inner = inner;
            _token = token;
        }

        public IAsyncEnumerator<ChatResult> GetAsyncEnumerator(CancellationToken ct) =>
            new Enumerator(_inner.GetAsyncEnumerator(ct), _token);

        private sealed class Enumerator : IAsyncEnumerator<ChatResult>
        {
            private readonly IAsyncEnumerator<ChatResult> _inner;
            private readonly CancellationToken _token;

            public Enumerator(IAsyncEnumerator<ChatResult> inner, CancellationToken token)
            {
                _inner = inner;
                _token = token;
            }

            public ChatResult Current => _inner.Current;

            public async ValueTask<bool> MoveNextAsync()
            {
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(_token);
                return await _inner.MoveNextAsync().AsTask().WaitAsync(linked.Token);
            }

            public ValueTask DisposeAsync() => _inner.DisposeAsync();
        }
    }
}

/// <summary>In-memory transcript + event record; the main-conversation adapter mirrors this shape.</summary>
public sealed class InMemorySink : IChatSessionSink
{
    public List<ChatMessage> Messages { get; } = new();
    public List<ChatAssistantRecord> Committed { get; } = new();
    public List<(string MessageId, ChatAssistantDelta Delta)> Deltas { get; } = new();
    public List<ChatToolResultRecord> ToolResults { get; } = new();
    public List<ChatTurnCheckpoint> Checkpoints { get; } = new();
    public List<ChatModelRequestAttemptView> Attempts { get; } = new();
    public List<ChatToolExecutionView> ToolStates { get; } = new();
    public List<(string MessageId, bool KeepPrefix)> Abandoned { get; } = new();

    public Dictionary<string, ChatAssistantDelta> StreamingState { get; } = new(StringComparer.Ordinal);

    public ValueTask<ChatTranscriptSnapshot> GetCurrentBranchAsync(CancellationToken ct) =>
        new(new ChatTranscriptSnapshot("turn-1", Messages.ToList()));

    public ValueTask<string> BeginAssistantAsync(ChatStepInfo step, CancellationToken ct)
    {
        var id = $"assistant-{step.StepNumber}";
        StreamingState[id] = ChatAssistantDelta.Empty;
        return new ValueTask<string>(id);
    }

    public ValueTask ApplyAssistantDeltaAsync(string messageId, ChatAssistantDelta delta, CancellationToken ct)
    {
        StreamingState[messageId] = delta;
        Deltas.Add((messageId, delta));
        return ValueTask.CompletedTask;
    }

    public ValueTask CommitAssistantAsync(string messageId, ChatAssistantRecord message, CancellationToken ct)
    {
        Messages.Add(message.Message);
        Committed.Add(message);
        StreamingState.Remove(messageId);
        return ValueTask.CompletedTask;
    }

    public ValueTask AbandonAssistantAsync(string messageId, bool keepDeliveredPrefix, CancellationToken ct)
    {
        Abandoned.Add((messageId, keepDeliveredPrefix));

        if (keepDeliveredPrefix && StreamingState.TryGetValue(messageId, out var state) &&
            (!string.IsNullOrEmpty(state.Content) || state.ReasoningContent != null))
        {
            Messages.Add(new ChatMessage(ChatMessageRole.Assistant)
            {
                Content = state.Content,
                ReasoningContent = state.ReasoningContent
            });
        }

        StreamingState.Remove(messageId);
        return ValueTask.CompletedTask;
    }

    public ValueTask SetModelRequestAttemptAsync(ChatModelRequestAttemptView attempt, CancellationToken ct)
    {
        Attempts.Add(attempt);
        return ValueTask.CompletedTask;
    }

    public ValueTask SetToolStateAsync(ChatToolExecutionView tool, CancellationToken ct)
    {
        ToolStates.Add(tool);
        return ValueTask.CompletedTask;
    }

    public ValueTask AppendToolResultAsync(ChatToolResultRecord result, CancellationToken ct)
    {
        ToolResults.Add(result);
        Messages.Add(new ChatMessage(ChatMessageRole.Tool, result.Result.ModelContent)
        {
            ToolCallId = result.ToolCallId
        });
        return ValueTask.CompletedTask;
    }

    public ValueTask CheckpointTurnAsync(ChatTurnCheckpoint checkpoint, CancellationToken ct)
    {
        Checkpoints.Add(checkpoint);
        return ValueTask.CompletedTask;
    }
}

public sealed class FakeApprovalService : IChatToolApprovalService
{
    public ChatApprovalDecision Decision { get; set; } = ChatApprovalDecision.Approved;
    public List<ChatApprovalRequest> Requests { get; } = new();
    public bool HasReadGrantResult { get; set; } = false;

    public ValueTask<ChatApprovalResponse> RequestApprovalAsync(ChatApprovalRequest request, CancellationToken ct)
    {
        Requests.Add(request);
        return new ValueTask<ChatApprovalResponse>(new ChatApprovalResponse(
            request.ConversationId, request.TurnId, request.ToolCallId, request.ArgumentsHash, Decision));
    }

    public ValueTask<bool> HasReadGrantAsync(string conversationId, string turnId, CancellationToken ct) =>
        new(HasReadGrantResult);
}

/// <summary>A scripted read-only tool backed by an in-memory file map (Phase 1 core loop tests).</summary>
public sealed class FakeReadTool : IChatTool
{
    public string Name => "read_file";
    public string Description => "Reads a file from the (fake) workspace.";
    public ChatToolExecutionMode ExecutionMode => ChatToolExecutionMode.ParallelSafe;
    public ChatToolRisk Risk => ChatToolRisk.ReadOnly;
    public TimeSpan Timeout => TimeSpan.FromSeconds(30);

    public JObject ParametersSchema => JObject.Parse(
        """{"type":"object","additionalProperties":false,"properties":{"path":{"type":"string"}},"required":["path"]}""");

    public Func<string, ChatToolExecutionResult> OnRead { get; set; }

    public ValueTask<ChatToolExecutionResult> ExecuteAsync(
        JObject arguments,
        ChatToolExecutionContext context,
        CancellationToken ct)
    {
        var path = (string)arguments["path"];
        ct.ThrowIfCancellationRequested();
        return new ValueTask<ChatToolExecutionResult>(OnRead?.Invoke(path) ?? ThrowMissing(path));
    }

    private static ChatToolExecutionResult ThrowMissing(string path) =>
        ChatToolExecutionResult.Failure(ChatToolStatus.Failed, "file_not_found", $"File not found: {path}");

    public ChatToolCallPresentation PresentCall(JObject arguments) =>
        ChatToolCallPresentation.Generic("Read file", $"Read · {arguments["path"]}");

    public ChatToolResultPresentation PresentResult(JObject arguments, ChatToolExecutionResult result) =>
        ChatToolResultPresentation.Generic("Read file", result.Ok ? "Read ok" : "Read failed",
                                           new JObject { ["path"] = arguments["path"] });
}

/// <summary>Chunk factories so the tests read like the SSE they represent.</summary>
public static class FakeChunks
{
    public static ChatResult RoleChunk(string role = "assistant") => new()
    {
        Id = "req-1",
        Choices = new[] { new ChatChoice { Index = 0, Delta = new ChatMessage(ChatMessageRole.FromString(role)) } }
    };

    public static ChatResult Content(params string[] parts) => new()
    {
        Id = "req-1",
        Choices = new[] { new ChatChoice { Index = 0, Delta = new ChatMessage(ChatMessageRole.Assistant, string.Join("", parts)) } }
    };

    public static ChatResult Reasoning(string text) => new()
    {
        Id = "req-1",
        Choices = new[] { new ChatChoice { Index = 0, Delta = new ChatMessage(ChatMessageRole.Assistant) { ReasoningContent = text } } }
    };

    public static ChatResult ToolCallChunk(int index, string id = null, string name = null, string args = null) => new()
    {
        Id = "req-1",
        Choices = new[]
        {
            new ChatChoice
            {
                Index = 0,
                Delta = new ChatMessage(ChatMessageRole.Assistant)
                {
                    ToolCalls = new[]
                    {
                        new ChatMessageToolCall
                        {
                            Index = index,
                            Id = id,
                            Function = string.IsNullOrEmpty(name) && string.IsNullOrEmpty(args)
                                           ? null
                                           : new ChatMessageFunction { Name = name, Arguments = args }
                        }
                    }
                }
            }
        }
    };

    public static ChatResult Finish(string reason = "stop") => new()
    {
        Id = "req-1",
        Choices = new[] { new ChatChoice { Index = 0, FinishReason = reason } }
    };

    public static ChatResult UsageOnly(int prompt = 10, int completion = 5) => new()
    {
        Id = "req-1",
        Choices = Array.Empty<ChatChoice>(),
        Usage = new ChatUsage { PromptTokens = prompt, CompletionTokens = completion, TotalTokens = prompt + completion }
    };

    public static ChatResult AssistantMessage(ChatMessage message, string finishReason = "stop") => new()
    {
        Id = "req-1",
        Choices = new[] { new ChatChoice { Index = 0, Message = message, FinishReason = finishReason } }
    };
}