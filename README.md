# CVR Tool

CVR Tool controls CVR DSP amplifiers from Windows, macOS or a browser on the local network. The desktop app includes an MCP server and a demo amplifier for offline use.

![CVR Tool Outputs view in a local demo session](docs/images/outputs.png)

## Download

| Platform | Direct download | Start after extracting |
| --- | --- | --- |
| Windows x64 · Intel / AMD | [Download ZIP](https://github.com/Woodwarth/CVR-Tool/releases/download/v0.8.2/CVR-Tool-0.8.2-win-x64.zip) | `START-CVR-TOOL.cmd` |
| Windows ARM64 · Snapdragon | [Download ZIP](https://github.com/Woodwarth/CVR-Tool/releases/download/v0.8.2/CVR-Tool-0.8.2-win-arm64.zip) | `START-CVR-TOOL.cmd` |
| Mac · Apple Silicon | [Download archive](https://github.com/Woodwarth/CVR-Tool/releases/download/v0.8.2/CVR-Tool-0.8.2-osx-arm64.tar.gz) | `CVR Tool.app` |

[Latest release and checksums](https://github.com/Woodwarth/CVR-Tool/releases/latest).
Extract the whole archive before starting the app. Each download includes the .NET runtime. The Windows packages are unsigned. The Mac app is ad-hoc signed and has not been notarized.

Connect the amplifier by Ethernet to discover it automatically. To use an iPad, open **Browser control** on the desktop, start the server and scan the QR code. Use **Updates** in the desktop app to check for a newer release.

[Development guide](docs/code-guide.md) · [MCP setup](docs/mcp.md) ·
[Network details](docs/network.md) · [Validation](docs/validation.md)

MIT license. Independent project; no manufacturer affiliation.
