# Validation — version 0.4.0

Windows x64 / .NET SDK 10.0.403:

- 112 core checks, including stable release selection, exact platform assets,
  SHA-256, staging/activation/reuse, rejected downloads and escaping paths,
  draft/prerelease filtering and analytic peak-EQ response checks.
- 63 network checks with synthetic loopback peers; no physical amplifier.
- 95 desktop checks, including plain gain labels, 0.5 dB keyboard steps,
  duplicate editor prevention, graph/field synchronization, compact matrix
  geometry, one update dialog and explicit check/download behavior.
- Main, Inputs and graphical EQ rendered directly and visually inspected.
- Shared-session MCP smoke test remains part of each native CI job.

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
were performed. Native .sd parameter import remains unimplemented.

Direct Avalonia images do not prove OS screen capture or macOS/Windows ARM GUI
behavior. Native Windows capture previously timed out. Interactive GUI checks
on Mac/Windows ARM and live MCP tool loading in an AI client remain separate.
Windows packages are unsigned; Mac uses ad-hoc signing without notarization.
