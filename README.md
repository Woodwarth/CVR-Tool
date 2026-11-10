# CVR Tool

A cross-platform desktop app with integrated MCP for CVR DSP amplifiers.
The interface and functions follow the CVR application, with the same controls
for two- and four-channel models.

## Download

| Platform | Direct download | Start after extracting |
| --- | --- | --- |
| Windows x64 · Intel / AMD | [Download ZIP](https://github.com/Woodwarth/CVR-Tool/releases/download/v0.5.0/CVR-Tool-0.5.0-win-x64.zip) | `START-CVR-TOOL.cmd` |
| Windows ARM64 · Snapdragon | [Download ZIP](https://github.com/Woodwarth/CVR-Tool/releases/download/v0.5.0/CVR-Tool-0.5.0-win-arm64.zip) | `START-CVR-TOOL.cmd` |
| Mac · Apple Silicon | [Download archive](https://github.com/Woodwarth/CVR-Tool/releases/download/v0.5.0/CVR-Tool-0.5.0-osx-arm64.tar.gz) | `CVR Tool.app` |

[Latest release and checksums](https://github.com/Woodwarth/CVR-Tool/releases/latest).
Extract the entire package. The runtime is included; no SDK or additional
software is required. Windows packages are unsigned; Mac is ad-hoc signed,
not notarized. No Intel Mac build.

From v0.4.0, choose **Updates → Check for updates → Download and restart**
inside the app. Updates are verified and installed in your user profile;
existing versions are retained. Export session settings before restarting.

Desktop and MCP share one local session. Manual CVR UDP connections through
MCP and local models are available. Physical-amplifier validation remains pending.

[Development guide](docs/code-guide.md) · [MCP setup](docs/mcp.md) ·
[Network details](docs/network.md) · [Validation](docs/validation.md)

MIT license. Independent project; no manufacturer affiliation.
