# IMVU Companion — Join Whisper Progress (paused)

**Last version:** v0.7.1 (released 2026-07-14 — icon crash fix, UI clock, session log line)
**Build:** `dotnet build C:\Users\serve\ansel\IMVUCompanion\IMVUCompanion.csproj -c Release`  
**Run:** `C:\Users\serve\ansel\IMVUCompanion\bin\Release\net8.0-windows10.0.19041.0\IMVUCompanion.exe`  
**Status:** Join greet (public) works. Proactive join whisper still fails (opens self or blocked as `target-is-bot`).

---

## Goal

When a user joins, send welcome extra message as **whisper to the joiner** (not self).  
Reply whispers via `!commands` + `icon-reply_from_whisper` **must not be changed** — they work.

---

## DOM facts (from user)

- Join row parent: `<div data-id="https://api.imvu.com/user/user-#######">` — permanent **userId**
- Two child divs: **first = avatar button** (left-click → menu → "Send a whisper"), **second = join text**
- Display name (`upperName` / `{name}`) can change; **userId is static**
- Join rows do **not** have `is-presenter` until the user speaks in chat

---

## What works (v4.4+)

- Join detection with `uid=#######` in log
- Public welcome message sends correctly
- Join row found by `data-imvu-bot-join` + `data-imvu-bot-user-id`
- Avatar left-click reports `avatar-clicked:uid=#######`

## What still fails

Last known log (v4.4):

```
Observed uid=38213697: 3AE6uCb | 3AE6uCb joined the chat
Whisper click [join-avatar] → user menu avatar-clicked:uid=38213697
Whisper [join-avatar] opened self — wrong target
Proactive whisper open: target-is-bot
```

v4.5 attempted fixes (user reports still didn't work — no new log captured):

- Visible-only whisper compose detection (avoid hidden `@bot` false positive)
- Scope `send_a_whisper` to menu matching `userId` or nearest join row
- Poll menu before target verification
- `ok-trusted` when verified `userId` + menu clicked

---

## Key files

| File | Role |
|------|------|
| `MainWindow.WebView.cs` | `ChatObserverJs`, `ProactiveWhisperJs`, join uid extract, avatar click, menu poll |
| `MainWindow.xaml.cs` | `ChatWorkItem.JoinUserId`, `HandleJoinGreetAsync`, queue |
| `MainWindow.xaml` | Window title version |

## Message format (observer → C#)

```
name\ttext\t0\tjoinRef\tuserId
```

## JS functions to know

- `extractUserIdFromWrapper` / `getJoinRowWrapper` — prefer `data-id` with `user/user-\d+`
- `clickJoinAvatarForWhisper` — left-click first-child avatar button
- `findSendAWhisperMenuItem(userId, joinRef)` — scoped menu search
- `verifyWhisperTarget` / `proactiveWhisperReady` — self-whisper guard
- Reply path: `findWhisperRow` + `.icon-reply_from_whisper` (untouched)

---

## Next things to try

1. **Capture v4.5 log** — confirm whether failure is still `target-is-bot`, `no-menu-item`, or `menu-clicked-but-unverified`
2. **Inspect menu DOM after avatar click** — DevTools: what opens? Is `data-menu-item="send_a_whisper"` inside a popup with joiner's `data-id`? Different attribute for join vs chat message?
3. **Alternative open path** — IMVU may use a profile card / slide-in, not a context menu; may need to click a different button after avatar click (not `send_a_whisper` immediately)
4. **CDP / ExecuteScript diagnostic** — dump visible menus + `data-id` after avatar click before clicking whisper
5. **Don't verify by folded name** — if compose opens with unreadable styled name, trust `userId` only and send anyway (loosen pre-send block in C#)
6. **Roster/participant list** — after join, user may appear in room list with `is-presenter` only on messages; try whisper from participant panel using `userId`
7. **Timing** — join row may need longer delay before avatar/menu is interactive

---

## Build note

Close `IMVUCompanion.exe` before build (file lock on exe).

---

## Session 2026-07-14 (done for today)

- v0.7.1 released: icon.ico startup crash fixed, clock UI, session start log line
- v0.7.0 GitHub release replaced (broken installer crashed on launch)
- Local dev: `.\scripts\Run-Dev.ps1` or `dotnet build` + `bin\Release\...\IMVUCompanion.exe`
- Next: fix proactive join whisper (`target-is-bot` / self-whisper after avatar click)

*Paused: user will continue later.*