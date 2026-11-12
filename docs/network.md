# CVR network access

The desktop automatically finds supported CVR devices on connected wired IPv4
interfaces using bounded identity broadcasts. Devices appear beside all three
demos. There are no IP-range probes or Wi-Fi discovery. Discovery and device
reads serialize access to the same local UDP endpoint, allowing several devices
to coexist. Close other amplifier controllers while using CVR Tool.

The observed live profile reads identity (function 0), native settings (27) and
telemetry (6). Settings refresh about every five seconds; status about every
half-second. Only supported reply shapes, source peers, lengths and checksums
are accepted. Full-sync completion uses an empty status-3 confirmation. Settings
and telemetry have separate timestamps; offline devices keep their last data
with an error and a red connection point. No automatic settings retries occur.

The observed profile permits input gain/mute, output trim/mute/delay/polarity,
routing gain/mute, input delay, input Peak-EQ gain/frequency/Q and bypass,
global input EQ bypass, HP/LP cutoff/type/bypass for BW-12 and BW-24,
Low-Z/70V selection, noise-gate enable/threshold,
FIR bypass and device renaming. Writes require fresh state and independent
readback; unrelated native bytes must remain unchanged. Reset, bridge, 100V,
limiters, other crossover/EQ types, output EQ, FIR coefficient transfer and device
preset operations remain protected pending variant-specific evidence.

Display lock (17/status 1) and standby
(15/status 0) are writable through the shared Desktop/MCP session. Each change
requires independent readback. Wink (83/status 0, byte 0/1) blinks the display;
function-83 queries return status 1/channel 32/segment 10/side 0 with one state
byte. Only 0/1 are accepted; missing state disables the live Wink control. Wink
refreshes about every five seconds, and original DSP bytes must remain intact. Standby transitions are asynchronous: telemetry
must confirm the operational state, and native byte 32 must subsequently settle.
Output and routing mute use active-low flags and full-settings readback; routing
gain and all unrelated native bytes must stay unchanged. The command is sent
once; only reads are repeated. Other native settings must
remain unchanged. Failed confirmation is an error, not an assumed success.

Native settings supply channel gain, trim, mute, delay, polarity, the full
routing matrix, input/output EQ and HP/LP, load mode, bridge, gate and limiter
settings. Current preset, firmware, name and MAC are shown when observed. Function 71
provides operating time as a uint32 little-endian minute counter at offset 74;
metadata refreshes once a minute and unknown time stays unknown.
Telemetry supplies voltage/current, V/I load when current is positive, per-output
temperature/status and reported power. Uncalibrated dBFS/input clip, unread FIR
coefficients and unqueried preset-slot names are explicitly unknown. No healthy
status or unity FIR is synthesized. Known settings can be exported to PC.

MCP `connect_amplifier` still accepts an explicit unicast IPv4/local interface
with configurable ports and timeout; its default is the observed restricted
profile with verified editing. The legacy `ReadOnly=true` option selects this
profile, not a blanket prohibition on writes: individual capabilities govern
permissions. `readOnly=false` selects the older
individual-query implementation, whose physical compatibility remains unverified.
The Network tab and manual connection/disconnect UI buttons are absent.

Selected reads and the enabled DSP writes were tested on one DSP-1004/106119. Output
polarity and Low-Z/70V command encodings were also captured from the original
controller. 100V was not selectable there; this does not establish a universal
model limitation. Two-channel profiles use the same feature model and have
synthetic coverage; physical two-channel validation is pending.

Desktop and MCP share one session owner. Protocol revisions are local estimates;
firmware has no reliable transaction identifier. Do not operate another writer
alongside the app. No firmware transfer is included. Captures, private presets,
device identifiers and manufacturer files are excluded from source/packages.

## Local fault-recovery changes after 0.7.3

Queries ignore corrupt complete messages and retain bounded fragmented replies
across identity announcements. Identical duplicate/out-of-order fragments are
accepted; conflicting duplicates are discarded. At most four partial messages
are retained, with oldest-first eviction. Stale draining is limited to 64
packets. Timeouts do not automatically resend requests or writes. Connection
handshakes run outside the device registry lock, keeping cached/demo reads free.
The protocol has no transaction ID: a delayed otherwise identical reply cannot
always be distinguished from a current one. Independent state readback remains
required. Offline UI controls are disabled while cached settings stay viewable.

See [preset/protocol/UI audit](preset-protocol-ui-audit.md) for preserved fields
that still need controls or physical evidence.
