@echo off
cd /d C:\Users\serve\ansel\IMVUCompanion
set LOG=%TEMP%\imvu-ship097b.log
echo start > "%LOG%"
taskkill /F /IM IMVUCompanion.exe 2>nul
timeout /t 2 /nobreak >nul
echo Publish >> "%LOG%"
call powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0Publish-Release.ps1" >> "%LOG%" 2>&1
if not exist "release\IMVUCompanion-Setup-v0.9.7.exe" (
  echo FAIL no setup >> "%LOG%"
  exit /b 1
)
echo git >> "%LOG%"
git add -A
git commit -m "Release v0.9.7 Bot Settings list focus and delete polish" >> "%LOG%" 2>&1
git push origin HEAD >> "%LOG%" 2>&1
git tag -f v0.9.7 >> "%LOG%" 2>&1
git push origin v0.9.7 --force >> "%LOG%" 2>&1
echo Finish >> "%LOG%"
call powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0Finish-Release.ps1" -Version 0.9.7 -SkipPreflight >> "%LOG%" 2>&1
echo Clean >> "%LOG%"
call powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0Clean-Stale.ps1" >> "%LOG%" 2>&1
del /f /q "%~dp0_ship097.ps1" 2>nul
del /f /q "%~dp0_ship097.cmd" 2>nul
git add -A
git commit -m "chore: v0.9.7 sha256 and cleanup" >> "%LOG%" 2>&1
git push origin HEAD >> "%LOG%" 2>&1
echo DONE >> "%LOG%"
type version.json >> "%LOG%"
gh release view v0.9.7 --repo BadCharmVU/IMVUCompanion >> "%LOG%" 2>&1
exit /b 0
