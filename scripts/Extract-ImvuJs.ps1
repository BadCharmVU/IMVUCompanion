# One-shot: extract JS raw-string constants from MainWindow.WebView.cs into Scripts/Imvu/*.js
$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent
$src = Join-Path $root "MainWindow.WebView.cs"
$outDir = Join-Path $root "Scripts\Imvu"
New-Item -ItemType Directory -Force -Path $outDir | Out-Null

$lines = Get-Content $src -Encoding UTF8
$names = @(
    "FindChatRootJs",
    "ExitWhisperModeJs",
    "WhisperFindRowJs",
    "ProactiveWhisperJs",
    "ImvuActiveChatHookJs",
    "ChatObserverJs",
    "CollectExistingJoinUidsJs"
)

function Get-RawStringContent([string[]]$lines, [string]$constName) {
    $start = -1
    for ($i = 0; $i -lt $lines.Count; $i++) {
        if ($lines[$i] -match "private const string $constName\s*=") {
            # Find opening """
            for ($j = $i; $j -lt [Math]::Min($i + 5, $lines.Count); $j++) {
                if ($lines[$j] -match '"""\s*$' -or $lines[$j] -match '=\s*FindChatRootJs\s*\+\s*"""') {
                    $start = $j + 1
                    break
                }
                if ($lines[$j] -match '"""') {
                    # same line might have content after """
                    $idx = $lines[$j].IndexOf('"""')
                    $after = $lines[$j].Substring($idx + 3)
                    if ($after.Trim().Length -gt 0 -and $after -notmatch '"""') {
                        # content starts on next line usually
                    }
                    $start = $j + 1
                    break
                }
            }
            break
        }
    }
    if ($start -lt 0) { throw "const $constName not found" }

    $buf = New-Object System.Collections.Generic.List[string]
    for ($k = $start; $k -lt $lines.Count; $k++) {
        if ($lines[$k] -match '^\s*"""\s*;?\s*$') { break }
        if ($lines[$k] -match '"""\s*;\s*$') {
            # closing on same line with content before
            $ci = $lines[$k].LastIndexOf('"""')
            if ($ci -gt 0) { $buf.Add($lines[$k].Substring(0, $ci)) }
            break
        }
        $buf.Add($lines[$k])
    }
    return ($buf -join "`n")
}

$map = @{
    "FindChatRootJs" = "find-chat-root.js"
    "ExitWhisperModeJs" = "exit-whisper-mode.js"
    "WhisperFindRowJs" = "whisper-find-row.js"
    "ProactiveWhisperJs" = "proactive-whisper.js"
    "ImvuActiveChatHookJs" = "active-chat-hook.js"
    "ChatObserverJs" = "chat-observer.js"
    "CollectExistingJoinUidsJs" = "collect-join-uids.js"
}

foreach ($n in $names) {
    $content = Get-RawStringContent $lines $n
    $file = Join-Path $outDir $map[$n]
    # Normalize: trim trailing blank lines
    $content = $content.TrimEnd() + "`n"
    [System.IO.File]::WriteAllText($file, $content, [System.Text.UTF8Encoding]::new($false))
    Write-Host "Wrote $file ($($content.Length) chars)"
}

Write-Host "Done."
