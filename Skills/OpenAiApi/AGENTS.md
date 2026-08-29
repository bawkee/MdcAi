# Skill: LLM API client (`MdcAi.OpenAiApi`) & multi-provider support

How this app calls the LLM — and where the **OpenRouter / provider-agnostic work** lives. Read this before changing anything under `Source/Common/MdcAi.OpenAiApi/` or the credential/settings flow.

---

## Project & location

- `Source/Common/MdcAi.OpenAiApi/MdcAi.OpenAiApi.csproj` — plain .NET 9 class library (no WinUI).
- Namespace `MdcAi.OpenAiApi`. Referenced by `MdcAi.ChatUI` (and transitively the app).
- Deps: `Newtonsoft.Json` 13.0.3, `SalaTools.Core` 1.0.1 (author's package → `SafeHttpClient`, `RelativeUri`, `ArgumentBasedMemoize`, `GetLogger()`).
- **`Nullable` disabled**, platforms `AnyCPU;x86;x64;ARM64`, net9.0.

> The package **is named/namespaced `OpenAiApi`** and everything in it is OpenAI-shaped. It is an "OpenAI-compatible chat client" that talks to **any** OpenAI-compatible endpoint: OpenAI, OpenRouter, and (with a descriptor) any self-hosted server.

## Public surface

The main entry is the **`IOpenAiApi`** interface (in `OpenAiClient.cs`). The app registers one
implementation — normally the **`ChatApiRouter`** — as the singleton `IOpenAiApi`.

```csharp
public interface IOpenAiApi {
    AiProvider ActiveProvider { get; }
    IReadOnlyList<AiProvider> Providers { get; }
    bool HasCredentials(string providerKey);
    Task<AiModel[]> GetModels();          // active provider only
    Task<AiModel[]> GetAllModels();       // every provider that has a key (grouped pickers)
    Task<ChatResult> CreateChatCompletions(ChatRequest request);
    IAsyncEnumerable<ChatResult> CreateChatCompletionsStream(ChatRequest request);
}
```

- `OpenAiClient(provider, credentials, client=null)` — one OpenAI-compatible endpoint. Everything provider-specific (base url, auth/attribution headers, model classification, default model) comes from the **`AiProvider` descriptor**.
- `ChatApiRouter(credentialsProvider, clientFactory=null)` — owns one `OpenAiClient` **per provider**, lazily created. **Routing is derived from the model id**: ids containing `/` (e.g. `anthropic/claude-...`) go to OpenRouter, everything else to OpenAI. `SetActiveProvider()` picks the default, `RefreshCredentials()` drops cached clients after credential edits.
- The DI wiring feeding creds lives in **`App.xaml.cs` → `RegisterApi()`**: registers `ICredsStore` (PasswordVault), creates the `ChatApiRouter` with a per-provider credentials resolver (`ProviderCreds.Build`), subscribes to provider/key changes.

## Providers (`Providers\`)

| Type | Role |
|---|---|
| `AiProvider` | descriptor: `Key`, `DisplayName`, `BaseUrl`, `DefaultModel`, classification + grouping funcs, `ConfigureDefaultHeaders`. |
| `AiProviders` | the catalog: `OpenAi` (`https://api.openai.com/v1/`), `OpenRouter` (`https://openrouter.ai/api/v1/`), `Get(key)`, `GetProviderForModelId(id)`. |
| `AiProviderCredentials` | `ApiKey`, `Organisation`, `RefererUrl`, `AppTitle` (OpenRouter attribution). |

Adding a provider = adding one descriptor to `AiProviders.All`. The legacy OpenAI-only `OpenAiClient(apiKey, organisation, apiVersion, client)` ctor was replaced by the descriptor ctor.

## DTOs (all in `Dto\`)

| Type | Role |
|---|---|
| `ApiResult` | base: `created`, `model`, optional mid-stream `error` (`ApiError`), response metadata (`Organization`, `ProcessingTime`, `RequestId` — reads `X-Generation-Id` too, `OpenaiVersion`). |
| `ChatResult : ApiResult` | `id`, `Choices` (defaults to empty, never null), `Usage`. |
| `ChatChoice` | `Index`, `Message`, `FinishReason`, `Delta`. |
| `ChatMessage` | `Role`, `Content`, `Name`, `ToolCalls`, `ToolCallId`, copy-ctor. |
| `ChatMessageRole` | smart string wrapper (`System/User/Assistant/Tool`, implicit conversions). |
| `ChatRequest` | `Model`, `Messages`, `Temperature`, `TopP`, `n`, `Streaming` (internal set), `MaxTokens`, penalties, `LogitBias`, `User`, `Tools`, `CompiledStop`, `ReasoningEffort` (new `reasoning_effort` param on the wire, JSON-omitted when null). Unsupported params are ignored by OpenRouter, safe to send. |
| `AiModel` | `ModelID`, `OwnedBy`, plus OpenRouter extras (`Name`, `ContextLength`, `Pricing` per-token USD → per-M parse helpers, `Reasoning`). Stamped after fetch: `ProviderKey`, `GroupKey`; `IsConversational`/`IsReasoning` are **provider-aware** (fall back to id heuristics when unstamped). `SupportedEfforts` — the effort levels a model supports: OpenRouter models from their fetched `reasoning.supported_efforts` metadata (authoritative), OpenAI-provider models by id family (o1*/o3*/o4*/gpt-5* → low/medium/high), null for everything else (no effort UI, `reasoning_effort` never sent). |
| `Permissions`, `ChatUsage` | usage + OpenRouter cost (`Cost`, `IsByok`, token details). |

## Streaming

- `CreateChatCompletionsStream` returns an **`IAsyncEnumerable<ChatResult>`** of SSE deltas; aggregation is the caller's job (ChatMessageVm's `Scan("", (a,b)=>a+b)`).
- SSE parsing is hardened for OpenRouter:
  - skips `: OPENROUTER PROCESSING` comment lines + blanks,
  - **mid-stream errors** (HTTP 200 + top-level `error`, `finish_reason:"error"`) are detected per chunk and thrown as typed exceptions,
  - **final usage chunk with an empty `choices` array** parses fine (default empty choices),
  - supports `[DONE]`.

## HTTP details & error handling (`HttpClientExtensions.cs`)

- `RequestAsync<T>` / `RequestAsync` / `RequestStreamingAsync<T>` are generic over OpenAI-compatible endpoints.
- Errors are typed via `ToApiException(error, status)` matching on **both** the HTTP status and the provider error code —
  - `invalid_api_key` code → `OpenAiInvalidApiKeyException`
  - 401 / 402 / 429 → `OpenAiApiAuthException` / `OpenAiApiQuotaException`
  - 500 → `OpenAiApiException`, anything else → `OpenAiApiException`
- Logging calls are **null-safe** — the library must not crash when no logger factory exists (e.g. in unit tests).

## How credentials reach the API (important)

- `AppCredsManager` / `ICredsStore` (`MdcAi.ChatUI/ViewModels/AppCredsManager.cs`) stores per-provider secrets in the Windows **PasswordVault**, names like `"openai:ApiKey"`, `"openrouter:ApiKey"` (dsh-style key refs). `ProviderCreds` derives names, builds `AiProviderCredentials` (defaulting OpenRouter attribution for localhost + app title), and migrates the legacy `"ApiKeys"` slot.
- **There is no "current provider"** — every provider's settings section is always visible in Settings. Each is its own VM sharing `ProviderSettingsVm` (base: common `ApiKey` + save-on-change into its own vault slots):
  - `OpenAiSettingsVm` — key + organisation,
  - `OpenRouterSettingsVm` — key + attribution (`RefererUrl`, `AppTitle`),
  - `SettingsVm` exposes both (`OpenAi`, `OpenRouter`) and computes `IsAnyProviderConfigured` (the app is usable when ANY provider has a key).
- `OpenAISettingsPage` / `OpenRouterSettingsPage` are the matching Settings sections, hosted side by side by `Settings.xaml`.
- `App.xaml.cs → RegisterApi()` registers `ICredsStore`, runs the legacy-key migration, builds the `ChatApiRouter` with a per-provider credentials resolver (`ProviderCreds.Build`), and refreshes cached client credentials when any provider field changes.

---

## Testing (added with the multi-provider work)

Three test projects, all in `Source\MdcAi.sln` (not built by the default sln configs — run via `dotnet test` on each project, arch matters for the WinUI one):

1. **`Source/Common/MdcAi.OpenAiApi.Tests`** — plain net9 unit tests: provider registry/classification, DTO deserialization (incl. OpenRouter extensions), SSE parsing (comments, mid-stream errors, empty-choices usage chunk), error mapping, router routing/catalog aggregation, header configuration (via `FakeHttpMessageHandler`). ~54 tests, no network.
2. **`Source/Desktop/MdcAi.ChatUI.Tests`** — WinUI-adjacent VM tests (`net9.0-windows`, x64). Covers the per-provider settings VMs (`OpenAiSettingsVm`, `OpenRouterSettingsVm` — creds, migration, trimming), `SettingsVm` (`IsAnyProviderConfigured` across both keys), `ChatSettingsVm` (model filtering, default-model fallback), `ConversationVm` (`IsAIReady` per any-key, model stamping). In-memory fakes: `InMemoryCredsStore`, `FakeOpenAiApi`. `TestRx.Init()` pins `RxApp.MainThreadScheduler` + exception handler.
3. **`Source/Common/MdcAi.OpenAiApi.IntegrationTests`** — live-network smoke tests. Keys come from **user secrets**:
   ```
   dotnet user-secrets set "OpenRouter:ApiKey" "sk-or-..." --project "Source\Common\MdcAi.OpenAiApi.IntegrationTests\MdcAi.OpenAiApi.IntegrationTests.csproj"
   dotnet user-secrets set "OpenAI:ApiKey"      "sk-..."    --project "Source\Common\MdcAi.OpenAiApi.IntegrationTests\MdcAi.OpenAiApi.IntegrationTests.csproj"
   ```
   Tests early-return when a provider's key isn't configured, so the suite stays green with one key. OpenRouter tests hit `/models` (stamps + groups) and run real non-stream + stream completions (also exercising the `: OPENROUTER PROCESSING` + usage-chunk paths).

---

## Calling it from the app (reference)

```csharp
// non-streaming
var completions = await api.CreateChatCompletions(new ChatRequest {
    Messages = /* list of ChatMessage */,
    Model = /* model id, e.g. "anthropic/claude-3-5-sonnet" or "gpt-4o" */
});
var text = completions.Choices.LastOrDefault()?.Message.Content;

// streaming (in ChatMessageVm.CreateGenerationStream)
api.CreateChatCompletionsStream(req)
   .ToObservable()
   .Select(m => m.Choices.LastOrDefault()?.Delta.Content);
```

The `IOpenAiApi` instance is reached in VMs via constructor injection (e.g. `ConversationVm(IOpenAiApi api, ...)`). The router picks the provider from the model id, so VMs never branch on provider.

---

## Debug / mock path

`ChatSettingsVm` and `ConversationVm` call `Debugging.MockModels` short-circuit (`MockModels`) and return `MockModels` static list instead of hitting the network — so model loading is offline in mocked mode, and both providers' sections are mocked.

Read next: `Skills/Reactive` (how the request/stream is consumed) and `Skills/Db` (message storage, which doesn't store provider).

