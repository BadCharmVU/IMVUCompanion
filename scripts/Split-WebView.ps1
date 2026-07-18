# Removes giant JS string constants from MainWindow.WebView.cs and rewrites references to ImvuScripts.
$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent
$path = Join-Path $root "MainWindow.WebView.cs"
$text = [System.IO.File]::ReadAllText($path)

# Remove each private const string XJs = """ ... """;
$patterns = @(
    'FindChatRootJs',
    'ExitWhisperModeJs',
    'WhisperFindRowJs',
    'ProactiveWhisperJs',
    'ImvuActiveChatHookJs',
    'ChatObserverJs',
    'CollectExistingJoinUidsJs'
)

foreach ($name in $patterns) {
    # Match from "private const string Name" through closing """;
    $rx = [regex]::new(
        "(?ms)^[ \t]*private const string $name\s*=\s*(?:FindChatRootJs\s*\+\s*)?"""".*?""""\s*;\r?\n",
        [System.Text.RegularExpressions.RegexOptions]::Multiline)
    $m = $rx.Match($text)
    if (-not $m.Success) {
        Write-Warning "Could not remove const $name"
    } else {
        $text = $rx.Replace($text, "", 1)
        Write-Host "Removed const $name ($($m.Length) chars)"
    }
}

# Replace usages of former const names with ImvuScripts properties
$repl = @{
    'FindChatRootJs' = 'ImvuScripts.FindChatRoot'
    'ExitWhisperModeJs' = 'ImvuScripts.ExitWhisperMode'
    'WhisperFindRowJs' = 'ImvuScripts.WhisperFindRow'
    'ProactiveWhisperJs' = 'ImvuScripts.ProactiveWhisper'
    'ImvuActiveChatHookJs' = 'ImvuScripts.ActiveChatHook'
    'ChatObserverJs' = 'ImvuScripts.ChatObserverFull'
    'CollectExistingJoinUidsJs' = 'ImvuScripts.CollectJoinUidsFull'
}

# Order: longer names first if any overlap (none really)
foreach ($k in ($repl.Keys | Sort-Object { $_.Length } -Descending)) {
    $text = $text.Replace($k, $repl[$k])
}

# Fix double ImvuScripts.ImvuScripts if any
$text = $text.Replace('ImvuScripts.ImvuScripts.', 'ImvuScripts.')

# ChatInputSelector stays as C# const (short)

[System.IO.File]::WriteAllText($path, $text, [System.Text.UTF8Encoding]::new($false))
Write-Host "Updated $path length $($text.Length)"
