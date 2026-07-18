# IMVU page scripts

These JavaScript files are injected into IMVU Next (WebView2). They are embedded into the app binary and also copied next to the exe for easy inspection.

When IMVU changes CSS class names or DOM structure, **edit the relevant `.js` file here** — not large C# string literals.

| File | Role |
|------|------|
| `find-chat-root.js` | Locate chat stream + input (main doc / iframes) |
| `active-chat-hook.js` | Capture IMVU `activeChat` when the room registers it |
| `chat-observer.js` | MutationObserver: joins, whispers, `!commands` → host messages |
| `collect-join-uids.js` | Seed already-greeted user ids from history |
| `proactive-whisper.js` | Silent / proactive whisper helpers (participants, send) |
| `whisper-find-row.js` | Find a whisper row for reply clicks |
| `exit-whisper-mode.js` | Close whisper bar / Escape |

C# loads them via `ImvuScripts` (`ImvuScripts.cs`).
