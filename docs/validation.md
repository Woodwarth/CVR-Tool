# Validation

## 0.7.4

Windows x64: 330 core, 255 loopback-network and 177 desktop checks passed
(762 checks), plus shared-session MCP smoke checks. Release built with zero
warnings/errors. Embedded web checks verify access-key enforcement, host/origin
checks, shared state, validation, .sd export and lifecycle. Chrome touch tests
at tablet/phone widths passed delay/EQ/routing/mode edits and two-/four-channel
layouts without page errors or horizontal clipping. Physical Safari/iPad
validation remains pending.

Direct Avalonia renderings cover Main and protected editors. Inputs gesture checks verify
release-only routing commits without mute changes, graphical Peak-EQ drafts and
BW-12/BW-24 HP/LP drafts, while other filter types remain protected. Gain editing survives a live refresh and
accepts decimal commas. These UI checks use synthetic devices, not new physical evidence.

On the connected DSP-1004, input gain/mute, output mute/trim/delay/polarity,
input delay/Peak-EQ gain/frequency/Q and bypass, global input EQ bypass, routing
gain, gate enable/threshold, FIR bypass and device rename were independently
read back. Original native settings were restored byte-for-byte; the original
name was restored separately. Input HP/LP cutoff, BW-12/BW-24 type and bypass
also passed direct full-settings readback and byte-exact restoration. Low-Z/70V encoding remains controller-captured
evidence, not a repeated load-mode switching test.

Current partial live evidence: **22/38 documented function IDs (58%)**. This
is not full command-variant coverage or a percentage of firmware bytes.
No signal is currently available: dBFS meter and input-clip calibration remains
pending. Unknown readings remain explicit. Other models and platform GUI
validation remain pending. Reset, bridge, 100V, limiters, other crossover/non-Peak/output
EQ, real FIR transfer and device preset writes remain protected.

## Published 0.7.3 validation (historical)

Local Windows x64 checks:

- 283 core checks: processing plans, input gain, independent outputs, routing,
  active-low flags, native layout migration, JSON/native backups and restoration.
- 198 loopback network checks: existing controls plus observed bulk responses,
  shared endpoint ownership, two-/four-channel live profiles, bounded discovery,
  operational control readback and asynchronous standby settling.
- 149 desktop checks: existing layouts/editors, automatic device presentation,
  name/connection status, read-only DSP protection, operational control availability,
  details and backups, unknown FIR/slots, and demo/live coexistence.
- Shared-session MCP smoke checks and a warning-free Release build.

Physical DSP-1004 / firmware identifier 106119:

- Automatic wired-LAN discovery found the device without entering its IP.
- Direct identity, full-settings and live-telemetry reads succeeded.
- Display lock and standby were written and independently read back, then returned
  to their initial unlocked/off states. Native settings were restored byte-for-byte.
- Wink on/off was independently queried on the physical amplifier and restored
  to its initial state; the complete native settings stayed byte-identical.
  Display blinking itself requires visual observation.
- Standby telemetry changes after a delay; native byte 32 follows asynchronously.
  Only reads are repeated while waiting; setting commands are not retried.
- Original-controller captures additionally confirm input gain, output trim/mute,
  routing coordinates/mute, delay, input EQ parameters, output polarity and
  Low-Z/70V command encoding. 100V was unavailable in that controller/device
  combination. Transport ACKs alone never establish settings success.

Current protocol progress: 18/38 documented function IDs have at least partial
live evidence (47%). This is not complete read/write coverage or a percentage of
all firmware bytes. Unassigned functions and untested variants remain pending.
Raw captures, private native presets and identifiers are not distributed.

The release workflow checks self-contained Windows x64/ARM64 and macOS ARM64
packages, native update installation and both packaged MCP entry points. Each
package contains one runtime and no PDBs. No Intel Mac package is built.

## Remaining limits

Live DSP settings stay read-only except output/routing mute, display lock, standby and Wink. Individual
query replies, other physical models/firmware, power-loss persistence, input
meter/clip calibration, live FIR coefficients and preset-slot-name reads remain
unvalidated. No healthy readings or unity FIR defaults are invented. The older
individual-query backend remains synthetic-tested, not generally hardware-verified.

Desktop checks render Avalonia directly. Native Windows capture still timed out;
these renders do not establish OS capture or macOS/Windows ARM interactive GUI
behavior. Mac packages are ad-hoc signed, not notarized; Windows is unsigned.
No scans of IP ranges, firmware transfers or manufacturer files are included.

## Changes in this release

Direct output and route muting passed on the same physical amplifier, followed
by byte-for-byte restoration of its original settings. The function-71 operating
time matches the user reference and is shown in the header/details. Delay typing
links metres/ms in both directions; Trim opens from the whole tile; Stereo/Bridge
requires confirmation; PC loading/saving offers only .sd. Structured JSON API
interchange remains supported. Final runs passed: 283 core, 198 network and 149 desktop checks, plus the shared-session MCP smoke check; the Release build completed without warnings or errors.

## Local offline work after 0.7.3

The network/preset/UI parity audit is documented in
[preset-protocol-ui-audit.md](preset-protocol-ui-audit.md). Local runs passed
319 core, 208 network and 160 desktop checks, shared-session MCP checks and a
Release build with zero warnings/errors. Private DSP-1004/DSP-1502 samples and
settings-only derivatives also round-trip byte-identically; no private bytes
were added to tests or documentation. New transport tests use loopback only.
Native metadata is supplied through a focused shared-session query: reading
labels does not request FIR coefficients or capture a complete preset.
This work is local, has not been published and adds no hardware evidence.
The 18/38 (47%) partial live-evidence count is unchanged.
