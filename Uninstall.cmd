@echo off
rem Run from a copy in %TEMP%: the uninstaller deletes the folder this file lives in.
copy /y "%~dp0Uninstall.ps1" "%TEMP%\Remotea-Uninstall.ps1" >nul
powershell -NoProfile -ExecutionPolicy Bypass -File "%TEMP%\Remotea-Uninstall.ps1"
pause
