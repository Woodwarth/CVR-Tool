# Validation — version 0.3.0

Local host: Windows x64, .NET SDK 10.0.403.

- Locked restore and Release solution build: zero warnings/errors.
- 93 core checks: validation, immutable previews, revisions, one-use/expired
  plans, uniform model features, input DSP, schema 3 and offline file analysis.
- 63 network checks: target/configuration validation, loopback transport,
  capture-derived lock fixture, synthetic CVR peer, confirmed output/routing/
  input DSP/name/control writes, shared-session access, telemetry mapping,
  rejected writes, external edits and missing replies.
- 89 desktop checks: meter/status geometry, symbols, equal two-/four-channel
  card dimensions, gain/routing/name controls, uniform scaling from 1008×588
  through 1920×1080, delay unit synchronization and input EQ editing.
- MCP stdio: two separate clients share one owner without --demo; channel,
  routing, input processing, controls, name, conflicts and schema 3 export.
- Main and Inputs directly rendered from Avalonia and visually inspected.

Native platform release jobs separately restore/build/test and smoke-test MCP
from each self-contained desktop executable. CI results and package checks
are recorded with the release. No development environment or external runtime
is needed by the downloaded app.

## Remaining validation

No physical amplifier was read or written by the new backend during these
checks. Protocol evidence comes from private local controller inspection and
the captured lock command; synthetic compatibility does not prove firmware
compatibility, timing, response semantics or persistence after power loss.
Physical profiles remain marked unverified. No scans or firmware transfers
were performed. Native .sd parameter import remains unimplemented.

Direct Avalonia images do not prove OS screen capture or macOS/Windows ARM GUI
behavior. Native Windows capture previously timed out. Interactive GUI checks
on Mac/Windows ARM and live MCP tool loading in an AI client remain separate.
Windows packages are unsigned; Mac uses ad-hoc signing without notarization.
