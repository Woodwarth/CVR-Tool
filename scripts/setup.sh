#!/usr/bin/env bash
set -euo pipefail
cvr_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$cvr_root"
if ! command -v dotnet >/dev/null 2>&1; then
  echo "Install .NET 10 SDK from https://dotnet.microsoft.com/download/dotnet/10.0 then rerun." >&2
  exit 1
fi
dotnet --list-sdks | grep -q '^10\.' || { echo ".NET 10 SDK required." >&2; exit 1; }
dotnet restore CvrTool.slnx --configfile NuGet.Config
dotnet build CvrTool.slnx --no-restore
dotnet run --project tests/CvrTool.Checks --no-build
python3 scripts/configure-mcp.py
if command -v code >/dev/null 2>&1; then
  for extension in ms-dotnettools.csharp AvaloniaTeam.vscode-avalonia openai.chatgpt; do
    code --install-extension "$extension"
  done
  code "$cvr_root/CVR-Tool.code-workspace"
else
  echo "Open CVR-Tool.code-workspace in VS Code; install the recommended extensions."
fi
