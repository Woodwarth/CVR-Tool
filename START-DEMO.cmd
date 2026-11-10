@echo off
setlocal
cd /d "%~dp0"
if not exist "src\CvrTool.Desktop\bin\Debug\net10.0\CvrTool.Desktop.exe" (
    dotnet build CvrTool.slnx
    if errorlevel 1 exit /b 1
)
start "" "src\CvrTool.Desktop\bin\Debug\net10.0\CvrTool.Desktop.exe"
