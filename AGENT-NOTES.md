# Agent self-notes (safe to delete)

Personal scratch notes from AI coding-agent sessions on this repo. Not project documentation. Delete freely.

---

## 2026-08-29 — Output artifact wrinkle (investigate later)

**What happened:** Mid-turn, while I was narrating the plan for the "premise is ignored on reasoning models" cleanup, my output stopped after emitting a stray token that looked like `<|itulo>` (the delivered text was "...dead UI state, so I'll cut it (and its unit test) too." followed immediately by the artifact and nothing else — my turn ended there). The user reports this happens **often and regardless of provider** (i.e. not tied to any particular LLM backend).

**Observations for future investigation:**
- The artifact resembles an incomplete control/marker token (reminiscent of a partial special token like `<|im_start|>` / `<|ido|>` / `<|python_tag|>` style syntax), suggesting a tokenizer/template leak rather than a semantic error.
- It occurred right at a natural "closing summary before tool calls" point — possibly an end-of-generation marker being emitted as literal text, or a truncation/max-token cutoff at a bad boundary.
- User says it happens regardless of provider — check harness/model-agnostic layers first: response de-tokenization, antml/template string handling, stop-sequence handling, or streaming chunk assembly dropping a close tag.

**Resume state at the time of the artifact (2026-08-29 ~21:57 local):**
Task in flight: remove the obsolete "premise is skipped for reasoning models" logic. Complete plan:
1. ✔ (in analysis) Identified all touchpoints:
   - `Source/Desktop/MdcAi.ChatUI/ViewModels/ChatMessageVm.cs` ~L290-310 — `isReasoning` gate skips inserting the premise System message in `CreateRequest()`.
   - `Source/Desktop/MdcAi.ChatUI/ViewModels/ChatSettingsVm.cs` L37 + L95-100 — `IsReasoningModel` flag + reactive chain (only consumer = the XAML warning banners).
   - `Source/Desktop/MdcAi.ChatUI/Views/Conversation.xaml` L451-453 and `Views/ConversationCategory.xaml` L196-198 — "⚠️ WARNING: Premise is ignored in reasoning models such as o1-* and o3-*" banners bound to `IsReasoningModel`.
   - `Source/Desktop/MdcAi.ChatUI.Tests/ChatSettingsVmTests.cs` L116-131 — test `Model_marks_reasoning_flag_from_stamped_metadata` (only remaining `IsReasoningModel` reference outside the VM/XAML).
   - Doc comments mentioning premise-skip: `Source/Common/MdcAi.OpenAiApi/Dto/AiModel.cs` L117, `Source/Common/MdcAi.OpenAiApi/Providers/AiProvider.cs` L42.
   - Keep (still meaningful): `IsReasoning`/`IsReasoningModel` classification itself (used for catalog filtering `m.IsConversational || m.IsReasoning` in `ConversationVm.cs` L280 + `ChatSettingsVm.cs` L128, effort support), `AiProvidersTests` / `AiModelTests` reasoning tests.
2. Edits: drop the gate (always insert premise), delete banner Borders, cut `IsReasoningModel` + chain + test, fix doc comments.
3. Verify: grep for leftovers (`IsReasoningModel`, "Premise is ignored", "premise is skipped"), run unit tests (`dotnet test` on `MdcAi.OpenAiApi.Tests`; try `MdcAi.ChatUI.Tests` too — may need VS/msbuild WinUI workload).