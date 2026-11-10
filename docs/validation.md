# Validation — version 0.5.0

Windows x64 / .NET SDK 10.0.403:

- 169 core checks: crossover validation, all eleven options, isolated edits,
  read-only previews/revisions, and analytical Butterworth/LR/Bessel landmarks.
- 74 network checks: synthetic UDP readback for every crossover type, frequency
  and bypass setting, preserving all unedited EQ bytes. No physical amplifier.
- 94 desktop checks: HP/LP controls, all eleven dropdown choices, synchronized
  cutoff handles, applied settings, removed tabs/footer text and existing UI behavior.
- Shared-session MCP smoke test includes highPass/lowPass preview, apply and readback.
- Main, Inputs and HP/LP EQ directly rendered and visually inspected.

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
