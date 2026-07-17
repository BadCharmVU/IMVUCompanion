$ErrorActionPreference = "Stop"
$root = "C:\Users\serve\ansel\IMVUCompanion"
$iscc = "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe"
if (-not (Test-Path $iscc)) { $iscc = "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe" }
$out = "C:\Users\serve\Temp\imvu-rel"
New-Item -ItemType Directory -Force -Path $out | Out-Null
$pub = Get-ChildItem $root -Directory -Filter "publish*" | Sort-Object LastWriteTime -Descending | Select-Object -First 1
if (-not $pub) { throw "No publish folder" }
$rel = $pub.FullName.Substring($root.Length).TrimStart('\')
$name = "IMVUCompanion-Setup-v0.9.0"
Write-Host "Using publish: $rel"
Write-Host "Output: $out\$name.exe"
& $iscc "/O$out" "/DPublishDir=..\$rel" "/DOutputDirOverride=$out" "/DSetupFileName=$name" "$root\installer\IMVUCompanion.iss"
$code = $LASTEXITCODE
"exit=$code" | Out-File "$out\status.txt"
if (Test-Path "$out\$name.exe") {
    $len = (Get-Item "$out\$name.exe").Length
    "size=$len" | Out-File "$out\status.txt" -Append
    # Place where release scripts look
    New-Item -ItemType Directory -Force -Path "$root\release-build" | Out-Null
    Copy-Item "$out\$name.exe" "$root\release-build\$name.exe" -Force
    Copy-Item "$out\$name.exe" "$root\release\$name.exe" -Force -ErrorAction SilentlyContinue
}
exit $code
