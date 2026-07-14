# IMVU Companion

WPF desktop app with an embedded **IMVU Next** chat (WebView2). Greets users when they join, responds to `!commands`, and supports optional welcome whispers.

**Current version:** v0.7

**Repository:** https://github.com/BadCharmVU/IMVUCompanion

**Download (releases):** https://github.com/BadCharmVU/IMVUCompanion/releases/latest

---

## Requirements

- Windows 10/11
- [.NET 8 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0)
- [WebView2 Runtime](https://developer.microsoft.com/microsoft-edge/webview2/)

---

## Quick start

### From source

```powershell
git clone https://github.com/BadCharmVU/IMVUCompanion.git
cd IMVUCompanion
dotnet build -c Release
.\bin\Release\net8.0-windows10.0.19041.0\IMVUCompanion.exe
```

### First run

1. Log in to IMVU in the **left panel** (login is saved in `%LOCALAPPDATA%\IMVUCompanion\WebView2`).
2. Open your chat room in the embedded browser.
3. Set **Bot Display Name** (AI Providers) to match your IMVU name exactly.
4. Click **Start Bot**.

Use **Exit** (top-right) to close the app. The window **X** is disabled on purpose so the bot is not closed accidentally.

---

## Features

- Embedded IMVU Next chat — no separate browser window
- Join greetings from editable **Message Templates** (`{name}` placeholder)
- Optional extra welcome message (public or whisper)
- **!Commands** with categories and languages
- **!bbot** AI hook (maintenance reply until providers are wired)
- Activity log with colored categories
- **Auto-update** — version button top-right checks GitHub for new releases

---

## Configuration files

Created next to the `.exe` at runtime (not in git):

| File | Purpose |
|------|---------|
| `messages.json` | Greeting templates |
| `commands.json` | `!command` replies |
| `ai_settings.json` | API keys — copy from `ai_settings.example.json` |

---

## Build a shareable installer exe

```powershell
dotnet publish -c Release -r win-x64 --self-contained true /p:PublishSingleFile=true
```

Output: `bin\Release\net8.0-windows10.0.19041.0\win-x64\publish\IMVUCompanion.exe`

Attach that file to a GitHub Release and bump `version.json` so testers get one-click updates.

---

## Windows SmartScreen

Unsigned builds may be blocked the first time:

1. Right-click the `.exe` → **Properties** → check **Unblock** → OK  
2. Or add the project folder under Windows Security → Exclusions

---

## Project layout

| Path | Description |
|------|-------------|
| `MainWindow.WebView.cs` | Chat observer, whisper logic, WebView2 |
| `MainWindow.xaml.cs` | Bot queue, greetings, commands |
| `MainWindow.Update.cs` | Update check and one-click upgrade |
| `PROGRESS-whisper-join.md` | In-progress join-whisper work notes |
| `GITHUB-SETUP.md` | Git clone, push, and release workflow |

---

## Changelog

Release notes are published on [GitHub Releases](https://github.com/BadCharmVU/IMVUCompanion/releases). Update log for v0.7 will be added when you publish the release.