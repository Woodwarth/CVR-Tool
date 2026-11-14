# Development and code guide

C# 14 / .NET SDK 10, Avalonia and MCP C# SDK. Restore versions are pinned.

```sh
dotnet restore CvrTool.slnx --configfile NuGet.Config --locked-mode
dotnet build CvrTool.slnx -c Release --no-restore
dotnet run --project tests/CvrTool.Checks -c Release --no-build
dotnet run --project tests/CvrTool.NetworkChecks -c Release --no-build
python scripts/check-mcp.py Release
dotnet run --project tests/CvrTool.DesktopChecks -c Release
```

Desktop checks require a graphical session. Their PNGs are direct Avalonia
renderings, not native OS screenshots. Private presets/FIR files can be supplied
as optional local test arguments; never add them to the repository.

| Group | Responsibility |
| --- | --- |
| Core | Immutable two-/four-channel models and backend contract |
| Application | Validation, plans, shared session, local state and CVR backend |
| Protocol | Explicit target validation, packed framing and UDP exchanges |
| Desktop | Fixed design canvas, page builders and editing controls |
| Web | Local browser server, pairing, session access and embedded UI assets |
| MCP | Stdio host and thin tool groups calling Application |
| Checks | Validation, shared clients, synthetic peers and UI behavior |

Each source file has a short English header describing its purpose and state.
UI and MCP never maintain
separate control logic. Single-use plans carry a two-minute expiry and device
revision. The session owner owns sockets; IPC clients own only pending plans.

The shell scales a fixed 1680×980 design uniformly. Minimum size is 1008×588.
Main channel cards stay 160 logical pixels across every model; no wrapping is
triggered by shrinking the window. Inputs opens delay and EQ editors and uses
the same output gain service. EQ edits preserve hardware filter type codes.

[Automation roadmap](architecture.md) · [MCP](mcp.md) · [Network](network.md)
