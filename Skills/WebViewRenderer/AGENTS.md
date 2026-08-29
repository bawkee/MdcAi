# Skill: WebView2 React chat renderer

How the WinUI app hosts a **React app in a WebView2** control to render chat messages (Markdown, syntax highlighting, selection, copy), and the C# ↔ JS message contract. Read this before working on anything that renders messages or touches `Conversation.xaml.cs`, `WebViewExtensions.cs`, the `WebView*Dto` files, or the `React Chat Renderer` project.

---

## What it does / why

`Conversation.xaml` contains a single **`<WebView2 x:Name="ChatWebView">`** that fills the chat area. It is deliberately **not** a XAML ListView — instead it loads a **prebuilt React bundle** and renders the conversation there. This gives full Markdown rendering, syntax highlighting, text selection, copy and scroll behavior that XAML ListViews are poor at.

The React source lives in `Source/React Chat Renderer/RendererApp/` (a **Create React App**: React 18, `@wooorm/starry-night` for syntax highlight, `Markdig` does markdown→HTML **on the C# side**, not in the browser). Its production build is zipped into **`ChatListUI.zip`** and shipped as a content asset of the `MdcAi.ChatUI` project.

## How the React app is served (not NavigateToString)

The C# host uses a **virtual HTTP server inside WebView2 via `WebResourceRequested`**:

1. `ChatWebView.CoreWebView2.Settings.IsWebMessageEnabled = true`.
2. `core.AddWebResourceRequestedFilter("http://localhost:3431/*", CoreWebView2WebResourceContext.All)`.
3. It sets `ChatWebView.Source = new Uri(@"http://localhost:3431/index.html")` (unless `Debugging.Enabled && Debugging.NpmRenderer`, in which case it points at a live dev server, e.g. `http://localhost:3000/`).
4. A `WebResourceRequested` event handler (`ProcessWebResource`) opens `ChatListUI.zip` (via `AppServices.GetAppFile("ChatListUI.zip")`), finds the entry matching the request path, maps its MIME via `WebViewExtensions.MimeTypes`, extracts it to an `InMemoryRandomAccessStream` (zip files aren't random-access) and returns `core.Environment.CreateWebResourceResponse(...)`.

So the WebView loads a locally-real URL (`http://localhost:3431/`) that is intercepted and served from the shipped zip. WebView2 caches responses, so the zip read happens only on first load.

## The message contract (`WebViewRequestDto { Name, Data }`)

**Both directions** use the same envelope: `WebViewRequestDto { string Name; object Data }` serialized with Newtonsoft.Json. There is **no schema/version field** — the `Name` discriminator is everything. (The React side defines a parallel structure.)

### C# → JS (the host sends via `CoreWebView2.PostWebMessageAsJson(...)`)
- **`SetMessages`** — `Data = WebViewSetMessagesRequestDto { Messages: WebViewChatMessageDto[] }`. Each message payload:
  ```jsonc
  {
    "Id": "...", "Role": "user|assistant",
    "Content": "<html>",      // pre-rendered HTML (from Markdig)
    "Version": 1, "VersionCount": 1, "CreatedTs": "...",
    "Model": "gpt-4o",        // model that produced the message (assistant msgs; null on user/legacy)
    "Provider": "OpenAI",     // provider display name serving that model (null when unknown)
    "Effort": "medium"        // reasoning effort that produced the message (assistant msgs; null on user/legacy/effort-less models)
  }
  ```

`Content` is set from `m.HTMLContent ?? $"<p>{m.Content}</p>"` (see `ChatMessageVmExt.GetWebViewDto`).

- **`SetSelection`** — `Data` is an **int index** into the messages array (used when the app wants to move selection in JS).
- **`HideCaret`** — signals the JS to remove the streaming typing-caret marker from the last message (the JS hides `#caret` elements after a delay).

### JS → C# (`window.chrome.webview.postMessage(obj)`, received via `CoreWebView2.WebMessageReceived`)
Consumed in `Conversation.xaml.cs` and deserialized to `WebViewRequestDto`:

- **`Ready`** — React app signals it has mounted and registered its handler. C# uses this as a "now safe to push messages" gate (`webReady`); it replays the latest `SetMessages` request.
- **`SetSelection`** — `Data` = index of clicked message → C# sets `SelectedMessage`.
- **`IsScrollToBottom`** — `Data` = bool; C# records whether the user is scrolled to bottom, to decide auto-scroll later.
- **`LogDebug` / `LogInfo` / `LogError`** — logging from the React side (message data carries `{Message, Stack, Name}`).

So if you add a new feature to the renderer, add a new `Name` value + its `Data` shape consistently to BOTH the C# (plus the `WebView*Dto` if you want a typed shape) and the React `handleMessage` dispatcher.

## Scroll behavior

- `WebViewExtensions.IsScrolledToBottom()` / `ScrollToBottom()` use `ExecuteScriptAsync`.
- On the C# side a `Subject<Unit> scrollToBottom` is throttled (500 ms) and only triggers a scroll if the user is currently scrolled down (`isScrolledDown`), and is fed from `PromptField.Events().BeforeTextChanging` when newlines are typed (because an auto-growing prompt reflows the WebView and up-scrolls).

## The React side (how it renders)

Key files in `Source/React Chat Renderer/RendererApp/src/`:

- `index.js` — reads `prefers-color-scheme`, sets `data-theme`, renders `<App>`.
- `App.js` — the app shell; keeps message `data` (from `SetMessages`), `selectedChat` index, and registers the `window.chrome.webview` `message` listener; posts back `Ready`, `SetSelection`, `IsScrollToBottom`; renders `.chat-list` rows. Author labels come from `messageMeta.js`: `user` → "You", `assistant` → "`Provider · Model`" (falls back to plain "Assistant"/"System" for legacy payloads that carry no model info), plus an italic `Effort: <level>` span next to the timestamp when the message carries per-message effort (`getEffortLabel` in `messageMeta.js`).
- `messageMeta.js` — pure helpers (`getAuthorLabel` / `getRoleClass` / `getEffortLabel`) for the author label + css role class + effort label; unit-tested in `messageMeta.test.js`.
- `components/autoScroll.js` — scroll listener decides auto-scroll on/off.
- `components/highlighter.js` — the syntax-highlight pipeline: parses the incoming HTML with `DOMParser`, finds `<code class="language-*">`, uses `starryNight.flagToScope` + `starryNight.highlight` + `hast-util-to-dom`, and wraps `<pre>` blocks with a copy button.
- `components/dateTime.js` — formats `CreatedTs` with `Intl.DateTimeFormat`.
- `logging.js` — posts `{ Name: "Log<Level>" }` back to the host.
- `markdown.css` — GitHub-flavored `.markdown-body` theme with light/dark `--color-prettylights-*` variables gated on both `prefers-color-scheme` and `[data-theme="..."]`.
- `src/sample1.json` — demo data showing the `{ Messages: [...] }` payload shape.

## Thematic integration / theming

WinUI honors the OS theme (dark/light) automatically; the WebView picks up the same light/dark signal via `prefers-color-scheme`/`data-theme`. The React `markdown.css` maps PrettyLights color variables for both modes.

## Build & packaging of the React app

1. `cd Source/React Chat Renderer/RendererApp && npm install && npm run build` (CRA) → outputs hashed static files into `build/`.
2. The built `build/*` directory is **zipped into `ChatListUI.zip`**.
3. `ChatListUI.zip` is checked in at `Source/Desktop/MdcAi.ChatUI/Assets/ChatListUI.zip`, wired via `<Content Update="Assets\ChatListUI.zip" CopyToOutputDirectory=PreserveNewest>` in the `MdcAi.ChatUI.csproj`.
4. At runtime the WebView serves files out of that zip via the localhost:3431 interceptor.

> When you change the React renderer you must **rebuild + re-zip into `ChatListUI.zip`** for the change to take effect in the packaged/unpackaged app. During development, set `Debugging.NpmRenderer = true` and run `npm start` so the WebView loads from `http://localhost:3000/` instead of the zip (also add `remote-debugging-port` in DEBUG to open DevTools).

## Gotchas & guide

- The WebView content is batteries-included: all HTML/markdown comes **already rendered** from C# (Markdig). The React side **does not parse markdown** — it only highlights code inside the `<code>` tags and styles the `.markdown-body`.
- C# must guard `PostWebMessageAsJson` for the WebView being disposed/closing (see the try/catch around COMException "object has been closed" in `Conversation.xaml.cs`).
- Keep the `Name` switches on both sides in sync; there's no type-safety across the boundary.
- If you change rendering, test both light and dark theme + a case where the message content is plain (no markdown) and an incrementally-appended streaming assistant message (the caret marker).

---

Read next: `Skills/Reactive` (the source data + how `LastMessagesRequest` is built at the VM side).

