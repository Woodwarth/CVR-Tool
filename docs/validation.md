# Validation — version 0.6.3

Windows x64 / .NET SDK 10.0.403:

- 248 core checks: per-output load mode, schema 5 migration, native mode preservation, paired legacy bridge, per-output gate/limiter isolation, read-only
  maximum voltages, timing/range checks, stale/expired plans, complete slot and
  JSON/native restoration, legacy JSON retention and existing processing checks.
- 93 network checks: independent loopback gate/limiter payloads and aggregate
  readback, bridge pair addressing, active-low flags, immutable limits, explicit
  readback rejection, FIR fragmentation and existing network/session checks.
- 118 desktop checks: per-output Low-Z/70V/100V editor, shared tiles/headings, A/B and C/D bridge editors, matched Trim/Mute blocks, gate On/Off and dB, both limiter
  sections, automatic timing, single editor activation and existing UI behavior.
- Shared-session stdio MCP verifies mode, gate, RMS/peak, full presets and
  previous channel/routing/input/output operations through two shared clients.
- Outputs and the limiter dialog directly rendered and visually inspected,
  alongside existing Main, Inputs, Presets, EQ and FIR rendering checks.
- Two private native CVR exports (DSP-1004/DSP-1502) were decoded, applied to local
  models and exported byte-for-byte unchanged. Files remain outside Git.

The README preview is a direct rendering of our local-demo Outputs controls;
it contains no physical-device measurements or private captures.

Release packages contain one shared runtime and exclude PDB debug symbols.
Both integrated desktop `--mcp` and the adjacent optional MCP apphost are checked.
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
Load mode, legacy bridge, noise gate and limiter writes are implemented from observed function
mappings and synthetic readback tests. Real amplifier behavior, automatic
limiter timing and bridge/load interactions require physical validation.
B118 files cannot store a gate threshold; B119 files retain it.

Direct Avalonia images do not prove OS screen capture or macOS/Windows ARM GUI
behavior. Native Windows capture previously timed out. Interactive GUI checks
on Mac/Windows ARM and live MCP tool loading in an AI client remain separate.
Windows packages are unsigned; Mac uses ad-hoc signing without notarization.
