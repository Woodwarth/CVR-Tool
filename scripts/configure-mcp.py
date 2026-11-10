#!/usr/bin/env python3
"""Configure project-local clients only. Preserve existing entries."""
import json
import pathlib
import shutil
import sys

root = pathlib.Path(__file__).resolve().parent.parent
dotnet = shutil.which("dotnet")
assembly = root / "src/CvrTool.Mcp/bin/Debug/net10.0/CvrTool.Mcp.dll"
if not dotnet or not assembly.is_file():
    sys.exit("Build the Debug MCP project first; dotnet must be on PATH.")
config = root / ".codex/config.toml"
config.parent.mkdir(exist_ok=True)
existing = config.read_text(encoding="utf-8-sig") if config.exists() else ""
section = "[mcp_servers.cvr_tool_demo]"
if section not in existing:
    with config.open("a", encoding="utf-8") as output:
        output.write("\n" + section + "\ncommand = " + json.dumps(dotnet) +
                     "\nargs = [" + json.dumps(str(assembly)) + ']\nstartup_timeout_sec = 30\n')
else:
    import re
    def migrate(match):
        block = match.group(0)
        if str(assembly).replace("\\", "\\\\") in block or "CvrTool.Mcp.dll" in block:
            block = block.replace(', "--demo"', '')
        return block
    config.write_text(re.sub(r"\[mcp_servers\.cvr_tool_demo\][\s\S]*?(?=\n\[|\Z)", migrate, existing), encoding="utf-8")
    print("Existing Codex CVR configuration retained with shared-session mode.")
claude_path = root / ".mcp.json"
claude = json.loads(claude_path.read_text(encoding="utf-8-sig")) if claude_path.exists() else {}
servers = claude.setdefault("mcpServers", {})
if "cvr-tool-demo" not in servers:
    servers["cvr-tool-demo"] = {"command": dotnet, "args": [str(assembly)]}
elif servers["cvr-tool-demo"].get("args") == [str(assembly), "--demo"]:
    servers["cvr-tool-demo"]["args"] = [str(assembly)]
claude_path.write_text(json.dumps(claude, indent=2) + "\n", encoding="utf-8")
print("Project MCP configurations ready. Trust this project and restart the client.")
