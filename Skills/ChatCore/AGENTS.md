# ChatCore — agentic step loop, tools, and security

The pure .NET core behind MdcAi's agentic path (DSH implementation proposal Phase 1). No WinUI, no ReactiveUI, no WebView2, no EF Core, no PasswordVault — ordinary async C# with deterministic unit tests in `Source/Common/MdcAi.ChatCore.Tests`.

Read this if you touch: the step loop, tool registry/execution, workspace security, request recovery, or anything under `Source/Common/MdcAi.ChatCore/`.

---

## What lives here

| Area | Files | Responsibility |
|---|---|---|
| Sessions | `Sessions/ChatSessionService.cs`, `ChatTurnRequest.cs`, `ChatTurnResult.cs`, `ChatSessionEvents.cs`, `ChatResponseAssembler.cs`, `ChatModelRequestRecovery.cs` | The turn/step driver, sink contract, streaming assembler, retry policy |
| Prompting | `Prompting/ChatPromptBuilder.cs` | Ordered system-prompt sections; one system message |
| Tools | `Tools/IChatTool.cs`, `ChatToolRegistry.cs`, `ChatToolScheduler.cs`, `ChatToolArgumentValidator.cs`, `Tools/BuiltIn/*` | Registry, host validation, bounded parallel scheduling, read/write/patch/PowerShell/job/delegate/goal built-ins |
| Security | `Security/WorkspacePathGuard.cs`, `WorkspaceReadObservationSet.cs`, `IChatToolApprovalService.cs` | Workspace boundary, prior-step read observations, approval seam |
| Process | `Process/ChatProcessRunner.cs`, `SystemProcessRunner.cs` | Bounded, cancellable process execution (PowerShell backend) |
| Jobs | `Jobs/*` | Background jobs: bounded output ring buffer + consuming cursor, per-conversation/app caps, ownership, shutdown |
| Helpers | `Helpers/HelperSessionService.cs` | One-shot read-only child sessions on the SAME step loop (delegate_task) |
| Goals | `Goals/*` | Durable goal state machine, budgets, exactly-once round admission, continuation controller |
| Context | `Context/ChatContextPlanner.cs`, `WorkspaceContextDiscoveryService.cs` | Token estimator + atomic context-unit planning; AGENTS.md discovery/framing |

Dependency rule: `MdcAi.ChatCore` → `MdcAi.OpenAiApi` only. It never references view models or EF entities; transcripts flow through `IChatSessionSink` (the ChatUI `ConversationChatSessionSink` is the main-conversation adapter, an in-memory sink exists for tests).

## The step loop in one paragraph

A turn is zero or more steps; a step is ONE `chat/completions` request plus all the tool calls it returned. `ChatSessionService.RunTurnAsync` derives each request from the ACCEPTED transcript branch (sink), pins one stable assistant placeholder id per step, streams into a fresh `ChatResponseAssembler`, commits the assembled message, and — if it carries tool calls — runs them through `ChatToolScheduler`, which validates, approves, executes and commits results in MODEL order. The loop stops when the model returns no tool calls or a guard (MaxSteps/MaxTokens/loop) ends the turn with an explicit outcome.

Scheduling (P3-08): contiguous `ParallelSafe` calls may execute concurrently on a bounded pool (default 4) with approval/preflight in model order and ordered commit; `Exclusive` calls are barriers before/after themselves; consecutive IDENTICAL calls are never parallelized (they belong to the loop guard). Writes/shell never run concurrently merely because the model emitted them together.

Invariants that must NEVER break (the transcript validator in tests checks these):

1. Assistant messages with `tool_calls` keep their content + reasoning + `reasoning_details` together; DeepSeek thinking + tools REQUIRES replaying prior `reasoning_content` (else HTTP 400).
2. Every `tool_call_id` has exactly one following `role:"tool"` message before the next assistant message, in model order.
3. A cancelled/incomplete tool group is repaired with deterministic cancelled/skipped results before that branch is reused.
4. A streaming prefix is marked `Interrupted` on cancel; a `finish_reason:"length"` with partial tool args executes nothing.
5. Only the CURRENT selected fork is ever model-visible.

## Safety model (read before changing tools)

- Tools are off by default per conversation; enabling requires a workspace folder.
- `WorkspacePathGuard` canonicalizes, rejects rooted/UNC/device paths and reparse-point (junction/symlink) escapes. It protects FILE tools only — PowerShell is full-trust and must stay honestly described.
- Existing-file writes/patches need a committed prior-step read observation (`WorkspaceReadObservationSet`) whose SHA-256 is rechecked immediately before the atomic write; failures return repairable `read_required` / `read_range_required` / `stale_read` / `match_conflict` results — never bytes changed.
- Approval is the `IChatToolApprovalService` seam; Phase 1 passes null (mutating tools then deny), Phase 2 wires inline approval.
- `ChatToolScheduler` wraps every expected failure in a standard JSON envelope (`{"ok":false,"status":...,"error":{"code":...,"retryable":...}}`) so the model gets predictable, repairable results instead of stack traces.

## Request recovery

`ChatRetryPolicy` (3 attempts, 500 ms → 10 s backoff), `ChatFailureClassifier` (only rate_limit/server/timeout/transport retry), fresh assembler per attempt, retry disabled after the first accepted delta, cancellation during backoff never dispatches. Attempt lifecycle is durable-before-wait via `IChatSessionSink.SetModelRequestAttemptAsync`.

## Testing

Run (all plain, no network, except the opt-in integration smokes):

```powershell
dotnet test Source/Common/MdcAi.ChatCore.Tests/MdcAi.ChatCore.Tests.csproj
dotnet test Source/Common/MdcAi.OpenAiApi.Tests/MdcAi.OpenAiApi.Tests.csproj
```

The ChatCore suite uses a scripted fake `IOpenAiApi`, in-memory sink, fake approval service, fake clock, and temporary-file roots — no key required. Live DeepSeek/OpenRouter tool smokes live in `MdcAi.OpenAiApi.IntegrationTests` (user-secrets key; early-return when absent).