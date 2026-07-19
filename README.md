# IMVU Companion

Windows desktop companion for **IMVU Next** chat rooms. IMVU runs inside the app (WebView2). The bot can greet joiners, answer `!commands`, send optional welcome whispers, and check for updates automatically.

**Current release:** [v0.9.4](https://github.com/BadCharmVU/IMVUCompanion/releases/latest)

Download **`IMVUCompanion-Setup-v0.9.4.exe`** from [Releases](https://github.com/BadCharmVU/IMVUCompanion/releases/latest). Ignore GitHub’s auto-generated “Source code” archives — they are not the app installer.

---

## Features

- Embedded IMVU Next chat (login and open your room in the left panel)
- **Welcome messages** when someone joins — public and/or whisper, with `{name}` placeholder
- Second optional welcome line (public or whisper)
- **!Commands** with categories and languages (English / Russian)
- **!bbot** AI hook (providers configurable; maintenance reply when not set up)
- Room-aware bot: works while you are in a room; pauses cleanly when you leave
- Activity log with clear categories
- **Auto-update** button (installed builds) — checks for newer versions and installs the Setup package
- Window layout remembered between sessions

---

## Install (testers)

1. Download the latest **Setup** `.exe` from [Releases](https://github.com/BadCharmVU/IMVUCompanion/releases/latest).
2. Run the installer (per-user install; no admin required in the usual configuration).
3. Launch **IMVU Companion** from the Start menu or desktop shortcut.
4. On later versions, use the **update** control in the app when it shows a new release available.

### First-time setup in the app

1. Log in to IMVU in the **left panel** (session is saved locally).
2. Open your chat room.
3. Set **Bot Display Name** (AI Providers) to match your IMVU display name exactly.
4. Configure welcome messages and `!commands` as you like.
5. Click **Start Bot**.

Use **Exit** in the app to close cleanly (bot stop / leave room as designed).

### Windows SmartScreen

Unsigned builds may show a first-run warning: right-click the installer or app → **Properties** → **Unblock** → OK, or choose **More info** → **Run anyway**.

---

## Your settings are kept

Welcome messages, `!commands`, AI settings, and window layout are stored under:

`%LOCALAPPDATA%\IMVUCompanion\`

| File | Purpose |
|------|---------|
| `messages.json` | Welcome / greeting templates |
| `commands.json` | `!command` replies |
| `ai_settings.json` | API keys and AI provider settings |
| `ui_layout.json` | Window size / panel layout |
| `WebView2\` | IMVU login session (browser profile) |

- Edits survive **app restarts**.
- Installing a **new version does not replace** your custom messages or commands.
- First run creates defaults (including sample welcome lines and sample `!commands`) if no file exists yet.

---

## Requirements

- Windows 10 or 11 (64-bit)
- [WebView2 Runtime](https://developer.microsoft.com/microsoft-edge/webview2/) — usually already present on Windows 11
- For building from source: [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)

---

## Build from source

```powershell
git clone https://github.com/BadCharmVU/IMVUCompanion.git
cd IMVUCompanion
dotnet build -c Release
```

Run the built app from the project’s Release output folder (framework-dependent build under `bin\Release\...`).

For day-to-day development in this repo:

```powershell
.\scripts\Run-Dev.ps1
```

That cleans temporary build folders, builds Release, and starts the local test executable.

**Do not** run day-to-day from `bin\Debug`, `publish\`, or installer-only `release\` folders unless you know you need those artifacts.

---

## Creating a release (maintainers)

Requires [Inno Setup 6](https://jrsoftware.org/isinfo.php) and [GitHub CLI](https://cli.github.com/) authenticated with `repo` and `gist` scopes.

```powershell
.\scripts\Ship-Release.ps1
```

Or step by step:

```powershell
.\scripts\Publish-Release.ps1
# then tag, create GitHub Release with the Setup exe, update version.json gist
```

Installer output: `release\IMVUCompanion-Setup-vX.Y.Z.exe`  
Update channel: public `version.json` gist referenced by the app (see `AppVersion.cs` / `version.json` in the repo).

---

## Project layout (source)

| Path | Role |
|------|------|
| `MainWindow.xaml` / `.xaml.cs` | UI, bot queue, greetings, commands |
| `MainWindow.WebView.cs` | WebView2 lifecycle, navigation, host messages |
| `MainWindow.WebView.Bridge.cs` | JS execution helpers, CDP click |
| `MainWindow.WebView.Whisper.cs` | Whisper / public send automation |
| `MainWindow.WebView.Join.cs` | Chat observer setup, join seeding, diagnostics |
| `Scripts/Imvu/*.js` | IMVU page scripts (edit these when IMVU DOM changes) |
| `ImvuScripts.cs` | Loads embedded / on-disk JS modules |
| `SecretProtector.cs` | DPAPI protection for API keys at rest |
| `MainWindow.Update.cs` | Update check and apply |
| `MainWindow.AiProviders.cs` | AI provider settings |
| `UserDataPaths.cs` | User config location under LocalAppData |
| `UpdateService.cs` | Version check, download, install |
| `scripts/Run-Dev.ps1` | Local Release build + run |
| `scripts/Clean-Stale.ps1` | Remove temporary publish/debug clutter |
| `scripts/Publish-Release.ps1` | Self-contained publish + installer |
| `scripts/Ship-Release.ps1` | Full ship: build, push, release, gist |
| `installer/IMVUCompanion.iss` | Inno Setup script |
| `version.json` | Release version / download URL for the update channel |

---

## Recent changes (v0.9.4)

- **Bot Settings:** add, edit, and organize `!commands` by category, with search and paging
- New commands and categories are **saved and kept after restart** (same storage idea as welcome messages)
- The same `!command` can have a **different reply per language**
- Language-aware welcome messages and bot replies
- Improved Add Command flow (categories and validation)
- **AI Settings** and **AI Providers** are **not connected yet** (still in development; UI only for now)

See [Releases](https://github.com/BadCharmVU/IMVUCompanion/releases) for full notes per version.

---

## License / support

Issues: [GitHub Issues](https://github.com/BadCharmVU/IMVUCompanion/issues)  
Releases: [GitHub Releases](https://github.com/BadCharmVU/IMVUCompanion/releases)
