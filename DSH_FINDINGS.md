# DSH Research Report & Adoption Plan for MDC AI

**Status:** findings for review — this document is input for `openai sol` to produce the implementation proposal we will build from.
**Date:** 2025 (initial research pass against the dsh source)
**Author:** AI coding agent (this session)

---

## 0. Source material & locations

| What | Location |
|---|---|
| **DSH (DeepSeek Harness) source repository** | `C:\Source\dsh\deepseek-harness` (git checkout; docs/ + packages/ + apps/) |
| DSH installed web-app build (bundled, not source) | `C:\Users\bojan\AppData\Local\DeepSeekHarness\app\node_modules\.pnpm\@deepseek-ai+dsh-web-app@0._3c9d7430dd94cca1a975b8b7bdd63f6b\` — only useful for the running GUI; **the source checkout is the authoritative reference** |
| MDC AI repository | `C:\Source\MdcAi` (this repo) |
| MDC AI tool-call DTO groundwork | branch `reasoning-models` — `ChatRequest.Tools`, `ChatTool`/`FunctionTool`, `ChatMessage.ToolCalls`/`ToolCallId` already exist there |

Primary dsh references read for this report: `docs/architecture.md` (turn flow), `docs/subsystems/{core,session,tools,subagent,goal,jobs,conversation}.md`, `packages/core/agent-loop/src/agent.ts` (the driver), `packages/core/agent-loop/src/tool-calls.ts` (tool scheduling), `packages/core/session/src/surface.ts` (history derivation), `packages/core/tools/src/*` (tool pipeline), `packages/subagent/subagent/src/*` (child agents).

---

## 1. Objective

Make MDC AI a genuinely agentic desktop assistant on the same pattern as dsh: one conversation that can read/write files, run PowerShell, iterate on code, and (later) delegate to child agents and run autonomously toward a goal — while staying a small BYOK WinUI app. We adopt dsh's **pattern, not its platform**. We explicitly do **not** attempt a "full dsh replica" (sandbox providers, permission-preset system, LSP/MCP/ACP seams, webhooks, schedules, multi-agent teams, distributed hosts, full event-sourced persistence with crash recovery).

Agreed scope: implement the three phases below (step loop + tools, transcript/UI, then the agentic extras: background jobs, one-shot subagents, goals, context management), minus the full-dsh replica, with continuable children deferred (see §4 decision points).

---

## 2. Executive summary — how dsh actually works

The question "is dsh one long conversation or a main agent talking to many small agents" has a two-layer answer:

1. **Inside one agent it IS one conversation** — but an *event-sourced* one. A `turn` is zero or more `step`s; a `step` is exactly one LLM request plus the tool calls that request made. The harness executes the tools (never the model), logs everything, and immediately issues a *new* request whose history now includes the assistant's `tool_calls` blocks and the `tool` results. The visual "agent message → tool call card → agent message → …" pattern is this loop unrolling. To the OpenAI-compatible API it is boring function calling: each step is a fresh `/chat/completions` call with a longer `messages` array. There is no magic at the wire level.

2. **Across agents, dsh does use many small agents — spawned through ordinary tool calls.** A subagent is a *second session with its own independent turn/step loop*, created by the harness when the parent model calls a `subagent(description, prompt)` tool. The child's final answer comes back to the parent as a normal `tool/result`. The parent keeps running concurrently; children can be *continuable* (parent sends follow-up `send_message` tool calls to a still-running child).

The most important transferable insight: **the session log is the source of truth; the model message history is a projection derived from it for every request** (`deriveMessages()` over the three "surface" event kinds: `user/message`, `assistant/message`, `tool/result`). MDC AI's fork/version tree is already the same philosophical bet — history as a rich structure, not a flat list. The step loop is the missing piece.

---

## 3. Findings in detail

### 3.1 The turn/step loop (source: `packages/core/agent-loop/src/agent.ts`, `docs/architecture.md`)

dsh's own definition: *"A step is one model request plus the tools it calls. A turn is zero or more steps: it opens before its first input is claimed and closes once nothing is owed."*

```
user prompt ──► Agent inbox (next-turn / next-step queues)
                    │
                    ▼
              turn/start                          ← durable session event
                    │ claim one queued message + any next-step input (steer/inject)
                    ▼
         ┌──► step/start ──────────────────────────────────────────────┐
         │       │                                                    │ loop back when:
         │       ▼                                                    │  • tool calls made work
         │    user/message (entered messages logged)                  │  • next-step input arrived
         │       │                                                    │
         │       ▼                                                    │
         │    deriveMessages() ──► LLM request                        │
         │    (provider, model, system, tools[], history)             │
         │       │                                                    │
         │       ▼                                                    │
         │    assistant/chunk*  (raw stream, logged per token)        │
         │       │                                                    │
         │       ▼                                                    │
         │    assistant/message (assembled; may carry tool_calls)     │
         │       │                                                    │
         │       ▼                                                    │
         │    tool/call* ──► pre-execute ──► execute ──► post-execute │
         │       │          (policy)          (fs, powershell, …)     │
         │       ▼                                                    │
         │    tool/result*  ──────────────────────────────────────────┘
         │
         │  no tool_calls left AND no next-step input → nothing owed
         ▼
     agent/turn-stopping ──► turn/end            ← durable event
```

Mechanics observed in the source:

- **One step = one model call + its tool executions.** After streaming, the driver filters `tool-call` content blocks. None → the step and turn complete. Some → `executeToolCalls()` runs them, appends `tool/result` events, reports "not concluded", so the turn loop opens the **next step** whose derived history contains the assistant's `tool_calls` *and* the results.
- **The inbox decides what enters the next step.** Three delivery modes on the `Agent` handle: `followup()` (a fresh turn), `steer()` (inject into the *next step* of the running turn — used to hop tool-produced context straight into the next model call), `inject()` (context that waits without waking the driver — e.g. `<system-reminder>`-framed instructions, file-change notices, skill content).
- **Cancellation and outcomes are first-class.** Turns end `completed | max-tokens | blocked | aborted | error`. `max-tokens` is sticky: once a step hits the ceiling, later steps cannot downgrade the turn outcome. Cancellation mid-stream finalizes any delivered prefix as an `interrupted` assistant message so replay stays honest.

### 3.2 Event-sourced session log (source: `packages/core/session/src/*`, `docs/subsystems/session.md`)

- The log is an **append-only sequence of typed `SessionEvent`s**: `turn/start`, `turn/end`, `step/start`, `step/end`, `user/message`, `assistant/chunk` (raw tokens — replay fidelity), `assistant/message`, `tool/call`, `tool/result`, `request/header`, `request/context`, `session/end-seed`.
- **Model-visible means logged.** dsh enforces a runtime invariant that anything reaching a model request must be reconstructable from the log. This is why new model-visible input requires a new session event type, never a side channel.
- **History is a projection.** `deriveMessages()` folds the log's *surface* (only `user/message`, `assistant/message`, `tool/result`) into the OpenAI-shaped `messages[]` array used for each request. Replay is just re-derivation. This is also what makes fork, resume, transcripts, and UI rendering all derive from one source.
- `tool/result` events carry `sourceEventSeqs` citing the `tool/call` they answer (the durable form of OpenAI's `tool_call_id` pairing).

### 3.3 Tool pipeline (source: `packages/core/tools/src/*`, `docs/subsystems/tools.md`)

A registered tool is a **`ToolDefinition`**: the model-facing `ToolSchema` (name/description/parameters — an explicit allowlist ensures callbacks never leak to the wire) plus:

- `execute(args, exec)` → pure JSON value, with a cooperative cancellation signal.
- `output`: canonical JSON Schema + a pure `render()` from validated value to model content (results are structured, then rendered — the model gets well-formed content, not raw spew).
- `presentCall(args)` / `presentResult(args, result)`: **pure, side-effect-free UI projections** — the same card you see in the dsh GUI is computed this way, and being pure it works both live and on replay.
- `timeoutMs`, `isConcurrencySafe`, `finalizeContent`, `deferContext`, `concludeTurn`.

Dispatch runs through waterfall hooks `tools/pre-execute` (allow/deny/ask policy) → monotonic guards → `tools/execute` (around-dispatch wrappers) → `tools/post-execute` → result materialization. Calls are scheduled by declared mode: `parallel` calls run concurrently in a bounded rolling pool; `exclusive` calls form ordering barriers. Results/contexts always commit in model order regardless of completion order.

### 3.4 Subagents (source: `packages/subagent/subagent/src/*`, `docs/subsystems/subagent.md`)

```
dsh process
│
├─ Agent A (session S1, own turn/step loop)
│    ├─ tool call: subagent(description, prompt) ──► harness creates Agent B
│    │                                                 (session S2, OWN loop and log,
│    │                                                  optionally seeded with a prefix
│    │                                                  of A's history, depth-capped)
│    │  B runs in background while A keeps working
│    ├─ tool call: send_message(childId, msg) ────► follow-up turn queued to B (continuable)
│    └─ B's final answer arrives as an ordinary tool/result
```

- **One-shot vs continuable.** One-shot children start and return once. Continuable children are durable child sessions with a resident activation; the parent can `send_message` (a follow-up turn) and `interrupt_agent`, and the runtime delivers a settlement notice. This is real machinery (activation manager, parent/child ownership, cold resume from persistence) — the deepest part of the platform, and the part we defer.
- **Child composition is inherited capability, not a new stack.** A child joins its parent's standing composition; tool restrictions can hide tools from a child. Provide a fresh inbox + log; add scoped persona, tool filter, output schema request.
- Parent history seeding: a child can be *fork-seeded* with a contiguous prefix of the parent's completed-turn log — this lets "review this conversation" children see context without copying.

### 3.5 Goals and background jobs (source: `docs/subsystems/goal.md`, `docs/subsystems/jobs.md`)

- **Goals:** a durable objective + round cap + revision; each continuation round is an *admitted user-message turn* attributed to the goal. The driver keeps opening turns without a human prompt while the goal is active and budget remains. (This is the mechanism behind "create_goal / resume / blocked / complete" in this harness's own tooling.)
- **Background jobs:** a long tool call (e.g. a long PowerShell run) returns a `JobId` immediately; the driver continues; the model polls with a separate tool call later; the UI renders a running progress row. The loop never blocks on wall-clock.

### 3.6 Why DeepSeek models perform noticeably better inside dsh — plausible mechanisms

We have no A/B data, so treat these as engineering hypotheses to validate during implementation, not proven facts. Each is grounded in something actually in the dsh source:

1. **Step structure suits reasoning models.** DeepSeek's chat model is instruction-tuned and produces verbose, thoughtful output; the harness gives it room by structuring work into bounded steps and *never* making it "do everything in one answer". The model can write a file, see the tool result, then reason about the next move.
2. **History stays faithful and complete.** Raw `assistant/chunk` events plus exact replay mean the model's own past outputs (including its long thinking) are fed back *exactly as produced* — no lossy re-rendering, no drift. DeepSeek's large context window is exploited rather than fought.
3. **Structured tool results, precise schemas.** Tool outputs go through the canonical-value → `render()` pipeline, so the model sees clean, well-typed content; tool schemas are assembled precisely and only model-facing fields ever leak to the wire. DeepSeek's function-calling quality gets clean material to work with.
4. **Rich injected context framing.** Context (AGENTS.md content, skill docs, file-change notices) is delivered as framed user messages (`<system-reminder>`-style), which instruction-tuned models respond to reliably — the model knows provenance of everything it reads.
5. **Adapter-aware request handling.** The harness re-logs each request header, keeps `max-tokens` sticky, respects reasoning-effort as a first-class option, and materializes adapter defaults — the small correctness details that keep a quirky provider predictable.
6. **Prompt-section assembly.** System prompt is assembled from typed sections (persona, deployment, skills, runtime) rather than one monolithic string, so structure survives as context grows.

The honest framing for the report: dsh and DeepSeek were built by the same team as a matched pair; what we can replicate cheaply is items 1–4's *shape* (step loop, faithful history, clean tool results, framed context) — which is exactly the scope in §4–5.

---

## 4. Adopted scope for MDC AI

### Phase 0 — Tool-call DTOs (assumed done separately, per plan)

Branch `reasoning-models` already carries `ChatRequest.Tools`, `ChatTool`/`FunctionTool`/`FunctionToolParams` and `ChatMessage.ToolCalls`/`ToolCallId`/role-tool support. This report assumes that lands first (merge to `main` or rebase work on top of it — see decision point D1).

### Phase 1 — The step loop + tool registry (core)

| Item | Detail |
|---|---|
| Session orchestrator | New `ChatSessionService` between `ConversationVm` and `OpenAiClient`: one loop per user message, issuing successive requests with growing history until the model stops calling tools |
| Tool registry | Scoped registry (Castle or a plain dictionary) of `ToolDefinition`s: `name`, JSON-Schema `parameters`, `execute(args, signal) → JsonValue`, optional pure `render` |
| Built-in tools (v1) | `read_file`, `write_file`, `patch_file`, `list_dir`, `grep`, `run_powershell` (spawn `pwsh.exe`, stream output back, time budget via cancellation) |
| Streaming | Accumulate tool-call deltas keyed by index (BlockAssembler analog); non-streaming fallback acceptable for v1 |
| History | Derive each request's `messages[]` from the stored conversation (fork tree → flat list), with the invariant: assistant messages containing `tool_calls` replayed verbatim (content included), each `role:"tool"` result carrying its matching `tool_call_id` |

### Phase 2 — Transcript & UI

| Item | Detail |
|---|---|
| Message model | Represent tool-call and tool-result as content nodes in the fork-tree message model so edit/fork keeps working |
| Renderer | New `Name`s in the stringly-typed `WebViewRequestDto { Name, Data }` bridge: tool-call card, collapsible tool result, running-job row; pure presenters (`presentCall`/`presentResult` style) computed on both live and replayed paths |
| Streaming feel | React side accumulates streamed text and tool-call fragments (same pattern as the existing thinking-block renderer) |

### Phase 3 — Agentic extras (everything except the full-dsh replica)

| Item | Decision | Effort estimate |
|---|---|---|
| Background jobs (long PowerShell → JobId, model polls) | **Include** | ~0.5 day |
| One-shot subagents (second session, same loop, optional parent-history seed, depth cap) | **Include** | ~0.5–1 day |
| Goals / autonomous continuation rounds | **Include** | ~1 day |
| Context management (window history: keep head + tail, summarize or pin old turns; token budget) | **Include — highest quality lever** | ~1 day |
| Continuable children (`send_message` to a running child, interrupt, cold resume) | **Deferred** (recommended skip for v1 — deepest machinery; revisit after goals land) | n/a now |
| Full dsh replica (sandbox providers, LSP/MCP/ACP seams, webhooks, schedules, workflows, multi-agent teams, full event-sourced persistence) | **Excluded by agreement** | never |

### Decision points for the reviewer to confirm

- **D1** Branch strategy: develop on `reasoning-models` (unfinished DTO work) vs. merge DTOs to `main` first and work from `main`?
- **D2** v1 streaming: implement streaming tool-call accumulation up front or ship non-streaming step loop first (render after each request) and add streaming in phase 2?
- **D3** Security model for `run_powershell`: BYOK desktop app executes real PowerShell locally — per-call confirmation dialog, allowlist of prefixes, or a "tool consent" toggle per tool? (dsh's permission presets are the platform-grade answer we deliberately do not copy wholesale; pick the smallest safe equivalent.)
- **D4** Continuable children: strictly deferred, or do we want them in the same project as a later milestone?
- **D5** Acceptance criteria: how do we know the DeepSeek experience "matches dsh"? Propose a small evaluation set (e.g. file-edit-and-test tasks with pwsh) to regression-test the loop as we build it.

---

## 5. Concrete implementation sketches (per phase)

### Phase 1 core loop (pseudocode, C#-flavored)

```csharp
// ChatSessionService — one loop per user message
var history = conversation.ForkTreeToMessages();          // system + past turns
while (true) {
    var response = await client.CreateChatCompletionsAsync(new ChatRequest {
        Messages = history, Tools = registry.Schemas(),   // model-facing only
        Stream = useStreaming,
    });
    if (response.Choice.Message.ToolCalls is not { Length: > 0 }) {
        history += response.Choice.Message;               // final answer, store + render
        break;
    }
    history += response.Choice.Message;                   // assistant WITH tool_calls, verbatim
    foreach (var call in response.Choice.Message.ToolCalls) {
        var value = await registry.Execute(call.Function.Name,
                                           JsonSerializer.Deserialize<JsonElement>(call.Function.Arguments),
                                           cancellationToken);
        history += new ChatMessage { Role = "tool", ToolCallId = call.Id,
                                     Content = Render(call.Function.Name, value) };
    }
    // loop: next request history now contains tool_calls + results
}
```

Tool abstraction (mirrors dsh's `ToolDefinition` shape, trimmed):

```csharp
public interface IChatTool {
    string Name { get; }
    JsonObject ParametersSchema { get; }                // JSON Schema for chat.tools[i].function.parameters
    Task<JsonElement> ExecuteAsync(JsonElement args, CancellationToken ct);
    // optional: string Render(JsonElement value) — structured → model content
    bool IsConcurrencySafe { get; }                     // v1: sequential loops, no parallel pool
}
```

### Realistic build sequence

1. **Phase 1a:** registry + `read_file`/`write_file`/`list_dir` + non-streaming loop behind a test hook (mock adapter) — proves the turn/step spine.
2. **Phase 1b:** `patch_file`, `grep`, `run_powershell` (process spawn + streaming + cancellation); streaming tool-delta accumulator.
3. **Phase 2:** tool-call/tool-result nodes in the fork tree, `WebViewRequestDto` names, React cards; keep `presentCall` pure.
4. **Phase 3a:** background jobs (JobId + poll tool + job row in UI).
5. **Phase 3b:** goals (continuation rounds as persisted turns with a round budget + cap).
6. **Phase 3c:** one-shot subagents (second conversation + same loop; seed with parent history prefix).
7. **Phase 3d:** context management (window head/tail + summarize-old + pinning; measure token budget per request).

Estimates assume an AI-pairing workflow; total realistic span is roughly 1.5–2 weeks of focused work, with Phase 1 delivering the 80% "dsh-feel" moment (interleaved agent/tool turns) first.

### Testing strategy

- **Unit:** registry validation (schema emission, argument parsing), loop driver against a fake OpenAI client (scripted success → tools → success), history derivation invariants (verbatim assistant-with-tool-calls replay, tool_call_id pairing).
- **Integration smoke (opt-in, user secrets like existing `IntegrationTests`):** a real-model run of "create file → read back → fix bug → pwsh verify" and assert the loop terminates.
- **Manual UI pass:** streaming, cancellation, edit/fork of a conversation containing tool turns.

---

## 6. Risks & gotchas (known before we start)

- **Streaming tool-call deltas** arrive as index-keyed fragments (`delta.tool_calls[i].function.arguments += …`); a naive accumulator breaks on multiple parallel calls. Keep the index-keyed assembler from day one if streaming v1 is chosen; otherwise sequential non-streaming calls are safe.
- **Verbatim replay of assistant messages with tool_calls** — the most common bug in hand-rolled loops: content or ordering gets stripped and the API rejects the follow-up ("tool_call_id does not match"). Our fork-tree persistence must store the whole assistant message with its block list.
- **Reasoning models + tools:** verify the chosen model reliably emits tool calls at the configured effort; some reasoning models are tool-shy at low effort. Validate during the smoke test; if needed, force tool_choice or raise effort for tool turns.
- **Local execution safety:** arbitrary `pwsh` on the user's machine is powerful; require explicit user consent per session/tool (decision D3). Never let tool output blindly enter the prompt without a renderer that trims/links (dsh caps result content too).
- **Context growth:** tool results are cheap to produce and expensive to keep; without windowing, long coding sessions blow the context. This is why Phase 3d is flagged as the highest-quality lever.
- **Cancellation:** pass cancellation through every tool call (WinUI already has good reactive cancellation; the loop must abort cleanly mid-stream and record what was delivered).

---

## 7. Open questions handed to the reviewer

1. Confirm the phase plan and build order (§5) or reorder for risk.
2. Answer decision points D1–D5 (§4).
3. Propose the concrete file-by-file change list (mdcai paths) for Phase 1 so we can start implementation immediately after review.
4. Propose the evaluation set for "DeepSeek feels as good as in dsh" (D5) — short tasks we can run against a real key as acceptance tests.

---

## 8. Reference index (dsh source paths used)

- Driver / loop: `C:\Source\dsh\deepseek-harness\packages\core\agent-loop\src\agent.ts`, `...\src\tool-calls.ts`
- Session log & derivation: `C:\Source\dsh\deepseek-harness\packages\core\session\src\index.ts`, `...\src\surface.ts`
- Tool pipeline: `C:\Source\dsh\deepseek-harness\packages\core\tools\src\{index,schema,presentation}.ts`
- Subagents: `C:\Source\dsh\deepseek-harness\packages\subagent\subagent\src\{types,continuation,descriptor}.ts`
- Docs: `C:\Source\dsh\deepseek-harness\docs\architecture.md` and `C:\Source\dsh\deepseek-harness\docs\subsystems\{core,session,tools,subagent,goal,jobs,conversation}.md`