# Implemented CVR protocol facts

Evidence: local static inspection of the installed AMP Controller and offline
comparison with the private display-lock capture. No manufacturer source,
binary, device identifiers or capture is included in this repository. The
implementation is independently written from the observed wire layout.
Selected operations have direct physical evidence on DSP-1004/106119; see
Validation for the exact scope. Other models/firmware remain unverified.

## Framing

Outer header, ten packed bytes: little-endian magic 55555 (offset 0), model
short (2), fragment count (4), last-fragment length short (5), one-based fragment
index (7), data/ACK byte (8), padding (9). Fragments contain at most 450 bytes.
ACK copies the received header with offset 8 set to 1. Padding is preserved.
The decoder bounds counts to 64 and validates exact datagram lengths.

Inner header: 0x55, function, status, channel, segment, four little-endian link
bits, input/output side byte. Link is a channel mask, not a transaction ID.
Status 0 is a response or a command requesting semantic acknowledgement,
1 a write, 2 a request and 3 a semantic command acknowledgement. Trailer: big-endian total inner
length (two bytes), then sum of preceding inner bytes modulo 256.
Channels and segments are zero-based on the wire.

## Supported mappings

| Function | Operation | Body |
| --- | --- | --- |
| 0 | Basic identity read | 75/79 bytes; model 0..31, name 32..63, analog inputs at 71, outputs at 73 |
| 6 | Telemetry read | Known layout sizes; see network notes |
| 9 | Input gain / output trim | Little-endian float; side 0=input, 1=output |
| 10 | Output mute | Active-low: byte 0 muted, 1 unmuted; side 1 |
| 12 | Routing | Float gain + active-low flag (0 muted, 1 enabled); channel=output, segment=input, side=0 |
| 14 | Delay | Float milliseconds; side 0=input, 1=output |
| 15 | Standby | Status-0 command; byte 1 on, 0 off. Telemetry and native-state settling confirm writes |
| 17 | Display lock | Byte 0/1; command matches captured frame |
| 18 | Output polarity | Controlled output A: byte 1=180°, 0=0°; side 1 |
| 16 | Factory reset | Command body byte 1; semantic ACK required |
| 27 | Full sync | B118/B119: 2232/2687 bytes |
| 30 | Input Peak band bypass/type | Controlled Peak slot: FF bypass, 00 active; channel=input, segment=filter, side 0. Channel-254 initialization semantics remain unconfirmed |
| 31 | Input EQ band gain | Float32 dB; channel=input, segment=filter slot, side 0 |
| 32 | Input EQ frequency | Float32 Hz; channel=input, segment=filter slot, side 0; controlled EQ5 test |
| 34 | Input EQ Q | Float32 Q; channel=input, segment=filter slot, side 0 |
| 36 | Input EQ global bypass | Side 0; byte 1 bypassed, 0 enabled; verify state through filter block |
| 37/38/39 | RMS attack/release/threshold | Int16 ms / byte ×Attack / float Vrms; side 1 |
| 40/41/42 | Peak hold/release/threshold | Int16 ms / Int16 ms / float Vpeak; side 1 |
| 46/47 | RMS/peak bypass | Byte 0 enabled, 1 bypassed; side 1 |
| 48 | RMS automatic timing | Byte 0 automatic, 1 manual; side 1 |
| 49 | Output load mode | Controlled status-0 command: byte 0 Low-Z, 1 70V; per channel, side 1. Value 2 (100V) remains unverified |
| 50 | Stereo/bridge pair | Byte 0 bridge, 1 stereo; channel is zero-based pair, side 1 |
| 54/55 | Peak/RMS limiter blocks | 13 packed bytes including read-only voltage maximum |
| 69 | Noise-gate bypass | Byte 0 enabled, 1 bypassed; side 1 |
| 83 | Wink / display blinking | Status-0 command, channel/segment/side 0/0/0; byte 0 off, 1 on. Status-2 query replies status 1/channel 32/segment 10/side 0 with one byte |
| 87 | Noise-gate threshold | Signed byte, -100..0 dB; side 1 |
| 43 | Output FIR | 512 LE floats, optionally preceded by a 32-byte name |
| 44 | FIR bypass | Byte 0 enabled, 1 bypassed; side 1 |
| 52 | Input/output filter block | 141 bytes; side 0/1 |
| 59 | Device presets | 34-byte mode/slot/name; mode 0 reads sixteen names, 1 stores, 2 recalls, 3 clears |
| 60 | Device name write | 32 padded bytes; read back through function 0 |
| 71 | Extended identity / operating time | Empty status-2 request; 93-byte status-1 reply, model header at 42, LE uint32 minutes at 74 |

The filter block contains ten packed 14-byte records and one global bypass
byte. Each record: signed type, float gain, float frequency, float Q, reset
byte. Records 1..8 are EQ; first/last are HP/LP crossovers; untouched fields are preserved.
Their signed type IDs map to BW-12, BE-12, LR-12, BW-18, BW-24, BE-24,
LR-24, BW-36, BW-48, BE-48 and LR-48 (0 through 10).
Negative type encodes disabled as -type-1; global bypass 0 means enabled.
No filter type is inferred from screenshot appearance.

The test suite includes a synthetic display-lock fixture and an independently
built UDP peer, readback rejection and timeouts. These demonstrate software
behavior, not physical-amplifier compatibility or persistence after power loss.

Large writes use 450-byte fragments with bounded counts and matching transport
ACKs for each fragment. A missing ACK reports a potentially partial write; no
automatic retries are made. Transport delivery is never taken as write success.
FIR/sync changes use independent readback; preset commands require status-3
acknowledgement and subsequent names/settings reads. See [native presets](native-presets.md).

RMS block offsets: Attack int16 at 0, Release multiplier byte at 2, Threshold
float at 3, Bypass at 7, Auto at 8, Maximum voltage float at 9. Peak block:
Hold int16 at 0, Release int16 at 2, Threshold float at 4, Bypass at 8,
Maximum voltage float at 9. Maximums are not editable. Individual function
writes require readback and final aggregate reads; existing protection is not
bypassed while threshold/timing parameters are edited.

## Local D1004 passive observations

A controlled local capture of DSP-1004 firmware identifier 106119 confirms
function 17, status 1, one-byte body 1 (display lock) and 0 (unlock). The device
sent transport ACKs after both commands; no semantic setting readback was
captured. Standby and audio settings were not changed by the analysis agent.

These device ACKs have count=0, last-length=450, index=1 and a ten-byte ACK
header, rather than echoing the request fields. The decoder accepts only this
specific additional framing shape. Fragment-write correlation remains strict;
this observation is not evidence for accepting such an ACK for every fragment.

Bulk reads observed from the original controller are function 27 (2687-byte
settings) and function 6 (92-byte status). Their device replies use status 1
and channel/segment 32/10 instead of echoing 0/0. The application transport
still needs a narrowly scoped response profile and further query evidence;
these passive captures do not establish active application compatibility.
Input volume at native channel offset 369 is distinct from output trim at 44.
No captures, addresses, device names or identifiers are included in this note.

A second controlled capture contains exactly two input-A gain writes: function 9,
status 1, channel/segment/side 0/0/0, float32 LE bodies 4.0 and 4.5 dB.
Transport ACKs followed both commands; this does not establish semantic readback.
Main faders, MCP input-processing edits and schema-7 presets now keep input gain
separate from output trim. The existing -80 to +18 dB application limits are
conservative UI limits, not a fully measured physical-device range. Individual
gain queries and output-trim writes still require physical validation. Main input
meter/clip mapping remains pending independent telemetry calibration.

A controlled output-A trim capture confirms the separate function-9/status-1,
channel/segment/side 0/0/1 write path. Its float32 LE bodies are -0.5 and 0.0 dB.
Both have following transport ACKs; no individual gain-query response was
observed. Sanitized encoder fixtures cover both commands. A later full sync
after reopening independently confirmed the preceding input-A volume at +4.5 dB.
The live 2687-byte settings block also round-trips through the native codec;
sync-only exports now preserve the absence of FIR blocks and their footer.

A user-confirmed controlled output-A mute test identifies function 10/status 1,
channel/segment/side 0/0/1, body 00 for mute and 01 for unmute. This corrects
an earlier inverted interpretation. Native output flag offset 48 also decodes
1 as unmuted in the independently captured pre-test full sync, matching the
user-confirmed initial state. Backend reads/writes and native import/export
now map this active-low flag to the app's ordinary true=muted boolean. Existing
App-JSON booleans are not reinterpreted. Routing flags have independent semantics
and are not changed from this observation. All four 92-byte telemetry status
fields remained code 8 throughout the test; they do not report this mute change.
Transport ACKs followed both commands; no individual mute-query response or
post-test settings readback was captured.

A controlled delay capture, explicitly identified by the user as Output A,
confirms function 14/status 1, channel/segment/side 0/0/1. Float32 LE bodies
are 8.89 ms (8.890000343322754 on decode) and 0 ms. Both have following transport
ACKs; no independent setting readback was captured. This validates the output
write encoding only; a controlled input-delay test is still pending.

A separate controlled Input A delay test confirms function 14/status 1,
channel/segment/side 0/0/0 with float32 LE 8.89 and 0 ms. This independently
confirms the side-0 input versus side-1 output distinction. Both commands have
following transport ACKs; individual query responses remain unobserved.

A user-confirmed Input 1 -> Output A matrix test sends function 12/status 1,
channel/segment/side 0/0/0, five-byte bodies: float32 gain 0 dB followed by 00
(muted) then 01 (enabled). Routing mute is therefore active-low as independently
established by this test, correcting the earlier assumption. The native pre-test
route 1-A flag is also 1, matching its user-confirmed enabled state. Backend and
native route flags are corrected; JSON true=muted semantics remain unchanged.
A diagonal route cannot establish axis order. Controlled off-diagonal evidence
and independent post-test readback are still pending.

Progress is reported by distinct documented function IDs with at least partial
live evidence: 8/31 (26%) after routing function 12. This is not a percentage of
all firmware data or fully validated read/write behavior; undocumented functions
may exist. IDs counted: 0, 6, 9, 10, 12, 14, 17 and 27.

An off-diagonal controlled Input 2 -> Output A test establishes routing wire
coordinates: function 12/status 1, channel 0 (output A), segment 1 (input 2),
side 0. This corrects the former transposed backend addressing. The user had to
unmute to edit in the original controller and dragged with the mouse. Captured
gain values were 0, -0.5, -2.7, -1.7, -1.2, 0 and +0.2 dB while enabled, followed
by mute with +0.2 dB retained. Do not report a return to zero: the last transmitted
value is +0.2 dB, muted. All eight commands have valid lengths/checksums and
following transport ACKs. Native off-diagonal storage order still requires
independent full-sync comparison. Progress remains 8/31 functions (26%).

Independent full sync after the off-diagonal test confirms +0.2 dB/muted for
Input 2 -> Output A: only bytes 65..68 differ from the earlier snapshot. This
establishes native output-major routing storage: record `36+(output-1)*515`,
float gain at `24+(input-1)*5`, active-low flag immediately following. The codec
now uses that order for both decode and encode. The original controller then
sent a full function-27/status-1 settings write; a subsequent device full sync
shows the route at 0 dB/muted. The user selected No at the close/save prompt.
Do not interpret that prompt as a guarantee of no device writes or establish
power-loss persistence from this sequence. Unknown changed bytes after offset
2160 remain opaque. Both captured settings blocks round-trip byte-for-byte.
The separately provided identity screenshot confirms four inputs/four outputs
and firmware 106119. No additional function ID is counted: progress stays 26%.

A controlled Input 1 global EQ off/on test sends function 36/status 1,
channel/segment/side 0/0/0, bodies 01 then 00, with following transport ACKs.
The backend now uses this targeted command for input EQ enable/bypass and
independently verifies byte 140 and unchanged filter records through function 52.
No individual function-36 read semantics or active application access have been
validated. Output EQ behavior is not generalized from this input-only test.
An additional function-32/status-1, channel/segment/side 0/1/0 float32 60 command
precedes the bypass toggles. That matches the pre-test EQ1 frequency numerically,
but its meaning remains unconfirmed until a controlled parameter change.
The EQ_IN_1 screenshot confirms global ON and visible EQ5/EQ6 settings; it
supplies a visual reference, not independent device readback.

The progress denominator now includes the newly documented IDs 30, 32 and 36,
including the still-unassigned observed commands. Current partial live evidence
is 9/34 functions (26% rounded): 0, 6, 9, 10, 12, 14, 17, 27, 36. This retains
the distinction between observed command meaning and complete read/write proof.

A separate controlled Input 1 / EQ5 frequency test confirms function 32/status 1,
channel 0 / segment 5 / side 0, float32 LE 1250 then 1200 Hz. Both have following
transport ACKs; there is no independent device settings readback in this capture.
The input backend uses targeted function 32 for frequency-only band changes and
verifies the expected whole filter block through function 52 after each changed
band. Other EQ fields, channels and filters must remain unchanged; aggregate
mismatch is an error. Individual function-32 queries and output-side behavior
are not inferred. Current partial live evidence: 10/34 function IDs (29%).

The controlled Input 1 / EQ5 gain test confirms function 31/status 1,
channel 0 / segment 5 / side 0, float32 +4.5 then +5 dB. A preceding
function-32 transfer repeats 1200 Hz without changing the frequency. All commands
have valid framing and following transport ACKs; semantic device readback is
not captured. Input gain-only and combined gain/frequency band edits now use
functions 31/32 with independent aggregate verification after each field.
Q, enable/type and crossover edits retain their separate existing block path;
output-side parameter commands are not inferred. Progress: 11/35 IDs (31%).

The controlled Input 1 / EQ5 Q test confirms function 34/status 1,
channel 0 / segment 5 / side 0, float32 3.5 then 4. The original controller
also repeats 4 before editing and transmits 3.5 and 4 multiple times; these
are captured UI sends, not agent retries. A preceding function-31 write repeats
+5 dB. Following transport ACKs do not constitute semantic setting readback.
Input band gain/frequency/Q edits now use functions 31/32/34 and independently
verify the filter block after each changed field. Type/enable and crossover
changes retain their existing block path. Progress: 12/36 function IDs (33%).

Input 1 / EQ5 Peak bypass is now independently captured as function 30/status 1,
channel 0 / segment 5 / side 0: FF bypass, 00 active. No gain/frequency/Q writes
occur in this capture. The targeted input path is enabled only for observed
Peak type 0 when bypass changes; other types retain the block path. Readback
must match the aggregate, including unchanged filter parameters. This does not
validate the earlier special channel-254 initialization traffic or output side.
Transport ACKs are not independent setting readback. Progress: 13/36 IDs (36%).

Output A polarity is now controlled-capture confirmed: function 18/status 1,
channel/segment/side 0/0/1, body 01 for 180 degrees and 00 for normal polarity.
Both commands have valid framing and following transport ACKs; post-test settings
readback is not yet captured. Existing backend/native mapping agrees. Original
controller startup also queries function 71 and receives 93 bytes; this remains
unassigned and expands the progress denominator. Current count: 14/37 IDs (38%).

Output A load-mode testing confirms function 49 uses status 0 (not ordinary
setting-write status 1), channel/segment/side 0/0/1, bodies 01 for 70V then
00 for Low-Z. Output A was muted during switching and subsequently unmuted.
100V could not be selected in the user's controller/device combination; no
100V command is claimed as physically tested, and this observation is not a
global model capability declaration. Existing individual-query backend mode
writes now use status 0. Transport ACKs are not semantic mode readback.
Current partial live evidence is 15/37 function IDs (41%).

The controlled standby test confirms function 15/status 0, channel/segment/side
0/0/0, bodies 01 then 00. Direct application validation established that telemetry
codes change to 1 on all outputs for standby; after exit a transient code 2 can
precede normal code 8. Native byte 32 updates asynchronously, so an immediate
full-sync read is insufficient. The live backend sends once and waits on fresh
telemetry plus settled native state, rejecting unrelated native changes. No
individual function-15 query semantics are assumed. Both operational controls
were successfully set/restored on the real device; original native settings
matched byte-for-byte afterwards. Current progress: 16/37 IDs (43%).

Extended identity function 71 is now directly validated: the minute counter at
offset 74 matches the user's operating-hours reference. The embedded header at
42..73 must match the connected device; malformed/mismatched metadata is rejected.
Direct application tests also confirm output and matrix mute writes with fresh
full-settings readback; route gain is preserved. Only previously active paths
were muted for testing; all native settings were restored byte-for-byte.
Current partial live evidence: 17/37 function IDs (46%).

Wink function 83 now has direct device evidence: status-0 commands 01 and 00
are independently confirmed using status-2 queries with bulk-shaped replies
(status 1, channel 32, segment 10, side 0, one byte). Ordinary echo-shaped query
matching and status-3 command replies do not apply to this tested firmware.
The test restored initial Wink off and left the complete native settings
byte-identical. This is device state confirmation; visible blinking needs user
observation. Progress: 18/38 documented function IDs (47%) with partial live evidence.
