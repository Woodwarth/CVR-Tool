@echo off
setlocal
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\setup-windows.ps1" -InstallMissing -InstallExtensions -OpenProject
if errorlevel 1 (
  echo.
  echo Einrichtung fehlgeschlagen. Bitte Fehlermeldung oben beachten.
  pause
  exit /b 1
)
echo.
echo Projekt eingerichtet. In VS Code Codex oeffnen und anmelden.
pause
