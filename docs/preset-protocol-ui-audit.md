# Native preset, protocol and UI audit

Offline audit after 0.7.3, using synthetic B118/B119 fixtures and local two-/four-channel preset samples. Private sample data is not distributed. The initial offline audit added no physical evidence. Subsequent controlled DSP-1004 tests brought partial live evidence to 22/38 documented function IDs (58%); the table reflects the current local build.

| Data/function | Native .sd representation | Application / UI | Live write status |
| --- | --- | --- | --- |
| Input gain | Channel record +369, float dB | Main / Inputs and MCP | Enabled; command 9/input side, independent readback |
| Output trim | Record +44, float dB | Outputs / MCP | Enabled; command 9/output side, independent readback |
| Output mute | Record +48, active-low | Main / Outputs / MCP | Enabled with full-settings readback |
| Input mute | Global +2192, one active-low byte per input | Main symbol button / MCP / native information | Enabled independently; unknown flags remain protected |
| Routing gain/mute | Record +24, four five-byte input entries | Inputs matrix / MCP | Gain and mute enabled with native readback |
| Input/output delay | Record +50/+54, float ms | Linked metre/ms editors | Enabled with native readback |
| Input/output EQ, HP/LP | Record +85/+226, two 141-byte blocks | Graphical editors / MCP | Input Peak gain/Hz/Q, band/global bypass and BW-12/BW-24 HP/LP cutoff/type/bypass enabled; other types/output EQ protected |
| Polarity | Record +58 | Outputs / MCP | Enabled; output command 18, native readback |
| Load mode | Record +367 | Outputs / MCP | Low-Z/70V enabled from captured encoding; 100V protected |
| Stereo/bridge | Global +34, one flag per pair | Confirmed pair switch; follower disabled | Protected |
| RMS/peak limiters | Record +59/+72, two 13-byte blocks | Editors / details / MCP | Protected |
| Noise gate | Record +373; B119 threshold +2232 | Outputs editor / details | Enable and known threshold enabled; B118 absent threshold remains unknown |
| FIR bypass / coefficients | Record +368; optional footer and 512-tap blocks | FIR viewer/editor / MCP | Bypass enabled with readback; coefficient writes protected; unknown curve stays blank |
| Input/speaker names | Record +378/+394, 16 bytes each | Newly visible native information; speaker tile uses stored label | Read-only; original bytes preserved |
| Configured load | Record +374, float | Newly visible native information, separate from measured V/I | Read-only |
| Source alignment | Record +0..23, analog/Dante/AES gain and delay | Newly visible native information | Read-only; no new source commands inferred |
| Source selector | Record +49 | Preserved; source code available through metadata | Mapping and model-specific options require verification |
| Input/output link maps | Global +2096..2159 | Preserved; no grouping controls | Not enabled |
| Analog source / automatic priority | Global +2196..2215 | Preserved; selector/priority UI remains missing | Not enabled |
| Additional routing/source bytes | Global +2216..2231 | Preserved; not treated as input gain | Meaning/options require separate evidence |
| Dynamic EQ | Record +410..514 | Preserved; no DEQ editor | Not enabled |
| Current preset name | Global +2160..2191 in B118 and B119 | Metadata; live header supports both layouts | No slot-name reads inferred |
| Sixteen device presets | Separate device storage | Demo slots implemented; real slot names unknown | Live store/recall/reset/clear protected |
| Display lock / standby | Global +33/+32 | Header / MCP | Verified writes; audio-preset load does not toggle these controls |
| Wink / operating time | Separate function 83 / extended identity 71 | Header / MCP / details | Wink verified; time read-only |

Record offsets are relative to `36 + (channel-1)*515`. Two-channel files still contain four fixed records; only configured channels are exposed. Additional blocks are retained through the original native template. A preserved byte is not a claim that its behavior is implemented or verified.

## Defects corrected locally

- Invalid inner checksums no longer abort a query before a later valid reply arrives.
- Identity announcements no longer replace an incomplete multi-fragment reply. Assembly tolerates reordering/identical duplicates, discards conflicting duplicates, and bounds memory to four messages. Oldest incomplete messages are evicted when needed.
- A new explicit query works after a fragment timeout; no request/write retransmission was added. Draining stale datagrams is bounded.
- Slow connection construction no longer holds the device registry lock during network I/O.
- Offline/stale devices disable operational and mute controls while cached settings remain inspectable. Read-only delay/mode/gate/limiter/FIR viewers can be opened.
- B118 current-preset names are recognized at the same native offset as B119.
- Settings-only files loaded into demos no longer retain old demo FIR coefficients. Absence is preserved through file export and local preset slots. A partially known FIR set cannot be exported as a fabricated complete file.

## Next physical comparison

Check source selection/alignment/priority, channel linking, names/configured load and dynamic EQ against deliberate controller operations before introducing live writes. Recheck packet-loss/interleaving behavior and the new read-only viewers on a connected amplifier. Unknown input-meter calibration, real FIR transfer and device slot-name reads remain open.
