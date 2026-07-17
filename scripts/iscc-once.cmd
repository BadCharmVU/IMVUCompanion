@echo off
set ROOT=C:\Users\serve\ansel\IMVUCompanion
set OUT=%ROOT%\release-build
if not exist "%OUT%" mkdir "%OUT%"
set ISCC=%LOCALAPPDATA%\Programs\Inno Setup 6\ISCC.exe
"%ISCC%" "/O%OUT%" "/DPublishDir=..\publish.20260717154954" "/DOutputDirOverride=%OUT%" "/DSetupFileName=IMVUSetup090" "%ROOT%\installer\IMVUCompanion.iss" > "%ROOT%\iscc-log.txt" 2>&1
echo EXIT=%ERRORLEVEL% >> "%ROOT%\iscc-log.txt"
dir "%OUT%\IMVUSetup090.exe" >> "%ROOT%\iscc-log.txt"
