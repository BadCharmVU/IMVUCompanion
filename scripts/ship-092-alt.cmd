@echo off
cd /d C:\Users\serve\ansel\IMVUCompanion
setlocal

set PUB=
for /f "delims=" %%D in ('dir /b /ad /o-d publish.* 2^>nul') do (
  if not defined PUB set PUB=%%D
)
if not defined PUB if exist publish set PUB=publish
if not defined PUB (
  echo No publish folder
  exit /b 1
)
echo Using publish: %PUB%

set OUTDIR=%CD%\release-v092
if not exist "%OUTDIR%" mkdir "%OUTDIR%"

set ISCC=
if exist "%LOCALAPPDATA%\Programs\Inno Setup 6\ISCC.exe" set ISCC=%LOCALAPPDATA%\Programs\Inno Setup 6\ISCC.exe
if exist "%ProgramFiles(x86)%\Inno Setup 6\ISCC.exe" set ISCC=%ProgramFiles(x86)%\Inno Setup 6\ISCC.exe
if exist "%ProgramFiles%\Inno Setup 6\ISCC.exe" set ISCC=%ProgramFiles%\Inno Setup 6\ISCC.exe
if not defined ISCC (
  echo ISCC not found
  exit /b 1
)

"%ISCC%" "/O%OUTDIR%" "/DPublishDir=..\%PUB%" "/DAppVersion=0.9.2" "%CD%\installer\IMVUCompanion.iss"
if errorlevel 1 exit /b 1

if not exist "%OUTDIR%\IMVUCompanion-Setup-v0.9.2.exe" (
  echo Installer missing
  dir "%OUTDIR%"
  exit /b 1
)

copy /Y "%OUTDIR%\IMVUCompanion-Setup-v0.9.2.exe" "%CD%\release\IMVUCompanion-Setup-v0.9.2.exe" 2>nul
echo Installer OK: %OUTDIR%\IMVUCompanion-Setup-v0.9.2.exe

powershell -NoProfile -ExecutionPolicy Bypass -File ".\scripts\git-push-092.ps1"
if errorlevel 1 exit /b 1

powershell -NoProfile -ExecutionPolicy Bypass -File ".\scripts\Finish-Release.ps1" -Version 0.9.2 -SkipPreflight
exit /b %ERRORLEVEL%
