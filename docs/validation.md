# Validation — version 0.6.0

Windows x64 / .NET SDK 10.0.403:

- 198 core checks: output EQ isolation, stale plans, FIR bounds and snapshots,
  full JSON/native restoration, both sync versions and unnamed/named FIR layouts,
  opaque-byte preservation, all sixteen slots, clear/reset and existing checks.
- 85 network checks: independent loopback framing, fragmented 512-tap FIR upload
  and readback, output EQ side selection, preset names, semantic command ACKs,
  readback failures, shared IPC and existing channel/routing checks.
- 104 desktop checks: output signal flow, output EQ including HP/LP, single FIR
  editor with three plots, preset actions/slots, reset cancellation and plain
  click-to-edit gain, plus existing resizing/Main/input behavior.
- Shared-session stdio MCP verifies output EQ/FIR, native export/import,
  sixteen-slot operations and explicit bulk-operation confirmation.
- Main, Inputs, Outputs, Presets and FIR directly rendered and visually inspected.
- Two private native CVR exports (DSP-1004/DSP-1502) were decoded, applied to local
  models and exported byte-for-byte unchanged. Files remain outside Git.

Release jobs additionally extract their real self-contained package through
the updater and run integrated MCP from the installed copy. This checks ZIP
on both Windows architectures and tar.gz/file permissions on Apple Silicon.
Download integrity and staging are tested without replacing this development
workspace. Automated checks do not perform an interactive full GUI restart.

## Remaining validation

No physical amplifier was read or written by the new backend during these
checks. Protocol evidence comes from private local controller inspection and
the captured lock command; synthetic compatibility does not prove firmware
compatibility, timing, response semantics or persistence after power loss.
Physical profiles remain marked unverified. No scans or firmware transfers
were performed. Native `.sd` support is limited to evidenced B-series 106118/106119 layouts.
Limiter, mode and noise-gate editing remain unavailable; original native bytes
are retained when importing and saving supported `.sd` files.

Direct Avalonia images do not prove OS screen capture or macOS/Windows ARM GUI
behavior. Native Windows capture previously timed out. Interactive GUI checks
on Mac/Windows ARM and live MCP tool loading in an AI client remain separate.
Windows packages are unsigned; Mac uses ad-hoc signing without notarization.
