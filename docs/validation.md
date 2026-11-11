# Validation — 0.7.1

Local Windows x64 checks:

- 282 core checks: processing plans, input gain, independent outputs, routing,
  active-low flags, native layout migration, JSON/native backups and restoration.
- 184 loopback network checks: existing controls plus observed bulk responses,
  shared endpoint ownership, two-/four-channel live profiles, bounded discovery,
  operational control readback and asynchronous standby settling.
- 138 desktop checks: existing layouts/editors, automatic device presentation,
  name/connection status, read-only DSP protection, operational control availability,
  details and backups, unknown FIR/slots, and demo/live coexistence.
- Shared-session MCP smoke checks and a warning-free Release build.

Physical DSP-1004 / firmware identifier 106119:

- Automatic wired-LAN discovery found the device without entering its IP.
- Direct identity, full-settings and live-telemetry reads succeeded.
- Display lock and standby were written and independently read back, then returned
  to their initial unlocked/off states. Native settings were restored byte-for-byte.
- Standby telemetry changes after a delay; native byte 32 follows asynchronously.
  Only reads are repeated while waiting; setting commands are not retried.
- Original-controller captures additionally confirm input gain, output trim/mute,
  routing coordinates/mute, delay, input EQ parameters, output polarity and
  Low-Z/70V command encoding. 100V was unavailable in that controller/device
  combination. Transport ACKs alone never establish settings success.

Current protocol progress: 16/37 documented function IDs have at least partial
live evidence (43%). This is not complete read/write coverage or a percentage of
all firmware bytes. Unassigned functions and untested variants remain pending.
Raw captures, private native presets and identifiers are not distributed.

The release workflow checks self-contained Windows x64/ARM64 and macOS ARM64
packages, native update installation and both packaged MCP entry points. Each
package contains one runtime and no PDBs. No Intel Mac package is built.

## Remaining limits

Live DSP settings stay read-only except display lock and standby. Individual
query replies, other physical models/firmware, power-loss persistence, input
meter/clip calibration, live FIR coefficients and preset-slot-name reads remain
unvalidated. No healthy readings or unity FIR defaults are invented. The older
individual-query backend remains synthetic-tested, not generally hardware-verified.

Desktop checks render Avalonia directly. Native Windows capture still timed out;
these renders do not establish OS capture or macOS/Windows ARM interactive GUI
behavior. Mac packages are ad-hoc signed, not notarized; Windows is unsigned.
No scans of IP ranges, firmware transfers or manufacturer files are included.
