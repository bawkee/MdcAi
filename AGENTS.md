# AGENTS.md — MdcAi (MDC AI)

An orientation document for AI coding agents (and humans) working on this repository. Read this first, then open the relevant `Skills/*` doc for the subsystem you are touching. If you're about to work on this repo, read this whole file before touching anything.

> **Documentation formatting:** These markdown docs are written for rich renderers (the harness GUI, Typora, GitHub, etc.) and are **not hard-wrapped to a line width** so maintain this rule for edits or new files..

---

## What is this project?

**MDC AI** is a native **Windows desktop** GPT *chat agent* — a ChatGPT-style UI running as a WinUI 3 (Windows App SDK) app that talks directly to an LLM **Chat Completions** API. It is a **BYOK** ("bring your own key") app: the user supplies an API key and chats directly with the (stateless) API. No proxy, no intermediate service.

It is **open-source** (Apache-2.0, © 2023 Bojan Sala) and lightweight. The project is shipping version **1.0.3**, mid-lifecycle: the codebase was recently updated from .NET 6 → **.NET 9**, EF Core upgraded, and — most recently — **multi-provider support landed** (OpenAI + OpenRouter via a provider registry + router).

### Key capabilities (current v1.0.3)
- Chat with GPT models (OpenAI) or any OpenRouter-routed model (Claude, DeepSeek, Llama, Gemini, ...), **streaming** token-by-token output rendered as rich **Markdown**.
- **Custom personalities/Categories** — each conversation belongs to a *Category*; each category (and each conversation, optionally overriding) has its own model + premise (system prompt) + sampling parameters. **Model pickers are grouped by provider/author** so the OpenRouter catalog never shows as a 400-item flat list.
- **Advanced Edit / forking**: you can edit a completion, which creates a *version* of a message and *forks* the conversation tree. The app remembers the current fork (`Head` → `Tail`) and the current selected version. (This is the app's most distinctive feature — see `Skills/Reactive` and `Db`.)
- Full chat history persisted **locally** in SQLite. API keys live in the Windows **PasswordVault** per provider.
- Privacy: everything stays on disk under the local app-data folder.
- A **React renderer inside a WebView2** control renders the messages with proper Markdown, syntax highlighting, selection and copy — instead of a XAML chat list.

### Where this project is going (author's stated direction)
- **Multi-provider (done for OpenAI + OpenRouter)**: the OpenAI-compatible client + `ChatApiRouter` now routes by model id; adding another provider = one more descriptor in `Skills/OpenAiApi`. A regular ChatGPT subscription would be a second `IOpenAiApi` implementation behind the same seam.
- Vector/semantic search over conversations; multimodal (image-in/out); custom tools / function calling (tool+calls + reasoning-effort plumbing is the next phase per the willingness to extend `ChatRequest`); self-hosted local LLMs. These are all "Planned" — not implemented.

---

## Repository layout (the big picture)

```
C:\Source\MdcAi\
├─ AGENTS.md                     ← you are here
├─ README.md                     ← user-facing marketing + planned features
├─ PRIVACY.md, LICENSE
├─ global.json                   → SDK 9.0.102
├─ Skills\                       ← agent skill docs (this folder)
└─ Source\
   ├─ .editorconfig
   ├─ Common\
   │  ├─ MdcAi.OpenAiApi\        ← LLM API client library (pure .NET, no UI). OpenAI + OpenRouter via AiProviders/ChatApiRouter.
   │  ├─ MdcAi.ChatCore\         ← agentic step loop, tools, workspace security (pure .NET, no UI/EF) - see Skills/ChatCore
   │  ├─ MdcAi.OpenAiApi.Tests\  ← unit tests (no network)
   │  ├─ MdcAi.ChatCore.Tests\   ← step-loop/tool/security tests (scripted fake API, no network)
   │  ├─ MdcAi.ChatUI.LocalDal.Tests\ ← EF migration/upgrade tests
   │  └─ MdcAi.OpenAiApi.IntegrationTests\ ← live smoke tests; keys via user secrets
   ├─ React Chat Renderer\
   │  └─ RendererApp\            ← the React app that renders chat inside the WebView2
   └─ Desktop\
      ├─ MdcAi.sln               ← solution (7 app projects + 4 test projects, tests opt-in)
      ├─ MdcAi\                  ← THE WinUI3 app shell (entry point, MainWindow, RootPage)
      ├─ MdcAi.ChatUI\           ← Views + ViewModels + WebView2 host (most of the UI logic)
      ├─ MdcAi.ChatUI.Tests\     ← ViewModel unit tests (OpenAiSettingsVm, OpenRouterSettingsVm, SettingsVm, ChatSettingsVm, ConversationVm)
      ├─ MdcAi.ChatUI.LocalDal\  ← EF Core + SQLite "user profile" data access (chat lists)
      └─ MdcAi.Extensions.WinUI\ ← app-wide helper lib (services, converters, base VMs via RxUIExt)
```

**Dependency direction (important):**

```
MdcAi (exe, shell)
  └── MdcAi.ChatUI (Views/VMs/WebView)                     [net9-windows, WinUI]
        ├── MdcAi.ChatCore        ← step loop, tools, security       [plain net9]
        │     └── MdcAi.OpenAiApi (API client, common)               [plain net9]
        ├── MdcAi.Extensions.WinUI (helpers)               [net9-windows, WinUI]
        │      └── MdcAi.ChatUI.LocalDal                    [plain net9]
        └── MdcAi.OpenAiApi  (API client, common)           [plain net9]
```

- `MdcAi.ChatUI` is the **heart**: all ViewModels, all Views, WebView2 bridge, and the reactive "fork tree" data model live there. `MdcAi` is just the MSIX shell + app bootstrap.
- `MdcAi.ChatCore`, `MdcAi.OpenAiApi` and `MdcAi.ChatUI.LocalDal` are **plain .NET 9** class libraries usable outside WinUI. `MdcAi.ChatCore` never references view models or EF entities - the transcript flows through `IChatSessionSink` (adapter in `MdcAi.ChatUI/Sessions`).

---

## Solution / project facts

Solution: `Source/MdcAi.sln`. Five projects: `MdcAi`, `MdcAi.ChatUI`, `MdcAi.Extensions.WinUI` (WinUI, `net9.0-windows10.0.19041.0`) plus `MdcAi.ChatUI.LocalDal` and `MdcAi.OpenAiApi` (plain `net9.0`). Target platform min version `10.0.17763.0`. Platforms: `x86;x64;ARM64`. Assembly version `1.0.3.0`.

- **MdcAi** (`Source/Desktop/MdcAi`): `OutputType=WinExe`, `TargetFramework=net9.0-windows10.0.19041.0`, `UseWinUI=true`, `Nullable=disable`. Primary deps: `Microsoft.WindowsAppSDK`, `ReactiveUI.Fody`, `Castle.Windsor`, `NLog`, `PInvoke.User32`, `Microsoft.EntityFrameworkCore.Design`.
- **MdcAi.ChatUI**: the VMs + Views. Deps: `Markdig`, `ReactiveUI.Fody`, `ReactiveMarbles.ObservableEvents.SourceGenerator`.
- **MdcAi.ChatUI.LocalDal**: EF Core + SQLite. Deps: `Microsoft.EntityFrameworkCore.Sqlite`, `FlexLabs.EntityFrameworkCore.Upsert`, EF tools. **Ships a copy of `Chats.db` as an embedded resource.**
- **MdcAi.Extensions.WinUI**: `Castle.Windsor` (DI), `ReactiveUI.WinUI`, `CommunityToolkit.WinUI*`, `Mapster`, `RxExt`, `RxUIExt.*`, `LinqMini`, `SalaTools.Core`.

> There is a **circular-ish but intended** pairing: the biggest DI/`ViewModel` primitives the app relies on (`ViewModel`, `ActivatableViewModel`, `[Reactive]`, `[Singleton]`, `SerialReactiveCommand`) come from the **author's own NuGet packages** — `RxExt`, `RxUIExt`, `RxUIExt.Windsor`, `RxUIExt.WinUI`, `SalaTools.Core`, `LinqMini`. These live in the NuGet cache under `%USERPROFILE%\.nuget\packages\`. They are **not** in this repo; the app just references them. Understand them through the `Skills/Reactive` doc.

---

## Core frameworks & libraries (the stack)

| Concern | Library | Notes |
|---|---|---|
| UI    | **WinUI 3** / Windows App SDK `2.4.0` | `UseWinUI`, XAML views, MSIX packaging |
| Web renderer | **WebView2** (Edge) | loads the React chat renderer from `ChatListUI.zip` |
| MVVM / reactivity | **ReactiveUI** `19.5.39` + **Rx Local (System.Reactive)** `6.x` | the backbone — see `Skills/Reactive` |
| App-specific reactive toolkit | **RxExt, RxUIExt, RxUIExt.Windsor, RxUIExt.WinUI** (author's packages) | base VMs, ViewHost, activation |
| DI | **Castle Windsor** `6.0.0` | auto-registers VMs+Views; service locator `AppServices.Container` |
| Data | **EF Core 9 + SQLite** + FlexLabs `.Upsert` | see `Skills/Db` |
| API | **Newtonsoft.Json** `13.0.3`, own `HttpClient` wrapper | see `Skills/OpenAiApi` |
| Persistence helper | **Mapster** (object mapping) | VM→DbEntity / DbEntity→VM |
| Markdown → HTML | **Markdig** | done in C#, before sending to the WebView |
| Logging | **NLog 5** + `Microsoft.Extensions.Logging` | logs under app-data, one file per process+time |
| App prefs | .NET `GlobalChatSettings` (settings designer) | tiny; `ShowGettingStartedConvoTip` |

---

## High-level architecture (the mental model)

A single-flow reactive MVVM app glued by **Castle Windsor DI** and **ReactiveUI**.

```
User types in Chat View (Conversation.xaml)
   │  SendPromptCmd
   ▼
ConversationVm (MdcAi.ChatUI/ViewModels/ConversationVm.cs)
   │  builds ChatMessage linked list (fork tree) via ChatMessageVm
   │  Request→ CreateChatCompletions[Stream](req)
   ▼
MdcAi.OpenAiApi/OpenAiClient (+ RequestStreamingAsync)
   │  SSE stream of ChatResult deltas
   ▼
ConversationVm / ChatMessageVm : scan deltas into aggregate, mark .IsCompleting
   ▼  HTMLContent (Markdown.ToHtml)
   ▼  viewModel.LastMessagesRequest → PostWebMessageAsJson("SetMessages", ...)
WebView2 host (Conversation.xaml.cs)
   │  → CoreWebView2.PostWebMessageAsJson
   ▼
React app inside WebView2 (ChatListUI.zip / e.g. index.html)
   │  renders messages, posts back {Name:"Ready"|"IsScrollToBottom"|"SetSelection"|...}
   ▼
C# side consumes `WebMessageReceived`, drives selection / scrolling
```

### The fork-tree / message model (core concept — read carefully)
The chat is **not a flat list in memory**: it is a **doubly-linked list of `ChatMessageVm`** where each message points to `Previous`/`Next`, and each position can hold **multiple `Version`s** (a `ChatMessageSelectorVm`). `ConversationVm.Head` → selector → `TrackNext(...)` walks to the current `Tail`. Persisting it flattens the tree into `DbMessage` rows with `IdMessageParent`, `Version`, `IsCurrentVersion` (see `Skills/Db`). This is what powers the Edit/fork feature. **Study** `ChatMessageVm`, `ChatMessageSelectorVm`, `ChatMessageVmExt` (flatten/reconstruct), and `ConversationVm`, but note these are defined in the `Reactive` skill too.

---

## Low-level architecture & nuances you must respect

### Bootstrap / DI (`App.xaml.cs`, `AppServices.cs`)
- `App` (in `MdcAi`) is `partial : ReactiveUI`'s `Application` + `ILogging`.
- On construction: creates the local-data folder, **builds `AppServices.WindsorContainer`** (`Install()`), configures NLog, sets `RxApp.DefaultExceptionHandler`, hooks `UnhandledException`, registers the EF `UserProfileDbContext` (transient) and `UserProfileDbContextWithTrans`, runs `database.MigrateAsync()` on startup, registers all VMs + Views, and `RegisterApi()` sets up the OpenAI client + wiring on settings.
- **`AppServices`** (`MdcAi.Extensions.WinUI/AppServices.cs`) is the *service-locator antipattern* used everywhere: `AppServices.Container.Resolve<X>()`, `AppServices.GetUserProfileDb()`, `GetUserProfileDbTrans()`, `GetLocalDataFolder()`, `GetAppFile(path)`. It calls `AppServices.Install()` which **removes Castle's property-injection inspector and adds a collection resolver** — so only **constructor injection** is used, and VMs can receive collections of implementations.

### ViewModel / View naming & registration
- VMs are classes named `*Vm`.
- DI register everything with `AppServices.Container.RegisterViewModelsAndViews("MdcAi.ChatUI")` (from `RxUIExt.Windsor`). Because the DI convention matches a VM to a view, the **view classes are named after the selector pattern** `[XxxVm → XxxView]` and the partial class is the VM-named `Xxx` view. **You almost always have `[DoNotRegister] class XxxViewBase : ReactivePage<XxxVm>` as the XAML `x:Class` root and `public sealed partial class XxxView` code-behind.** This avoids the DI auto-registering base classes twice. See `Views/Conversation.xaml.cs`, `Views/RootPage.xaml.cs`.
- Views mount content through `RxUIExt.WinUI`'s `<winUi:ViewHost ViewModel="{x:Bind ...}" />`.

### Reactive: `ViewModel` vs `ActivatableViewModel`, `[Reactive]`, commands
- Base types (defined in `RxUIExt`): `ViewModel : ReactiveObject` (plain), `ActivatableViewModel : ViewModel, IActivatableViewModel` (has `Activator` + `WhenActivated` support).
- Every bindable VM property is marked `[Reactive]` (from `ReactiveUI.Fody`). The Fody weaver (`FodyWeavers.xml` → `<ReactiveUI/>`) rewrites `[Reactive]` properties into reactive backing fields that raise change notifications. In XAML, bind with `Mode=OneWay/TwoWay`.
- Commands: `ReactiveCommand<Unit, TResult>`. Created via `ReactiveCommand.CreateFromObservable(...)`, `.CreateFromTask(...)`, often with an `observeOnMainThread` scheduler and a **can-execute** observable as last argument (e.g. `this.WhenAnyValue(vm => vm.CanSendPrompt)`).
- **The entire logic is reactive chains**: `this.WhenAnyValue(vm => vm.X)`, `.Where(...)`, `.Select(...)`, `.ObserveOnMainThread()`, `.Subscribe()`. Always end a chain with `.SubscribeSafe()`.
- `RxUIExt` also provides `SerialReactiveCommand` (a command not blocked by "double-tap guards") — for commands you want to allow re-entrancy on.

> Full reactive detail → `Skills/Reactive`.

---

## The View → ViewModel map (who shows what)

| View (UI) | ViewModel | Where it's hosted |
|---|---|---|
| `RootPage` (NavigationView) | `MainVm` | root page |
| `Conversation` (chat) | `ConversationVm` | ViewHost under `ConversationsPivot` |
| `ConversationCategory` | `ConversationCategoryVm` | category editor |
| Getting-started tips pages | (static partial) | sub-navigator in Conversation |
| `Settings` | `SettingsVm` | Settings pivot |
| `OpenAISettingsPage` | `OpenAiSettingsVm` (per-provider section, base `ProviderSettingsVm`) | inside Settings |
| `OpenRouterSettingsPage` | `OpenRouterSettingsVm` (per-provider section, base `ProviderSettingsVm`) | inside Settings |
| `AboutPage`, `PrivacyInfoWindow`, `LicensesWindow` | (dialogs) | about/privacy/licenses |

Route: `MainVm`↔`ConversationsVm`↔`SettingsVm`. `MainVm` is `[Singleton]`.

---

## Build, run, package

WinUI 3 needs:

- **Visual Studio 2022** (17.x) with the **Windows App SDK / WinUI 3** workload. MsBuild (`msbuild.exe`) is the build driver, not just `dotnet build`.
- The repo builds for `x86/x64/ARM64` in four configs each: `Debug`, `Debug-Unpackaged`, `Release`, `Release-Unpackaged`. The `Packaged` flag (set from the config suffix) toggles MSIX vs unpackaged output.
- The CI (`Source/.github/workflows/dotnet-desktop.yml`) runs `Release-Unpackaged x64`.

Follow `Skills/BuildPackaging` for packing/unpackaging, signing, and where the `.pfx`/certs live.

---

## Testing / debugging conveniences
- **NLog** writes a per-process date-stamped log file in the local-appdata dir; level can be raised.
- The app has **mock mode** toggles in `Debugging` (in `MdcExtensions.WinUI/Debugging.cs`): `Debugging.Enabled`, `MockMessages`, `MockModels`, `NumberedMessages`, `AutoSendFirstMessage`, `NpmRenderer` (serve React from a local dev server on port 3000), `LogSql`. Flipping these to `true` lets you run the UI fully offline (no API).
- **ReactiveUI error dialog**: `RxApp.DefaultExceptionHandler` shows a "Something Broke 😳" ContentDialog.

---

## Guard rails / gotchas (know these before editing; expand as you learn)

- **Service locator everywhere.** Don't "fix" it into pure DI without a big refactor; match the codebase.
- **Nullable is disabled** in all projects (`<Nullable>disable</Nullable>`). Code relies on null freely.
- **WebView2 contract is stringly-typed JSON** via `WebViewRequestDto { Name, Data }`. Adding a rendered feature means adding a new `Name`. See `Skills/WebViewRenderer`.
- **Don't break the fork/DbVersion invariant** — the conversation tree <-> `DbMessage`(with Version/IsCurrentVersion/IdMessageParent) round-trip is subtle. When changing message storage, keep both sides consistent.
- **Evals/EF migrations**: the shipped `Chats.db` embedded resource must match current schema. To add a schema change, run EF migrations (see `Skills/Db`) and update the embedded db copy.
- **Name-matching between View and VM** is done by convention through DI. If you add a new VM you usually add a new matching view; if you add a VM-only (dialog), fine.
- **Comments/code style**: casual, first-person, sometimes with profanity/sarcasm ("Fuck. Me. WinUI"). Match this tone loosely; it's OK to be informal. 4-space indent, `file-scoped` namespaces, `using` directives **after** the namespace, trailing commas in multi-line object initializers, `// TODO:` comments where relevant. There's a `.editorconfig` + ReSharper conventions for alignment.

---

## Where to go next (read before working in a subsystem)

| If you will touch… | Read |
|---|---|
| the chat data model, forks, conversation tree | `Skills/Reactive` + `Skills/Db` |
| EF Core / SQLite / migrations / `Chats.db` | **`Skills/Db`** |
| the LLM API call, streaming, adding new provider/OpenRouter | **`Skills/OpenAiApi`** |
| the WebView2 React renderer / messages | **`Skills/WebViewRenderer`** |
| build it / package it / sign it / CI | **`Skills/BuildPackaging`** |
| extend any VM/View, add a dialog, bind data | **`Skills/Reactive`** |

> Each `Skills/<Sub>/AGENTS.md` is self-contained. Start here, drill down there.

