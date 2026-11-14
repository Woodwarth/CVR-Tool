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
| 10 | Input/output mute | Active-low: byte 0 muted, 1 unmuted; side 0=input, side 1=output |
| 12 | Routing | Float gain + active-low flag (0 muted, 1 enabled); channel=output, segment=input, side=0 |
| 14 | Delay | Float milliseconds; side 0=input, 1=output |
| 15 | Standby | Status-0 command; byte 1 on, 0 off. Telemetry and native-state settling confirm writes |
| 17 | Display lock | Byte 0/1; command matches captured frame |
| 18 | Output polarity | Controlled output A: byte 1=180Â°, 0=0Â°; side 1 |
| 16 | Factory reset | Command body byte 1; semantic ACK required |
| 27 | Full sync | B118/B119: 2232/2687 bytes |
| 30 | Input Peak band bypass/type | Controlled Peak slot: FF bypass, 00 active; channel=input, segment=filter, side 0. Channel-254 initialization semantics remain unconfirmed |
| 31 | Input EQ band gain | Float32 dB; channel=input, segment=filter slot, side 0 |
| 32 | Input EQ frequency | Float32 Hz; channel=input, segment=filter slot, side 0; controlled EQ5 test |
| 34 | Input EQ Q | Float32 Q; channel=input, segment=filter slot, side 0 |
| 36 | Input EQ global bypass | Side 0; byte 1 bypassed, 0 enabled; verify state through filter block |
| 37/38/39 | RMS attack/release/threshold | Int16 ms / byte Ã—Attack / float Vrms; side 1 |
| 40/41/42 | Peak hold/release/threshold | Int16 ms / Int16 ms / float Vpeak; side 1 |
| 46/47 | RMS/peak bypass | Byte 0 enabled, 1 bypassed; side 1 |
| 48 | RMS automatic timing | Controlled output A: byte 1 automatic, 0 manual; side 1. Unlike limiter bypass, this flag is active-high |
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
| 60 | Device name write | 32-byte field; observed DSP-1004 accepts at most 31 ASCII bytes plus NUL; read back through function 0 |
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

A observed controlled output-A mute test identifies function 10/status 1,
channel/segment/side 0/0/1, body 00 for mute and 01 for unmute. This corrects
an earlier inverted interpretation. Native output flag offset 48 also decodes
1 as unmuted in the independently captured pre-test full sync, matching the
observed initial state. Backend reads/writes and native import/export
now map this active-low flag to the app's ordinary true=muted boolean. Existing
App-JSON booleans are not reinterpreted. Routing flags have independent semantics
and are not changed from this observation. All four 92-byte telemetry status
fields remained code 8 throughout the test; they do not report this mute change.
Transport ACKs followed both commands; no individual mute-query response or
post-test settings readback was captured.

A controlled delay capture, identified as Output A,
confirms function 14/status 1, channel/segment/side 0/0/1. Float32 LE bodies
are 8.89 ms (8.890000343322754 on decode) and 0 ms. Both have following transport
ACKs; no independent setting readback was captured. This validates the output
write encoding only; a controlled input-delay test is still pending.

A separate controlled Input A delay test confirms function 14/status 1,
channel/segment/side 0/0/0 with float32 LE 8.89 and 0 ms. This independently
confirms the side-0 input versus side-1 output distinction. Both commands have
following transport ACKs; individual query responses remain unobserved.

A observed Input 1 -> Output A matrix test sends function 12/status 1,
channel/segment/side 0/0/0, five-byte bodies: float32 gain 0 dB followed by 00
(muted) then 01 (enabled). Routing mute is therefore active-low as independently
established by this test, correcting the earlier assumption. The native pre-test
route 1-A flag is also 1, matching its observed enabled state. Backend and
native route flags are corrected; JSON true=muted semantics remain unchanged.
A diagonal route cannot establish axis order. Controlled off-diagonal evidence
and independent post-test readback are still pending.

Progress is reported by distinct documented function IDs with at least partial
live evidence: 8/31 (26%) after routing function 12. This is not a percentage of
all firmware data or fully validated read/write behavior; undocumented functions
may exist. IDs counted: 0, 6, 9, 10, 12, 14, 17 and 27.

An off-diagonal controlled Input 2 -> Output A test establishes routing wire
coordinates: function 12/status 1, channel 0 (output A), segment 1 (input 2),
side 0. This corrects the former transposed backend addressing. The test required an operator to
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
shows the route at 0 dB/muted. The editor was closed without saving.
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
100V could not be selected in the tested controller/device combination; no
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
offset 74 matches the front-panel operating time. The embedded header at
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

Controlled Input 1 mute/unmute confirms function 10/status 1, channel/segment/side
0/0/0 with byte 00 muted then 01 enabled, approximately 2.53 seconds apart.
Both checksums match; the original native input-mute flag at absolute offset
2192 was 01. Final independent full-settings readback remains pending. Earlier
function-30/channel-254 startup traffic is excluded from this interpretation.
Function 10 already had output-side evidence, so progress stays 18/38 IDs (47%).

Controlled Output A noise-gate on/off confirms function 69/status 1,
channel/segment/side 0/0/1: byte 00 enabled, 01 bypassed. The valid commands
are approximately 3.57 seconds apart and bracketed by output mute/unmute.
The initial gate state was off; no threshold write occurred.
Final independent full-settings readback remains pending. This adds one
live-evidenced function: 19/38 documented IDs (50%).

Controlled Output A gate threshold confirms function 87/status 1,
channel/segment/side 0/0/1: signed byte BF (-65 dB) then BA (-70 dB),
approximately 5.30 seconds apart. Output mute/unmute brackets the edits; no
function-69 gate toggle appears in this capture. Checksums match and the
requested original threshold is restored by the controller command. Final
independent native readback remains pending. Progress: 20/38 IDs (53%).

Controlled Output A FIR on/off confirms function 44/status 1,
channel/segment/side 0/0/1: byte 00 active, 01 bypassed, approximately 3.01
seconds apart. Output mute/unmute brackets the edits. No function-43 transfer
or fragmented outgoing data appears in this capture; coefficients were not
changed. The originally off state was requested again. Final independent
native readback remains pending. Progress: 21/38 documented IDs (55%).

After the controlled input-mute, gate enable/threshold and FIR-bypass sequence,
an independent function-27 read confirms all 2687 native settings bytes match
the original pre-test block. Input 1 and Output A are enabled, gate/FIR are
bypassed and gate threshold is -70 dB. This verifies the restored final state
and absence of unrelated native changes; intermediate native states were not
queried. Partial function coverage remains 21/38 (55%).

Direct Main validation confirms input gain, input mute and device rename with
independent fresh reads. Function 60/status 1 sends a 32-byte ASCII name;
function-0 identity queries independently confirm the temporary and restored
name. The complete native settings block is unchanged after restoration.
This adds function 60 to live evidence: 22/38 documented IDs (58%).

The limited live writer additionally passed fresh native readback after each
output trim/delay/polarity, input delay, input Peak gain/frequency/Q/band/global
bypass, routing gain, gate enable/threshold and FIR-bypass edit. Outputs were
muted first and restored last; all original native bytes matched afterwards.
Load-mode switching was not repeated; its Low-Z/70V evidence remains the earlier
controlled capture. 100V, bridge, limiters, HP/LP, non-Peak types, output EQ, FIR
coefficient transfer, device preset operations and reset remain protected.

Controlled Input 1 HP enable/bypass confirms function 30/status 1,
channel/segment/side 0/0/0: 00 enabled then FF bypassed, approximately 2.59 s
apart (frames 1323/1397 in the private crossover capture). All outputs were
muted first. Checksums match. A preceding function-32/segment-1 transfer repeats
60 Hz for EQ1; it is not interpreted as HP frequency. Channel-254 startup
traffic is excluded. Independent native readback is still pending; this does
not enable all crossover types. Function 30 already counts toward live evidence,
so progress remains 22/38 documented IDs (58%).

Controlled Input 1 HP frequency confirms function 32/status 1,
channel/segment/side 0/0/0, float32 LE 80 Hz then 20 Hz, approximately 4.19 s
apart (frames 12429/12553); a second 20 Hz send precedes HP bypass. Checksums
match. The same interval also contains function-37 output-side writes 8 then
25 (int16), whose deliberate user action has not been established; these are
not counted as new limiter evidence. Native readback remains pending.
Function 32 already counts: progress stays 22/38 documented IDs (58%).

Controlled Input 1 HP filter selection confirms function 30/status 1,
channel/segment/side 0/0/0: 04 for 24 dB/oct Butterworth, then 00 for
12 dB/oct Butterworth, and FF for bypass. Private frames 5887/6165/6207
have valid checksums. The controller also sends function 31 gain 0 dB and
function 32 frequency 20 Hz on HP slot 0; these repeated values are not
interpreted as independent HP gain controls. Additional 00/FF toggles follow.
This establishes enabled types 0 and 4 and bypass for type 0 only. Disabled
nonzero-type encoding, remaining type choices and independent native readback
remain pending. Progress stays 22/38 documented function IDs (58%).

Controlled Input 1 LP enable/bypass confirms function 30/status 1,
channel/segment/side 0/9/0: 00 enabled then FF bypassed, approximately 2.47 s
apart (private frames 7965/8039). Checksums match. HP and LP therefore use
separate filter slots 0 and 9; output-side behavior is not inferred. Native
readback remains pending. Progress stays 22/38 documented IDs (58%).

Controlled Input 1 LP frequency confirms function 32/status 1,
channel/segment/side 0/9/0, float32 LE 18000 then 19900 Hz (private frames
9451/9757), approximately 10.94 s apart. A repeated 19900 Hz send precedes
LP bypass FF. Checksums match; no other setting-write functions occur in this
step. Independent native readback remains pending. Progress stays 22/38
documented function IDs (58%).

Clean repeated Input 1 LP type/bypass test confirms function 30/status 1,
channel/segment/side 0/9/0: 00 enabled BW-12, 04 enabled BW-24, then FB
(signed -5) disabled BW-24 (private frames 489/603/789 in the retry capture).
This directly supports disabled type encoding -type-1 for observed type 4,
without generalizing every filter type. Checksums match. The controller repeats
LP gain 0 through function 31 and frequency 19900 Hz through function 32;
no global EQ toggle appears. LP currently remains disabled BW-24 until the
restoration step. Native readback remains pending. Progress: 22/38 IDs (58%).

LP restoration is command-confirmed in the retry capture: function 30/input 1/
segment 9 sends 04 (re-enable BW-24), 00 (BW-12), then FF (bypass), with
function 32 repeating 19900 Hz. Frames 2316/2441/2499 have valid checksums.
This restores the requested initial LP state at command level; independent
native readback remains pending. Progress stays 22/38 documented IDs (58%).

Controlled Input 1 HP BW-24 bypass/re-enable confirms function 30/status 1,
channel/segment/side 0/0/0: 04, FB, 04, 00, FF in private retry-capture
frames 3983/4045/4135/4241/4339. Checksums match. Frequency is repeated as
20 Hz. HP finishes at disabled BW-12; LP was previously restored to disabled
BW-12 at 19900 Hz. This adds type-4 disabled/re-enable evidence for HP, not
new function IDs: progress remains 22/38 (58%).

Independent function-27 read after controller exit confirms Input 1 HP disabled
BW-12 at 20 Hz and LP disabled BW-12 at 19900 Hz. Compared with the baseline
before manual capture, only current-preset-name bytes 2160..2171 differ; all
other native bytes match. This label change is retained, not treated as a
filter setting or overwritten.

The new restricted live crossover writer passed direct DSP-1004 validation:
for HP and LP separately, change cutoff, enable BW-12, select BW-24, bypass,
re-enable, and restore the original filter. Each changed field requires a
fresh full-settings read with exact unrelated-byte preservation. All outputs
were muted first through the already evidenced command and restored last.
The final native block matches the immediate pre-write baseline byte-for-byte.
Only types 0/BW-12 and 4/BW-24 are enabled in UI/backend/MCP; unverified stored
or requested types remain protected. Progress stays 22/38 IDs (58%).

The short dedicated HP-type capture expired before operation of the
controller (zero packets); the actual sequence was recovered from the
continuously running Wireshark capture and extracted privately. Controlled
Input 1 HP selection confirms function 30/status 1/channel 0/segment 0/side 0:
01 = 12 dB Bessel, 02 = 12 dB Linkwitz-Riley, 03 = 18 dB Butterworth, then
00 = BW-12 and FF = bypass. Original Wireshark frames 63746/63902/64159/
64477/64539 have valid checksums. Frequency 20 Hz and gain 0 are repeated.
There was no intermediate HP bypass/re-enable: this establishes
enabled type selection only, not FE/FD/FC bypass values. Current HP state is
command-restored to BW-12 off at 20 Hz. Independent native readback and bypass
variants for types 1/2/3 remain pending. No new function IDs: 22/38 (58%).

Controlled Input 1 HP Bessel-12 bypass confirms function 30/status 1,
channel/segment/side 0/0/0: 01 active then FE (signed -2) bypassed, private
frames 1115/1181 in the new-types bypass capture. Checksums match; the
controller repeats gain 0 and frequency 20 Hz. HP currently remains disabled
Bessel-12 for the next deliberate step. Re-enable/native readback pending.
Partial function evidence remains 22/38 (58%).

Controlled Input 1 HP Linkwitz-Riley-12 bypass confirms function 30/status 1,
channel/segment/side 0/0/0: preceding 01 re-enables Bessel-12, then 02 selects
LR-12 and FD (signed -3) bypasses it. Private frames 6379/6637/6683 have valid
checksums. Gain 0 and frequency 20 Hz are repeated. HP currently remains
LR-12 bypassed; its re-enable/native readback remains pending. Function-ID
coverage is unchanged at 22/38 (58%).

Controlled Input 1 HP Butterworth-18 bypass confirms function 30/status 1,
channel/segment/side 0/0/0: 02 re-enables LR-12, 03 selects BW-18, then FC
(signed -4) bypasses BW-18 (private frames 7761/7879/7937). Checksums match;
frequency 20 Hz is repeated. BW-18 re-enable and restoration to BW-12 have
not appeared in the inspected capture yet; do not claim full restoration.
Function-ID progress remains 22/38 (58%).

HP BW-18 re-enable/restoration is now command-confirmed: function 30/status 1,
channel/segment/side 0/0/0 sends 03, then 00 for BW-12, then FF for bypass
(private frames 11871/11997/12043). Checksums match and function 32 repeats
20 Hz. This completes captured enable/bypass/re-enable variants for HP types
1/2/3; independent native readback/direct-writer tests for those types remain
pending. Five of eleven HP types now have controlled selection/bypass evidence
(0..4); LP still has types 0 and 4. Function-ID coverage stays 22/38 (58%).

Controlled Input 1 HP Bessel-24 selection/bypass confirms function 30/status 1,
channel/segment/side 0/0/0: 05 active, FA (signed -6) bypassed, private frames
14275/14325. Checksums match; gain 0 and frequency 20 Hz are repeated. HP now
remains bypassed Bessel-24. Re-enable and independent native readback for this
type remain pending. Six of eleven HP types have selection/bypass capture
evidence; function-ID progress remains 22/38 (58%).

Controlled Input 1 HP Linkwitz-Riley-24 selection/bypass confirms function 30/
status 1/channel 0/segment 0/side 0: preceding 05 re-enables Bessel-24,
06 selects LR-24, and F9 (signed -7) bypasses it. Private frames
15287/15397/15463 have valid checksums; gain 0 and frequency 20 Hz repeat.
HP currently remains bypassed LR-24. Its re-enable/native readback remains
pending. Seven of eleven HP types have controlled selection/bypass evidence;
function-ID progress stays 22/38 (58%).

Controlled Input 1 HP Butterworth-36 selection/bypass confirms function 30/
status 1/channel 0/segment 0/side 0: preceding 06 re-enables LR-24, 07
selects BW-36, and F8 (signed -8) bypasses it. Private frames
16545/16671/16721 have valid checksums; gain 0 and frequency 20 Hz repeat.
HP currently remains bypassed BW-36; re-enable/native readback pending.
Eight of eleven HP types have selection/bypass capture evidence. Function-ID
coverage stays 22/38 (58%).

Controlled Input 1 HP Butterworth-48 selection/bypass confirms function 30/
status 1/channel 0/segment 0/side 0: preceding 07 re-enables BW-36, 08
selects BW-48, and F7 (signed -9) bypasses it. Private frames
19659/19829/19883 have valid checksums; gain 0 and frequency 20 Hz repeat.
HP currently remains bypassed BW-48; re-enable/native readback pending.
Nine of eleven HP types have selection/bypass capture evidence. Function-ID
coverage stays 22/38 (58%).

Controlled Input 1 HP Bessel-48 selection/bypass confirms function 30/status 1/
channel 0/segment 0/side 0: preceding 08 re-enables BW-48, 09 selects BE-48,
and F6 (signed -10) bypasses it. Private frames 22305/22423/22469 have valid
checksums; gain 0 and frequency 20 Hz repeat. HP currently remains bypassed
BE-48; re-enable/native readback pending. Ten of eleven HP types have
selection/bypass capture evidence. Function-ID coverage stays 22/38 (58%).

Controlled Input 1 HP Linkwitz-Riley-48 selection/bypass confirms function 30/
status 1/channel 0/segment 0/side 0: preceding 09 re-enables BE-48, 0A selects
LR-48, and F5 (signed -11) bypasses it. Private frames 23523/23649/23715 have
valid checksums; gain 0 and frequency 20 Hz repeat. HP currently remains
bypassed LR-48; re-enable and BW-12 restoration are not yet in the inspected
capture. All eleven HP types now have controlled selection/bypass evidence,
while independent native/direct-writer validation beyond types 0/4 remains
pending. LP evidence still covers types 0/4. Function-ID progress: 22/38 (58%).


## Controlled output limiter evidence on DSP-1004

Controlled output-A edits establish status-1/channel-0/segment-0/side-1
functions 37/38/39/40/41/42/46/47/48. Attack and Peak Hold/Release are
little-endian int16 milliseconds; RMS release is a one-byte attack multiplier;
thresholds are float32 actual volts. Controlled timing values were 25/26 ms,
16/17 times attack, 50/51 ms and 300/301 ms. Range boundaries remain untested.

Limiter bypass values are 1 bypassed and 0 enabled. RMS automatic timing is
**1 automatic and 0 manual**, matching native RMS block offset 8 and the
original controller's disabled manual timing fields. This corrects the previous
inverted assumption in native decoding/encoding and the legacy function-48
writer. Unknown flag values remain rejected.

The original controller may send coupled RMS/Peak threshold commands after a
single threshold edit. This is controller behavior; independent single-field
restoration writes preserved every unrelated native byte. All nine final
restoration commands passed separate full-settings readbacks. The original
thresholds/timings and enabled automatic state were restored; outputs stayed muted.

This adds nine partially evidenced function IDs: **31/38 (82%)**. It does not
establish all numeric ranges, other outputs/models/firmwares, power-loss
persistence, or dedicated function-54/55 query behavior. The default live
limiter write restrictions remain unchanged. Private captures are not published.


## Device-name length boundary

On DSP-1004/106119, 14- and 31-byte ASCII device names passed exact independent
function-0 identity readback after function-60 writes. A non-terminated 32-byte
name was truncated to 31 bytes with a final NUL. The application rejected the
mismatched readback; the original name was restored and all native DSP bytes
remained unchanged. Physical device names now require 1–31 ASCII characters;
local demo names retain their 32-character limit. Other firmware/model limits
and persistence after a power cycle remain unverified. ID coverage stays 31/38
(82%), with partial evidence per function.
## Offline controller comparison and capture checks

These are receive layouts in the multi-model controller. For each capture, record the model, side, channel, function and reassembled payload length. Match values against the controller UI and the full-settings read before assigning a layout to a DSP model.

| ID | Controller interpretation | Hardware comparison |
| --- | --- | --- |
| 43 | 512 binary32 FIR taps, optionally preceded by a 32-byte name (2048/2080 bytes). The controller derives displayed FIR phase from the complex frequency response. | Open one output FIR and compare name and taps with its exported file. |
| 50 | A bridge flag addressed by output pair. | Compare both members in a settings read before/after switching one pair. |
| 51 | Channel copy data; the receiver selects between two channel structures by length. | Capture a channel copy and compare channel and length before assigning fields. |
| 52 | Output EQ accepts ten or seven packed 14-byte filter records followed by a bypass byte (141/99 bytes). Input uses ten. | Open one input EQ and one output EQ, record side and length and compare HP/LP positions with the full settings. |
| 53 | Dynamic EQ has a 21-byte layout (three int16 times, knee byte, three float points, gain and bypass bytes) and a 105-byte layout (three arrays of four int16 times, four knees, sixteen float points, three float frequencies, bypass byte). | Match payload size, channel, displayed times, frequencies and bypass to a single editor read. |
| 54/55 | Peak/RMS limiter block; separate from the individual parameter commands. | Compare complete returned blocks with settings bytes and the existing individual-parameter captures. |
| 57 | Speaker data; the receiver recognizes several model-specific lengths. | Match a captured payload with the matching controller profile before decoding. |
| 59 | Mode 0 requests names. Received name tables have 16, 24 or 40 fixed 32-byte entries. UI slots are one-based; store/recall encode zero-based indices. The same receive ID also carries a 34-byte scenario record or 97-byte three-name record. | Open the slot selector, record visible slot range and compare first/last names with the table. UI visibility may be model-specific. |
| 79 | Analog input type, one byte, addressed by input channel. | Compare the returned value with the displayed source type. |
| 80 | Input priority, four bytes: primary, secondary, backup-enabled (1), signed threshold. | Compare input channel, both source names and threshold against settings. |
| 81 | Allocation, 40 bytes: four mode bytes, four sensitivity bytes, four binary32 load values and four binary32 voltages, indexed by output. | Compare configured values per output, separately from telemetry. |

Reset remains protected. Additional IDs in this offline comparison are multi-model candidates pending evidence on target hardware.

## DSP-1004 automatic bench verification, 2026-11-13

Six reversible control groups passed with an independent final full-settings read identical to the baseline. For status-2 read requests the amplifier returned status 1, the addressed channel and side, and segment 10 for FIR (43), bridge (50), peak (54) and RMS (55). Output EQ (52) and source alignment (62) echoed the segment. All four FIR replies held 512 taps with a name field. Both bridge flags, four output EQ blocks and all eight limiter blocks matched the full settings byte for byte. Function 59 returned forty 32-byte name fields. The operator confirmed that all 40 preset slots are selectable on the DSP-1004 front-panel display. Store and recall through the network protocol remain unverified. All twelve source-alignment replies matched the corresponding eight-byte native records at their current zero values.

Original 38-ID cohort: 37/38 (97%) have at least partial hardware evidence; reset remains untested. Additional ID 62 has read evidence. Extended inspection candidates 82, 88 and 93 timed out. The run did not calibrate meters with an input signal or visually verify display blinking.


A DSP-1004 output EQ write was verified with all outputs muted. A status-0 function 52 output block changed Output A Peak band 1 gain from 0 to -0.5 dB. Full settings confirmed the change and matched the original byte for byte after restoring the band and mutes. Other band values and crossover variants were not individually exercised by this test.

## DSP-1004 device preset store and recall

Function 59 mode 1 stored the muted configuration in slot 40. After changing
output A trim from 0 to -0.5 dB, mode 2 restored 0 dB. The native settings
matched the stored values except for the current preset label. The amplifier
reported "#40.CVR Slot Test" for the stored name "CVR Slot Test". The first
39 slot names remained unchanged. The original settings including output mutes
were restored; PC recall left the current label "PC Recall". This test covers
store and recall on slot 40. Clear-all and persistence across power cycles
were not exercised. Coverage remains 37/38 IDs; factory reset (16) is outstanding.

## DSP-1004 factory reset, firmware 106119

A single function-16 status-0 command with body 01 produced a transport ACK
followed immediately by a full function-27 settings reply (status 1, channel
32, segment 10, side 0). No matching function-16 status-3 reply was observed.
An independent subsequent settings read showed Output A trim reset from
-12.5 to 0 dB, all four output mutes released and current preset label Null.
All 40 slot names survived. The four named slots were recalled; one returned
a different FIR coefficient on output C compared with the original capture.
This brings the original 38-function cohort to 38/38 with at least partial
physical evidence on this amplifier.
After reconnecting, the original active settings and all four FIR blocks matched the backup byte for byte, excluding the current preset label.

## Four additional DSP-1004 functions, firmware 106119

| ID | Hardware observation | Scope |
| --- | --- | --- |
| 33 | A combined frequency/gain write set EQ slot 1 to 250 Hz and -0.5 dB on input 1 and output A. Each independent settings read matched the expected DSP bytes. | Eight bytes, frequency then gain as little-endian float32. Both original values restored. |
| 51 | Each of four channel-copy reads returned 515 bytes, status 1, addressed channel, segment 10, side 0. | Bytes 0 through 372 matched the respective native channel record. Differences start at byte 373. Destination writes untested. |
| 53 | Each of four dynamic EQ reads returned 105 bytes, status 1, addressed channel, segment 0, side 0. | All four blocks matched the corresponding native record at offset 410 exactly. Current blocks were identical across channels. |
| 57 | Speaker data reads returned 2415 bytes for all four channels, status 1, addressed channel, segment 10. | For each channel, both side selections returned identical payloads. Field meanings and import remain unverified. |

All 2687 bytes of full settings matched the pre-test capture after restoration.

## DSP-1004 NET firmware 106119: offline receive dispatch

The NET image has SHA-256 `cddb41b79b7deaaecf5e234b4d6349e5eaa2a3cad4f71662829c701abe81d5dc`. File offsets below refer to the raw image. The Thumb code loads the function byte at 0x229D2. Its comparison chain through 0x22BAE selects 68 distinct IDs (decimal, inclusive ranges):

    0, 4, 6–12, 14–28, 30–44, 46–62, 68–69, 71–72, 74, 77–80, 83, 87, 91

Function 10 provides a cross-check against captured mute behavior. Its branch reaches 0x23514, separates statuses 0/1 from 2, and checks the binary value and side on the update path. IDs 33, 51, 53 and 57 have explicit branches, consistent with the bench observations above. IDs 81, 82, 88, 90 and 93 have no case in this receive path. This finding is specific to the image and path; other models and unsolicited messages may differ.

Function 91 has a channel-indexed status-2 path at 0x2D81C. At 0x2D8F0–0x2D902 it submits a 30-byte reply with function 91, status 1, the requested channel and segment 10. Field meanings and physical responses remain unverified. The 68 cases measure static dispatch coverage, not bench verification or feature count.

### Function 91: copying output limiters

AMP Controller routes function 91 to its limiter decoder. Its limiter editor reads the source output at segment 0 and reuses the returned body when pasting to the destination output. The packed 30-byte structure agrees with the length and field stores in firmware 106119:

| Body bytes (inclusive) | Contents |
| --- | --- |
| 0-12 | RMS limiter: attack, release multiplier, threshold, bypass, automatic timing flag, maximum voltage (same field order as function 55). |
| 13-25 | Peak limiter: hold, release, threshold, bypass, maximum voltage (same field order as function 54). |
| 26-29 | Configured limiter load, little-endian float32. AMP Controller assigns it to the output's load setting. |

The desktop reference interprets byte 8 as automatic when zero in its combined-block receive path. The bench evidence for function 48 records automatic when one. Verify byte 8 against physical readbacks before sharing flag decoding across these functions. For the first read-only bench check, compare slices 0-12 and 13-25 to same-channel function 55 and 54 replies, and compare the final float to the configured limiter load. Physical function-91 reads and destination writes remain unverified.

### Function 48 and function 91 byte 8

In the 106119 NET image the single-channel function-48 update accepts 0 or 1 and passes the byte unchanged to the write/readback helper at file offset 0x2548C. Its read path at 0x254EC and the function-91 path at 0x2D82E index the same table (0x0803DE56, entries 1140 through 1143) and call the same getter. Function 48 returns the getter byte directly. Function 91 stores it at body offset 8 (its working pointer has advanced by one). Both therefore report the same raw flag for a given output and stable device state.

AMP Controller assigns the same Boolean expression (raw byte equals zero) in both receive handlers. The earlier wording implying a function-specific polarity difference was misleading. The bench report associates raw 1 with disabled manual timing controls. The getter establishes raw equivalence; an observation of actual limiter timing under both settings would distinguish the DSP behavior from the controller's presentation.
