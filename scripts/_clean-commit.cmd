@echo off
cd /d C:\Users\serve\ansel\IMVUCompanion
dotnet build IMVUCompanion.csproj -c Release --nologo -v q > "%TEMP%\imvu-build.txt" 2>&1
echo BUILD_EXIT=%ERRORLEVEL%>> "%TEMP%\imvu-build.txt"
git add -A
git status --short >> "%TEMP%\imvu-build.txt"
git commit -m "chore: remove obsolete research dumps, one-off scripts, and old installers" >> "%TEMP%\imvu-build.txt" 2>&1
git push origin HEAD >> "%TEMP%\imvu-build.txt" 2>&1
echo DONE>> "%TEMP%\imvu-build.txt"
