# CVR Tool

A cross-platform desktop app with integrated MCP for CVR DSP amplifiers.
The interface and functions follow the CVR application, with the same controls
for two- and four-channel models.

[Download v0.3.0](https://github.com/Woodwarth/CVR-Tool/releases/tag/v0.3.0):
Windows x64, Windows ARM64 and macOS Apple Silicon. Extract the entire package;
Windows: run `START-CVR-TOOL.cmd`. Mac: open `CVR Tool.app`.
The runtime is included; no development environment or additional runtime is required.
Packages are unsigned (Mac ad-hoc signed, not notarized).

Desktop and MCP share one local session. Manual CVR UDP connections and local
models are available. Physical-amplifier validation remains pending.

[Development guide](docs/code-guide.md) · [MCP setup](docs/mcp.md) ·
[Network details](docs/network.md) · [Validation](docs/validation.md)

MIT license. Independent project; no manufacturer affiliation.
