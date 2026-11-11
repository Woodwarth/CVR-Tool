# CVR Tool working context

## Scope
Cross-platform .NET 10 / Avalonia desktop app with integrated stdio MCP.
Windows x64/ARM64 and macOS ARM64; no Intel Mac. All two-channel models share
the same feature set; variants differ by channel count (two or four).

## Working rules
- User requested publication of 0.7.3 on 2026-11-12 and a WhatsApp message to Jannis Fischer after the complete release is available. Consult artifacts/publication-batch.json. Minimum 30 minutes between Git upload batches; no periodic uploads without requested changes. Treat push/tag and resulting platform release as one publication batch. Keep captures and private analysis local.
- Communicate in German; UI, documentation and source comments are English.
- After each protocol step report progress as live-evidenced documented function IDs / documented function IDs; count partial evidence explicitly, never claim total unknown-protocol coverage. Current local baseline: 18/38 (47%); newly observed IDs expand the denominator.
- Use Chrome, never Edge. Do not generate images.
- Read README and docs/validation.md before major changes.
- UI and MCP call Application and share one session owner; no parallel UDP writers.
- Keep physical compatibility claims separate from synthetic protocol tests.
- User authorized automatic CVR discovery on 2026-11-11: bounded identity broadcasts on wired amplifier-LAN interfaces; share UDP ownership with live reads. No IP-range scans, firmware work or Wi-Fi/company-network discovery.
- Never publish manufacturer executables/source, private presets or captures.
- MCP stdout is protocol only; logs go to stderr.
- Never claim a build or test passed without running it.
- Updates: docs/updates.md. Keep checks explicit and preserve old installations.
- Source headers describe purpose, units, state and scope in approximately five lines.
- Latest UI requests: linked metre/ms delay typing, whole-tile Trim editing, confirmed Stereo/Bridge showing only current state, .sd-only PC loading/saving, operating hours, writable output/matrix mute with readback.

## Development
Commands: docs/code-guide.md. MCP: docs/mcp.md. Protocol: docs/network.md and
docs/protocol-evidence.md. Future measurement orchestration: docs/architecture.md;
do not describe that roadmap in README. Native codec scope: docs/native-presets.md; unknown layouts must be rejected.

## Validation limits
Manual CVR UDP is implemented from local protocol evidence and tested against
synthetic loopback peers. Selected identity/settings/telemetry reads and display-lock/standby/Wink/output-mute/route-mute writes passed on one DSP-1004/106119; other models and macOS/Windows ARM GUI validation remain pending. Native Windows capture previously timed out; direct Avalonia
renderings verify our controls, not the OS. No inferred healthy telemetry.
