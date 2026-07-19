@echo off
setlocal
cd /d C:\Users\serve\ansel\IMVUCompanion
set ROOT=%CD%

echo ==> Killing processes that may lock release\
taskkill /F /IM ISCC.exe 2>nul
taskkill /F /IM IMVUCompanion.exe 2>nul
timeout /t 2 /nobreak >nul

echo ==> Clean alternate output dir
if exist "%ROOT%\release-build" rmdir /s /q "%ROOT%\release-build"
mkdir "%ROOT%\release-build" 2>nul

echo ==> Ensure publish exists
if not exist "%ROOT%\publish.20260719074944\IMVUCompanion.exe" (
  echo Re-publish...
  powershell -NoProfile -ExecutionPolicy Bypass -Command "dotnet publish '%ROOT%\IMVUCompanion.csproj' -c Release -r win-x64 --self-contained true -o '%ROOT%\publish' /p:PublishReadyToRun=true /p:DebugType=None /p:DebugSymbols=false"
  set PUBREL=..\publish
) else (
  set PUBREL=..\publish.20260719074944
)

set ISCC=%LOCALAPPDATA%\Programs\Inno Setup 6\ISCC.exe
if not exist "%ISCC%" set ISCC=%ProgramFiles(x86)%\Inno Setup 6\ISCC.exe
if not exist "%ISCC%" set ISCC=%ProgramFiles%\Inno Setup 6\ISCC.exe

echo ==> ISCC to release-build
"%ISCC%" "/O%ROOT%\release-build" "/DPublishDir=%PUBREL%" "/DAppVersion=0.9.4" "%ROOT%\installer\IMVUCompanion.iss"
if errorlevel 1 exit /b 1

if not exist "%ROOT%\release-build\IMVUCompanion-Setup-v0.9.4.exe" (
  echo Installer missing
  exit /b 1
)

echo ==> Copy installer to release\
if not exist "%ROOT%\release" mkdir "%ROOT%\release"
copy /Y "%ROOT%\release-build\IMVUCompanion-Setup-v0.9.4.exe" "%ROOT%\release\IMVUCompanion-Setup-v0.9.4.exe"
if errorlevel 1 exit /b 1

echo ==> git commit/push/tag
powershell -NoProfile -ExecutionPolicy Bypass -File "%ROOT%\scripts\Ship-Release.ps1" -SkipBuild -CommitMessage Release-v0.9.4
exit /b %ERRORLEVEL%
