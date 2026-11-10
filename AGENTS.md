# CVR Tool working context

## Scope
Cross-platform .NET 10 / Avalonia desktop app with integrated stdio MCP.
Windows x64/ARM64 and macOS ARM64; no Intel Mac. All two-channel models share
the same feature set; variants differ by channel count (two or four).

## Working rules
- Communicate in German; UI, documentation and source comments are English.
- Use Chrome, never Edge. Do not generate images.
- Read README and docs/validation.md before major changes.
- UI and MCP call Application and share one session owner; no parallel UDP writers.
- Keep physical compatibility claims separate from synthetic protocol tests.
- No flashing, automatic discovery or scans on the company network.
- Never publish manufacturer executables/source, private presets or captures.
- MCP stdout is protocol only; logs go to stderr.
- Never claim a build or test passed without running it.
- Updates: docs/updates.md. Keep checks explicit and preserve old installations.
- Source headers describe purpose, units, state and scope in approximately five lines.

## Development
Commands: docs/code-guide.md. MCP: docs/mcp.md. Protocol: docs/network.md and
docs/protocol-evidence.md. Future measurement orchestration: docs/architecture.md;
do not describe that roadmap in README. Native preset inspection is not decoding.

## Validation limits
Manual CVR UDP is implemented from local protocol evidence and tested against
synthetic loopback peers. Physical amplifier and macOS/Windows ARM GUI validation
are pending. Native Windows capture previously timed out; direct Avalonia
renderings verify our controls, not the OS. No inferred healthy telemetry.
