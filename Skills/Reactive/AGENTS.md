# Skill: Reactive & MVVM (ReactiveUI / Rx / RxUIExt)

The single most important thing to understand before modifying any `ViewModel`, `View`, or reactive data model in this app. The whole app is **data-flow over Rx** on top of **ReactiveUI**, wired by **Castle Windsor** and using the **author's `RxUIExt` helper packages** for the base VM classes and view hosting.

---

## The reactive stack (what is actually on the NuGet graph)

| Package | Used for |
|---|---|
| **ReactiveUI** `19.5.39` | `ReactiveCommand`, `RaiseAndSetIfChanged`, `ReactiveObject`, `IReactiveObject` |
| **System.Reactive** (`System.Reactive.Linq`) `6.0.x` | `IObservable`, `WhenAnyValue`, LINQ to Rx, `Subject`, `CompositeDisposable` |
| **ReactiveUI.Fody** `19.5.39` | IL-weaves `[Reactive]` auto-properties into change-notifying reactive properties (via `FodyWeavers.xml` → `<ReactiveUI/>`) |
| **ReactiveMarbles.ObservableEvents.SourceGenerator** `1.3.1` | generates typed `this.Events().Xxx` Rxizer event observables from CLR/WinUI events (uses `ObservableEvents` source generator) |
| **RxExt** (author) | `RxApp.MainThreadScheduler`-style helpers, `SubscribeSafe`, memoize tools, HTTP helpers also used by `SalaTools.Core` |
| **RxUIExt** (author) | `ViewModel`, `ActivatableViewModel`, `ViewModelChangeTracker`, `SerialReactiveCommand`, `ObserveOnMainThread`/`SubscribeSafe` extension context |
| **RxUIExt.Windsor** (author) | Castle Windsor integration: `RegisterViewModelsAndViews`, `ViewHost`/`CastleServiceViewLocator`, `[Singleton]`/`[DoNotRegister]`-style attributes |
| **RxUIExt.WinUI** (author) | WinUI `ViewHost` control, `ReactivePage`/`ReactiveUserControl` for WinUI |
| **SalaTools.Core** (author) | `Logical` helpers for `ILogging`, `SafeHttpClient`(used by API lib), memoize. brings `GetLogger()` |

These packages are in the NuGet cache (not the repo). Reference locations for study: `%USERPROFILE%\.nuget\packages\{rxext,rxuiext,rxuiext.windsor,rxuiext.winui,salatools.core}`.

---

## Base classes you'll literally subclass

- **`ViewModel`** (`RxUIExt.ViewModel : ReactiveObject`) — plain bindable VM base. Has `RaiseAndSetIfChanged` from ReactiveUI, plus `TrackChanges(...)` for dirty tracking.
- **`ActivatableViewModel`** (`RxUIExt.ActivatableViewModel : ViewModel, IActivatableViewModel`) — adds an `Activator` object so a view can subscribe/dispose on activation (`viewModel.Activator.Activated` / `viewModel.WhenActivated(...)`). Use when the VM needs to know when the UI shows it.
- Views likely subclass a Reactive base over their VM, e.g. `[DoNotRegister] class ConversationBase : ReactiveUserControl<ConversationVm>` and the sealed partial `public sealed partial class Conversation : ConversationBase`.

> `MainVm`, `SettingsVm` are `[Singleton]` (Castle). Conversation, Category, previews are resolved transient per item (services within app).

---

## The `[Reactive]` attribute & binding

Mark any bindable VM property with `[Reactive]` (namespace `ReactiveUI.Fody.Helpers`):

```csharp
[Reactive] public bool IsCompleting { get; private set; }
[Reactive] public string Content { get; set; }
```

Fody rewrites these into proper `RaisePropertyChanged`-backed properties at compile time. You simply read/write them. There's no `[ObservableAsProperty]` usage here (that's a different, command-driven pattern not used in this codebase).

In XAML: `{x:Bind ViewModel.SomeProp, Mode=OneWay}` (compiled bindings) or classic `{Binding Path=..., Mode=TwoWay}`.

---

## Commands

- `ReactiveCommand<Unit, TResult>` — create with `ReactiveCommand.CreateFromObservable`, `ReactiveCommand.CreateFromTask`, or `ReactiveCommand.Create`.
- Optional can-execute stream as the **last** argument (an `IObservable<bool>`). The VM computes guard bools reactively and lets the command observe them:

```csharp
SendPromptCmd = ReactiveCommand.CreateFromObservable(
    () => Observable.Return(Unit.Default),
    this.WhenAnyValue(vm => vm.CanSendPrompt));   // enable/disable
```

- Execute from UI via `<Button Command="{x:Bind ViewModel.SaveCmd}"/>` or in code with `.InvokeCommand(vm.Cmd)` on an observable / `.Execute()`.
- Observe command state: `cmd.IsExecuting` streams a bool; `cmd.WhenExecuting()` emits on the main thread when the command starts (used heavily e.g. in ConversationVm). `cmd.ThrownExceptions` for errors — but the app also routes unhandled errors to `RxApp.DefaultExceptionHandler`.
- **Observable-returning commands** end with `.ObserveOnMainThread()` and are subscribed so GUI side-effects happen on the UI thread.

---

## How the app wires events to Rx (ObservableEvents)

Given a WinUI control `PromptField`, you can do:

```csharp
PromptField.Events().PreviewKeyDown
    .Select(...)
```

`Events()` is produced by the `ReactiveMarbles.ObservableEvents` source generator (transforms the event `X` into an `IObservable<EventPattern<XEventArgs>>`). It is used throughout the views (`Conversation.xaml.cs`, `RootPage.xaml.cs`: `NavigationViewControl.Events().BackRequested`, etc.).

---

## Key reactive idioms used everywhere

```csharp
// observe a property (recompute on change), then route to something
this.WhenAnyValue(vm => vm.Messages)
    .WhereNotNull()                 // skip null
    .Select(m => m.CreateWebViewSetMessageRequest())
    .ObserveOnMainThread()          // UI thread
    .Do(r => LastMessagesRequest = r)   // side-effect
    .SubscribeSafe();               // always terminate

// combine several observables
Observable.CombineLatest(
    this.WhenAnyValue(vm => vm.IsEmpty),
    GlobalChatSettings.Default.WhenAnyValue(s => s.ShowGettingStartedConvoTip))
    .Select(l => l.All(x => x))
    .ObserveOnMainThread()
    .Do(v => ShowGettingStartedTips = v)
    .SubscribeSafe();

// nested switches to follow a changing source (e.g. track the current Tail)
this.WhenAnyValue(vm => vm.Head)
    .Select(i => i == null ? Observable.Return((ChatMessageSelectorVm)null) : TrackNext(i))
    .Switch()                       // only the most recent inner observable
    .Do(t => Tail = t)
    .SubscribeSafe();

// throttle/debounce for save-once-idle
Observable.Merge(...)
    .Throttle(TimeSpan.FromMilliseconds(500))
    .InvokeCommand(SaveCmd);

// single-shot activation
Activator.Activated.Take(1).InvokeCommand(LoadModelsCmd);
```

- `.SubscribeSafe()` is the project's way of `Subscribe(...)` while routing exceptions through a default UNHANDLED-error sink instead of crashing. Use it for every terminal subscription.
- `.ObserveOnMainThread()` is provided by the app (`RxUIExt`) and used after the blocking work to get back to the UI thread.
- `WhenAnyValue` needs `[Reactive]` properties to actually raise change notifications; that's why everything is `[Reactive]`.

---

## Activation lifecycle (`Activator`, `WhenActivated`)

- `ActivatableViewModel.Activator` exposes `Activator.Activated` — an `IObservable<Unit>` you can subscribe to for one-time setup (e.g. `Activator.Activated.Take(1).InvokeCommand(SomeCmd)`).
- Views implement `WhenActivated((disposables, viewModel) => { ... })` where subscriptions are registered with `.DisposeWith(disposables)` so they auto-unsubscribe when the view deactivates.
- Previews / full items pair: when a `PreviewVm` becomes "the selected item", the parent `ConversationsVm` calls `Activator.Activate()`/`Deactivate()` on the activated VM (see `ConversationsVm.cs` `.PairWithPrevious()` block). This is standard reactive activation flow.

---

## Dirty tracking (`ViewModelChangeTracker` / `TrackChanges`)

`ViewModel` provides `TrackChanges(propertyNames...)` returning a `ViewModelChangeTracker` (observable + `.IsDirty()` + `.Clean()`). Used by `ChatSettingsVm` to know when settings changed:

```csharp
var changes = TrackChanges(nameof(Streaming), nameof(Temperature), /* ... */);
changes.Select(_ => changes.IsDirty()).Do(i => IsDirty = i).Subscribe();
...
changes.Clean();  // after a save/load
```

---

## The forked conversation data model (critical to preserve)

This is the most intricate reactive structure in the app. Understand it before touching anything chat-related. Files: `ChatMessageVm.cs`, `ChatMessageSelectorVm.cs`, `ChatMessageVmExt.cs`, `ConversationVm.cs`, `ConversationVmExt.cs`.

- **`ChatMessageVm`** is a node in a **doubly-linked list** (`Previous`, `Next`), each node *also* has a `Selector` (`ChatMessageSelectorVm`).
- **`ChatMessageSelectorVm`** holds **all versions** of a message at a given position. It has `Versions` (an `ObservableCollection<ChatMessageVm>`), `Message` (current selected version), `Version` (1-based), and `NextCmd`/`PrevCmd`/`DeleteCmd`.
- **`ConversationVm.Head`** is the first *selector*; `.Tail` is the last selector. The linked list is derived reactively: `TrackNext(head)` recurses through `.Next` and always emits the current `.Tail` (see ConversationVm). `.Messages` is then a flat `ObservableCollection<ChatMessageVm>` reconstructed from `Head` for rendering.
- **Editing**: `EditSelectedCmd` copies the selected message's content into `Prompt`, user edits & sends (`SendPromptCmd`), which **forks**: a new `ChatMessageVm` with a new selector is created, or replaces `EditMessage.Message` (adding a version). The flat `Messages` list and `Tail` update reactively.
- **Completion**: when the tail is a `User` role message, `ConversationVm.WhenActivated` observes the tail and creates an `Assistant` completion message with `CompleteCmd` (streaming or not) → `ChatMessageVm.CompleteCmd` runs `Conversation.Api.CreateChatCompletions[Stream]` and aggregates SSE chunks into `Content`, throttled-renders `HTMLContent`.
- **Working model lives on the conversation, not the settings**: `ConversationVm.SelectedModel` is the model in use right now (user pick or auto-derived on load); `ConversationVm.SelectedModelLabel` is its pretty picker label, recomputed from `SelectedModel + Models`. On load the working model is decided by `ConversationVm.ResolveWorkingModel` (pure, unit-tested): a deliberate user pick is never stomped; else the model of the last AI reply (per-message `ChatMessageVm.Model`); else the **loaded** category default. "Loaded" matters: before `ChatSettingsVm.IdSettings` is set, `Settings.Model` is still the ctor placeholder — caching that was the "picks the first/placeholder model on legacy chats" bug. `SelectedModel` is never used as a *source* in the decision, only as its output.
- **Working effort mirrors the working model, one-on-one**: `ConversationVm.SelectedEffort` (transient working state "low"/"medium"/"high"), `SelectEffortCmd` (user picks, `_effortUserPicked` flag), and `ResolveWorkingEffort` (pure, unit-tested) resolve exactly like model: user pick → last reply's per-message `ChatMessageVm.Effort` → category default `Settings.Effort` → `AiEffort.ClosestToMedium` ("pick the middle one"; legacy categories carry null effort). The one extra rule: the resolved value must be valid for the current model's `SupportedEfforts` (invalid → clamp to closest-to-medium), and models without effort support always resolve to null — no effort UI, `reasoning_effort` never sent (`ChatMessageVm.CreateRequest` guards on the model's `SupportedEfforts`). `ChatSettingsVm.Effort` holds only the **persisted default** and is clamped/cleared when the catalog loads, exactly like `Model`. The send-button shows `SelectedEffortLabel` ("Effort: medium") next to the model label. `ChatMessageVm.Effort` is stamped when a completion starts and round-trips to/from `DbMessage.Effort` (`ToDbMessages`/`FromDbMessages`) and to the renderer via `WebViewChatMessageDto.Effort`. The picker's behavior is traced to NLog (debug level, `app-*.log` in the local-data folder) via the app's `ILogging` extension (`this.LogDebug(...)` — `ConversationVm`/`ChatSettingsVm` implement the empty `ILogging` marker interface), which back onto the NLog `LogManager` configured in `App.xaml.cs::ConfigureNLog` — there is no `nlog.config` file, the config is created in code. The conversation view is cached **by type** in `RootPage.xaml` (`CacheType="ByType"`), so keep picker UI bound to VM properties (x:Bind), never subscriptions made in a view's `WhenActivated` — those can watch a stale VM.

> **Never break the doubly-linked `Previous/Next` + `Selector.Versions` + `Head/Tail` invariant**. Persistence flattens and rebuilds it (`ToDbMessages` / `FromDbMessages` in `ChatMessageVmExt.cs` + `ConversationVmExt.ToDbConversation`) — see `Skills/Db`. If you touch the tree, keep both the in-memory and the db projections consistent.

---

## DI: Castle Windsor + auto registration specifics

- `AppServices.Container` is the world-read ioc; access anywhere via `AppServices.Container.Resolve<X>()`.
- **Constructor injection only**; "property injection" removed in `AppServices.Install()`.
- **Collections resolution** enabled (`CollectionResolver`), so VMs can `IEnumerable<T>` of impls.
- `App` calls `RegisterViewModelsAndViews("MdcAi.ChatUI")` + registers from the caller assembly — meaning **you don't have to register VMs/Views manually**; they are discovered by an assembly scan (the base class / partial shell is marked `[DoNotRegister]` so it isn't double-registered).
- Lifetimes: `[Singleton]` on the VM marks singleton (`MainVm`, `SettingsVm`); default is transient.
- Registration of the two EF contexts is manual in `App.xaml.cs` (`UserProfileDbContext` transient with a log lambda; `UserProfileDbContextWithTrans` transient).

---

## Pitfalls & rules of thumb (expanded from experience)

- Place `WhenActivated` subscription-registration, not lingering field subscriptions, for anything that touches the visual tree.
- Prefer `.ObserveOnMainThread().Do(...).SubscribeSafe()` over manual `await`-in-subscriber.
- When building nested "follow the current thing" logic, the `.Select(...)` returning an inner Observable + `.Switch()` pattern is standard here; keep it — it both cancels the previous tracking and avoids memory leaks.
- The UI is **not** `await`-heavy; it's chain-heavy. Don't try to replace a reactive chain with a big `async void` void in a VM — match the existing style.
- Test/debug in mocked mode (`Debugging`) rather than hitting the API — see root `AGENTS.md`.

---

### Read next
- `Skills/Db` — the flattened model + how the fork tree round-trips to SQLite.
- `Skills/WebViewRenderer` — how the reactive `LastMessagesRequest` reaches the WebView.
- `Skills/OpenAiApi` — the streaming data the chains consume.

