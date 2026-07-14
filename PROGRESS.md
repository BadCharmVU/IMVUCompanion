# IMVU Companion — Progress Log

## Session: 2026-07-09 (done)

### Status: Working

**v3.2.1** — join greetings and chat I/O are confirmed working in the embedded WebView2 app.

### What works

- Embedded IMVU Next (WebView2, left panel)
- Login persists in `%LOCALAPPDATA%\IMVUCompanion\WebView2`
- **Send Test** — posts to chat input
- **Start Bot** — MutationObserver + 2s join poll on `.chat-stream2`
- **Join greetings** — template from `messages.json` (`joinEvent`, `{name}` placeholder)
- **!commands** — built-in command replies
- **Discover / Inspect / Get Recent** — DOM probes (fixed JS IIFE wrapping)
- Message Templates UI — edit/save greeting variations

### Key fix (v3.2 → v3.2.1)

`ExecuteScriptAsync` requires invoked expressions. Scripts that prepended `FindChatRootJs` were wrapped into invalid JS like `(function...() => {...})();`, causing silent failures (`Chat: ?`, observer never installed). Fixed with unified `JsIife()` wrapper.

### Run

```
C:\Users\serve\ansel\IMVUCompanion\bin\Release\net8.0-windows10.0.19041.0\IMVUCompanion.exe
```

Build:

```
dotnet build -C Release
```

### Key files

| File | Role |
|------|------|
| `MainWindow.xaml` | Split UI (WebView2 + bot panel) |
| `MainWindow.WebView.cs` | WebView2 init, JS helpers, chat observer, send |
| `MainWindow.xaml.cs` | Bot logic, join parsing, templates, commands |
| `messages.json` | Greeting templates + `joinEvent` |

---

## Next session: User-defined AI models UI

Planned work (not started):

1. **Settings UI** — panel to add/configure AI providers (Grok, OpenAI, etc.)
   - API key / endpoint (secure storage)
   - Model name selection
   - Enable/disable per provider
2. **Wire chat → AI** — on incoming chat (non-join, non-command), optional AI reply
3. **Prompt templates** — user-editable system prompt + room context
4. **Rate limits / cooldown** — avoid spamming chat

Deferred from earlier:

- Room moderation (boot/remove users) — IMVU Next has no Classic `library.zip` API
- External browser CDP attach — replaced by embedded WebView2