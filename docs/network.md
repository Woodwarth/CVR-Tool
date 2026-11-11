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

The live DSP view is read-only. Display lock (17/status 1) and standby
(15/status 0) are writable through the shared Desktop/MCP session. Each change
requires independent readback. Standby transitions are asynchronous: telemetry
must confirm the operational state, and native byte 32 must subsequently settle.
The command is sent once; only reads are repeated. Other native settings must
remain unchanged. Failed confirmation is an error, not an assumed success.

Native settings supply channel gain, trim, mute, delay, polarity, the full
routing matrix, input/output EQ and HP/LP, load mode, bridge, gate and limiter
settings. Current preset, firmware, name and MAC are shown when observed.
Telemetry supplies voltage/current, V/I load when current is positive, per-output
temperature/status and reported power. Uncalibrated dBFS/input clip, unread FIR
coefficients and unqueried preset-slot names are explicitly unknown. No healthy
status or unity FIR is synthesized. Known settings can be exported to PC.

MCP `connect_amplifier` still accepts an explicit unicast IPv4/local interface
with configurable ports and timeout; its default is the observed DSP-read-only
profile with supported operational controls. `readOnly=false` selects the older
individual-query implementation, whose physical compatibility remains unverified.
The Network tab and manual connection/disconnect UI buttons are absent.

Selected reads and operational writes were tested on one DSP-1004/106119. Output
polarity and Low-Z/70V command encodings were also captured from the original
controller. 100V was not selectable there; this does not establish a universal
model limitation. Two-channel profiles use the same feature model and have
synthetic coverage; physical two-channel validation is pending.

Desktop and MCP share one session owner. Protocol revisions are local estimates;
firmware has no reliable transaction identifier. Do not operate another writer
alongside the app. No firmware transfer is included. Captures, private presets,
device identifiers and manufacturer files are excluded from source/packages.
