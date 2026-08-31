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

namespace MdcAi.ChatCore.Sessions;

using MdcAi.ChatCore.Prompting;
using MdcAi.ChatCore.Security;
using MdcAi.ChatCore.Tools;
using MdcAi.OpenAiApi;

/// <summary>
/// The stateless step driver (DSH proposal §6.2). A turn is zero or more steps; a step is one
/// LLM request plus all the tool calls it returned. After an assistant message or tool result is
/// accepted it is appended to the transcript (via the sink), and the NEXT request is derived from
/// that accepted branch - never from a private drifted list.
///
/// One stable assistant placeholder id is reserved before each request; deltas and the final
/// commit address that same node, so a streaming placeholder and a second completed assistant
/// can never both appear. On cancel, a delivered prefix is finalized as interrupted/honest.
/// </summary>
public sealed class ChatSessionService
{
    private readonly IOpenAiApi _api;
    private readonly ChatToolRegistry _registry;
    private readonly ChatToolScheduler _scheduler;
    private readonly ChatPromptBuilder _promptBuilder;

    public ChatSessionService(
        IOpenAiApi api,
        ChatToolRegistry registry,
        ChatPromptBuilder promptBuilder = null,
        ChatToolScheduler scheduler = null)
    {
        _api = api;
        _registry = registry;
        _promptBuilder = promptBuilder ?? ChatPromptBuilder.Default;
        _scheduler = scheduler ?? new ChatToolScheduler(registry);
    }

    public async Task<ChatTurnResult> RunTurnAsync(
        ChatTurnRequest turn,
        IChatSessionSink sink,
        CancellationToken ct)
    {
        if (turn == null)
            throw new ArgumentNullException(nameof(turn));
        if (sink == null)
            throw new ArgumentNullException(nameof(sink));

        await sink.CheckpointTurnAsync(new ChatTurnCheckpoint(turn.TurnId, "started", 0, 0), ct);

        var stickyOutcome = ChatTurnOutcome.Completed;
        var totalToolCalls = 0;
        var readSet = new WorkspaceReadObservationSet();

        for (var step = 1; step <= turn.Limits.MaxSteps; step++)
        {
            ct.ThrowIfCancellationRequested();

            var branch = await sink.GetCurrentBranchAsync(ct);
            var request = BuildRequest(turn, branch);
            var messageId = await sink.BeginAssistantAsync(new ChatStepInfo(turn.TurnId, step), ct);

            ChatAssistantRecord assembled;
            try
            {
                assembled = await StreamAndAssembleAsync(request, messageId, sink, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                await SafeAbandonAsync(sink, messageId, keepDeliveredPrefix: true);
                return new ChatTurnResult(ChatTurnOutcome.Cancelled, step, totalToolCalls, turn.TurnId);
            }
            catch (OpenAiApiException ex)
            {
                await SafeAbandonAsync(sink, messageId, keepDeliveredPrefix: false);
                return new ChatTurnResult(ChatTurnOutcome.Failed, step, totalToolCalls, turn.TurnId,
                                          ToErrorCode(ex), ex.Message);
            }
            catch (Exception ex)
            {
                await SafeAbandonAsync(sink, messageId, keepDeliveredPrefix: false);
                return new ChatTurnResult(ChatTurnOutcome.Failed, step, totalToolCalls, turn.TurnId,
                                          "unexpected_error", ex.Message);
            }

            await sink.CommitAssistantAsync(messageId, assembled, ct);

            if (assembled.IsMaxTokens)
                stickyOutcome = ChatTurnOutcome.MaxTokens;

            // No tool calls -> the step (and therefore the turn) is done.
            if (assembled.Message.ToolCalls is not { Length: > 0 })
                return await CompleteAsync(sink, turn, stickyOutcome, step, totalToolCalls, ct);

            // A max-token or incomplete tool call must never be executed.
            if (assembled.IsMaxTokens || !assembled.HasCompleteToolArguments)
                return await CompleteAsync(sink, turn, ChatTurnOutcome.MaxTokens, step, totalToolCalls, ct);

            if (assembled.Message.ToolCalls.Length > turn.Limits.MaxToolCallsPerStep)
                return await CompleteAsync(sink, turn, ChatTurnOutcome.MaxSteps, step, totalToolCalls, ct);

            totalToolCalls += assembled.Message.ToolCalls.Length;
            if (totalToolCalls > turn.Limits.MaxToolCallsPerTurn)
                return await CompleteAsync(sink, turn, ChatTurnOutcome.MaxSteps, step, totalToolCalls, ct);

            // The scheduler validates, approves, executes and commits each result in model order.
            await _scheduler.ExecuteAsync(assembled.Message.ToolCalls, turn, sink, readSet, step, ct);
        }

        return await CompleteAsync(sink, turn, ChatTurnOutcome.MaxSteps, turn.Limits.MaxSteps, totalToolCalls, ct);
    }

    private ChatRequest BuildRequest(ChatTurnRequest turn, ChatTranscriptSnapshot branch)
    {
        var messages = new List<ChatMessage>();
        messages.Add(new ChatMessage(ChatMessageRole.System, _promptBuilder.Compose(turn)));
        messages.AddRange(branch.Messages);

        var tools = turn.EnabledToolNames is { Count: > 0 } ? _registry.ToWireTools(turn.EnabledToolNames) : null;

        return new ChatRequest
        {
            Model = turn.Model,
            ProviderKey = turn.ProviderKey,
            Messages = messages,
            Tools = tools,
            NumChoicesPerMessage = 1,
            ReasoningEffort = string.IsNullOrEmpty(turn.Effort) ? null : turn.Effort
        };
    }

    private async Task<ChatAssistantRecord> StreamAndAssembleAsync(
        ChatRequest request,
        string messageId,
        IChatSessionSink sink,
        CancellationToken ct)
    {
        var assembler = new ChatResponseAssembler();
        var pushedAny = false;

        await foreach (var chunk in _api.CreateChatCompletionsStream(request, ct))
        {
            assembler.Accept(chunk);

            // Push the latest aggregate streaming state (the sink renders the newest state of the
            // single placeholder node; Commit carries the canonical message).
            if (assembler.HasAcceptedDelta || assembler.ToolCalls.Count > 0)
            {
                await sink.ApplyAssistantDeltaAsync(messageId, assembler.BuildCurrentDelta(), ct);
                pushedAny = true;
            }
        }

        // Ensure a final delta is visible even when the stream delivered no meaningful content.
        if (!pushedAny)
            await sink.ApplyAssistantDeltaAsync(messageId, assembler.BuildCurrentDelta(), ct);

        return assembler.BuildRecord();
    }

    private static async Task<ChatTurnResult> CompleteAsync(
        IChatSessionSink sink,
        ChatTurnRequest turn,
        ChatTurnOutcome outcome,
        int step,
        int totalToolCalls,
        CancellationToken ct)
    {
        var status = outcome switch
        {
            ChatTurnOutcome.Completed => "completed",
            ChatTurnOutcome.MaxTokens => "max_tokens",
            ChatTurnOutcome.MaxSteps => "max_steps",
            ChatTurnOutcome.Cancelled => "cancelled",
            ChatTurnOutcome.Failed => "failed",
            _ => "completed"
        };

        await sink.CheckpointTurnAsync(new ChatTurnCheckpoint(turn.TurnId, status, step, totalToolCalls,
                                                               outcome.ToString()), ct);

        return new ChatTurnResult(outcome, step, totalToolCalls, turn.TurnId);
    }

    private static ValueTask SafeAbandonAsync(IChatSessionSink sink, string messageId, bool keepDeliveredPrefix)
    {
        // The original token is cancelled; use a fresh one so cleanup always runs.
        return sink.AbandonAssistantAsync(messageId, keepDeliveredPrefix, CancellationToken.None);
    }

    private static string ToErrorCode(OpenAiApiException ex) =>
        ex is OpenAiApiQuotaException ? "rate_limit_or_quota"
      : ex is OpenAiApiAuthException ? "auth_error"
      : ex is OpenAiInvalidApiKeyException ? "invalid_api_key"
      : "api_error";
}