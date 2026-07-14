# Put IMVU Companion on GitHub (work from any PC)

Use a **private** GitHub repo so your bot code and notes are backed up, but API keys and IMVU session data stay off the public internet.

---

## What you need on each computer

| Tool | Purpose |
|------|---------|
| [Git for Windows](https://git-scm.com/download/win) | `git clone`, commit, push |
| [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) | `dotnet build` |
| [WebView2 Runtime](https://developer.microsoft.com/microsoft-edge/webview2/) | Embedded IMVU browser |
| Grok in PowerShell / Cursor | Continue coding with the AI assistant |

**Not in Git:** WebView2 login cookies live in  
`%LocalAppData%\IMVUCompanion\WebView2` — you log into IMVU again on each new PC (or copy that folder manually if you really want the same session).

---

## One-time: install Git (this PC)

PowerShell (admin optional):

```powershell
winget install --id Git.Git -e --source winget
```

Close and reopen PowerShell, then:

```powershell
git --version
```

---

## One-time: create GitHub repo

1. Go to [github.com/new](https://github.com/new)
2. Name: e.g. `IMVUCompanion`
3. Visibility: **Private** (recommended)
4. Do **not** add README/license/gitignore (we already have them locally)
5. Create repository

---

## One-time: push from this computer

```powershell
cd C:\Users\serve\ansel\IMVUCompanion

# Optional: set your name/email for commits
git config --global user.email "you@example.com"
git config --global user.name "Your Name"

git init
git add .
git status
# Confirm ai_settings.json and bin/ are NOT listed (blocked by .gitignore)

git commit -m "Initial commit: IMVU Companion v4.5"
git branch -M main
git remote add origin https://github.com/YOUR_USERNAME/IMVUCompanion.git
git push -u origin main
```

GitHub will ask you to sign in. Easiest: **Git Credential Manager** (installed with Git for Windows) or a [Personal Access Token](https://github.com/settings/tokens) instead of password.

---

## On any other computer

```powershell
cd $HOME\projects   # or wherever you keep code
git clone https://github.com/YOUR_USERNAME/IMVUCompanion.git
cd IMVUCompanion

copy ai_settings.example.json bin\Release\net8.0-windows10.0.19041.0\ai_settings.json
# Edit that file (or create after first build) with your API key + Bot Display Name

dotnet build -c Release
.\bin\Release\net8.0-windows10.0.19041.0\IMVUCompanion.exe
```

Open the folder in **Cursor** (or your Grok PowerShell session pointed at this path) and say: *"Continue from PROGRESS-whisper-join.md"*.

---

## Day-to-day workflow

```powershell
cd C:\path\to\IMVUCompanion
git pull
# ... work with Grok ...
git add -A
git status
git commit -m "v4.6: describe what changed"
git push
```

---

## Security checklist (important)

- [ ] **Never commit** `ai_settings.json` — it contains API keys (`.gitignore` blocks it)
- [ ] If you ever pasted a key in chat or almost committed it, **rotate/revoke** that key in the provider dashboard
- [ ] Keep repo **private** unless you intentionally open-source scrubbed code
- [ ] `messages.json` / `commands.json` are ignored by default; copy them manually if you want the same bot phrases on another PC

---

## Optional: sync bot messages/commands via Git

If you *want* welcome text and `!commands` in the repo, edit `.gitignore` and remove the `messages.json` / `commands.json` lines, then:

```powershell
copy bin\Release\net8.0-windows10.0.19041.0\messages.json .\messages.json
copy bin\Release\net8.0-windows10.0.19041.0\commands.json .\commands.json
git add messages.json commands.json
git commit -m "Add bot message templates"
```

The app loads these from the **exe folder** at runtime; after clone you still copy or symlink them into `bin\Release\...\` after build, or we can later change the app to load from project root.

---

## Paused work

See `PROGRESS-whisper-join.md` for join-whisper status and next debugging steps.