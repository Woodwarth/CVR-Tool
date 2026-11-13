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

Current partial live evidence: **31/38 documented function IDs (82%)**. This
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
time matches the front-panel reading and is shown in the header/details. Delay typing
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


## Local output-limiter protocol continuation

On the same DSP-1004, controlled output-A limiter commands establish nine more
function IDs (37/38/39/40/41/42/46/47/48), with independent full-settings
readback after each final restoration command. RMS automatic timing is
active-high, unlike limiter bypass. Its native codec and legacy writer are
corrected; default live limiter editing remains protected.

Release build: zero warnings/errors. Local runs passed 334 core, 257 synthetic
loopback-network and 177 desktop checks (768 total), plus shared-session MCP.
These software checks do not increase physical protocol coverage. Per-ID
percentage coverage is not defined: a command observation is partial evidence,
not full parameter-range, model, channel or firmware coverage.


## Local device-name boundary verification

DSP-1004/106119 accepted 14- and 31-byte ASCII names with exact independent
identity readback. A 32-byte name was shortened to 31 bytes; the application
reported the mismatch and restored the original name. All native DSP bytes
remained unchanged. Physical names now reject lengths above 31 before sending;
local demo names keep their previous limit. Persistence after a power cycle
and limits on other models/firmware remain untested. ID evidence stays 31/38.

Debug and Release builds passed without warnings/errors. Local validation
passed 338 core, 260 synthetic loopback-network and 177 desktop checks
(775 total), plus the shared-session MCP smoke test.


## 0.7.5 browser pairing and connection-loss handling

Desktop browser control shows a QR code generated locally from the complete
connection URL, including its access-key fragment. An independent decoder
recovered the exact URL/key from the actual Avalonia-rendered control. Clearing
the connection removes the QR payload. Dependency versions are locked.

A separate authenticated heartbeat continues during editing, uses a three-second
request timeout and expires the connection after five seconds without a
successful response. Failed requests and browser offline signals also pause
controls. A persistent banner and a warning inside open editors show connection
loss. Writes stay disabled until fresh state is loaded; drafts are preserved
without replay. Expired keys direct the user to the current Desktop QR code.
Returning from a hidden tab rechecks stale connection state immediately.

Local checks passed: 338 core, 263 synthetic loopback-network and 180 desktop
checks (781 total), plus integrated Desktop MCP smoke checks. Chrome touch checks
at tablet/phone sizes passed normal edits, offline operation, stalled/refused
server connections and expired keys during editing, followed by recovery without
replay or page errors. Physical Safari/iPad validation is still pending.
This work adds no physical protocol evidence: 31/38 IDs (82%), partial evidence.

## Local browser-control changes after 0.7.5 (unpublished)

Passed: 338 core, 271 loopback-network and 187 desktop checks (796 total), plus
integrated MCP smoke checks. The isolated Release build completed with zero
warnings/errors. HTTP checks cover second-client rejection, owner-only release,
explicit disconnect, timed takeover and cleared lifecycle indicators. Desktop
checks cover persistent Off/On/Connected status, its dialog, and UI-thread shutdown.

Chrome tablet/phone checks verify immediate dB preview and actual input-event
writes before release, coalescing rapid movements, exclusive browser ownership,
control handover, discarded queued gain on disconnect, and the existing
connection-loss/draft-recovery paths. Layouts have no horizontal page clipping.
The browser's controls now follow the Desktop layout and vector symbols.
Physical iPad/Safari and physical-amplifier gain-streaming validation remain
pending. No additional protocol evidence is claimed: 31/38 IDs (82%), partial.
