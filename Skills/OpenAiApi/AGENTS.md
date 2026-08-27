# Skill: LLM API client (`MdcAi.OpenAiApi`) & multi-provider direction

How this app calls the LLM — and the **single most important area to touch for the planned
OpenRouter / provider-agnostic / ChatGPT-subscription work**. Read this before changing anything
under `Source/Common/MdcAi.OpenAiApi/` or the credential/settings flow.

---

## Project & location

- `Source/Common/MdcAi.OpenAiApi/MdcAi.OpenAiApi.csproj` — plain .NET 9 class library (no WinUI).
- Namespace `MdcAi.OpenAiApi`. Referenced by `MdcAi.ChatUI` (and transitively the app).
- Deps: `Newtonsoft.Json` 13.0.3, `SalaTools.Core` 1.0.1 (author's package → `SafeHttpClient`,
  `RelativeUri`, `ArgumentBasedMemoize`, `GetLogger()`).
- **`Nullable` disabled**, platforms `AnyCPU;x86;x64;ARM64`, net9.0.

> The package **is named/namespaced `OpenAiApi`** and everything in it is OpenAI-shaped.
> It is *currently* OpenAI-only, but the goal is to make it treat *any* OpenAI-compatible endpoint
> (OpenRouter, self-hosted OpenAI-compatible servers) — and eventually ChatGPT-Chat (non-API) too.
> Treat it as "an OpenAI-compatible chat client", NOT "OpenAI-only".

## Public surface

The main entry is the **`IOpenAiApi`** interface (in `OpenAiClient.cs`) and its default
implementation `OpenAiClient` (a `partial` class split across `OpenAiClient.cs` and
`OpenAiClientCompletions.cs`).

```csharp
public interface IOpenAiApi {
    string ApiKey { get; }
    string Organisation { get; }
    string ApiVersion { get; }
    Task<AiModel[]> GetModels();
    Task<ChatResult> CreateChatCompletions(ChatRequest request);
    IAsyncEnumerable<ChatResult> CreateChatCompletionsStream(ChatRequest request);
}
```

- Constructing `OpenAiClient(apiKey, organisation="", apiVersion="v1", client=null)`.
- `SetCredentials(apiKey, organisation)` — sets `Bearer` auth + `Api-Key` + `OpenAI-Organization`
  default headers and clears the memo cache.
- `GetModels()` memoized GET to `RelativeUri("models")`.
- The DI wiring that feeds creds lives in **`App.xaml.cs` → `RegisterApi()`**: it resolves the
  `SettingsVm`, creates an `OpenAiClient` singleton, registers it as `IOpenAiApi`, and subscribes
  to `settings.OpenAi.ApiKey`/`OrganisationName` changes to call `SetCredentials`.

## DTOs (all in `Dto\`)

| Type | Role |
|---|---|
| `ApiResult` | base: `created`, `model`, plus non-serialized `Organization`, `ProcessingTime`, `RequestId`, `OpenaiVersion`. |
| `ChatResult : ApiResult` | `id`, `Choices` (IReadOnlyList<ChatChoice>), `Usage` (ChatUsage). |
| `ChatChoice` | `Index`, `Message` (full, non-stream), `FinishReason`, `Delta` (streaming). |
| `ChatMessage` | `Role`, `Content`, `Name`, `ToolCalls`, `ToolCallId`, copy-ctor. |
| `ChatMessageRole` | smart string-value wrapper with `System/User/Assistant/Tool` statics. |
| `ChatRequest` | `Model`, `Messages`, `Temperature`, `TopP`, `n`, `Streaming` (internal set), `MaxTokens`, penalties, `LogitBias`, `User`, `Tools`. `CompiledStop` handles single-or-array `stop`. |
| `AiModel` | `ModelID`, `OwnedBy`, `Object`, `Created`, `Permission`; presets `Gpt35Turbo/Gpt4Turbo/Gpt4/Gpt4o`; `IsConversational` (id starts `gpt`) & `IsReasoning` (id starts `o1|o3`). |
| `Permissions` | model-ownership flags. |
| `ApiResult`/`ApiResult` | response metadata. |

The `Role`, `ChatMessageRole` uses a "smart-string" pattern (string wrapper with statics +
`FromString` + implicit conversions + `IEquatable`). Note `IsUser` reads `Value != "user"`
(appears inverted — handle with care; check the file before depending on it).

## Streaming

- `CreateChatCompletions` (non-stream): sets `request.Streaming=false` → wraps a `ChatResult`.
- `CreateChatCompletionsStream` (streaming): sets `request.Streaming=true` → returns an
  **`IAsyncEnumerable<ChatResult>`** where each `ChatChoice.Delta.Content` is an incremental token
  (or the leading `role` chunk).
- **Aggregation is the CALLER's job** — the library yields individual SSE chunks, not an
  aggregated message. The app aggregates them in `ChatMessageVm.CompleteCmd` (see
  `Skills/Reactive`) by scanning the deltas (`Scan("", (a,b)=>a+b)` when streaming).
- The HTTP client side: `HttpClientExtensions.RequestAsync` (non-stream) and
  `RequestStreamingAsync<T>` (SSE parse). `RequestStreamingAsync` reads header metadata once,
  streams lines, strips `data:`, `yield break`s on `[DONE]`, and deserializes each chunk.

## HTTP details & error handling (`HttpClientExtensions.cs`)

- `RequestAsync(uri, verb, postData, streaming)` serializes `postData` (null-ignored) to JSON and
  uses `HttpCompletionOption.ResponseHeadersRead` when streaming.
- On non-success it parses an `ApiError` from the response `error` object, logs it, and throws a
  typed exception:
  - `OpenAiInvalidApiKeyException` (error code `invalid_api_key`)
  - `OpenAiApiAuthException` (401)
  - `OpenAiApiQuotaException` (429 — rate limit / quota)
  - `OpenAiApiException` (500)
  - else `HttpRequestException`.
- It also reads response headers (`OpenAI-Organization`, `X-Request-ID`, `OpenAI-Processing-Ms`,
  `OpenAI-Version`) into the result metadata.

## How credentials reach the API (important)

- `AppCredsManager` (`MdcAi.ChatUI/ViewModels/AppCredsManager.cs`) — **not here** — stores the API
  key + org name in the Windows **PasswordVault** (secret), keyed off `ResourceName = "MdcAi"`, names
  `"ApiKeys"` / `"OrganisationName"`.
- `OpenAiSettingsVm` loads them on init and mirrors changes back.
- `RegisterApi()` (App) binds them to the client.

So the API key never lives in the SQLite DB — it's in the OS credential vault.

---

## ⚠️ What's OpenAI-specific / must change for OpenRouter & co. (actionable)

The following are wedded to OpenAI today and will need parameterizing to support OpenRouter, a
ChatGPT subscription (via some other gateway), or self-hosted OpenAI-compatible servers:

1. **Base URL is hard-coded** in `OpenAiClient` ctor:
   `BaseAddress = new($"https://api.openai.com/{ApiVersion}/")`.
   OpenRouter = `https://openrouter.ai/api/v1` (among others); the app has no concept of "which
   endpoint" yet. Minimal change: make the base URL settable/configurable (ctor param or a
   settings-driven property), and stop assuming `ApiVersion` + openai domain.
2. **Providers queued in the ctor / `SetCredentials`** — the headers "Bearer", "Api-Key",
   "OpenAI-Organization" are hard coded. OpenRouter accepts a `Bearer <your-openrouter-key>` with
   a different `Authorization` (and often an `HTTP-Referer`/`X-Title` for attribution). So the
   header set + auth scheme must become provider-configurable.
3. **Response metadata parsing** in `ParseHeaders` reads OpenAI-specific header names
   (`OpenAI-Organization`, `OpenAI-Processing-Ms`, `OpenAI-Version`) — harmless if absent, but not
   general.
4. **Model classification helpers** `AiModel.IsConversational`/`IsReasoning` use `StartsWith("gpt")`
   and `"o1"/"o3"`. OpenRouter exposes arbitrary model ids (e.g. `anthropic/claude-...`,
   `meta-llama/...`, `deepseek/deepseek-...`); these helpers would misclassify. Make classification a
   per-provider or per-model-string concern (a `model`→capability resolver).
5. **Preset `AiModel`** constants (`Gpt35Turbo` etc.) are OpenAI ids → replace/augment with a generic
   catalog or keep as openai defaults but make the default-model per provider.
6. **`invalid_api_key`** error code and the full exception class names/types are OpenAI-specific.
7. **SSE parsing** is mostly fine — OpenAI-compatible endpoints (incl. OpenRouter) use the same
   `data: ...`/`[DONE]` framing — so `RequestStreamingAsync` largely carries over.
8. **`ChatCompletionsUrl`** literal `"chat/completions"`; fine for OpenAI-compatible servers.

### Suggested refactor direction (minimal, risk-averse)
- Introduce a **provider descriptor** (name, base URL, auth scheme, header mapping, classify-model
  fn, default model). Have `OpenAiClient` accept it; `RegisterApi()` picks it from settings.
- Keep `IOpenAiApi` the stable seam the Views/VMs already consume — do not change its shape unless
  you rewrite the whole pipeline.
- For a **ChatGPT subscription** scenario (no API key, cookie/`chatgpt`-backend auth, no SSE, no /
  completions endpoint) the abstraction likely needs a **second implementation of `IOpenAiApi`**
  (a session/DTO-based gateway), not just endpoint config — the REST streaming + `GetModels` +
  key flows don't map 1:1. Plan it behind `IOpenAiApi` so the UI VMs don't care.

---

## Calling it from the app (reference)

```csharp
// non-streaming
var completions = await api.CreateChatCompletions(new ChatRequest {
    Messages = /* list of ChatMessage */,
    Model = /* AiModel or string */
});
var text = completions.Choices.LastOrDefault()?.Message.Content;

// streaming (in ChatMessageVm.CreateGenerationStream)
api.CreateChatCompletionsStream(req)
   .ToObservable()
   .Select(m => m.Choices.LastOrDefault()?.Delta.Content);
```

The `IOpenAiApi` instance is reached in VMs via constructor injection (e.g. `ConversationVm(IOpenAiApi api, ...)`).

---

## Debug / mock path

`ChatSettingsVm` and `ConversationVm` call `Debugging.MockModels` short-circuit (`MockModels`) and
return `MockModels` static list instead of `GetModels()` — so network isn't hit in mocked mode.

---

Read next: `Skills/Reactive` (how the request/stream is consumed) and `Skills/Db` (message storage, which
doesn't store provider).