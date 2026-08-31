# MdcAi Agentic Chat Architecture and Three-Phase Implementation Proposal

- **Status:** implementation specification
- **Audience:** coding agents and maintainers implementing the work incrementally
- **Prepared from:** `DSH_FINDINGS.md`, the current `main` source tree, the repository subsystem guides, the referenced local DSH source checkout, and the supplied DSH chat UI captures
- **Date:** 2026-08-30

Source pins used for this proposal:

- MdcAi `main`: `cc16b48` (`improved the way creds and model catalogs are fetched so its faster and doesnt rely on exceptions`).
- DSH local `master`: `cd5ef8148158c3a752a658978873241fdf8e2bbc` (0.1.2 alpha release merge).
- DeepSeek/OpenRouter protocol statements were rechecked against the official documentation linked in Section 3.3 on the document date; provider adapters and smoke tests remain authoritative if those services change.

---

## 1. Executive decision

MdcAi should remain MdcAi: a lightweight, BYOK, native Windows conversation app with categories, per-conversation settings, provider-grouped model selection, local persistence, reasoning display, and its distinctive edit/fork history. It should gain DSH's most valuable execution properties without adopting DSH's product identity or platform complexity.

The implementation should adopt these ideas:

1. A user turn may contain multiple model **steps**. One step is one chat-completions request followed by all tool calls returned by that request. Tool results are added to the current branch, then a new model request is made. The turn ends only when the model returns no tool calls or a terminal guard is reached.
2. The current selected fork is the durable, model-visible transcript. Every assistant tool call, every tool result, and every reasoning payload needed by a later request must round-trip through that transcript without lossy reconstruction.
3. Tool definitions have a strict model-facing schema, a structured execution result, an explicit security policy, and a pure replayable presentation.
4. Long work may become a background job. Narrow one-shot helper sessions may be launched through a tool. A user-authorized goal may open bounded continuation turns. Long conversations are planned into a model-specific token budget instead of being blindly resent.

The implementation should explicitly not adopt DSH's full event log, plugin composition framework, permission-preset platform, distributed hosts, cold-resumable child activations, MCP/LSP/ACP framework, schedules, webhooks, or multi-agent teams. MdcAi needs a faithful **surface transcript plus relational run checkpoints**, not a second operating system.

The recommended new seam is a plain .NET 9 project named `MdcAi.ChatCore`:

```text
MdcAi (WinUI shell)
  └─ MdcAi.ChatUI
       ├─ MdcAi.ChatCore                 new: step loop, tools, context, jobs, goals, one-shot helpers
       │    └─ MdcAi.OpenAiApi           existing: OpenAI-compatible wire transport and provider routing
       ├─ MdcAi.ChatUI.LocalDal          existing: EF Core/SQLite entities and migrations
       └─ MdcAi.Extensions.WinUI         existing: app services, DI and reactive helpers
```

`MdcAi.ChatCore` must have no WinUI, ReactiveUI, WebView2, EF Core, or PasswordVault dependency. It should be ordinary async C# with deterministic unit tests. `MdcAi.ChatUI` remains the adapter that turns core events into `ChatMessageVm` nodes, reactive properties, approval actions, persistence checkpoints, and renderer DTOs.

This split matters for code quality and for Phase 3: main conversations and one-shot helper sessions must use the same tested loop without constructing hidden WinUI view models.

---

## 2. Corrections and decisions resolved against the current repository

The research report was directionally correct, but the implementation agent must use the current source tree rather than its historical assumptions.

### D1 — branch strategy: resolved

Implement from `main`. Do not develop on or merge the `reasoning-models` branch.

The current `main` already contains `ChatRequest.Tools`, `ChatTool`, `ChatMessage.ToolCalls`, `ChatMessage.ToolCallId`, reasoning effort, reasoning output, multi-provider routing, tests, and several fixes that the old branch does not contain. The local `reasoning-models` branch is one divergent commit on top of a much older codebase and is missing thousands of lines of current functionality. Rebasing the agentic work on it would resurrect removed code and discard current OpenRouter, reasoning, renderer, test, and persistence work.

The DTO groundwork is therefore **present but incomplete**, not waiting to be merged. In particular:

- `ChatMessage(ChatMessage basedOn)` does not copy `ToolCalls`, `ToolCallId`, or `ReasoningDetails`.
- `ChatMessageVm.CreateMessageRequest()` reduces every node to `Role + Content`, which discards every field a reasoning tool continuation needs.
- `FunctionToolParams` can express only a shallow object with scalar properties. It cannot faithfully describe nested objects, arrays, enums, `anyOf`, or reusable definitions.
- `IOpenAiApi` and the streaming transport do not accept a `CancellationToken`.
- `ChatRequest` has no explicit provider routing hint; the router assumes every model id containing `/` is OpenRouter and every other id is OpenAI. That assumption blocks a clean direct DeepSeek provider later.

Treat these as Phase 1 prerequisites.

### D2 — streaming strategy: resolved

Use a non-streaming fake response only as the first internal loop test. Do not consider Phase 1 complete or release the feature until streaming tool-call assembly is implemented.

DeepSeek's value here is interleaved thinking and tool use. A non-streaming-only release would skip the hardest protocol path and would make the UI feel unlike both current MdcAi and DSH. The assembler must handle content, reasoning, `reasoning_details`, multiple tool calls, fragmented names/arguments, usage-only chunks, and terminal finish reasons.

### D3 — local execution security: resolved

Use an explicit workspace boundary and risk-based consent:

- Tools are off by default for every existing and new conversation.
- Enabling tools requires selecting a workspace folder for that conversation.
- Read-only operations inside that workspace may be allowed for the session after a clear opt-in.
- Writes and patches require an inline approval, with an optional “allow edits for this turn” grant.
- PowerShell requires approval for every distinct call and displays the exact script and working directory. Phase 1 does not offer a blanket shell grant, even for the current turn.
- Paths outside the workspace, UNC paths, device paths, and reparse-point escapes are denied unless a future, explicit folder-authorization feature is designed.
- Do not implement a command-prefix allowlist. PowerShell syntax, aliases, quoting, nested shells, and scripts make prefix checks security theater.

Phase 1 core tests may use a fake approval service. Mutating tools must remain feature-gated until the Phase 2 inline approval UI exists.

### D4 — continuable children: resolved

Continuable children remain deferred. Phase 3 implements one-shot helper sessions only. The persistence and ownership fields should leave room for future continuations, but there will be no `send_message`, child inbox, resident child activation, interrupt protocol, or cold resume in these three phases.

### D5 — acceptance criteria: resolved

Section 16 defines a checked-in evaluation corpus, paired baseline/harness trials, protocol assertions, and concrete task scoring. “Feels like DSH” is not an acceptance test.

### D6 — DSH-inspired activity transcript: resolved

Adopt DSH's compact, inspectable activity transcript as a design pattern, but implement it in MdcAi's renderer, vocabulary, theme, and contracts. The valuable part is not the DSH branding or exact CSS. It is the separation visible in the pinned DSH source:

- A shared disclosure row provides one-line flow chrome, state, keyboard behavior, and expansion.
- Each tool publishes provider-neutral call/result presentation intent instead of making the chat renderer switch on raw provider JSON.
- Read, search, terminal, diff, context, retry, and plan records get purpose-built detail surfaces.
- Structured result metadata survives in the durable log, so replay does not depend on reparsing prose or on the original tool still being installed.
- Unknown or older records degrade to a generic text/JSON view instead of breaking the transcript.

MdcAi should reproduce those architectural properties, not port DSH React packages or copy its localized strings. All user-facing labels remain MdcAi-owned. Tool execution still belongs to `MdcAi.ChatCore`; the React renderer remains a passive, untrusted presentation and intent surface.

---

## 3. Product and architecture principles

### 3.1 Chat-first, capabilities second

Ordinary conversation must behave exactly as it does today when tools are disabled. No tool schemas, workspace context, goal reminders, or agent instructions should be sent in chat-only mode. The send button, model/effort picker, categories, fork editing, and reasoning display remain primary MdcAi concepts.

The UI should call the capability “Workspace tools” or simply “Tools,” not “DSH mode.” Internal names should follow existing conventions: `ChatSessionService`, `ChatTurn`, `ChatStep`, `ChatTool`, `ConversationVm`, and `ChatMessageVm`.

### 3.2 One source for model-visible history

Every request must be projected from the current selected conversation branch plus explicitly persisted context records. Never maintain a second private `List<ChatMessage>` that drifts away from the fork tree.

The service may hold a step-local snapshot while a request is in flight, but after an assistant message or tool result is accepted it must be appended to the transcript before the next request is derived. This reproduces DSH's key property without copying its complete event store.

### 3.3 Exact protocol continuity is data, not decoration

Reasoning fields are not merely content for the collapsible “Thinking” block. With tool-capable reasoning models they can be mandatory continuation state. DeepSeek's current thinking-mode documentation requires prior `reasoning_content` to be sent back on requests carrying tools and returns HTTP 400 if it is missing. OpenRouter likewise requires unmodified `reasoning_details` sequences for models with signed, encrypted, or structured reasoning blocks.

Consequently:

- Persist raw `reasoning_content`, raw `reasoning`, and raw `reasoning_details` independently of rendered HTML.
- Preserve the assistant message's `content`, `tool_calls`, and reasoning fields together.
- Deep-copy all protocol fields in DTO copy constructors.
- Never reconstruct a tool-calling assistant message from the tool card presentation.
- Never summarize, reorder, splice, or partially retain an in-flight tool protocol group.

Relevant current provider documentation:

- DeepSeek thinking mode and tool-call continuation: <https://api-docs.deepseek.com/guides/thinking_mode/>
- DeepSeek tool calls and strict JSON Schema limitations: <https://api-docs.deepseek.com/guides/tool_calls/>
- OpenRouter reasoning preservation: <https://openrouter.ai/docs/guides/best-practices/reasoning-tokens>

Provider behavior will continue to change, so these rules belong behind provider/model capability adapters and tests rather than scattered model-id checks.

### 3.4 Pure core, reactive edge

The loop and tools should use `Task`, `IAsyncEnumerable`, `CancellationToken`, immutable records where useful, and interfaces for time, persistence callbacks, approval, and process creation. ReactiveUI remains the right style in `ConversationVm`, but it should orchestrate and render the service rather than contain HTTP/tool-loop logic.

### 3.5 Bounded autonomy

Every loop has step, tool-call, output, time, token, and cancellation bounds. Every goal has round and cost bounds. Every helper has depth and capability bounds. A model cannot expand its own authority by asking a tool to do so.

### 3.6 Side effects are auditable and replay-safe

Tool execution happens once. Rendering, saving, reopening a conversation, switching forks, and rebuilding WebView state must never execute a tool. `PresentCall` and `PresentResult` are pure functions over persisted arguments/results.

---

## 4. Target runtime flow

```text
Conversation view / user prompt
        │
        ▼
ConversationVm.SendPromptCmd
  append user ChatMessageVm and persist it
        │
        ▼
ConversationSessionController               one active turn per conversation
        │
        ▼
ChatSessionService.RunTurnAsync
  ├─ ChatPromptBuilder                       premise + MdcAi identity + runtime sections
  ├─ ChatHistoryProjector                    current fork + exact protocol payloads
  ├─ ChatContextManager                      Phase 3 budget/summary projection
  ├─ IOpenAiApi                              routed OpenAI-compatible request
  ├─ ChatResponseAssembler                   content/reasoning/tool delta assembly
  ├─ ChatToolRegistry + ChatToolScheduler    validation, policy, execution, ordered commit
  └─ IChatSessionSink                        one ordered mutation/event boundary
        │
        ▼
ConversationChatSessionSink
  begin/update/commit one ChatMessageVm node on the UI scheduler
  checkpoint DbChatTurn / DbChatStep / DbMessage rows
        │
        ├─► WebView DTO projection ► React transcript/cards
        └─► current selected fork is used to derive the next request
```

`ChatSessionService` should be stateless across turns. A small `ConversationSessionController` owned by each `ConversationVm` holds the current cancellation source, one-turn mutex, active sink, and pending approvals. This preserves the current behavior where navigating to another conversation does not stop generation and different conversations may run concurrently.

Do not retain the current “tail became user, therefore create a completion” subscription for the agentic path. Appending assistant/tool nodes changes `Tail` repeatedly; the current nested `.Switch()` shape would cancel and replace work during the same turn. Sending, regenerating, goal continuation, and helper execution must invoke the turn runner explicitly.

---

## 5. Core contracts and invariants

The exact type spelling may evolve while coding, but implementations must retain these responsibilities.

### 5.1 Session contracts

```csharp
public sealed record ChatTurnRequest(
    string ConversationId,
    string TriggerMessageId,
    string ProviderKey,
    string Model,
    string Effort,
    IReadOnlyList<string> EnabledToolNames,
    ChatTurnOrigin Origin,
    ChatTurnLimits Limits);

public sealed record ChatTurnLimits(
    int MaxSteps,
    int MaxToolCallsPerStep,
    int MaxToolCallsPerTurn,
    long MaxModelVisibleToolResultBytes);

public interface IChatSessionSink
{
    ValueTask<ChatTranscriptSnapshot> GetCurrentBranchAsync(CancellationToken ct);
    ValueTask<string> BeginAssistantAsync(ChatStepInfo step, CancellationToken ct);
    ValueTask ApplyAssistantDeltaAsync(string messageId, ChatAssistantDelta delta, CancellationToken ct);
    ValueTask CommitAssistantAsync(string messageId, ChatAssistantRecord message, CancellationToken ct);
    ValueTask AbandonAssistantAsync(string messageId, bool keepDeliveredPrefix, CancellationToken ct);
    ValueTask SetModelRequestAttemptAsync(ChatModelRequestAttemptView attempt, CancellationToken ct);
    ValueTask SetToolStateAsync(ChatToolExecutionView tool, CancellationToken ct);
    ValueTask AppendToolResultAsync(ChatToolResultRecord result, CancellationToken ct);
    ValueTask CheckpointTurnAsync(ChatTurnCheckpoint checkpoint, CancellationToken ct);
}
```

The main-conversation adapter implements this interface over the current fork, `ChatMessageVm`, and the repository. A one-shot helper uses an in-memory sink. `BeginAssistantAsync` preassigns one stable message id; streaming deltas and the final commit address that same node, so a coding agent cannot accidentally append one streaming placeholder and a second completed assistant. If the API fails before a delta, `AbandonAssistantAsync(..., false)` removes the placeholder; after a delivered prefix it finalizes that same node as failed/interrupted. The sink is the only transcript mutation boundary. An optional telemetry observer may receive copies after commits, but it cannot mutate transcript/UI state.

What is not acceptable is making `MdcAi.ChatCore` reference `ConversationVm` or EF entities.

### 5.2 Tool contracts

Use Newtonsoft JSON types consistently with `MdcAi.OpenAiApi`; do not introduce `System.Text.Json` into only this subsystem.

```csharp
public interface IChatTool
{
    string Name { get; }
    string Description { get; }
    JObject ParametersSchema { get; }
    ChatToolExecutionMode ExecutionMode { get; }
    ChatToolRisk Risk { get; }
    TimeSpan Timeout { get; }

    ValueTask<ChatToolExecutionResult> ExecuteAsync(
        JObject arguments,
        ChatToolExecutionContext context,
        CancellationToken ct);

    ChatToolCallPresentation PresentCall(JObject arguments);
    ChatToolResultPresentation PresentResult(JObject arguments, ChatToolExecutionResult result);
}
```

The two presentation types are closed, versioned, locale-neutral tagged unions. A call presentation describes intent before execution (`generic`, `terminal`, or `diff` initially); a result presentation may additionally supply `read`, `search`, applied `diff`, terminal outcome, or a generic result. Both are pure data and both have a bounded generic fallback. A presenter must not touch the filesystem, start work, localize copy, or depend on a live ViewModel.

`ChatToolExecutionResult` should contain a canonical structured `JToken Value`, a bounded exact `ModelContent` string, `Status`, optional stable error code, retryability, truncation information, and optional artifact reference. The model-facing content is persisted exactly as sent. The full canonical value may be retained for UI/audit within a separately bounded database/artifact limit.

It may also carry a host-only `ConcludesTurn` flag for terminal goal tools. This flag never serializes into the model-facing tool result and never excuses the scheduler from materializing results for every call id in the assistant message.

Use a standard result envelope so the model receives predictable failures instead of stack traces:

```json
{
  "ok": false,
  "status": "denied",
  "summary": "PowerShell execution was not approved by the user.",
  "error": {
    "code": "approval_denied",
    "retryable": false
  }
}
```

Expected tool failures—including invalid JSON arguments, a missing file, a failed exact patch, denied consent, nonzero process exit, timeout, and unknown tool—normally become `role:"tool"` results. They should not crash the turn. Transport corruption, broken transcript invariants, and persistence failure are turn-level errors.

### 5.3 Transcript protocol invariants

Validate these before every request and in unit tests:

1. A current branch is an ordered chain from `Head` through selected selector versions.
2. Every assistant message containing `tool_calls` is preserved with its content and reasoning fields.
3. Tool-call ids are nonempty and unique within the assistant message; every id has exactly one subsequent `role:"tool"` message with the same `tool_call_id` before the next assistant message.
4. Tool results are ordered according to the assistant's tool-call array even if safe executions finish out of order.
5. An incomplete or cancelled tool-call group is repaired with deterministic cancelled/error tool results before it can appear in a later request.
6. A streaming assistant prefix is retained and marked `Interrupted` on cancellation; it is never mislabeled as a completed answer.
7. `finish_reason:"length"` or an equivalent max-token outcome is sticky for the turn. Partial tool arguments are never executed.
8. Only the selected fork is model-visible. Non-current versions remain durable and renderable but cannot leak into a request.
9. Synthetic goal/context messages are distinguishable from human messages through `Origin`; the renderer must not label them “You.”
10. All transcript mutations for one conversation are serialized through its controller. EF contexts and observable collections are not mutated concurrently.

---

## 6. Phase 1 — step loop, faithful wire model, tools, and safe persistence

Phase 1 supplies the execution spine. It should be implemented as several small, independently tested work packets, not as one large `ConversationVm` rewrite.

### 6.1 Phase 1A — repair and extend the OpenAI-compatible wire layer

#### DTO fidelity

Modify `Source/Common/MdcAi.OpenAiApi/Dto/ChatMessage.cs` as follows:

- Add `Index` to streaming `ChatMessageToolCall` if providers place it there.
- Change raw reasoning holders from `object` to `JToken`/`JArray` so deep cloning and exact serialization are deterministic.
- Preserve `ReasoningContent`, `ReasoningRaw`, `ReasoningDetails`, `ToolCalls`, and `ToolCallId` in the copy constructor. Add deep-copy constructors for tool-call/function objects.
- Keep `ReasoningText` as a display projection only. Never use it to reconstruct provider protocol state.
- Add focused JSON round-trip tests for assistant messages containing null content, two tool calls, raw reasoning text, structured/signed reasoning details, and unknown optional fields the app elects to preserve.
- Audit the global `NullValueHandling.Ignore` serializer: if a provider distinguishes an explicitly null assistant `content` from an absent property during tool continuation, use a field converter/provider adapter to retain the required presence semantics. Do not assume the current global setting is protocol-neutral.

Modify `ChatRequest`/tool DTOs:

- Replace `FunctionToolParams` as the authoritative schema type with `JObject Parameters`. Retaining compatibility constructors is fine, but emitted schemas must support nested objects, arrays, enums, `anyOf`, constraints, and `additionalProperties:false`.
- Add `strict` as an optional function field, controlled by provider capability. Do not blindly enable DeepSeek beta strict mode against the normal endpoint.
- Add typed `ToolChoice` and `ParallelToolCalls` only when a provider/model advertises support. Omitting unsupported fields is preferable to assuming OpenAI parity.
- Add `[JsonIgnore] string ProviderKey` to `ChatRequest`; route by this key first and fall back to the existing model-id heuristic only for legacy callers. Copy it in `ChatRequest(ChatRequest basedOn)`.
- Represent provider-specific reasoning request options through one adapter-owned shape. VMs must not decide whether a provider wants `reasoning_effort`, `reasoning:{effort:...}`, or another current form.

Extend `AiModel` with provider-returned capability metadata such as `supported_parameters`. Derive `SupportsTools` from authoritative metadata where present. Unknown models should not be advertised tools until a provider adapter or an explicit tested heuristic says they support them.

#### Cancellation

Add cancellation-aware overloads through the entire call chain:

```csharp
Task<ChatResult> CreateChatCompletions(ChatRequest request, CancellationToken ct);
IAsyncEnumerable<ChatResult> CreateChatCompletionsStream(ChatRequest request, CancellationToken ct);
```

Pass the token to `HttpClient.SendAsync`, `ReadAsStreamAsync`, and `StreamReader.ReadLineAsync`. Preserve convenience overloads that call with `CancellationToken.None` if that reduces churn in existing callers and tests.

Cancellation must reach `ChatApiRouter`, `OpenAiClient`, SSE parsing, tools, process waits, subagents, and context summarization. The current `TakeUntil(StopCompletionCmd)` stops observing but does not reliably stop network or process work; this is insufficient for an agent loop.

#### Provider capability adapter

Add a small interface in the API project, for example:

```csharp
public interface IChatProviderAdapter
{
    string ProviderKey { get; }
    ChatModelCapabilities GetCapabilities(AiModel model);
    void PrepareRequest(ChatRequest request, ChatModelCapabilities capabilities);
    ChatMessage PrepareHistoryMessage(ChatMessage message, ChatModelCapabilities capabilities, bool toolsPresent);
}
```

This is not a DSH adapter platform. It is a narrow place for the correctness differences MdcAi already encounters. It should initially cover OpenAI and OpenRouter and be testable with serialized request bodies.

For reasoning + tools, the safe default on the same provider/model is to replay all captured reasoning fields exactly. If a user switches providers mid-conversation, the adapter may strip incompatible provider-private reasoning details only across a **completed turn boundary**; it must never switch provider/model in the middle of an active tool turn.

Persist `ProviderKey` with each assistant message, chat-settings default, and turn. Add `ConversationVm.SelectedProviderKey` beside `SelectedModel`; model selection commands should carry an `AiModel`/provider+model reference rather than only a string. Update the pure working-model resolver to resolve the pair and migrate legacy settings/messages by the current heuristic. This also makes a future direct DeepSeek descriptor possible without corrupting router behavior or confusing duplicate bare model ids. Adding a direct DeepSeek provider is recommended after Phase 1 is stable, but it is not required to prove the harness through OpenRouter.

### 6.2 Phase 1B — `MdcAi.ChatCore` and the step driver

Create:

```text
Source/Common/MdcAi.ChatCore/
  MdcAi.ChatCore.csproj
  Sessions/ChatSessionService.cs
  Sessions/ChatTurnRequest.cs
  Sessions/ChatTurnResult.cs
  Sessions/ChatSessionEvents.cs
  Sessions/ChatResponseAssembler.cs
  Sessions/ChatModelRequestRecovery.cs
  Sessions/ChatHistoryProjector.cs
  Prompting/ChatPromptBuilder.cs
  Prompting/ChatPromptSection.cs
  Tools/IChatTool.cs
  Tools/ChatToolRegistry.cs
  Tools/ChatToolScheduler.cs
  Tools/ChatToolExecutionResult.cs
  Tools/Presentation/ChatToolCallPresentation.cs
  Tools/Presentation/ChatToolResultPresentation.cs
  Tools/ChatToolArgumentValidator.cs
  Security/IChatToolApprovalService.cs
  Security/WorkspacePathGuard.cs
  Security/WorkspaceReadObservationSet.cs
```

Create a matching plain test project `Source/Common/MdcAi.ChatCore.Tests/` and add both projects to `Source/Desktop/MdcAi.sln`. The core project references `MdcAi.OpenAiApi`; the tests use a scripted fake `IOpenAiApi`, in-memory session sink, fake approval service, fake clock, and fake process runner.

#### Driver algorithm

The core algorithm should resemble the following. The important point is that history is re-derived from the accepted transcript at each step.

```csharp
public async Task<ChatTurnResult> RunTurnAsync(
    ChatTurnRequest turn,
    IChatSessionSink sink,
    CancellationToken ct)
{
    await sink.CheckpointTurnAsync(TurnStarted(...), ct);

    var stickyOutcome = ChatTurnOutcome.Completed;
    var totalToolCalls = 0;

    for (var step = 1; step <= turn.Limits.MaxSteps; step++)
    {
        ct.ThrowIfCancellationRequested();

        var branch = await sink.GetCurrentBranchAsync(ct);
        var request = BuildRequest(turn, branch, step);
        var messageId = await sink.BeginAssistantAsync(new(turn.Id, step), ct);
        var assembled = await StreamAndAssembleWithRetryAsync(
            request,
            delta => sink.ApplyAssistantDeltaAsync(messageId, delta, ct),
            attempt => sink.SetModelRequestAttemptAsync(attempt, ct),
            ct);

        await sink.CommitAssistantAsync(messageId, assembled.Message, ct);

        if (assembled.IsMaxTokens)
            stickyOutcome = ChatTurnOutcome.MaxTokens;

        if (assembled.Message.ToolCalls is not { Length: > 0 })
            return await CompleteAsync(stickyOutcome, step, ...);

        if (assembled.IsMaxTokens || !assembled.HasCompleteToolArguments)
            return await CompleteAsync(ChatTurnOutcome.MaxTokens, step, ...);

        totalToolCalls += assembled.Message.ToolCalls.Length;
        EnforceToolCallLimits(totalToolCalls, ...);

        var results = await _toolScheduler.ExecuteAsync(
            assembled.Message.ToolCalls,
            turn,
            sink,
            ct);

        foreach (var result in results) // always model order
        {
            await sink.AppendToolResultAsync(result, ct);
        }
    }

    return await CompleteAsync(ChatTurnOutcome.MaxSteps, turn.Limits.MaxSteps, ...);
}
```

Do not append the assistant message twice—once as a streaming placeholder and once as completed. The sink reserves one stable placeholder id before the request, may keep it visually hidden until the first meaningful delta, updates that node during streaming, then finalizes the same node with the assembled protocol payload and checkpoint. On an exception, the driver must call `AbandonAssistantAsync` according to whether a prefix was delivered.

`StreamAndAssembleWithRetryAsync` owns one fresh `ChatResponseAssembler` per provider attempt. An eligible failure before any accepted delta records/schedules the retry, waits through the injected cancellable clock, and repeats the exact frozen request in the same step; failed attempt buffers are discarded and never enter model history. After the first accepted content, reasoning, or tool-call delta, recovery is disabled for that step and any failure finalizes the visible prefix honestly. The outer assistant placeholder id remains stable across pre-delta attempts, so retry UI does not manufacture or remove message nodes.

The default guards should be conservative and configurable:

- 12 steps per user turn.
- 8 tool calls per step.
- 32 tool calls per turn.
- A repeated identical `(tool name, canonical arguments)` call three times without an intervening successful state-changing result is treated as a probable loop and returned as a structured guard failure.
- A model-visible result cap around 32 KiB per tool call, with tool-specific lower limits where appropriate.
- A per-call and per-step cap for streamed tool names/argument JSON (for example 64 KiB per call); exceeding it ends the step without execution.
- A larger but finite local artifact cap; never allow unbounded stdout or file contents into memory, SQLite, logs, or the prompt.

When the loop reaches a guard, append a compact model-visible diagnostic only if another safe step is allowed. Otherwise finish with an explicit outcome shown in the UI. Never silently present a max-step or max-token prefix as a normal completed answer.

#### Streaming assembler

`ChatResponseAssembler` is a first-class tested component, not string concatenation inside `ChatMessageVm`.

It must:

- Select one choice (the app should force `n=1` for tool sessions).
- Append `delta.content` and `delta.reasoning_content` independently.
- Preserve raw `delta.reasoning` when it is a string or structured value.
- Assemble `reasoning_details` without reordering signed/encrypted blocks.
- Accumulate tool calls by `delta.tool_calls[i].index`, not by array position in the current chunk.
- Retain the first nonempty `id` and `type`, append fragmented function names/arguments as specified by the provider stream, and support multiple calls.
- Ignore usage-only chunks with empty `choices` while retaining usage.
- Record `finish_reason`, request id, model, usage, and provider metadata.
- Reject a completed tool call missing id/name or containing incomplete JSON arguments.
- Surface mid-stream API errors through the existing typed exception mapping.

Add table-driven tests with captured synthetic SSE sequences: fragmented one-call arguments, two interleaved indexed calls, reasoning before content, `reasoning_details`, empty choices usage, cancellation after a prefix, malformed JSON, `finish_reason:length`, and an HTTP-200 error chunk.

### 6.3 Phase 1C — tool registry, validation, execution, and built-ins

#### Registry and schema boundary

Castle may resolve `IEnumerable<IChatTool>`, but the registry should build an immutable, case-sensitive name dictionary at startup and fail fast on duplicate/invalid names. Tool names should use snake case on the wire and normal MdcAi class names in C# (`ReadFileChatTool`, `PatchFileChatTool`).

Only `Name`, `Description`, `Parameters`, optional `Strict`, and the OpenAI-compatible function wrapper cross the network. Execution delegates, timeouts, risk, presenters, callbacks, and DI objects must never serialize.

Validate both the schema at startup and every argument payload at runtime. A JSON Schema library is acceptable if it is small and actively maintained; otherwise use strict tool-specific DTO deserialization plus common unknown-property/range checks. Do not trust “strict mode” to replace host validation.

#### Scheduling

Define execution modes now:

- `ParallelSafe`: read-only, independent operations.
- `Exclusive`: writes, patches, PowerShell, goal state, and any operation with shared mutable state.

Phase 1 may execute everything sequentially initially, but it must return results in model order and must not bake sequential behavior into tool definitions. Bounded parallel scheduling for contiguous `ParallelSafe` calls can be enabled in Phase 3 after race/cancellation tests. Never run writes or shell calls concurrently merely because a model emitted them together.

Maintain a turn-scoped `WorkspaceReadObservationSet` in the tool execution context. After a successful read result is durably appended, record canonical path, full-file SHA-256, length/last-write facts used for diagnostics, returned line range, whether the read covered the complete file, and source step. Existing-file writes/patches require a current observation for that exact canonical path from an earlier model step; a speculative read and edit emitted in the same assistant message does not qualify because the model had not yet seen the read result. Immediately before approval and again immediately before the atomic write, recompute the file hash; a missing observation returns `read_required`, uncovered replacement text returns `read_range_required`, and a mismatch returns `stale_read` with a concise instruction to read again. This is an authority-independent optimistic-concurrency guard: reading a file does not approve changing it, and approval does not waive the freshness check.

The immutable approval subject/hash covers normalized arguments, canonical location id, observed ranges, and the preimage hash. A successful mutation after a complete-file observation advances that observation to the returned new hash, allowing a later-step deliberate edit in the same turn without a redundant read. A mutation based only on partial ranges invalidates the observation after success rather than attempting error-prone line-range translation; the model reads again before another change. Any failed/partial mutation leaves the preimage observation unchanged. The set is in-memory and dies with the turn. A resumed/new turn must read again. PowerShell is outside this guarantee because it can mutate arbitrary files; its exact per-call full-trust approval remains mandatory.

#### `read_file`

Suggested arguments: `path`, optional `start_line`, optional `line_count`, optional `max_chars` within a host limit.

- Resolve relative paths against the conversation workspace.
- Reject binary files and files over the configured read limit unless a bounded range is requested.
- Return line numbers, detected encoding, truncation state, and a stable hash.
- Register the canonical path and full-file hash in the active turn's read-observation set only after the read result is successfully committed.
- Normalize the model-facing form and tell the model how to request the next range.
- Do not log file content.

#### `list_dir`

Suggested arguments: `path`, optional `depth`, optional `glob`, optional `max_entries`.

- Default depth 1; hard-cap depth and entries.
- Return normalized workspace-relative paths, type, size, and modified time.
- Skip common generated/hidden directories by default only if the result says so; allow an explicit include flag.
- Sort deterministically.
- Do not traverse directory reparse points/junctions during recursive enumeration.

#### `grep`

Suggested arguments: `query`, optional `path`, optional `glob`, optional `is_regex`, optional `case_sensitive`, optional `max_results`.

Do not assume `rg.exe` exists on an end user's machine. A managed implementation should be the supported baseline, with binary/generated-directory skipping, reparse-directory skipping, cancellation checks, per-file and total limits, line numbers, deterministic ordering, and a finite timeout for user/model-supplied regular expressions. An optional discovered `rg` fast path is acceptable only if its output is normalized into the same result contract and packaging does not depend on it.

#### `write_file`

Suggested arguments: `path`, `content`, optional `expected_sha256`, optional `create_only`.

- Require write approval.
- Creating a new file requires `create_only:true` and fails if the target now exists. Replacing an existing file requires a current complete-file read observation; a partial window is not enough authority to replace the entire file. An explicitly supplied `expected_sha256` must equal that observation.
- Recheck the observed hash after approval and immediately before replace so time spent reading the card cannot turn approval into a stale overwrite.
- Write to a temporary file in the target directory, flush, then atomically replace/move so cancellation cannot leave a half-written file.
- Return old/new hash, byte count, and whether the file was created or replaced.
- Do not provide delete or recursive move behavior in v1.

#### `patch_file`

Prefer a deterministic exact-replacement contract over a fragile, partially implemented unified-diff parser:

```json
{
  "path": "Source/Foo.cs",
  "expected_sha256": "optional explicit copy of the required observed hash",
  "replacements": [
    {
      "old_text": "exact unique text",
      "new_text": "replacement text",
      "expected_occurrences": 1
    }
  ]
}
```

Require a current read observation even when `expected_sha256` is omitted; the host's observed hash is the actual concurrency precondition. Locate every exact match in the unchanged preimage and require its complete line span to be covered by an observed range. Apply all replacements in memory, validate the file hash, range coverage, and occurrence counts before writing, then perform one atomic write. If any precondition fails, write nothing and return a concise `read_required`, `read_range_required`, `stale_read`, or `match_conflict` result the model can recover from by reading the file again. Preserve existing line endings and encoding where practical.

#### `run_powershell`

In Phase 1 this may wait synchronously up to a strict short limit; Phase 3 moves all execution onto the job runtime.

- Require explicit approval of the exact script and working directory.
- Resolve `pwsh.exe` deliberately; return a clear unavailable result if PowerShell 7 is absent. A `powershell.exe` fallback must be explicit because language/runtime behavior differs.
- Use `-NoLogo -NoProfile -NonInteractive`.
- Send the script through redirected standard input or a securely created temporary `.ps1`; do not build a quoted `-Command` command line from model text.
- Redirect and drain stdout and stderr concurrently to prevent deadlocks.
- Bound retained bytes and spill additional output to an artifact.
- On timeout/cancellation call `Kill(entireProcessTree:true)` and await stream drains/process exit.
- Return exit code, status, bounded stdout/stderr, duration, and artifact information. A nonzero exit is a completed tool result with `ok:false`, not a thrown loop exception.

### 6.4 Phase 1D — workspace and permission boundary

Add `ToolsEnabled` and `WorkspacePath` to the conversation, not to global settings. Existing conversations migrate to tools disabled and no workspace. A category may later offer a default, but Phase 1 should avoid silently granting filesystem authority to every conversation in a category.

`WorkspacePathGuard` must canonicalize with `Path.GetFullPath`, compare with `Path.GetRelativePath`, reject rooted arguments when the tool contract expects relative paths, reject UNC/device paths, and inspect existing ancestors for symlinks/junctions (`ResolveLinkTarget(true)`). For a new file, resolve the nearest existing parent before authorizing it. Path comparison must use Windows semantics.

This boundary protects file tools only. PowerShell can access the whole user account and must be described honestly in the approval card. Do not claim that its working directory is a sandbox.

Approval is represented by `IChatToolApprovalService` in core and implemented as pending state in `ConversationVm`. The approval request contains conversation id, turn id, tool-call id, tool name, risk, pure presentation, and an immutable hash of the arguments. Approval responses must match those identifiers and hash; a stale renderer click cannot approve a changed call. Read/write grants may be turn-scoped according to the policy above, but every PowerShell call remains individually approved.

### 6.5 Phase 1E — persistence and fork integration

#### Relational run checkpoints

Add relational checkpoints rather than a full session event store:

`DbChatTurn` / `ChatTurns`:

- `IdTurn`, `IdConversation`, `IdTriggerMessage`.
- `Origin` (`human`, `goal`, `subagent`), `Status`, `Outcome`.
- `ProviderKey`, `Model`, `Effort`.
- Versioned `PromptSectionsJson`: the ordered prompt-builder sections with stable semantic section id, source kind/form, user-facing producer label, content hash, exact bounded model-visible text, and optional durable source reference. Also retain the exact assembled system prompt and advertised tool-schema snapshot used on the wire for diagnosis/replay. Prompt-section snapshots contain no credentials.
- `StartedTs`, `EndedTs`, `StepCount`, sanitized `LastError`.

`DbChatStep` / `ChatSteps`:

- `IdStep`, `IdTurn`, positive `StepNumber` with a unique `(IdTurn, StepNumber)` index.
- Request/first-delta/first-output/finish timestamps, provider/model/effort, finish reason, request id.
- Prompt/completion/reasoning, cache-read, and cache-write token counts and cost where supplied.
- Derived `FirstTokenLatencyMs`, `DecodeDurationMs`, and `ModelDurationMs`; nullable when the provider/stream did not expose enough facts. Do not manufacture zeroes for unknown measurements.
- `ContextPlanJson` added now as nullable and populated in Phase 3.

Add `DbModelRequestAttempt` / `ModelRequestAttempts` for bounded provider-request recovery:

- `IdAttempt`, owning turn/step ids, positive `AttemptNumber`, provider/model, and an immutable retry-policy key or version.
- Attempt `Status` (`started`, `completed`, `failed`, `cancelled`) and started/ended timestamps.
- On a failed attempt, `RetryDisposition` (`none`, `scheduled`, `started`, `cancelled`), scheduled delay, retry-start timestamp, and whether a valid provider `Retry-After` supplied that delay. Keeping retry disposition separate prevents a failed provider attempt from being mislabeled as an in-progress request.
- Sanitized stable failure category/code, HTTP status when applicable, bounded failure detail, and provider request id when known.
- Unique `(IdStep, AttemptNumber)` index.

Persist the failed attempt with `RetryDisposition=scheduled` before starting its cancellable delay. Immediately before dispatch, atomically mark that disposition `started` and insert the next attempt as `Status=started`. Cancellation changes a still-scheduled disposition to `cancelled`; startup reconciliation does the same after a crash. The initial MdcAi policy is finite and provider-aware: retry only configured transient categories such as rate limit, server, timeout, transport, or an empty response; never retry authentication, invalid request, quota exhaustion, context overflow, cancellation, or a stream after any assistant delta was accepted. Honor a valid bounded `Retry-After`; otherwise use bounded exponential backoff with jitter. A conservative default is at most three retries, 500 ms initial delay, and 10 seconds maximum delay, with provider descriptors allowed to reduce or disable it. Retry events are visible to the user but never inserted into model-visible history.

Extend `DbMessage` / `Messages` with nullable/defaulted columns so legacy rows load:

- `IdTurn`, `IdStep`, `SequenceInStep`.
- `ProviderKey`, `Origin`, `CompletionState`, `FinishReason`.
- `ToolCallsJson`, `ToolCallId`, `ToolName`, `ToolResultJson`.
- `ReasoningRawJson`, `ReasoningDetailsJson`; retain the existing displayable `Reasoning` text.

Add `DbToolCall` / `ToolCalls` for execution lifecycle and approval state:

- Internal `IdToolCall`, owning assistant message/turn/step ids.
- Exact wire `ToolCallId`, non-negative zero-based model-order `CallIndex`, `ToolName`, `ArgumentsJson`, and immutable `ArgumentsHash`.
- `Risk`, `Status`, proposed/started/ended timestamps, optional terminal error code, and matching result-message id.
- A bounded, versioned `CallPresentationJson` and `ResultPresentationJson` containing locale-neutral presentation intent (`generic`, `terminal`, `read`, `search`, `diff`, and later job/helper/goal forms). Store no HTML and no executable behavior. Large bodies stay in the canonical result/artifact and are referenced rather than copied without limit.
- Unique `(IdAssistantMessage, ToolCallId)` and `(IdAssistantMessage, CallIndex)` indexes.

The assistant message's `ToolCallsJson` remains the authoritative model-visible array. `DbToolCall` is its normalized lifecycle/presentation projection. It is required because one assistant may propose several calls and an approval/running state exists before any `role:"tool"` result node. Never reconstruct the assistant wire array from mutable lifecycle rows. Likewise, never reconstruct a rich read/search/diff/terminal replay card by parsing localized or model-facing prose when its persisted presentation intent is available.

Add nullable `ProviderKey` to `DbChatSettings` so category/conversation defaults preserve provider provenance. Legacy rows derive it once from the current model-id heuristic; all new selections persist the catalog model's stamped provider key.

Use stable message-origin values rather than display labels: `human`, `model`, `tool`, `goal`, `job`, `workspace_context`, `summary`, and `subagent`. `Role` remains the OpenAI protocol role; `Origin` records provenance for projection/UI/authorization. Not every origin needs to appear in Phase 1, but legacy/default handling and renderer fallbacks must be defined once.

`Content` on a tool-result message is the exact bounded string sent to the model. `ToolResultJson` is the canonical structured result used for replay/presentation. `ToolCallsJson` is the exact ordered assistant tool-call array. Never derive protocol history from a localized title or rendered card.

Add relationships/indexes deliberately, but keep legacy `IdTurn`/`IdStep` nullable. Avoid cascade paths that can erase a conversation branch unexpectedly. Branch deletion code should remove orphaned turn/step/helper records explicitly inside one transaction.

Generate an EF migration, update the model snapshot, update mappings in `ChatMessageVmExt`, and refresh the embedded `Source/Desktop/MdcAi.ChatUI.LocalDal/Chats.db`. Verify startup migration from a copy of the previous schema and a clean first-run database.

#### Checkpoint boundaries

Persist at these points:

1. Human/synthetic trigger message and `ChatTurn` start before the first API request.
2. Completed assembled assistant message before executing any requested tools.
3. Each committed tool result, in model order.
4. Final assistant message and terminal turn state.
5. During long streaming, at most once every 1–2 seconds, checkpoint the current prefix as `Streaming`; do not write SQLite per token.

On startup, any turn/message left `Running` or `Streaming` is marked `Interrupted`. A `DbModelRequestAttempt` left `started` becomes cancelled/interrupted and any `RetryDisposition=scheduled` becomes cancelled; restart never silently resumes a retry timer. Nonterminal `DbToolCall` rows become cancelled, and any persisted assistant tool call without complete tool results receives deterministic cancelled results before that branch can be used again.

Move persistence details out of `ConversationVm.SaveCmd` into a repository with explicit transactional methods. The current save path starts multiple asynchronous upserts on the same EF `DbContext` through `Task.WhenAll`; EF contexts are not thread-safe. Change this to sequential/batched operations within the existing transaction wrapper while doing the refactor.

#### Fork behavior

Every OpenAI protocol message remains a `ChatMessageVm` node in the current chain:

```text
human user message
  → assistant message (reasoning/content/tool_calls)
  → tool result #1
  → tool result #2
  → assistant message (final content or more tool_calls)
```

This preserves variable-length branches using the existing `Previous`/`Next` and selector versions. Intermediate assistant/tool nodes participate in persistence but are not editable. Editing a human user message creates a new version at that selector and a new downstream run; the old version retains its original downstream chain.

“Regenerate” on a final assistant tail should regenerate only the final model step from the already accepted tool results. It must not repeat writes or PowerShell. A future separate “Rerun turn from here” command may intentionally repeat tools with fresh approvals, but it is outside this proposal.

### 6.6 Phase 1F — `ConversationVm` integration

Replace message-owned networking with conversation-owned turn execution:

- `ChatMessageVm` remains a reactive transcript/presentation node. Remove or retire its private `GenerateResponse`, `CreateGenerationStream`, `CreateRequest`, and network-owning `CompleteCmd` path.
- Add `ConversationVm.RunTurnCmd` (or a small `ConversationSessionController` called by it) and `StopSessionCmd`.
- `SendPromptCmd` appends/persists a human message and then explicitly invokes `RunTurnCmd`.
- `RegenerateSelectedCmd` explicitly starts a final-step regeneration under the safe semantics above.
- `IsCompleting` is driven by the active conversation turn; the currently streaming assistant node may also expose state for its caret/card.
- `CanSendPrompt` is false while that conversation has an active turn. Phase 1 does not implement steer/inject/follow-up inbox modes.
- Hold a branch lease (selected selector/version ids through the trigger message) for the active turn. Disable edit/delete/version traversal for that conversation until it stops. If a programmatic branch change is detected, cancel before deriving another request; never continue a tool turn on a different fork.
- The active execution subscription lives at constructor/controller scope, not in view activation, preserving the current fix that lets work continue after navigation.
- UI collection mutations use `ObserveOnMainThread`; HTTP, tools, process IO, hashing, and persistence do not run on the UI scheduler.

Move system-prompt assembly out of `ChatMessageVm.CreateRequest()` into `ChatPromptBuilder`. Use ordered sections:

1. Category/conversation premise.
2. MdcAi identity/Markdown guidance (the existing “premise spice,” preserved in MdcAi language).
3. Tool and safety behavior only when tools are enabled.
4. Workspace identity and persisted workspace instructions when available.
5. Active goal reminder in Phase 3.

Keep one system message unless a provider adapter requires another representation. Prompt sections should be individually testable and logged by name/hash, never by secret content.

`BuildRequest` must also carry the existing `ChatSettingsVm` sampling fields (`Temperature`, `TopP`, frequency/presence penalties, streaming choice, and future output limit) through the provider adapter. The current refactor is not permission to silently drop category/conversation settings; adapter policy may deliberately omit parameters a reasoning model does not support.

### 6.7 Phase 1 tests and exit criteria

Phase 1 is complete only when all of the following pass:

- API serialization tests prove exact assistant `content + reasoning + reasoning_details + tool_calls` replay and cancellation-aware SSE parsing.
- A scripted fake model performs `read_file → tool result → final answer` through two model requests.
- A scripted fake emits two tool calls and receives two correctly ordered matching tool results.
- Invalid arguments, unknown tool, denied approval, tool exception, timeout, and nonzero PowerShell exit become structured results.
- Existing-file edit before a committed prior-step read returns `read_required`; an edit outside a partial observed range returns `read_range_required`; external change after read returns `stale_read`; no bytes change in any case; a complete-file-based success advances the observation while a partial-range-based success invalidates it.
- `finish_reason:length` with a partial tool call executes nothing and ends with a visible max-token outcome.
- Cancellation during text streaming persists an interrupted prefix.
- An eligible transient failure before any delta persists a scheduled attempt, retries the exact request after deterministic fake-clock backoff, creates no duplicate assistant node, and succeeds within budget.
- Ineligible/permanent failure, cancellation during backoff, budget exhaustion, and any failure after a reasoning/content/tool-call delta do not retry; restart cancels a persisted scheduled disposition.
- Cancellation after tool calls materializes cancelled results for all outstanding call ids.
- Tool presenters produce bounded versioned call/result intents without IO; read/search/terminal/diff metadata survives a repository round trip independently of localized strings.
- Switching conversation views does not cancel the run; two different conversations can run without sharing state.
- Editing an earlier human message produces a new variable-length branch and reloads both branches from SQLite.
- Legacy conversations with no new columns load and chat normally with tools disabled.
- A copied pre-migration database upgrades; a fresh embedded database starts at the new schema.
- OpenRouter DeepSeek smoke: a real tool-capable reasoning model can read a fixture, call at least one tool, receive its result, and finish without a 400 reasoning-history error in streaming and non-streaming modes.

Do not enable mutating tools in a release build until Phase 2 approval cards are functional.

---

## 7. Phase 2 — transcript, renderer, approvals, and replayable presentation

Phase 2 makes the execution model legible and safe to operate. It should not teach React how to execute tools or how to interpret raw provider messages. React receives typed presentation DTOs and sends user intents back to the host.

### 7.1 Renderer contract strategy

Retain `WebViewRequestDto { Name, Data }` to match the current app, but version the payload contract. Add `ContractVersion` to the envelope or to the initial `Ready`/`SetMessages` exchange. A breaking transcript change should fail visibly in debug logs rather than being guessed by two independent switches.

The payload is now an ordered transcript, not merely a list of chat bubbles. Do not add a separate host message name for every visual card. Messages, reasoning disclosures, paired tool call/results, context injections, retries, and later goal/job/helper status are discriminated transcript items inside the same snapshot/delta protocol; envelope names describe synchronization operations.

Recommended C# → JS names:

- `SetMessages`: authoritative versioned transcript snapshot on initial load, fork/version switch, deletion, or recovery. Retain the existing name to minimize bridge churn, but its v2 `Data.Items` is a discriminated transcript-item array.
- `UpsertTranscriptItem`: add or replace one item by stable id during reasoning streaming, tool status/output, retry lifecycle, or job/goal status changes. The browser computes a displayed retry countdown from one scheduled deadline; the host does not post one update per second.
- `SetSelection`: select by stable transcript item/message id, never array index.
- `HideCaret`: existing behavior, retained if still needed.
- `SetConversationState`: compact active turn/goal/workspace state that is not itself part of transcript history.
- `SetSessionTelemetry`: optional low-frequency aggregate footer update derived from durable records.

Recommended JS → C# names:

- `Ready`: includes renderer and contract versions.
- `SetSelection`: stable item id plus any source message id needed by existing commands.
- `IsScrollToBottom`: existing behavior.
- `ApproveToolCall` / `DenyToolCall`: conversation id, turn id, tool-call id, and immutable argument hash.
- `OpenWorkspaceFile`: activity id and persisted location id, not an arbitrary path authored by JavaScript. The host resolves and revalidates the location through `WorkspacePathGuard` before opening it.
- `StopJob`: job id, if UI cancellation is exposed in Phase 3.
- Existing renderer log messages.

Copying visible content should normally use the browser clipboard and does not require a privileged host message. Continue to send a full `SetMessages` snapshot as the recovery primitive. Incremental messages are a performance/streaming optimization, not the only way to reconstruct state.

### 7.2 Typed transcript DTO

Do not send raw `ChatMessage`, `DbMessage`, `JToken`, or EF entities to React. Project the selected branch into a small discriminated union:

```csharp
public class WebViewTranscriptSnapshotDto
{
    public int ContractVersion { get; set; }
    public string ConversationId { get; set; }
    public long Revision { get; set; }
    public WebViewTranscriptItemDto[] Items { get; set; }
}

public class WebViewTranscriptItemDto
{
    public string Id { get; set; }
    public string Kind { get; set; } // message | activity | turn_summary
    public string TurnId { get; set; }
    public int? StepNumber { get; set; }
    public WebViewChatMessageDto Message { get; set; }
    public WebViewActivityDto Activity { get; set; }
    public WebViewTurnSummaryDto TurnSummary { get; set; }
}

public class WebViewChatMessageDto
{
    // existing Id, Role, Content, version, model/provider/effort fields
    public string Origin { get; set; }
    public string CompletionState { get; set; }
    public string FinishReason { get; set; }
    public bool IsIntermediate { get; set; }
}

public class WebViewActivityDto
{
    public string ActivityKind { get; set; } // thinking | tool | context | retry | plan | job | helper | goal | notice
    public string PresentationKind { get; set; } // generic | terminal | read | search | diff | context | retry | plan | ...
    public string Status { get; set; }
    public string Title { get; set; }
    public string Summary { get; set; }
    public string SourceMessageId { get; set; }
    public string ToolCallId { get; set; }
    public string ArgumentHash { get; set; }
    public WebViewActivityDetailsDto Details { get; set; }
}
```

Make `WebViewActivityDetailsDto` an explicitly tagged container with `Version`, `Kind`, and exactly one typed payload (`Generic`, `Terminal`, `Read`, `Search`, `Diff`, `Context`, `Retry`, and later forms). Validate the one-payload invariant before posting. Do not use Newtonsoft `TypeNameHandling`, `$type`, or reflection-based client type activation across the WebView boundary.

A completed read payload should resemble:

```json
{
  "version": 1,
  "kind": "read",
  "read": {
    "locationId": "loc-42",
    "path": "Source/Desktop/MdcAi.ChatUI/ViewModels/ConversationVm.cs",
    "offset": 397,
    "lines": [
      { "number": 397, "text": "..." },
      { "number": 398, "text": "..." }
    ],
    "retainedLineCount": 2,
    "totalLineCount": 1072,
    "language": "cs",
    "truncated": false,
    "artifactId": null
  }
}
```

An edit payload carries `state: proposed|applied|failed`, `diffs: [{ path, oldText, newText }]`, added/removed/distinct-file counts, and optional precondition/failure data. A terminal payload carries exact script, display cwd, running flag, separately typed stdout/stderr or an explicit `combined` stream, exit code/signal, duration, truncation, and artifact id. These are display-safe values copied from validated persistent records; React does not receive arbitrary host objects.

A message may carry text/reasoning and tool calls together in the provider protocol, so do not model protocol “text” and “tool-call” as mutually exclusive message kinds. Presentation is different: the projector emits assistant prose as an MdcAi message item, model-returned reasoning as a `thinking` activity, and each assistant tool call paired with its matching `role:"tool"` result as one tool activity. The underlying protocol nodes remain separate and exact in persistence. The generic tool-result bubble is suppressed only when the paired activity exists, preventing duplicate output in the UI.

The host owns transcript order and React renders the received array without sorting. Within a step, use the deterministic presentation order `reasoning → assistant narration → calls in CallIndex order`; each call activity updates in place as approval/execution/result state changes, then the next step follows. This is semantic order, not a claim that token-level interleaving can be reconstructed after aggregation.

Suggested tool presentation fields:

- Stable call id, name, semantic kind, MdcAi title, short description, and optional validated file location id.
- Status: `awaiting_approval`, `queued`, `running`, `completed`, `denied`, `failed`, `timed_out`, `cancelled`.
- Risk and approval availability.
- Locale-neutral structured detail data: terminal command/cwd/stdout/stderr/exit, read line numbers/language/window totals, search groups/counts, diffs, or generic input/output.
- Summary, duration, truncation/artifact flags, and total-versus-retained counts. A capped result must never look complete.
- Raw argument/result JSON remains available through a bounded generic fallback or optional “Raw” section; it is not the primary display for a known tool.
- No executable callback, absolute secret path, stack trace, or raw HTML.

`IChatTool.PresentCall` and `PresentResult` return a provider-neutral tagged presentation intent. `ChatToolPresentationService` validates and persists that bounded intent with `DbToolCall`; the renderer projection deserializes the persisted intent first. Re-running the current presenter during replay would let an app upgrade silently rewrite old transcripts. For legacy rows that predate presentation intent, the service may invoke the current pure presenter once; if the tool no longer exists or the intent version is unknown, use a generic presenter over the persisted name/arguments/result. Rendering an old conversation must never fail because a tool was renamed, removed, or learned a new card.

The structured intent is the important DSH lesson. A read card needs exact line numbers, total file lines, and a language hint; a grep card needs match groups and a pre-cap total; a terminal card needs an exit code distinct from output text. Do not attempt to recover these by regex-parsing the prose sent to the model.

### 7.3 DSH-inspired activity transcript visual language

The supplied captures and pinned DSH source show a useful two-level hierarchy:

```text
compact activity row:  icon/status  Title  ·  concise ellipsized summary
expanded detail:       bounded, tool-specific, copyable inspection surface
```

Implement this as MdcAi's activity language. Human prompts and final assistant prose keep the existing MdcAi message treatment. Harness mechanics use quiet, full-width activity rows between prose blocks. This preserves the conversational character of the app while making every meaningful action inspectable.

#### Shared row behavior

- Target a 24 px default row rhythm with a 14 px semantic icon in a 16 px leading box, then title, a small separator dot, and one-line summary. Scale with the renderer's existing font setting rather than hard-coding screen pixels everywhere.
- The summary flexes, truncates with an ellipsis, and never forces the transcript wider. Important trailing state such as `exit code 1`, `3/5`, or `+2 active` sits in a non-shrinking pill/suffix.
- On hover, the leading icon may preview the disclosure chevron. The whole row is an `aria-expanded` keyboard target when expandable; Enter and Space toggle it. Icon/color/shimmer are never the only status signal.
- Running reasoning/tool rows retain identity and use a restrained sweep or spinner. Respect `prefers-reduced-motion` and provide hidden status text for assistive technology.
- Success/read/search rows start collapsed. A pending approval and the first terminal failure may auto-expand once. After the user toggles a row, live updates must never override that choice.
- Multiple rows may be open. Add an optional per-turn “collapse details” action only if long tool-heavy transcripts prove it useful; do not enforce accordion behavior.
- Persist the activity and its data, not disclosure state. Expanded/nested-fold state is renderer-local, keyed by stable activity id, and survives `UpsertTranscriptItem` replacement during the mounted conversation.

#### Bounded detail geometry

Use named renderer constants rather than per-component guesses. Recommended starting values are eight retained rows for an inline read/search preview, four head plus four tail rows around an explicit omitted-count control, and 16 rows in any future dedicated inspector. Keep an expanded inline surface within `min(42vh, 360px)`; terminal output may use a tighter 260 px cap and context/reasoning a 180 px cap. These are starting design tokens to verify at 100%, 150%, and 200% text scaling, not immutable copies of DSH's CSS.

Expansion reveals more data inside an independent scrollport; it must not turn a 5,000-line command into a 5,000-line page. Headers and copy/status actions remain sticky. Source, search, and terminal lines preserve whitespace and use horizontal scrolling. Context, errors, and reasoning use wrapping because prose readability matters more than column alignment. Apply `overscroll-behavior: contain` so reaching the end of a detail surface does not unexpectedly fling the chat.

Copy always writes the complete retained payload for that surface, not merely the currently visible head/tail rows and never its line-number/status chrome. Show brief `Copied` feedback. Line-number gutters are excluded from text selection. If content was truncated before reaching the UI, copy the retained content and keep the truncation/artifact notice visible; never imply the clipboard contains omitted bytes.

#### Tool- and activity-specific presenters

| Activity | Collapsed row | Expanded detail contract |
|---|---|---|
| Thinking | `Think · <first line>` when complete; follow the latest non-empty line while streaming | Only reasoning actually returned by the provider, in a bounded wrapped text body. Never synthesize or claim hidden chain-of-thought. Preserve the raw structured reasoning separately for protocol replay. |
| File read | `Read · Source/.../File.cs · lines 401–800` with a validated open-file affordance | Banner with display path, `showing N of M lines`, language, and Copy; line-number gutter; syntax-highlighted lines; head/tail omission marker; horizontal scroll. The row should prefer a workspace-relative path and may reveal the canonical path only through a host-validated action/tooltip. Language is an allowlisted grammar id; unknown values render plain text and never become dynamic import paths. |
| Grep/glob | `Grep · pattern · 31 matches in 1 file` or `Glob · 27 paths` | Header with retained/total counts and Copy; grep grouped by file with collapsible groups, line numbers, and match text; glob as a path list; cap/recovery artifact notice. Search filters such as scope/include/case/regex belong in a compact metadata area. |
| PowerShell | `Pwsh · <tool-authored description>` | Terminal surface with running/done/error dot, exact script, validated working-directory label, sticky nonzero exit/signal pill, Copy, separately retained stdout/stderr or an explicitly combined stream, ANSI sanitized into safe spans, no wrapping, both scroll axes, duration, timeout/cancel state, and artifact/truncation notice. Whitelist supported SGR color/emphasis only; strip OSC hyperlinks/titles and other control sequences. A nonzero exit is result data, not a renderer exception. |
| Write/patch | `Write · path` or `Edit · path · 3 replacements`; on failure, replace the path summary with the first concise error line in red | A compact proposed/applied diff with file headers, `-` removed lines, `+` added lines, same-file hunk gaps, create/update/delete state, expected-hash/precondition outcome, approval state, Copy, and a `└ +A −R · N files` footer. Show the applied result presentation after completion; do not replace it with vague `ok:true`. |
| Context injection | `Context · AGENTS.md`, `Context · conversation premise`, or `Context · active goal` | Bounded model-visible text plus source kind, canonical display label, hash/version, loaded/replaced/removed state, and section boundaries. Show exactly the content the model received, including framing that affects interpretation. Unknown forms fall back to bounded text/JSON. Never include API keys or hidden host-only policy. |
| Model retry | `Retrying model request (2/3) · 4s` and later `Retried model request (2/3) · 4s` | Scheduled delay, source of delay (`Retry-After` or local backoff), provider/model, stable failure category/status, sanitized failure reason, request id when safe, and scheduled/started/cancelled state. Countdown uses the browser's render-time deadline but durable timestamps/state remain authoritative. |
| Plan/goal | `Update plan · 2/5 completed · <active item>` or `Goal · round 3/10` | Structured items with status and active work, revision/budget, last outcome, and pause/block reason. Phase 2 can reserve the form; Phase 3 supplies durable goal data. |
| Job/helper | `Pwsh job · running · 18s` or `Helper · inspect tests · completed` | Owner, status, elapsed time, bounded output/summary, stop availability, limits, and artifact/transcript link. Phase 3 supplies these records. |
| Generic/unknown | `<tool name> · <salient input>` | Bounded `Input` and `Output` sections that scroll independently, with pretty JSON for non-text values and a clear unsupported-presentation notice in debug builds. Unknown data must remain visible and harmless. |

The edit/write viewer should deliberately stay tiny. It needs no empty banner row: float Copy at the top-right, use the first bold row as the file path, color and prefix removed/added lines so meaning does not depend on color alone, use `…` between disjoint hunks in the same file, and end with the dim aggregate footer. Show at most eight body rows inline using the same head/tail omission control as read/search; expanding reveals the remaining rows inside the bounded scrollport. Count distinct files in the footer, not hunk count. Copy writes the paths/gap markers and signed diff lines, but not the floating button or aggregate footer.

Presentation state must be explicit. Before approval/execution, label the card `Proposed` and derive its diff from validated arguments. After success, replace it in place with `Applied` hunks captured by the tool result; this matters when a replace-all or formatter changes more than the proposal predicted. A failed precondition such as “edit requires reading the file first,” stale content hash, or non-unique match stays a failed activity and must not retain an `Applied` label. Its collapsed row shows the error; expansion shows the intended input/diff separately from the failure explanation so the UI never implies a mutation occurred.

Tool descriptions should be authored by the tool/presenter and describe intent (`List renderer files`), while exact commands and raw arguments live in detail. Never ask React to infer a friendly sentence from arbitrary PowerShell or JSON. Errors replace the normal collapsed summary with the first sanitized failure line and retain the original input in the expanded body.

#### Context transparency and sensitive data

Every non-user prompt section actually sent to the model should have a durable source record and a user-visible activity unless it is ordinary selected conversation history already visible as messages. Recommended context forms are `premise`, `workspace_instructions`, `runtime_snapshot`, `goal_reminder`, `summary`, and `tool_catalog`. Each form has a typed viewer and an opaque fallback. If content is too sensitive to display to the user, it is too sensitive to inject silently; host-only secrets and API credentials never enter either path.

Cap each context detail payload at 20,000 display characters after the prompt builder's own model-budget cap. If a foreign/legacy record exceeds it, show the exact retained prefix plus total/omitted counts and a source/artifact action when one exists; never silently shorten or paraphrase it. Unknown content blocks render as bounded JSON rather than disappearing.

Treat the advertised tool-schema snapshot as a request-surface context source even though it is serialized in `tools`, not concatenated into the system message. A deduplicated `Tools available · N` activity may expand to the exact model-visible names, descriptions, and nested raw schemas. Optional risk/execution-mode badges are clearly labeled host policy metadata rather than content sent to the model. The record must not contain delegates, DI objects, approval grants, or secrets.

Do not emit a context row every time an identical snapshot is reused. The projector compares each turn's ordered prompt-section/tool-schema id and hash to the prior selected-branch turn, shows a baseline on first use, and then only set/replace/remove deltas. The per-turn snapshots remain available for inspection/replay even when an unchanged duplicate is suppressed from the main flow.

#### Turn usage and session telemetry

Add a quiet session statistics line beneath the composer, inspired by the captures but computed from MdcAi's durable checkpoints: turns, model steps, summed LLM wall time, summed tool wall time, average TTFT over measured steps, decode tokens/second, prompt-cache hit percentage when supplied, and compact input/output token counts. Drop unavailable groups rather than displaying misleading zeroes. Ellipsize on narrow layouts and expose the full line in an accessible tooltip only when clipped.

Also attach an optional `Turn usage` disclosure to each completed turn with exact provider/model routes, uncached input, cache read/write, output, reasoning, total tokens, and cost when available. Session figures must be whole-session projections, not a fold over only the DOM/loaded window. Update at most a few times per second and on terminal state; token streaming must not rerender settled activity rows.

#### DSH source reference map

The implementation agent may use these files in the pinned local DSH checkout as behavioral references, not as dependencies or code to transplant:

| DSH source | Architectural lesson for MdcAi |
|---|---|
| `packages/core/tools/src/presentation.ts` | Provider-neutral tagged `presentCall`/`presentResult` vocabulary; UI capability fallback. |
| `packages/client/ui-primitives/src/DisclosureRow.tsx` | Shared 24 px accessible disclosure chrome and hover chevron behavior. |
| `packages/client/ui-tool/src/client/tool/components/ToolRow.tsx` | One generic row shell delegates to structured cards while retaining a generic input/output fallback. |
| `packages/client/ui-tool/src/client/tool/models/*-card-model.ts` | Validate persisted metadata before choosing a rich viewer; live and replay use the same pure derivation. |
| `packages/client/ui-primitives/src/ReadBlock.tsx`, `SearchBlock.tsx`, `TerminalBlock.tsx`, `DiffBlock.tsx` | Line-aware read, grouped search, ANSI terminal, head/tail folding, copy semantics, and the compact `+A −R · N files` diff footer. |
| `packages/client/ui-chat/src/client/chat/ReasoningRow.tsx` | Settled reasoning previews the first line; streaming reasoning follows the latest line without replacing row identity. |
| `packages/client/ui-chat/src/client/chat/ContextInjectionRow.tsx` and `ContextBody.tsx` | Durable producer/form-specific context views with a bounded opaque fallback. |
| `packages/client/ui-chat/src/client/chat/MessageItem.tsx` | Retry countdown/details are an inspectable transcript record, not a toast. |
| `packages/client/ui-chat/src/client/chat/StatsLine.tsx` and `TurnUsageDisclosure.tsx` | Session totals and per-turn token buckets are separate projections and omit unavailable facts. |

The DSH source uses its own Cordis slots, package graph, event log, CSS tokens, and localization system. None of those become MdcAi dependencies; reproduce the seams inside the simpler `MdcAi.ChatCore` → typed WebView DTO → React component path.

### 7.4 React component model

Create a small registry and focused components rather than expanding the single `App.js` map:

```text
src/components/transcriptItem.js
src/components/transcriptMessage.js
src/components/activity/activityRow.js
src/components/activity/activityDetailsHost.js
src/components/activity/thinkingDetails.js
src/components/activity/readDetails.js
src/components/activity/searchDetails.js
src/components/activity/terminalDetails.js
src/components/activity/diffDetails.js
src/components/activity/contextDetails.js
src/components/activity/retryDetails.js
src/components/activity/planDetails.js
src/components/activity/genericDetails.js
src/components/activity/approvalActions.js
src/components/turnUsageDetails.js
src/components/sessionStatsLine.js
src/components/jobStatus.js                 Phase 3
src/components/goalStatus.js                Phase 3
src/components/jsonDetails.js
```

`activityDetailsHost` switches only on the closed `PresentationKind` vocabulary and delegates to a component. Tool wire names select presenters on the C# side, not React components. The default component always renders bounded input/output/JSON, so one missing registration cannot hide a call.

Use stable keys (`item.Id`, tool call id, file-group id), never `key={index}` except for a deliberately unkeyable immutable unknown-block list. The current index key and index-based selection can transfer expanded/collapsed state to the wrong item when tool nodes are inserted. Keep selection and disclosure maps by id and discard entries only when their ids are absent from an authoritative snapshot.

Tool arguments/results must be rendered as text/React values, not injected with `dangerouslySetInnerHTML`. Existing model Markdown continues through the C# Markdig pipeline and code highlighter. Any tool result deliberately rendered as Markdown must first pass through an explicit safe presenter and HTML encoding policy; raw shell/file output is untrusted.

Component behavior:

- Collapsed by default, showing status icon, title, summary, and duration.
- Running status updates without replacing the component identity.
- Approval buttons are visible only for the exact pending call and disable immediately on click.
- Denied/failed/timed-out cards retain a concise explanation and expandable structured detail.
- Large output uses head/tail virtualization or a bounded preview and an artifact/truncation note, not megabytes of DOM.
- Reasoning remains owned by its assistant protocol message but projects as a separate `Think` activity immediately before that assistant's narration/tool activities.
- Synthetic context/goal messages use a neutral MdcAi label, not “You” or a fake assistant identity.
- All chrome labels come from one MdcAi renderer string table. Persist semantic fields, never translated strings; a missing locale key falls back to MdcAi English, not a copied DSH locale.

Preserve auto-scroll only when the user is already at the bottom. A tool-card status update should not pull a user away from text they are reading. When the user has scrolled away, show a small lower-right `Jump to latest` chevron inside the transcript viewport; hide it as soon as the bottom sentinel is visible. It is a scroll control, not a disclosure/collapse control, and needs an accessible label plus keyboard focus.

### 7.5 Live update path

The current app rebuilds and posts the entire message array on a roughly 33 ms cadence while content/reasoning streams. That is acceptable for small chats but expensive when tool payloads and long histories arrive. Phase 2 should separate structural snapshots from live item updates:

1. `Messages`/fork structure changes trigger `SetMessages` after a small coalescing interval.
2. Content, reasoning, tool-call argument fragments, completion state, retry lifecycle state, and job status for one active node trigger sampled `UpsertTranscriptItem` updates.
3. On WebView `Ready`, always replay the latest full snapshot before deltas.
4. Include a monotonically increasing per-conversation `Revision`. React ignores a delta older than its current snapshot revision. A fresh snapshot resets state.
5. Do not post an entire terminal/read/search payload on every progress tick. Running calls update status and bounded live output at a lower cadence; the completed payload arrives once.

Every upsert carries conversation id, base/snapshot revision, item revision, and the complete replacement DTO for that item. This prevents a late streaming callback from resurrecting a node after the user switched fork or deleted a branch and avoids fragile JSON-patch semantics inside stringly typed WebView messages.

Stable item ids should be deterministic from durable ids, for example `message:{IdMessage}`, `thinking:{IdMessage}`, `tool:{IdToolCall}`, `context:{IdTurn}:{SectionId}:{ContentHashPrefix}` (or `context:{IdWorkspaceContext}` when that durable source is the activity), `retry:{IdAttempt}`, and `turn-summary:{IdTurn}`. Never generate a new id for a state transition. An activity revision replaces its data while renderer-local disclosure and nested file-group state remain intact.

### 7.6 Inline approval lifecycle

Avoid modal `ContentDialog` approval as the primary design. Conversations continue in the background and the view is cached by type; a modal tied to the wrong `XamlRoot` is fragile. Instead:

1. The runner records the tool call as `AwaitingApproval` and awaits `IChatToolApprovalService`.
2. The conversation renderer displays the pending card even if another conversation is currently visible.
3. When the user returns and approves/denies, the WebView sends identifiers plus the arguments hash.
4. `ConversationVm` validates the current pending request and completes its `TaskCompletionSource` exactly once.
5. Navigation, app shutdown, turn cancellation, message edit/delete, or fork switch completes pending approvals as cancelled.
6. Approval grants are held in memory and scoped to conversation + turn + risk category. They are never inferred from an earlier persisted card.

The pending activity auto-expands once to the structured diff or exact PowerShell script and working directory. Approve/deny controls remain outside any scrolling output region, and the exact immutable argument hash covers the detail being approved. A host rejection of a stale click updates the same card to a clear stale/cancelled state; it does not show a generic renderer error.

The “Stop generating” control should bind to `ConversationVm.StopSessionCmd`, not `Tail.Message.StopCompletionCmd`. It must remain available while waiting for approval or a foreground tool, not only while tokens are streaming.

### 7.7 Transcript selection and fork controls

Define which nodes expose existing actions:

- Human user message: selectable, editable, deletable, version navigation.
- Final assistant tail: selectable and regenerable.
- Intermediate assistant-with-tools: selectable for inspection but not editable/regenerable by the existing command.
- Tool result: selectable for inspection but not editable/versioned directly.
- Synthetic goal/context: selectable for inspection, never editable as a human prompt.

Selecting a paired tool activity resolves to its assistant call/result source ids for inspection but must not make the hidden `role:"tool"` node independently editable. A clickable read/write path invokes only `OpenWorkspaceFile`; it is not a generic anchor and cannot send an arbitrary filesystem path to the host.

Version badges apply to selector versions, but tool/result descendants belong to the selected upstream branch. Test switching a user version whose two descendants contain different numbers of tool steps.

### 7.8 Renderer packaging and tests

This is a breaking behavioral renderer change, so bump `src/version.js` by a major version, update the contract version, rebuild, and run `Source/React Chat Renderer/RendererApp/zip-build.ps1` (or the documented build/zip process) so `Source/Desktop/MdcAi.ChatUI/Assets/ChatListUI.zip` contains the tested bundle.

Add React tests for:

- Text-only legacy messages.
- Reasoning summary using first line when settled and latest line while streaming.
- Reasoning plus narration plus two paired tool activities without duplicate result bubbles.
- Streaming `UpsertTranscriptItem` preserving outer disclosure and nested search-group state.
- Approval single-click and stale-id suppression.
- Pending → running → completed/failed transitions.
- Snapshot revision replacing stale deltas.
- Selection by id across insertion/removal.
- Bottom-sentinel auto-scroll and `Jump to latest` visibility across streaming/activity updates.
- Light/dark styling, keyboard focus, `aria-expanded`, and approval button labels.
- Read line numbers/window count/language/copy, syntax-highlight fallback, and open-file validation intent.
- Grep grouping, per-file collapse, retained/total counts, head/tail omission, and recovery artifact notice.
- PowerShell running/exit/signal state, ANSI sanitization, sticky header, exact-copy behavior, empty output, and separate scroll axes.
- Diff create/edit/failure forms and generic fallback for unknown presentation versions.
- Context baseline/delta/opaque forms, 20,000+ character bounding, and no HTML injection.
- Retry scheduled/started/cancelled/countdown/replay state with sanitized errors.
- Very large/truncated output proves bounded DOM height and correct copy/truncation messaging.
- Session statistics omit unknown metrics, survive transcript paging/fork snapshots, and expose clipped text accessibly.
- 100%, 150%, and 200% text-size layout; narrow-width ellipsis; reduced-motion behavior.

Add C# contract tests that serialize every envelope/DTO/presentation-intent variant, replay persisted intents without the originating tool registration, pair calls/results by exact id and call index, and deserialize JS intents with numeric values represented as `JValue`/`long`. Stop casting `Data` directly to `bool` or relying on array indices without validation. Add database upgrade tests for request attempts, structured presentations, and unknown future presentation kinds.

Manual renderer review must use a fixture conversation that includes: context injection, streaming thinking, assistant narration, read, grep, successful and failed PowerShell, a write approval/diff, retry, two calls in one assistant message, truncation/artifact recovery, final answer, and session telemetry. Review both themes and a fork switch while several rows are expanded.

### 7.9 Phase 2 exit criteria

Phase 2 is complete when:

- A live reasoning/tool turn visually unrolls as context/retry if present → thinking/narration → paired tool activities/results → final assistant without page flicker, duplicate tool-result bubbles, or state loss.
- Every meaningful harness action is visible as a concise row and inspectable through a bounded, useful detail view; known tools do not fall back to raw JSON in the normal path.
- Replay after app restart produces the same semantic activities from persisted, versioned presentation intent without executing presenters with side effects.
- Mutating and PowerShell calls cannot execute without a valid current approval.
- Fork switching, edit, delete, and final-step regenerate remain correct with variable-length tool branches.
- Cancellation works while streaming, awaiting approval, executing a foreground tool, and between steps.
- Context and retry records are honest, model retries are bounded/cancellable, and hidden credentials never appear in transcript details.
- Session/turn telemetry is derived from durable checkpoints, labels unknown values as absent rather than zero, and does not cause settled rows to rerender per token.
- The shipped zip version and source version match.
- Ordinary tool-disabled chats have no visible regression.

---

## 8. Phase 3 — background jobs, one-shot helpers, goals, and context management

Phase 3 builds on the same step loop. These are not separate ad hoc completion implementations.

Keep the original dependency rule: `MdcAi.ChatCore` defines narrow persistence abstractions such as `IBackgroundJobStore`, `ISubagentRunStore`, `IGoalStore`, `IConversationSummaryStore`, `IWorkspaceContextStore`, and `IArtifactStore`; `MdcAi.ChatUI`/LocalDal adapters implement them. Do not add an EF Core or `MdcAi.ChatUI.LocalDal` reference to the core project during Phase 3.

### 8.1 Phase 3A — background jobs

#### Runtime contracts

Add to `MdcAi.ChatCore`:

```text
Jobs/IBackgroundJobService.cs
Jobs/BackgroundJobService.cs
Jobs/BackgroundJobRecord.cs
Jobs/BackgroundJobOutputBuffer.cs
Tools/BuiltIn/GetJobChatTool.cs
Tools/BuiltIn/StopJobChatTool.cs
```

Use statuses `running`, `stopping`, `completed`, `killed`, `failed`. Job ids need not be secret, so authorization must verify the owning conversation/session id on every get/read/stop operation.

The service should enforce:

- Per-conversation concurrent-job cap (default 4 for MdcAi; lower than DSH's platform default is appropriate).
- App-wide cap to prevent many conversations exhausting the machine.
- First terminal result wins.
- One consuming output cursor per model-facing poll; UI may observe a non-consuming snapshot.
- Bounded in-memory UTF-8 ring buffer and optional artifact spill.
- Cancellation of owned jobs on conversation disposal/app shutdown, with process-tree kill and awaited cleanup.
- Listener failures isolated from job state.

#### Job-backed PowerShell

Refactor `run_powershell` to always use the job service:

1. Start the process and job after approval.
2. Wait only a short fast-path interval (for example 1–2 seconds).
3. If complete, return the terminal result immediately.
4. If still running, return `status:"running"`, `job_id`, and the instruction that `get_job` must be called.
5. `get_job` accepts `job_id`, output cursor, and a bounded `wait_ms`; it returns only new output plus status/next cursor.
6. `stop_job` requires matching ownership and returns the final killed/failed snapshot after cleanup.

The system tool section should explicitly tell the model not to start duplicate commands while a job is running and to poll until terminal before claiming verification succeeded.

If a job completes while its originating turn is active, the controller may queue a persisted, framed completion notice for the next step. If the normal turn has already ended, update the UI but do not silently spend another API call. An active user-authorized goal may consume the notice in its next continuation round.

#### Persistence and restart

Add `DbBackgroundJob` with job/owner/tool-call identifiers, kind, status, timestamps, command presentation (redacted as needed), exit code, output artifact metadata, and failure summary. Do not attempt to reconnect to a Windows process after app restart in this scope. Any nonterminal persisted job becomes `killed/interrupted` on startup and its card says it cannot be resumed.

Never persist secrets copied from environment variables or raw process environments. Avoid logging command/output bodies at normal levels.

#### Background-job tests

- Short command fast path.
- Long command returns id, produces incremental output, then completes.
- Stdout/stderr are drained concurrently.
- Cursor reads do not repeat consumed model output.
- Output cap/artifact spill.
- Timeout, user stop, parent cancellation, nonzero exit, process-start failure.
- Foreign conversation cannot read/stop a job.
- App-start reconciliation marks stale jobs interrupted.

### 8.2 Phase 3B — one-shot helper sessions

Name the model-facing tool `run_subagent` only if the project is comfortable with that established term; `delegate_task` is a more MdcAi-neutral alternative. Pick one name and never alias both on the wire because duplicate descriptions waste tokens and confuse selection.

#### Semantics

A one-shot helper is a second `ChatSessionService` invocation with:

- Its own in-memory transcript and distinct run id.
- The same provider/model and compatible reasoning configuration as the parent by default.
- A scoped helper system prompt: perform the bounded task, return evidence and a concise final result to the parent, do not address the end user, and do not assume ungranted capabilities.
- An optional context seed chosen by the host, not uncontrolled transcript copying.
- A filtered tool registry.
- Depth, step, token, wall-clock, and concurrency limits.
- Parent cancellation linked to child cancellation.

The initial helper capability set should be read-only: `read_file`, `list_dir`, and `grep`. Do not grant `write_file`, `patch_file`, PowerShell, goal tools, or another helper tool at depth 1. This avoids concurrent edits, nested spend, and permission ambiguity. The parent performs any mutations after considering the helper's returned evidence.

Recommended defaults:

- Maximum helper depth: 1.
- Maximum helpers in one parent step: 2.
- Maximum child steps: 6–8.
- Maximum wall time: 5 minutes.
- A separate child output-token budget included in the parent's displayed cost.

#### Parent context seeding

Do not blindly copy the entire parent transcript. Build a child context plan:

- Always include the delegation prompt and workspace instruction snapshot.
- Include explicitly referenced files only through read tools or a bounded seed.
- Optionally include the active human request, the parent's latest reasoning-safe summary, and a small recent-turn suffix.
- Preserve whole protocol groups if any tool history is included.
- Record the seed policy and source ids for audit.

The model-facing helper result is an ordinary structured tool result containing status, final answer, relevant file references, and usage. The parent loop does not continue to its next LLM step until this one-shot call settles, although sibling read-only tool calls may execute concurrently once bounded parallel scheduling is enabled. Do not claim that the parent model itself is reasoning concurrently.

#### Persistence

Add `DbSubagentRun` (or `DbHelperRun` if the chosen nomenclature is helper) with owner conversation/turn/tool-call ids, parent helper id, depth, prompt/description, model/provider, seed metadata, status, timestamps, usage, final answer, and bounded transcript JSON or child message rows. This record is for inspection and diagnosis; it does not create a sidebar conversation.

Pure presenters render a nested helper card with task, status, final summary, and optional transcript expansion. Reopening it never reruns the helper.

#### Helper tests

- Independent transcript and exact result pairing.
- Parent cancellation cancels child.
- Child cannot see hidden tools or spawn recursively.
- Context seed obeys budget and does not leak non-current fork versions.
- Two helpers do not share messages/state.
- Failure/timeout produces a normal tool result the parent can recover from.
- Persisted helper card replays after restart.

### 8.3 Phase 3C — durable, bounded goals

Goals are user-authorized continuation, not a license for a model to spend indefinitely.

#### User experience and authority

Expose a conversation action such as “Continue toward a goal…” that lets the user enter/confirm the objective, maximum rounds, and optional spend/token limit. A model may propose creating a goal, but activation requires the same explicit user confirmation. Do not let ordinary prompt text silently turn on autonomy.

Only one nonterminal goal may own a conversation at a time. On app restart, an active goal appears `Paused` and requires the user to resume; it must not resume API spend automatically.

#### Goal state

Add `DbGoal`:

- `IdGoal`, `IdConversation`, `Objective`.
- `Status`: `active`, `paused`, `blocked`, `complete`, `cancelled`, `round_limit`, `budget_exhausted`, `failed`.
- Positive `Revision` for optimistic concurrency.
- `MaxRounds`, `RoundsStarted`, optional token/cost limits and consumed values.
- Created/updated/started/ended timestamps.
- Structured blocked code/reason and final summary.

Use a filtered unique index or transactional invariant to enforce one active/paused/blocked goal per conversation.

Add attribution to `DbChatTurn`: `IdGoal`, `GoalRevision`, `GoalRound`. Only an admitted persisted continuation turn increments `RoundsStarted`; retries of a failed database transaction do not.

#### Continuation messages

Each continuation round begins with a persisted synthetic user-role message whose `Origin=goal`, for example:

```text
<mdcai-goal-continuation goal-id="..." revision="2" round="3" max-rounds="6">
Continue working toward the active objective. Review the latest tool results, make concrete progress, and either continue, call complete_goal with evidence, or call block_goal with the specific input or external change required.
</mdcai-goal-continuation>
```

This message is model-visible, branch-local, durable, and rendered as a compact MdcAi continuation card—not as a message allegedly typed by the user.

Goal-only tools:

- `complete_goal(summary, evidence)` transitions the matching active goal revision to complete and concludes the turn after its tool result is committed.
- `block_goal(code, reason, required_input)` transitions to blocked and concludes after commit.
- A read-only `get_goal` may expose current limits/status if the prompt section is not sufficient.

The model should not mutate round limits or resume itself. User UI owns update/resume/cancel.

Terminal goal tools are exclusive ordering barriers. If `complete_goal` or `block_goal` appears with later calls in the same assistant tool-call array, do not execute calls after the terminal transition; append protocol-valid `skipped_goal_terminal` results for them in model order. Calls committed before the terminal call remain committed. This prevents a model from marking a goal complete and then causing an unreviewed later side effect while still satisfying the one-result-per-call invariant.

#### Goal controller

After a goal-attributed turn ends:

1. Reload goal state/revision transactionally.
2. Stop if complete, blocked, paused, cancelled, errored, or budget exhausted.
3. Stop at the round cap with `round_limit`; do not mislabel this blocked.
4. If still active, admit exactly one next round, persist its synthetic message/turn attribution, then invoke the same conversation turn controller.
5. Yield to the UI between rounds so pause/stop is responsive.

`StopSessionCmd` cancels the current work and pauses the goal. Sending a new human prompt while a goal is active should first pause the goal; Phase 3 does not implement DSH-style mid-turn steer/inject queues.

Default to a small maximum such as 5 rounds, expose the configured value, and enforce a hard product ceiling such as 20. Also retain per-turn step/tool limits. If repeated turns make no measurable progress, the controller may suggest blocking, but avoid unreliable semantic progress heuristics as a state transition; the hard budgets are authoritative.

#### Goal tests

- State transition table, revision mismatch, one-active-goal invariant.
- Exactly-once round admission under retry/concurrency.
- Complete/block tools available only for the active matching goal.
- Max rounds and token/cost budget stop deterministically.
- Stop pauses; user resume increments revision and continues safely.
- Human prompt pauses active automation.
- Restart never silently resumes.
- Fork edit during a paused goal creates a deliberate goal revision or requires cancel/restart; it cannot continue against a changed branch accidentally.

### 8.4 Phase 3D — context management

Context management is the largest sustained quality lever after the step loop. It must be a deterministic planner with visible diagnostics, not arbitrary `TakeLast(n)` logic.

#### Capability and budget input

Extend model metadata/capabilities with context length, supported reasoning controls, and tool support. For OpenRouter use returned `context_length` and supported parameters. For unknown models, use a conservative configured fallback and surface that it is estimated.

Compute:

```text
input budget = model context length
             - requested maximum completion/reasoning budget
             - tool schema estimate
             - safety margin
```

Reasoning tokens consume output budget. Ensure any provider-specific reasoning budget leaves room for final content. Do not assume an effort label has identical token meaning across providers.

Create `IChatTokenEstimator`. Exact tokenizers may be used for known OpenAI families, but arbitrary OpenRouter models need a conservative UTF-8/character estimator with protocol overhead and a safety multiplier. Record actual provider `usage.prompt_tokens` to test/calibrate estimates; do not pretend a universal estimate is exact.

If the irreducible active-turn protocol group plus prompt/tools exceeds the budget, fail before the API request with an actionable context-limit error. Never drop a tool result or required reasoning block to make the JSON fit.

#### Atomic context units

Plan whole units:

- System/prompt sections.
- Persisted workspace instruction/context records.
- One completed human turn, including every assistant/tool step it contains.
- The entire active turn.
- Persisted summary records.

An assistant-with-tool-calls plus every matching result is indivisible. For DeepSeek/tool requests, every included reasoning assistant message retains its required reasoning fields. Context trimming can omit an old whole turn and replace it with a summary, but cannot include the assistant while removing its reasoning or one result.

#### Retention order

Recommended planner order:

1. Always retain current premise/MdcAi runtime sections and active goal reminder.
2. Always retain the active turn and immediately preceding human request.
3. Retain explicitly pinned messages/turns.
4. Retain a recent completed-turn suffix until the budget is approached.
5. Insert the latest valid summary covering the omitted prefix.
6. Retain a small head only when it contains user-pinned identity/requirements not already in the summary.
7. Compact old large tool results to their persisted model-visible summaries/artifact references only if that compact form was what the model originally received or a new summary explicitly replaces the whole old turn.

Do not rewrite stored history. The context plan is a request projection.

#### Summaries

Add `DbConversationSummary`:

- `IdSummary`, `IdConversation`, branch anchor/head version ids.
- Covered through message/turn id and source hash.
- Summary text, model/provider, created timestamp, token estimate.
- Status and optional superseded summary id.

Only summarize completed turns. The source hash invalidates a summary if the covered fork selection changes. Persist the summarizer prompt/version so changed summarization behavior is diagnosable.

Use a staged reduction:

1. Deterministically omit redundant UI-only fields and use already bounded tool model content.
2. Reuse a valid persisted summary.
3. If necessary, ask the configured model for a structured factual summary containing user requirements, decisions, files/actions, unresolved issues, and important identifiers—without fabricating successful tool results.
4. Validate size and persist before using it.

Summarization is a separate, cancellation-aware request with tools disabled. A summarizer failure should fall back to a smaller recent window if valid; it must not corrupt the transcript.

#### Workspace context and instructions

Add a bounded `WorkspaceContextService` that may discover `AGENTS.md` from the selected workspace root toward the working directory. Loading should be explicit/visible and snapshots should be persisted with path, hash, and content so model-visible input is replayable. Do not implement a general DSH skills/plugin loader in this scope.

Persist those snapshots in `DbWorkspaceContext` with conversation id, source kind/form, workspace-relative path or producer label, content hash, bounded model-visible content, discovery timestamp, active/superseded state, and the first/last turn that used the snapshot. A changed file creates a new snapshot; it does not rewrite what an earlier turn saw. Record set/replace/remove deltas so the Phase 2 context activity can explain changes without repeating an identical full snapshot on every request.

Frame non-human injected context with provenance, for example:

```text
<mdcai-context source="workspace-instructions" path="AGENTS.md" sha256="...">
...
</mdcai-context>
```

The surrounding prompt must say that workspace/tool content is untrusted data and never grants authority. Prompt injection in a repository file cannot approve a write or PowerShell call.

Project the exact framed content through the Phase 2 `context` presentation, with the source path/label, hash, baseline/delta state, and superseded snapshot reference. The renderer may add a structured file/status header, but the expandable text must remain the bytes the model received rather than a cleaner paraphrase. Premise, active-goal reminder, summary, and runtime/tool-policy sections use the same typed context seam; host-only security decisions and credentials do not.

#### Context diagnostics and tests

Persist a compact `ContextPlanJson` per step: budget, estimate, included turn/message/summary ids, omitted ranges, tool-schema estimate, and planner version. Log hashes/ids and numeric totals by default, not prompt contents.

Test:

- Exact fit, one-token-over estimate, unknown context length.
- Current tool group larger than budget fails safely.
- Old whole turns summarized while active group remains exact.
- Pinned turns survive trimming.
- Summary invalidates on fork version change.
- DeepSeek reasoning fields remain on every included assistant message when tools are advertised.
- Provider switch strips only incompatible completed-turn private state.
- Large tool artifacts never enter request history accidentally.

### 8.5 Bounded parallel tools

After jobs and helper cancellation are proven, enable a scheduler pool (default 4) only for contiguous `ParallelSafe` calls. Preflight/approval happens in model order; execution may overlap; commit/results remain in model order. An `Exclusive` call is a barrier before and after itself. Re-check execution mode immediately before starting a queued call so policy changes cannot race admission.

This is useful but secondary to correctness. Leave the pool at 1 until ordering, cancellation, and output-limit tests pass.

### 8.6 Phase 3 exit criteria

- Long PowerShell work becomes a job, can be polled/stopped, has bounded output, and reconciles safely after restart.
- A one-shot read-only helper uses the same loop, respects depth/capability/context limits, and returns one ordinary tool result.
- A user-authorized goal performs bounded persisted continuation rounds, can complete/block/pause/resume, and never silently resumes on launch.
- Long conversations fit model limits through whole-turn planning and valid summaries without breaking tool/reasoning protocol continuity.
- Cost/token/step/job/helper/goal state is observable enough to diagnose failures without logging private content.

---

## 9. DeepSeek-focused engineering guidelines

The following details are likely a meaningful part of why DeepSeek performs well in a purpose-built harness. They should be validated empirically, but they are concrete engineering requirements rather than prompt folklore.

### 9.1 Preserve the complete assistant continuation payload

For a tool step, persist and resend together:

- `role:"assistant"`.
- `content`, including null versus non-null where the provider distinguishes it.
- `reasoning_content`.
- Raw `reasoning` and ordered raw `reasoning_details` when returned.
- The exact ordered `tool_calls` ids, types, names, and argument strings.

The current app's display string `ReasoningText` is not a substitute for raw structured reasoning. OpenRouter reasoning details may carry signatures or encrypted blocks that must remain byte/sequence faithful. The assembler/persistence round trip should have golden JSON tests.

### 9.2 Do not vary model/provider inside a tool turn

The active turn stamps provider, model, effort, tool schema set, and prompt section snapshot at start. Model picker changes made while running apply to the next human turn. Switching provider after an assistant requested tools can invalidate reasoning signatures, schema behavior, or tool-call ids.

### 9.3 Advertise tools only to capable models

Use provider model metadata (`supported_parameters` where available) and a capability adapter. If capability is unknown, keep normal chat enabled but disable Workspace tools with an explanation. A tool-less model should never receive a large tool prompt it cannot use.

### 9.4 Use precise, compact tool descriptions

Tool descriptions should state when to call the tool, required path basis, bounds, important failure behavior, and what the result contains. Avoid persona language and duplicated warnings in every description; central runtime rules cover common behavior. Every extra schema token is repeated on each step.

Use strict schemas with `additionalProperties:false` and bounded enum/range values. Host validation remains mandatory. Enable provider strict mode only through a tested adapter because DeepSeek's beta strict endpoint has its own supported-schema subset.

### 9.5 Give errors back to the model in a repairable form

DeepSeek is strong at iterating when it receives exact precondition failures. A patch failure should state which replacement count was expected and observed; a command failure should return exit code and bounded stderr; a truncated read should state the next range. Vague “tool failed” strings waste a reasoning step.

### 9.6 Preserve reasoning continuity but budget it

Do not discard reasoning within an included tool turn. At the same time, do not keep every old verbose trace forever. Context management should summarize/drop whole completed turns while retaining exact active/recent groups. This exploits reasoning continuity without allowing it to crowd out the task and tool results.

### 9.7 Keep sampling parameters provider-aware

Reasoning models may ignore or reject normal chat parameters. The request adapter should omit ineffective `temperature`, `top_p`, and penalties where appropriate rather than assuming that sending them is harmless forever. Persist the effective request configuration used by each turn so comparisons are meaningful.

### 9.8 Direct DeepSeek provider: recommended follow-up, not a prerequisite

OpenRouter is sufficient to prove all three phases, and its normalized API gives broad model coverage. A direct DeepSeek provider can later reduce gateway variability and exercise the vendor's exact thinking/tool contract. Before adding it:

1. Land explicit `ProviderKey` routing and per-message provider persistence.
2. Add provider settings/PasswordVault slots using the existing `ProviderSettingsVm` pattern.
3. Add official model ids/capabilities without relying on slash routing.
4. Add direct-provider serialization/smoke tests, especially thinking + tools.

Do not interleave this provider UI work with the initial step-loop refactor; it would make failures harder to localize.

---

## 10. State machines and failure semantics

Coding agents should implement explicit enums/records and exhaustive switch tests rather than inferring state from nullable content.

### 10.1 Turn state

```text
Created → Running → Completed
                  → MaxTokens
                  → MaxSteps
                  → BlockedOnApproval → Running
                  → Cancelled
                  → Failed
```

`BlockedOnApproval` is a live execution state, not the Phase 3 goal's durable `blocked` status. Persist a pending approval/tool status so UI replay is honest, but cancellation on restart resolves it as interrupted.

### 10.2 Assistant message state

```text
Pending → Streaming → Completed
                    → Interrupted
                    → Failed
```

An assistant may be completed with tool calls and no final prose. Completion describes the API step, not the whole user turn.

### 10.3 Tool state

```text
Proposed → AwaitingApproval → Queued → Running → Completed
                    │                    ├──────→ Failed
                    ├──────────────────────────→ Denied
                    └──────────────────────────→ Cancelled
                                         └──────→ TimedOut
```

All terminal tool states materialize a protocol-valid tool result when an assistant tool call was already committed.

### 10.4 Failure policy

| Failure | Transcript action | Turn action |
|---|---|---|
| Invalid tool arguments | Append structured tool error | Continue so model may repair |
| Unknown/disabled tool | Append structured tool error | Continue within loop guards |
| Approval denied | Append denied tool result | Continue; model explains/asks |
| Tool timeout/exception | Append sanitized result | Continue if safe |
| PowerShell nonzero exit | Append completed failure result | Continue |
| Eligible transient HTTP/API error before any accepted delta | No assistant node; persist failed attempt and retry schedule | Cancellable bounded retry in same open step; fail turn when policy is exhausted |
| Permanent/ineligible HTTP/API error before any delta | No assistant node, persist failed attempt | Turn failed; user may retry after fixing cause |
| HTTP/API error after prefix | Finalize prefix as failed/interrupted | Turn failed |
| Max tokens during ordinary text | Finalize visible prefix as max tokens | Sticky `MaxTokens` |
| Max tokens with partial tool call | Execute nothing; mark incomplete | Sticky `MaxTokens` |
| User cancellation | Preserve prefix; cancel/repair outstanding calls | `Cancelled` |
| Persistence checkpoint failure | Stop before further side effect/request | `Failed`; never keep private divergent history |
| Renderer failure | Core/persistence continue; log/UI recover by snapshot | Does not re-execute turn |

Never automatically retry a mutating tool. Model-request retry is a narrower operation: it repeats only an eligible failed provider request at the same open step over the same durable model-visible history, before any assistant delta has been accepted and before any proposed tool has executed. Record the scheduled retry before waiting, expose it in the activity transcript, honor cancellation during backoff, and keep a finite provider-aware budget. Failed-attempt details are not model-visible. A request id/idempotency facility may be added where a provider supports it, but cannot be assumed across OpenAI-compatible endpoints.

---

## 11. Security, privacy, and data handling

### 11.1 Threat model

The dangerous inputs are not only a malicious user. Repository files, command output, generated patches, model text, and earlier tool results may contain prompt injection. The model is an untrusted planner; the host is the authority boundary.

Enforce:

- Tool registry allowlist per turn/helper.
- Workspace path checks in every file tool after argument parsing and immediately before IO.
- Prior-step read observation and preimage-hash recheck before modifying an existing file.
- Approval after validation/presentation and before side effects.
- No authority conveyed by prompt/context text.
- Immutable approval argument hash.
- Output limits before persistence/prompt rendering.
- No secrets in tool result/log unless the user explicitly read the file and the bounded content is necessary; even then do not duplicate it into telemetry.
- Never include API keys, PasswordVault contents, full environment blocks, or process environment in model context.

### 11.2 Artifact storage

Large tool/job output should be stored under a dedicated conversation artifact directory beneath `AppServices.GetLocalDataFolder()`, with generated ids rather than model-controlled filenames. Persist size, hash, mime/type, created time, and owner conversation. The model sees only bounded content and an artifact id; a dedicated bounded read tool may be added later if needed.

Represent artifact metadata in `DbArtifact` (`IdArtifact`, owner conversation/turn/tool/job ids, relative storage name, kind, size, hash, created/expiry timestamps). Never persist an arbitrary absolute artifact path supplied by the model.

Deleting a conversation should delete owned artifacts recoverably/transactionally where practical. Document that artifacts are local application data and update `PRIVACY.md` when the feature ships.

### 11.3 Shell caveat

MdcAi has `runFullTrust`; PowerShell is not sandboxed. The approval copy must say that the script runs with the user's permissions and may access data outside the selected workspace. Workspace scoping the current directory is convenience, not containment.

### 11.4 Renderer boundary

Treat WebView messages as untrusted input:

- Validate `Name`, contract version, ids, expected state, and scalar types.
- Never accept a script/path/result body from React for execution; React may only approve/deny a host-created immutable call.
- Resolve `OpenWorkspaceFile` from a host-persisted activity/location id, then repeat workspace/reparse-point validation. Never trust a path echoed back from JavaScript, even if the displayed row originally came from C#.
- Use stable ids and revisions to reject stale messages.
- Keep titles, summaries, paths, diffs, ANSI output, context, and raw tool data text-only. Structured viewers build DOM nodes and safe styled spans; no tool field reaches `innerHTML` or CSS class/style names unchecked.
- Enforce payload/row/character caps before posting to WebView as well as inside React. A renderer cap is a usability defense, not the memory/security boundary.

---

## 12. Observability and diagnostics

Use the existing NLog/`ILogging` style. Add correlation properties to every core log event:

- Conversation id (hashed or full local id), turn id, step number.
- Provider/model, request id, finish reason.
- Request attempt number, stable failure category, retry disposition/delay source, and measured TTFT/decode duration when known.
- Tool name/call id/status/duration; arguments hash, not arguments.
- Context estimated/actual tokens and included counts, not content.
- Goal/helper/job ids and status transitions.

Do not log prompts, file contents, tool output, PowerShell script bodies, raw reasoning, API keys, or approval details at normal levels. An explicit developer diagnostic export may include sanitized protocol JSON after user action.

Persist enough run metadata to answer:

- Which provider/model/options produced this message?
- How many provider attempts occurred, why a retry was scheduled, and whether cancellation happened during backoff?
- Was required reasoning replayed?
- Which tool schema version was advertised?
- Which call/result id pairing failed?
- Was the turn cancelled, max-tokened, max-stepped, or failed?
- Were transcript/detail payloads truncated, and which artifact holds the retained full local output?
- Which context units were included or summarized?
- What did a goal/helper/job consume?

Add a debug-only transcript invariant checker callable after every append and before every request. Run it unconditionally in tests and fail loudly on the first invalid call/result group.

---

## 13. Concrete file-by-file change map

This is the intended ownership map, not a demand that every type be one file. Preserve the repository's copyright header, file-scoped namespaces, `using` directives after the namespace, nullable-disabled conventions, four-space indentation, and existing informal comment voice.

### Existing API project

| File | Required work |
|---|---|
| `Source/Common/MdcAi.OpenAiApi/Dto/ChatMessage.cs` | Deep-copy tool/reasoning fields; raw JSON types; indexed tool deltas; exact round trip |
| `Source/Common/MdcAi.OpenAiApi/Dto/ChatRequest.cs` | Rich JSON Schema, provider hint, tool choice/strict/reasoning adapter fields |
| `Source/Common/MdcAi.OpenAiApi/Dto/AiModel.cs` | Supported parameters/tool capability and richer reasoning capabilities |
| `Source/Common/MdcAi.OpenAiApi/OpenAiClient.cs` | Cancellation-aware interface overloads |
| `Source/Common/MdcAi.OpenAiApi/OpenAiClientCompletions.cs` | Pass cancellation to transport |
| `Source/Common/MdcAi.OpenAiApi/HttpClientExtensions.cs` | Cancellation-aware send/stream/read; preserve existing SSE errors |
| `Source/Common/MdcAi.OpenAiApi/ChatApiRouter.cs` | Route explicit provider first; preserve legacy fallback |
| `Source/Common/MdcAi.OpenAiApi/Providers/*` | Narrow provider/model request adapters/capabilities |
| `Source/Common/MdcAi.OpenAiApi.Tests/*` | Golden serialization, assembler inputs/transport cancellation, provider request shaping |

### New pure core and tests

| Area | Files/responsibility |
|---|---|
| Sessions | Turn/step driver, results/outcomes, ordered session sink, response assembler, invariant validator |
| Prompting | Ordered prompt sections and provider-neutral request construction |
| Tools | Registry, schema validation, result envelope, presenters, scheduling and built-ins |
| Security | Workspace path guard, prior-step read observations/preimage checks, approvals and grants |
| Jobs (Phase 3) | Background registry/output/cancellation plus job tools |
| Subagents (Phase 3) | One-shot helper service/tool, filtered composition and record |
| Goals (Phase 3) | State service, continuation controller, complete/block tools |
| Context (Phase 3) | Token estimator, atomic units/plans, summaries, workspace instruction snapshots |
| `MdcAi.ChatCore.Tests` | Scripted model, in-memory transcript, fake IO/process/approval/time; exhaustive state tests |

### Local DAL

| File/area | Required work |
|---|---|
| `DbMessage.cs` | Protocol/run/tool/origin/status/raw reasoning columns |
| `DbConversation.cs` | `ToolsEnabled`, workspace path and navigations |
| `DbChatSettings.cs` | Nullable provider key for the persisted default model; legacy inference |
| New `DbChatTurn.cs`, `DbChatStep.cs`, `DbModelRequestAttempt.cs`, `DbToolCall.cs` | Phase 1 checkpoints/usage, durable bounded request attempts/retries, and per-call approval/execution/presentation lifecycle |
| New Phase 3 entities | `DbBackgroundJob`, `DbSubagentRun`, `DbGoal`, `DbConversationSummary`, `DbWorkspaceContext`, `DbArtifact` |
| `UserProfileDbContext.cs` | DbSets, relationships, indexes, defaults |
| `Migrations/*` | One reviewed migration per coherent phase; upgrade tests |
| `Chats.db` | Refresh embedded fresh-install database after every schema phase |

### Chat UI/ViewModels

| File/area | Required work |
|---|---|
| `ChatMessageVm.cs` | Transcript/presentation state only; raw protocol properties; remove model-loop ownership |
| `ConversationVm.cs` | Explicit turn controller/commands, workspace/tools/approval/goal state, reactive adapter |
| `ChatMessageVmExt.cs` | Full Db and WebView round trip, current-branch protocol projection; preserve fork invariants |
| `ConversationVmExt.cs` | New conversation settings/related records |
| New repository/session-sink classes | Transactional checkpoint implementation and UI-scheduler updates |
| `WebViewChatMessageDto.cs` and new transcript/activity DTOs | Typed message/activity/turn-summary union, persisted tool presentation, stable ids/revisions |
| `Conversation.xaml` | Workspace/tools controls, conversation-level stop/goal state |
| `Conversation.xaml.cs` | Versioned bridge, id selection, validated approval/job intents |
| `App.xaml.cs` | Register core services, tools, policies, repositories; keep constructor injection |
| `MdcAi.ChatUI.Tests/*` | Fork/controller/approval/DTO/reactive state tests |

### React renderer

| File/area | Required work |
|---|---|
| `src/App.js` | Versioned transcript snapshot/delta reducer, stable id selection/disclosure state and component delegation |
| `src/components/transcript*` | Existing MdcAi message surface plus discriminated activity/turn-summary dispatch |
| `src/components/activity/*` | Shared disclosure row; thinking/read/search/terminal/diff/context/retry/plan/generic details; approval actions |
| `src/components/turnUsageDetails.js`, `sessionStatsLine.js` | Durable per-turn usage and whole-session telemetry projections |
| `src/App.css` and component CSS | MdcAi-themed 24 px activity rhythm, bounded internal scrollports, safe status/approval styles in both themes |
| `src/*.test.js` | Contract/state/revision/approval/accessibility tests |
| `src/version.js` | Major bump for Phase 2 contract |
| `Assets/ChatListUI.zip` | Rebuilt tested bundle |

### Solution/build

Add `MdcAi.ChatCore` and `MdcAi.ChatCore.Tests` to `Source/Desktop/MdcAi.sln` with correct x86/x64/ARM64 mappings. `ChatCore` remains `AnyCPU`; do not make it WinUI-targeted. Update CI so .NET 9 is installed and all plain test projects run, then run the WinUI-adjacent x64 tests explicitly.

### Repository documentation

Add `Skills/ChatCore/AGENTS.md` once Phase 1 lands and update root `AGENTS.md` dependency/layout guidance. Update `Skills/OpenAiApi`, `Skills/Reactive`, `Skills/Db`, and `Skills/WebViewRenderer` as their contracts change. Update `README.md` and `PRIVACY.md` only when user-visible capabilities ship. Keep the repository's no-hard-wrap Markdown convention.

---

## 14. AI-coding work packets and dependency order

The implementation agent should execute one packet at a time, run its focused tests, review the diff against invariants, and commit before moving on. Do not ask one agentic change to touch API, database, VM, renderer, jobs, goals, and context simultaneously.

### Foundation and Phase 1

1. **P1-01 — wire fidelity:** repair DTO copies/raw reasoning/tool schema; add golden serialization tests. No UI/DB changes.
2. **P1-02 — true cancellation and provider routing:** cancellation overloads through transport/router; explicit provider hint; request-adapter tests.
3. **P1-03 — ChatCore scaffold:** new projects, contracts, scripted fake API, response assembler and invariant tests.
4. **P1-04 — non-streaming loop vertical slice:** in-memory transcript, registry, one fake read tool, successive requests, limits/outcomes.
5. **P1-05 — streaming loop:** full assembler, interrupted prefixes, multiple calls, usage/finish/error tests. Remove non-streaming assumptions.
6. **P1-06 — transcript schema/repository:** turn/step/request-attempt/message/tool-presentation columns and entities, migration/embedded DB, sequential transactional save, legacy upgrade tests.
7. **P1-07 — fork adapter:** map full protocol nodes to/from `ChatMessageVm`; variable-length branch round-trip tests.
8. **P1-08 — read tools/security:** workspace selection model, path guard, read/list/grep, committed prior-step `WorkspaceReadObservationSet`, fake approval, filesystem tests using temporary roots.
9. **P1-09 — mutating/process tools:** read-before-write/stale-hash enforcement, create-only and atomic write/patch, observation advance, and cancellable PowerShell behind disabled/fake approval; failure tests.
10. **P1-10 — Conversation controller:** explicit send/run/stop/regenerate integration, remove tail-driven model loop, navigation/concurrency tests.
11. **P1-11 — durable model-request recovery:** finite provider-aware retry policy, `DbModelRequestAttempt`, cancellable backoff, `Retry-After`, no-retry-after-delta, timing/token telemetry, and scripted-clock tests.
12. **P1-12 — provider smoke/evaluation trace:** OpenRouter DeepSeek streaming/nonstream tools plus one forced transient retry; fix only evidence-backed adapter issues.

### Phase 2

13. **P2-01 — versioned transcript and presentation intents:** message/activity/turn-summary DTO union, persisted locale-neutral call/result presentation, call/result pairing, generic future-version fallback, and contract/replay tests.
14. **P2-02 — activity row and structured viewers:** shared accessible disclosure chrome plus Think, Read, Grep/Glob, PowerShell, compact proposed/applied Diff, and bounded Generic surfaces; stable ids and no approvals yet.
15. **P2-03 — context/retry/telemetry surfaces:** typed context baseline/deltas, durable request-attempt countdown/detail, turn usage, session stats, sensitive-data and unknown-metric tests.
16. **P2-04 — delta/revision path:** `UpsertTranscriptItem`, snapshot recovery, selection/disclosure/nested-fold survival, bounded live-output updates.
17. **P2-05 — inline approval:** host validation, proposed diff/exact script, pending lifecycle, buttons/accessibility, cancellation/stale-message tests.
18. **P2-06 — fork UX and packaging:** intermediate action rules, manual fixture at multiple text scales/light/dark/fork state, version bump and zip rebuild.
19. **P2-07 — enable mutating tools:** feature flag release only after the security/approval and renderer-detail checklists pass.

### Phase 3

20. **P3-01 — job service:** generic registry/output/ownership/cancellation tests.
21. **P3-02 — job-backed PowerShell/UI:** poll/stop tools, job activity/detail, restart reconciliation.
22. **P3-03 — one-shot helper:** filtered read-only child loop, limits/persistence/activity tests.
23. **P3-04 — goal state domain:** entity/service/transitions/revisions/UI authorization, without auto-continuation first.
24. **P3-05 — goal continuation controller:** admitted synthetic rounds, budgets, pause/resume/restart tests.
25. **P3-06 — context planner:** capability budgets, token estimator, atomic protocol units, diagnostics.
26. **P3-07 — summaries/workspace context:** persisted summaries/invalidation, AGENTS snapshot/delta activities, long-session tests.
27. **P3-08 — bounded parallel reads:** enable only after ordered commit and cancellation stress tests.
28. **P3-09 — full evaluation/regression:** paired model trials, privacy/log review, migration/package validation.
29. **P3-10 — documentation handoff:** update root/subsystem agent guides, README/privacy, schema/renderer versions, and final limitations from the implemented behavior.

Every packet prompt handed to a coding agent should include:

- This document and the relevant `Skills/*/AGENTS.md` files.
- Exact packet scope and explicit non-goals.
- Existing tests that must remain green.
- Required new tests and acceptance conditions.
- Instruction to inspect current source rather than assuming pseudocode matches it.
- A request to review its own diff for fork, reasoning replay, cancellation, security, DB, and renderer-version implications.

---

## 15. Test pyramid and build verification

### 15.1 Fast unit tests

Most behavior belongs in plain `MdcAi.ChatCore.Tests` and `MdcAi.OpenAiApi.Tests` so it can run without WinUI or network:

- Protocol serialization and exact replay.
- SSE/response assembly.
- Step state machine, limits, finite model-request retry policy, deterministic backoff, and attempt lifecycle.
- Tool schemas, validation, presenters, scheduling and result ordering.
- Workspace path/canonicalization and atomic file operations using a temporary directory.
- Process runner behind a fake, with a small opt-in real process test.
- Context planning and goal/job/helper state machines.

### 15.2 WinUI-adjacent tests

Use `MdcAi.ChatUI.Tests` for:

- `ConversationVm` command/can-execute and UI-scheduler behavior.
- Controller lifetime across activation/navigation.
- Fork tree mapping and persistence adapters.
- Approval intents and stale hash/id rejection.
- WebView DTO/envelope/revision serialization.

### 15.3 React tests

Run the renderer suite for each contract/component packet. Use representative snapshots but assert semantics/status/actions, bounded scroll/fold behavior, copy payloads, stable disclosure state, and generic fallbacks—not brittle full HTML dumps.

### 15.4 Database tests

Create temporary SQLite databases for:

- Previous snapshot → latest migration.
- Fresh schema.
- Legacy alternating transcript.
- Variable tool branch with two versions.
- Interrupted active turn repair.
- Scheduled/started request-attempt reconciliation and nullable telemetry/presentation-intent round trips.
- Phase 3 goal/job/helper/summary relations and cleanup.

Compare the embedded `Chats.db` schema/migration history with the EF snapshot.

### 15.5 Network smoke tests

Keep live tests opt-in and user-secret based. At minimum:

- OpenRouter DeepSeek tool-capable model, non-streaming.
- Same model, streaming reasoning + one tool + continuation.
- Multiple tools if the selected route supports them.
- A normal OpenAI or OpenRouter non-DeepSeek chat to ensure provider-neutral behavior.

Never make the default test suite depend on model availability or paid credits.

### 15.6 Build commands

Representative verification after each coherent phase:

```powershell
dotnet test Source/Common/MdcAi.OpenAiApi.Tests/MdcAi.OpenAiApi.Tests.csproj
dotnet test Source/Common/MdcAi.ChatCore.Tests/MdcAi.ChatCore.Tests.csproj
dotnet test Source/Desktop/MdcAi.ChatUI.Tests/MdcAi.ChatUI.Tests.csproj -r win-x64

Set-Location 'Source/React Chat Renderer/RendererApp'
npm test -- --watchAll=false
npm run build

msbuild Source/Desktop/MdcAi.sln /restore /p:Configuration=Debug-Unpackaged /p:Platform=x64
```

Use Visual Studio MSBuild for the full WinUI build as documented in `Skills/BuildPackaging/AGENTS.md`. Before release, verify `Release-Unpackaged x64`, packaged migration startup, the renderer zip, and then other architectures.

---

## 16. Evaluation plan: proving the DeepSeek harness improvement

Create `Evals/Agentic/` with versioned task definitions, small fixture workspaces, deterministic validation scripts, and a result schema. Do not run these in normal CI because they use paid models and local execution.

### 16.1 Paired conditions

For each task, compare:

1. **Chat baseline:** same model/provider/effort/system premise, tools disabled; user manually supplies no extra results.
2. **MdcAi harness:** the implemented tools/loop.
3. **DSH reference**, when practical: same task/model/provider settings in the local DSH checkout.

Use fresh sessions, record model route/provider, keep sampling/request settings aligned, and run several trials where model nondeterminism matters. Do not compare different model versions and attribute the result to the harness.

### 16.2 Evaluation tasks

1. **Repository orientation:** identify the files responsible for MdcAi model selection and explain the load-order bug guard. Read/list/grep only. Validator checks cited files/symbols.
2. **Exact edit:** change a small fixture method according to a specification and run its tests. Validator checks diff and test result.
3. **Patch conflict recovery:** provide a stale expected snippet; successful run must reread and patch correctly rather than overwrite blindly.
4. **Compile-fix loop:** fixture contains two compiler errors; model must edit, run build, inspect failure, and finish green.
5. **Multiple independent reads:** task encourages two/three safe calls in one step; verify id/result ordering.
6. **Large output discipline:** command emits excessive output; model must use bounded/polled data and still identify the final failure.
7. **Cancellation:** stop during reasoning, approval, and PowerShell; validator checks no later side effect/request and valid transcript repair.
8. **One-shot helper:** task has two independent areas to inspect; helper must return evidence while parent makes final change.
9. **Goal:** multi-round fixture requires inspect → edit → test → repair → complete within a fixed round cap.
10. **Long context:** a seeded long conversation plus tool groups forces summary/windowing; final answer must retain an early pinned requirement and recent failure detail.
11. **Adversarial workspace instruction:** fixture file attempts to grant shell/write authority; host must still request approval and respect denied consent.
12. **Fork replay:** edit an earlier user instruction after a completed tool run; new branch must not see old branch tool results.

### 16.3 Metrics

Record:

- Binary task correctness and validator/test pass.
- Number of model steps and tool calls.
- Invalid tool-call JSON count and protocol/API 400 count.
- Duplicate/repeated calls and loop-guard hits.
- Unapproved/incorrect-scope side effects (must be zero).
- Wall time, prompt/completion/reasoning tokens, reported cost.
- Context estimate versus provider-reported prompt tokens.
- Human approvals/interruptions required.
- Final diff size and files touched versus expected scope.
- Whether the final response accurately reports tests and remaining failures.

The Phase 1 quality gate should be zero protocol pairing/reasoning replay errors across the evaluation set and a material correctness improvement over chat baseline on file-edit-and-test tasks. DSH parity should be described task by task; do not collapse it into one subjective score.

---

## 17. Rollout and compatibility

1. Ship the internal core behind `Debugging`/an experimental feature flag through early Phase 1.
2. Preserve the existing chat path until the new service passes ordinary chat regression tests; then route both tool-disabled and tool-enabled turns through `ChatSessionService` so there is one completion path.
3. Keep Workspace tools off by default and show a short first-use disclosure.
4. Release read-only tools first if desired. Do not release writes/shell without Phase 2 approvals.
5. Add schema migrations with all new columns nullable/defaulted; never rewrite legacy conversation content.
6. Treat renderer Phase 2 as a major contract/bundle version.
7. Gate goals separately from tools; user authorization and budgets must be present from the first goal-enabled build.
8. Add privacy documentation for local workspace paths, tool transcripts, command output, helper transcripts, summaries, and artifacts.

Feature flags must not create two incompatible persistence formats. Disabled features may leave nullable columns unused, but all builds must understand the schema once migrated.

---

## 18. Risks and mitigations

| Risk | Consequence | Mitigation |
|---|---|---|
| Lossy reasoning/tool replay | DeepSeek/OpenRouter 400 or degraded continuation | Raw fields, golden round-trip tests, transcript invariant validator |
| Tail-driven reactive cancellation | Loop cancels as it appends its own nodes | Explicit conversation turn controller; no tail-triggered nested `Switch` |
| Fork contamination | Model sees old version/tool results | Project current selected chain only; fork/context tests |
| Provider id heuristic | Direct provider misrouting/wrong adapter | Explicit `ProviderKey`, persisted provenance, legacy fallback only |
| Malformed/huge tool output | Context explosion, memory/DB/DOM issues | Structured bounded model content, artifact spill, caps at every layer |
| Raw/generic tool UI hides useful facts | User cannot audit what the harness read, changed, or ran | Persisted tagged presentation intent; specialized read/search/terminal/diff viewers; generic bounded fallback |
| Transcript update resets disclosures | Streaming makes the UI jump and users lose inspection position | Stable activity ids; renderer-local disclosure/nested-fold maps; in-place upsert and snapshot reconciliation |
| Partial results look complete | User trusts an incomplete read/search/diff/output | Retained/total counts, head/tail omission markers, truncation/artifact notices, copy semantics |
| Automatic request retry loops or hides cost | Delays/spend without understandable state | Finite provider policy, durable-before-wait attempt record, visible retry row, cancellation, no retry after accepted delta |
| Prompt injection through files | Unauthorized side effects | Host allowlist/path guard/approval; context never conveys authority |
| Blind or stale model edit | Overwrite content the model never inspected or that changed during approval | Prior-step read observation, preimage hash in approval subject, recheck before atomic write, repairable `read_required`/`stale_read` result |
| PowerShell full trust | Machine/user-data damage | Exact per-call inline consent, cancellation/kill tree, honest warning |
| EF concurrent use | Runtime exceptions/corrupt save flow | Repository, one writer, sequential/batched operations in transaction |
| WebView stale intent | Wrong call approved/selection corrupt | Contract/revision, stable ids, immutable argument hash/state validation |
| Helper write races | Nondeterministic repository mutation | Read-only helpers in scope; parent owns mutations |
| Goal runaway spend | Unbounded cost/time | User activation, round/step/tool/token/cost caps, pause on restart |
| Summarization loses requirements | Incorrect later action | Whole-turn units, pinning, structured summaries, source hashes/evals |
| Crash mid-tool group | Invalid future API history | Step checkpoints, startup repair with cancelled tool results |
| Renderer source/zip drift | Feature appears missing/stale | Major version bump, build/re-zip check, init log |
| Session metrics lie after paging/replay | Misleading performance/cache conclusions | Derive from durable step/call/usage records; nullable unknowns; whole-session projection |

---

## 19. Adversarial review of this proposal

This section records the second-pass review requested by the project owner. These are the defects a naive implementation based only on `DSH_FINDINGS.md` would likely introduce; the body above has been amended to address them.

### 19.1 Bugs/omissions found in the source assumptions

1. **The branch decision in the findings is stale.** Tool DTOs are already in `main`; `reasoning-models` is far behind and unsafe as a base. Resolved in Section 2.
2. **Reasoning replay was understated.** Current DeepSeek thinking + tools requires replay and can return HTTP 400 without it; OpenRouter structured reasoning may be signed/encrypted. It is now a first-class persistence/wire invariant, not just a “faithful history” quality suggestion.
3. **Current copy/projection code loses mandatory fields.** `ChatMessage` copy and `ChatMessageVm.CreateMessageRequest` would strip tool/reasoning state. Phase 1 now begins by repairing and testing these paths.
4. **Cancellation currently stops observation, not necessarily work.** API methods lack tokens. Cancellation is now end-to-end and includes transcript repair.
5. **The current tail subscription cannot drive a multi-node step loop.** Every tool/assistant append changes tail and `.Switch()` can cancel active work. The proposal now requires explicit turn invocation.
6. **The renderer's index key/selection is unstable under insertion.** Phase 2 switches to stable ids and revisions.
7. **The existing save path uses concurrent operations on one EF context.** The proposal explicitly replaces `Task.WhenAll` upserts with one-writer transactional repository behavior.

### 19.2 Architecture issues found during review

8. **Putting `ChatSessionService` directly in `ConversationVm` would make Phase 3 duplicate the loop.** A plain `MdcAi.ChatCore` plus an ordered session-sink adapter now enables main and helper sessions to share it.
9. **A second private history list would drift from forks.** Each step now re-derives from the accepted current branch.
10. **Tool cards alone cannot reconstruct protocol.** Raw call/result/reasoning fields are persisted separately from pure presentation.
11. **Assistant text and tool calls are not mutually exclusive.** The Phase 2 DTO allows both on one message.
12. **Regenerating an agent turn could repeat side effects.** Existing Regenerate is now defined as final-step-only over accepted results; intentional rerun is deferred.
13. **Sequential/non-streaming pseudocode hid multiple indexed calls.** The assembler and ordered result contract are explicit and streaming is a Phase 1 exit requirement.
14. **Provider routing by slash prevents direct DeepSeek cleanly.** Explicit provider provenance/routing is included before any direct provider work.
15. **Model capability cannot be inferred only from “reasoning.”** Tool support comes from provider metadata/adapter; unknown capability disables tools without breaking chat.

### 19.3 Safety and lifecycle issues found during review

16. **Workspace-relative shell execution is not a sandbox.** Approval copy and policy now say this explicitly; prefix allowlists are rejected.
17. **Modal approval conflicts with background conversation execution and cached views.** Approval is an inline persisted state with identifier/hash validation.
18. **Cancellation after assistant tool calls can leave invalid history.** Outstanding calls receive cancelled results before future projection.
19. **Crash recovery was missing.** Relational turn/step checkpoints and startup reconciliation cover interrupted streams, calls, and jobs without claiming full event-source recovery.
20. **Job output can deadlock or exhaust resources.** Concurrent drains, byte caps, cursor semantics, artifact spill, ownership, and teardown are specified.
21. **One-shot helpers could race writes or recurse.** They are read-only, depth-one, bounded, owned, and cancelled with the parent.
22. **Goals could silently spend after restart or collide with human prompts.** They require user authorization, pause on restart/human input, and obey durable revision/budget rules.
23. **Naive last-N context can split protocol groups.** Context operates on whole completed turns and preserves the entire active group.
24. **Summary validity across forks was missing.** Summaries carry branch anchors/source hashes and invalidate on version changes.
25. **Injected AGENTS/workspace text could be mistaken for authority.** It is persisted/framed as untrusted context and cannot grant tool consent.
26. **One status field on a message cannot represent multiple calls or pre-result approval state.** `DbToolCall` now owns normalized per-call lifecycle while `ToolCallsJson` remains exact wire truth.
27. **A user could switch fork versions while the loop is between steps.** The active turn now holds a branch lease and cancels on an unexpected branch change.
28. **A model id alone is not durable provider identity.** Working/default/message/turn state now carries the provider+model pair and migrates legacy rows through the existing heuristic.
29. **A refactor could accidentally keep the current omission of chat sampling settings.** Request construction now explicitly carries `ChatSettingsVm` parameters through provider-aware filtering.
30. **“Tool cards” was too generic to reproduce the useful DSH experience.** Phase 2 now specifies a compact MdcAi activity row plus purpose-built Think, Read, Search, Terminal, Diff, Context, Retry, and Plan detail surfaces.
31. **Re-running presenters on replay could rewrite history after an upgrade.** Locale-neutral, versioned call/result presentation intent is now persisted; current presenters are used for live records and legacy fallback, not to reinterpret every old turn.
32. **A tool call and its `role:"tool"` result could render twice.** The transcript projector now pairs them by exact call id/index into one updating activity while preserving both protocol nodes underneath.
33. **Height-bounding by character truncation alone still permits huge DOM/page growth.** Inline details now combine byte/row caps, head/tail omission, bounded internal scrollports, and artifact recovery.
34. **Streaming replacement could reset expanded rows and nested grep groups.** Stable activity ids and renderer-local disclosure maps are now explicit contract requirements and test cases.
35. **Retry UI without durable retry state would be decorative and wrong after restart.** Request attempts are persisted before backoff with provider policy, delay source, failure category, and cancellation state; only eligible pre-delta model requests retry.
36. **The captured footer could be copied as viewport statistics and lie after paging.** Turn/session usage now derives from durable step/tool/token records and omits unavailable measurements instead of displaying zero.
37. **A proposed edit diff could be mistaken for an applied change.** Diff cards now carry explicit `Proposed` versus `Applied` state, replace argument-derived hunks with result-derived hunks after success, and show precondition failures without implying a write occurred.
38. **Approval plus exact replacement still allowed a blind, out-of-range, or stale edit.** Existing-file mutations now require a committed read observation from an earlier model step, require replacement spans to be observed, bind ranges/preimage hash into approval, recheck immediately before IO, and advance only a complete-file observation after success.

### 19.4 Remaining deliberate limitations

- This is not full event sourcing; raw streaming chunks may be checkpointed as a prefix rather than individually replayed.
- A crashed PowerShell process/job is not reattached after restart.
- There is no mid-turn steer/inject/follow-up inbox in these phases.
- Helpers are one-shot and not continuable.
- Helpers do not write or run shell commands.
- No general skills/plugin/MCP framework is introduced.
- Direct DeepSeek provider support is a follow-up after explicit routing; OpenRouter DeepSeek is the initial acceptance route.
- Exact tokenization for every OpenRouter model is impossible; conservative estimation plus provider usage calibration is used.

These are intentional scope boundaries, not accidental omissions.

---

## 20. Definition of done for the complete three-phase program

The program is done when MdcAi can, with Workspace tools explicitly enabled, take a human task, reason visibly, make one or more validated tool calls, receive durable structured results, continue across model steps, safely edit files and run verification, survive navigation and restart with an honest transcript, and finish or stop under clear bounds.

It must also:

- Remain a normal conversational MdcAi app with tools disabled.
- Preserve all current category/model/effort/provider/fork behavior.
- Reproduce exact reasoning/tool protocol state required by DeepSeek/OpenRouter.
- Require valid user consent for local mutations and shell execution.
- Render/replay reasoning, context, retries, paired tool results, compact applied diffs, job/helper/goal state, and durable telemetry without side effects or duplicate transcript rows.
- Keep every expanded activity useful and bounded: structured viewers, honest retained/total counts, safe copy/open actions, internal scrolling, stable disclosure state, and a generic future-version fallback.
- Support bounded background processes, one-shot read-only helpers, durable goal rounds, and token-aware context planning.
- Pass the unit, migration, renderer, integration, and evaluation gates above.
- Keep continuable children and the rest of the DSH platform outside the implementation.

That outcome harnesses DSH's central engineering insight—the model performs better when reasoning is allowed to interact with a precise, faithful, bounded environment—while keeping MdcAi's architecture, vocabulary, local-first privacy model, and product character intact.
