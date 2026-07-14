# IMVU Companion (Web DOM for IMVU Next - structured, no Classic format)

WPF app that launches/attaches to browser with **your profile** (simple internal launch, seamless reuse, no re-login ever), uses event-driven MutationObserver on chat-stream2 for real chat messages/joins (structured DOM extraction of speaker from user el + msg text - NO ":" / "name: text" parsing, as web does not display usernames or : in chats like Classic). Greets on joins, responds to !commands with @speaker.

All per user requirements: event only (no poll), no new files/clutter, dev tools like Inspect useful, launch inside app.

## Key points from latest update
- **No calibration buttons**. Fixed defaults + smart DOM discovery.
- **Connects to already-open Edge** (you start Edge with `--remote-debugging-port=9222` + load https://www.imvu.com/next/... chat room yourself).
- **Event driven only**: reacts to *new* DOM nodes that look like chat lines ("Name: text").
- **Strict parsing**: only lines with ":", speaker before ":", message part *starts with* "!" → reply "@speaker ..."
- **Filters aggressively**: skips radio/ON AIR/http/www/listen/self-log phrases to avoid loops.
- **Minimal UI**: Start/Stop Bot, Send Test, Get Recent Messages (for debugging what it sees), Proper Exit. Red alive banner + log.
- Stays open (protections: cancel close, explicit shutdown only, heartbeats, crash logging).

Built-in commands:
- `!hi`, `!hello`, `!wave`, `!thanks`, `!help`, `!bot`

## Troubleshooting: Windows 11 blocks the app from running

Windows 11 (SmartScreen / Windows Defender) often blocks unsigned .exe files like this development companion app, especially because it uses browser automation, P/Invoke for input, and launches Edge with debug/profile args. This is normal for custom tools.

**Quick unblock (do this first):**
1. Locate the exe (e.g. `ansel\IMVUCompanion\bin\Debug\net8.0-windows10.0.19041.0\IMVUCompanion.exe` or Release equivalent).
2. Right-click the .exe → Properties.
3. On the General tab, near the bottom under "Security", check the "Unblock" box if present → Apply → OK.
4. Run the exe again.

**If you get the "Windows protected your PC" dialog:**
- Click "More info" → "Run anyway".

**To stop repeated blocks during development:**
1. Open Windows Security (search "Windows Security" in Start).
2. Virus & threat protection → Manage settings (under Virus & threat protection settings).
3. Scroll down to "Exclusions" → Add or remove exclusions.
4. Add exclusion for the folder `C:\Users\serve\ansel\IMVUCompanion\` (or just the .exe).
5. Optionally add the whole project folder.

**Run from PowerShell (recommended for unblock + run):**
```powershell
cd ansel\IMVUCompanion
Unblock-File -Path "bin\Debug\net8.0-windows10.0.19041.0\IMVUCompanion.exe" -ErrorAction SilentlyContinue
.\bin\Debug\net8.0-windows10.0.19041.0\IMVUCompanion.exe
```

**Build Release for a "cleaner" exe (less likely to flag):**
```powershell
dotnet build -c Release
```
Then run from `bin\Release\net8.0-windows10.0.19041.0\IMVUCompanion.exe` and unblock as above.

**If still blocked:**
- Ensure .NET 8 Desktop Runtime is installed: https://dotnet.microsoft.com/download/dotnet/8.0 (Windows x64 Desktop Runtime).
- Try running as Administrator (right-click → Run as administrator).
- Temporarily disable Real-time protection in Windows Security (re-enable after test).
- The app does legitimate automation (browser control + DOM events for IMVU Next chat). If your AV is very strict, the exclusion above is the permanent fix.
- Build self-contained portable exe (no runtime dependency): `dotnet publish -c Release -r win-x64 --self-contained true /p:PublishSingleFile=true` → use the published exe in `bin\Release\net8.0-windows10.0.19041.0\win-x64\publish\`.

After unblocking, run as usual. The protections (stay-open, etc.) are intentional and may look suspicious to SmartScreen on first runs.

## How to Run (simple internal launch + web structured DOM)
1. Build/run the companion (in `ansel\IMVUCompanion`):
   Prefer the Release build (see Troubleshooting above for why on Win11):
   ```
   dotnet build -c Release
   .\bin\Release\net8.0-windows10.0.19041.0\IMVUCompanion.exe
   ```

   (Debug works too if you unblock it: `dotnet build` then Debug exe.)

2. Click **Start Bot** (or Send Test). It launches Edge/Chrome *internally* with your profile (reuses login, no re-login, one window "inside" app). Loads IMVU room.

3. In companion:
   - Click **Inspect Chat DOM** or **Get Recent Messages** to see RAW + structured (speaker from DOM user el in message row + text; no : needed).
   - In your IMVU web room, have someone join → bot greets.
   - Type command like `!hi` (as chat message) → bot replies with the actual name (from .cs2-name in chat DOM) + response (no @ prefix).
   - No manual flags, no constant scan (pure observer events).

4. **Proper Exit**.

No new files created. Clutter cleaned. Event-driven only. Web/Next recommended (Classic fallback if forced). Use Inspect output if speaker/msg extraction needs tune for your room's message rows.

## Message Templates UI (new)
- Expand the "Message Templates" section (below controls).
- Choose event e.g. "Welcoming".
- List shows variations (use {name} placeholder for the username from .cs2-name).
- Edit the selected in the box, use Add/Remove/Update.
- Click Save to persist to messages.json (loaded on start).
- On join, bot randomly picks one variation, replaces {name}, sends e.g. "Hey ♡, glad you joined!" (no @).
- This gives full control + random variations as requested. Extend for other events later.

## What changed in the latest update ("what happened")
- Switched connection strategy to CDP-first: requires your pre-running Edge instance. No more auto-launch of fresh Edge (which created new profile + login wall).
- Added best-container discovery in JS (counts real "shortname: message" patterns across potential chat divs/ul/sections with chat/message/scroll classes).
- Stricter JS filter everywhere (name len 1-36 before :, no radio/on air/http/www/listen, msg not url).
- Uses GetChatLinesAsync helper for Initial scan + Get Recent + probes.
- Observer attaches to best container, uses same looksLike filter before calling back to C#.
- Fixed re-binding error by exposing once.
- Added missing Exit handler + _exiting guard so "Proper Exit" works and close protection is bypassable only intentionally.
- Cleaned some logs/instructions.
- (Note: previous build was stale vs source; now source should build clean.)

If Get Recent still only shows "ON AIR:" or similar radio lines, the chat messages in the IMVU Next DOM may be rendered in a way that their innerText doesn't contain the classic "name: text" in one node (separate spans for name + body, or virtualized list, or shadow DOM). Paste the output of Get Recent + any extra info (e.g. from Edge DevTools on the debug port, inspect a chat message element's classes/parent) and we can tighten the selectors further (e.g. target specific message components and reconstruct "name: msg").

The old Classic/OCR + screen capture path and calibration was fully removed per your requests (no more TL/BR/Input Pt, no constant capture spam).

## Project / Build
- Uses Microsoft.Playwright 1.60.0 (CDP + automation).
- Target net8.0-windows10.0.19041.0 + WPF.
- Logs + heartbeats to `C:\Users\serve\imvu_companion_crash.log` (very verbose for debugging).
- Source: MainWindow.xaml.cs (all logic).

## Remaining / Next
- If radio still dominates, refine JS discovery (more specific class names from your page).
- Handle chat input reliably (current Send falls back to Keyboard if no input selector).
- Add more commands or make _commandReplies configurable.
- Support detecting the exact room without hardcoding.

Run the fresh build after these fixes and try with the Edge flag + loaded room. Then report what "Get Recent Messages" shows and whether it reacts to : !cmd .

This should finally match: detect existing Edge, see actual chat (not just radio), event-driven replies with @name.