# Split MainWindow.WebView.cs into lifecycle / bridge / whisper / join partials
$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent
$src = Join-Path $root "MainWindow.WebView.cs"
$lines = Get-Content $src -Encoding UTF8

function Write-Partial([string]$name, [int]$start1, [int]$end1, [string]$extraUsings = "") {
    # 1-based inclusive line numbers
    $body = $lines[($start1 - 1)..($end1 - 1)]
    # Strip leading "public partial class MainWindow {" and trailing "}" from middle slices
    $content = @"
using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.Web.WebView2.Core;
$extraUsings
namespace IMVUCompanion;

public partial class MainWindow
{
$($body -join "`n")
}
"@
    # If body already includes class open, this will double-wrap - so pass only method bodies
    $path = Join-Path $root $name
    [System.IO.File]::WriteAllText($path, $content, [System.Text.UTF8Encoding]::new($false))
    Write-Host "Wrote $name lines $start1-$end1"
}

# File structure after strip (1-based):
# 11: class open
# 12-132: lifecycle (through NavReload)  -> keep in WebView
# 133-267: bridge (JsIife .. TryParsePoint)
# 268-end before whisper ChatInput: room + leave
# Actually use method line numbers from grep

# Simpler approach: copy full file then delete sections from each

$full = [System.IO.File]::ReadAllText($src)

# We'll manually create 4 files from line ranges of METHODS only (inside class)

$header = @'
using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.Web.WebView2.Core;

namespace IMVUCompanion;

public partial class MainWindow
{
'@

function Slice([int]$from, [int]$to) {
    # 1-based inclusive, content includes indent
    return ($lines[($from-1)..($to-1)] -join "`n")
}

# Lifecycle: fields + Init through NavReload (12-133)
$lifecycle = $header + "`n" + (Slice 12 133) + "`n}`n"
[System.IO.File]::WriteAllText((Join-Path $root "MainWindow.WebView.cs"), $lifecycle, [System.Text.UTF8Encoding]::new($false))
Write-Host "MainWindow.WebView.cs lifecycle"

# Bridge: JsIife through TryParsePoint (135-267)
$bridge = $header + "`n" + (Slice 135 267) + "`n}`n"
[System.IO.File]::WriteAllText((Join-Path $root "MainWindow.WebView.Bridge.cs"), $bridge, [System.Text.UTF8Encoding]::new($false))
Write-Host "Bridge"

# Room + leave + whisper through FinishWhisper (273-583) including ChatInputSelector
$whisper = $header + "`n" + (Slice 273 583) + "`n}`n"
[System.IO.File]::WriteAllText((Join-Path $root "MainWindow.WebView.Whisper.cs"), $whisper, [System.Text.UTF8Encoding]::new($false))
Write-Host "Whisper (includes room leave/public send)"

# Join: Seed through Setup (585-666)
$join = $header + "`n" + (Slice 585 666) + "`n}`n"
[System.IO.File]::WriteAllText((Join-Path $root "MainWindow.WebView.Join.cs"), $join, [System.Text.UTF8Encoding]::new($false))
Write-Host "Join"

Write-Host "Done. Line count total was $($lines.Count)"
