# Skills — agent subsystem guides

Each subfolder is a self-contained, deep-dive guide an AI agent (or human) should read **before**
working in that subsystem. The root `AGENTS.md` is the orientation/onboarding document; drill down
here for detail.

## Index

| Skill | Covers | Read before you touch… |
|---|---|---|
| **[Reactive](Reactive/AGENTS.md)** | ReactiveUI / Rx / RxUIExt MVVM, `[Reactive]`, commands, activation, `ViewModelChangeTracker`, DI with Castle Windsor, the **forked conversation tree**. | Any ViewModel, View, or reactive data model; the `ConversationVm`/`ChatMessageVm` tree. |
| **[Db](Db/AGENTS.md)** | SQLite + EF Core data layer, `UserProfileDbContext`, schema, migrations, `Chats.db`, upserts/transactions, the fork-tree ↔ DB round-trip. | Anything touching `LocalDal`, migrations, or the schema. |
| **[OpenAiApi](OpenAiApi/AGENTS.md)** | The `MdcAi.OpenAiApi` LLM client, streaming, error handling, credentials → PasswordVault, and **what must change to support OpenRouter / multi-provider / ChatGPT subscription**. | Any API call, credentials flow, or the planned provider-agnostic work. |
| **[WebViewRenderer](WebViewRenderer/AGENTS.md)** | The React renderer inside WebView2, serving from `ChatListUI.zip`, and the exact C# ↔ JS `{Name, Data}` message contract. | `Conversation.xaml.cs`, `WebView*Dto`, the `React Chat Renderer` project, scrolling. |
| **[BuildPackaging](BuildPackaging/AGENTS.md)** | Building/running/packaging (MSIX + unpackaged), configs, `UNPACKAGED` symbol, signing, CI. | `.csproj`/`.sln`/`.appxmanifest`, `Pack.ps1`/`BuildUnpacked.ps1`, CI workflow. |

## How the pieces fit

The app is a **reactive MVVM WinUI 3 shell** that renders chat in a **WebView2 React viewer**, and
persists conversations to **SQLite** while calling an **LLM API**:

```
+---------------- normalized by ReactiveViewModel
|  View (WinUI XAML)  ── (ReactiveUI) ─▶ ViewModel (fork tree)
+---------------------        │
                              ├─▶ OpenAiApi (streaming LLM)
                              └─▶ LocalDal (SQLite)  ──▶ ChatList render in WebView2
```

Keep the root `AGENTS.md` (orientation + conventions) and the relevant skill open together; they
are meant to be read as a pair.