# CVR Tool working context

## Scope
Cross-platform .NET 10 / Avalonia desktop app with integrated stdio MCP.
Windows x64/ARM64 and macOS ARM64; no Intel Mac. All two-channel models share
the same feature set; variants differ by channel count (two or four).

## Working rules
- User requested publication of 0.7.3 on 2026-11-12 and a WhatsApp message to Jannis Fischer after the complete release is available. Consult artifacts/publication-batch.json. Minimum 30 minutes between Git upload batches; no periodic uploads without requested changes. Treat push/tag and resulting platform release as one publication batch. Keep captures and private analysis local.
- Communicate in German; UI, documentation and source comments are English.
- After each protocol step report progress as live-evidenced documented function IDs / documented function IDs; count partial evidence explicitly, never claim total unknown-protocol coverage. Current local baseline: 22/38 (58%); newly observed IDs expand the denominator.
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

## Current local work after 0.7.3
User requested network robustness and a .sd/protocol/UI comparison. See docs/preset-protocol-ui-audit.md. Publication of 0.7.4 is explicitly requested; keep private evidence local. Offline fault tests, metadata visibility, safe read-only viewers and FIR-absence preservation do not increase live protocol coverage.

Main has priority today. Next public build must enable all physically evidenced supported write variants with independent readback; reset stays disabled. The target model catalog is docs/models.md (twelve models, channel count from last digit). No input signal is currently available; input-meter/clip calibration remains pending.

Next-build requirement (2026-11-12): include an integrated local-network web server and touch-friendly English browser controls for iPad/Safari, without an iPad app. Web, Desktop and MCP must share the existing Application/session owner and the same verified-write restrictions. Show the connection address and provide server start/stop in Desktop. Implemented for 0.7.4 in src/CvrTool.Web; HTTP and Chrome touch checks passed. Physical iPad/Safari validation is pending. Publication of 0.7.4 was subsequently explicitly authorized.

PC handover requested: preserve local private protocol evidence and reference files; sort the parent project folder and prepare a transfer archive for an existing Windows 11 PC. No OS upgrade of this PC. Last captured HP state: BW-12 off at 20 Hz, all 11 HP type/bypass encodings captured; only BW-12/BW-24 have independent direct-writer validation. LP remains BW-12 off at 19900 Hz with only types 0/4 captured. Next protocol work: remaining LP types, then independent native readback and restricted writer expansion.

Private handover data now lives beside the repository: ../ProjectFiles/Protocol (current-session.txt contains a relative session directory), ../ProjectFiles/References, ../ProjectFiles/Tools. Old generated builds are preserved separately under ../Archive/GeneratedBuilds, not included in transfer. Never publish ProjectFiles or archives.
