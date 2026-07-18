# IMVU Companion

A Windows desktop bot for **IMVU Next** chat rooms. It runs IMVU inside the app (WebView2), greets people when they join, answers `!commands`, and can send an optional welcome whisper.

**Current release:** v0.9.0  
**Install (testers):** https://github.com/BadCharmVU/IMVUCompanion/releases/latest — download **`IMVUCompanion-Setup-v0.9.0.exe`** only (ignore GitHub’s auto-generated “Source code” links; those cannot be turned off).

---

## What it does

- Embedded IMVU chat — log in and open your room in the left panel
- Join greetings from **Message Templates** (`{name}` placeholder)
- Optional extra welcome (public or whisper)
- **!Commands** with categories and languages
- **!bbot** AI hook (placeholder reply until providers are connected)
- Activity log, English/Russian UI
- **Auto-update** when a new installer is published on GitHub

---

## Requirements

- Windows 10/11
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) — for building/running from source
- [WebView2 Runtime](https://developer.microsoft.com/microsoft-edge/webview2/) — usually already installed on Windows 11

---

## Daily development (this PC)

**Source code lives here:**

`C:\Users\serve\ansel\IMVUCompanion`

That folder is the working copy. GitHub (`main`) is backup/sync — not where you run the app day to day.

### Run after every code change (one command)

```powershell
cd C:\Users\serve\ansel\IMVUCompanion
.\scripts\Run-Dev.ps1
```

That script:

1. Removes stale `bin\Debug`, old `publish.*`, and other unused build folders
2. Builds **Release** (not Debug)
3. Starts the **only** local test exe:

`bin\Release\net8.0-windows10.0.19041.0\IMVUCompanion.exe`

See also `DEV-RUN.txt` in the project root.

**Do not test from** `bin\Debug`, `publish\`, or `release\` — those are not your dev build.

No installer is needed while developing. **Only run `.\scripts\Publish-Release.ps1` when a version is ready for testers.**

### First-time setup in the app

1. Log in to IMVU in the **left panel** (session saved under `%LOCALAPPDATA%\IMVUCompanion\WebView2`).
2. Open your chat room.
3. Set **Bot Display Name** (AI Providers) to match your IMVU name exactly.
4. Click **Start Bot**.

Use **Exit** (top-right) to close. The window **X** is disabled so the bot is not closed by accident.

### Config files (user data — kept across restarts and updates)

Stored under `%LOCALAPPDATA%\IMVUCompanion\` (not next to the `.exe`, so rebuilds/installers never overwrite them):

| File | Purpose |
|------|---------|
| `messages.json` | Welcome / greeting templates |
| `commands.json` | `!command` replies |
| `ai_settings.json` | API keys and AI provider settings |
| `ui_layout.json` | Window size / panel layout |

On first run the app creates defaults (including 3 English welcome lines). Edits are saved there only. Version updates do not replace these files. Older next-to-exe copies are migrated automatically once.

---

## When you are ready for a new release

Only when **you** say so:

```powershell
cd C:\Users\serve\ansel\IMVUCompanion
.\scripts\Publish-Release.ps1
```

Requires [Inno Setup 6](https://jrsoftware.org/isinfo.php). Output: `release\IMVUCompanion-Setup-vX.Y.Z.exe`

Then: update `version.json`, run `.\scripts\Update-VersionGist.ps1` (public update check), upload the `.exe` to GitHub Release or Gumroad.

Test the installer on your **other PC** — that machine does not need the source repo.

---

## Sync code with GitHub

```powershell
cd C:\Users\serve\ansel\IMVUCompanion
git add -A
git commit -m "describe your change"
git push
```

Clone on another dev PC:

```powershell
git clone https://github.com/BadCharmVU/IMVUCompanion.git
cd IMVUCompanion
dotnet build -c Release
```

---

## Windows SmartScreen

Unsigned builds may warn on first run: right-click the `.exe` → **Properties** → **Unblock** → OK.

---

## Project layout

| Path | Role |
|------|------|
| `bin\Release\net8.0-windows10.0.19041.0\` | **Local dev/test exe** (only folder you run day to day) |
| `scripts/Run-Dev.ps1` | Build + run dev exe (use after every change) |
| `scripts/Clean-Stale.ps1` | Remove unused Debug/publish.* folders |
| `scripts/Publish-Release.ps1` | Build installer → `release\` (release day only) |
| `publish\` | Self-contained app output (created by Publish-Release) |
| `release\` | Installer `.exe` for GitHub/testers |
| `MainWindow.WebView.cs` | Chat observer, whispers, WebView2 |
| `MainWindow.xaml.cs` | Bot queue, greetings, commands |
| `MainWindow.Update.cs` | Update check and upgrade |