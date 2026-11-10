# Manual CVR network access

Network accepts one explicit unicast IPv4 peer and local interface, with
configurable UDP ports and timeout. Defaults observed in the original
controller are amplifier 45455 and controller 45454. Close other controllers
if they already occupy the source port. No DNS, broadcast discovery, scanning,
automatic reconnection, firmware transfer or retrying writes is implemented.

Connect from the Network page or MCP `connect_amplifier`. Connection reads
identity and all output settings before exposing the device. Only two-/four-
channel identity layouts are accepted. An unsupported response fails instead
of exposing simulated parameter defaults. The original offline preparation
and loopback-only transport remain available as separate components.

The owner serializes requests. Incoming fragments have bounded sizes, length
and checksum validation; transport acknowledgements are not semantic success.
Gain, mute, delay, polarity, routing, EQ, name and control writes are followed
by reads. A timeout or mismatch is an error, even if the write reached the amp.
There is no atomic hardware rollback. Session plans are invalidated on every
write attempt and observed setting change. Firmware does not supply a reliable
transaction ID or atomic revision; do not operate another controller alongside
this app. Identical delayed replies cannot always be distinguished.

Basic identity, per-output settings, routing and input EQ formats were examined
locally in the installed controller. The display-lock frame also matches the
private capture. Independent synthetic loopback peers verify the implemented
contract. **No physical amplifier has been exercised with this implementation.**
Different firmware can reject a query; unsupported paths report errors.

Device-side names currently use ASCII, 1–32 bytes. Readback confirms the current
name; retention across a power cycle depends on firmware and is untested.
Input filter edits preserve crossover records, type codes and reset bytes.
The editor changes frequency/gain/Q/enable state, not filter type. Application
edit ranges are conservative limits, not verified physical model specifications.

Telemetry supports known 56/72/76/88/92/96-byte four-slot and 32-byte two-slot
layouts. Per-channel temperature stays unknown in the 32-byte layout. Load is
V/I when I > 0. Output level in dBFS, Hi-Z and bridge remain unknown without
verified calibration/status data. Status codes are enum values, not a bitmask.

Manual target configuration is saved under the user's local application data.
It contains addresses, not credentials; no automatic connection happens at startup.
Private captures, presets, controller binaries and analysis artifacts are ignored
and excluded from source control and packages.
