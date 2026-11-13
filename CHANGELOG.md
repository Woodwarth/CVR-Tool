# Changelog

## 0.8.0

- Add live output EQ editing, channel switching and paired EQ/limiter copies.
- Import, export and remove output FIR filters. Generate a 512-tap FIR from an EQ draft.
- Add EQ reset, simultaneous gain and phase curves, half-decibel matrix edits and RMS/peak power controls.
- Refresh Main telemetry without replacing focused controls. Scale the workspace to wide displays.
- Manage demo devices from the sidebar and load native presets with device readback.
- Add browser pairing on all IPv4 interfaces and display connection state in the header.

- Align browser control with Desktop: device sidebar, compact identity header,
  vertical Main faders/meters and compact Inputs signal flow.
- Show and send input gain while dragging, coalescing rapid positions and
  serializing readback; discard pending positions on disconnect.
- Permit one browser page at a time with explicit disconnect and automatic
  abandoned-session expiry. Preserve parallel HTTP requests within that page.
- Show persistent Off / On / Connected browser-server status in Desktop.
- Drain browser-server shutdown without capturing the UI thread.

## 0.7.5

- Add a locally generated QR code containing the complete private iPad browser link.
- Show a persistent Desktop-connection banner and a warning inside open editors.
- Detect offline, stalled and stopped app connections even while editing; pause
  controls until fresh state is loaded and preserve drafts without automatic replay.
- Explain expired connection keys and recover automatically when the app responds.
- Correct RMS automatic timing to active-high in native files and the legacy writer.
- Restrict physical device names to 31 ASCII characters plus a wire terminator,
  preventing the observed 32-byte truncation; keep local demo names at 32 characters.
- Extend controlled DSP-1004 limiter evidence to 31/38 documented function IDs
  with partial coverage; unverified live writes and factory reset remain protected.
- Verify local QR decoding and Chrome tablet/phone fault recovery. Physical
  iPad/Safari validation remains pending.

## 0.7.4

- Enable evidenced live input gain/mute, output trim/delay/polarity, routing gain,
  input delay/Peak EQ, gate, Low-Z/70V, FIR bypass and device-name edits.
- Require independent readback and preserve unrelated native bytes.
- Commit live Main gain edits in 0.5 dB steps; add independent input mute.
- Enable graphical live input Peak EQ and evidenced BW-12/BW-24 HP/LP cutoff,
  type and bypass; keep other filter types protected.
- Verify HP/LP writes on DSP-1004 with full native readback and exact restoration.
- Preview live routing drags and write once on release, preserving route mute.
- Keep Inputs output-gain drafts across refreshes; improve text contrast and accept decimal commas.
- Keep uncalibrated meters/clip unknown and all reset operations protected.
- Add the twelve requested target models without claiming hardware validation.

- Add integrated local-network browser control with responsive iPad layouts,
  per-session private access links, and shared Desktop/MCP state.
- Serve local browser assets without internet or an extra iPad app.
- Recover from corrupt/interleaved/duplicate UDP replies with bounded assembly.
- Keep cached/demo reads responsive during slow connection handshakes.
- Expose preserved native labels, input mute, source alignment and configured load.
- Open protected live settings viewers and disable writes on offline devices.
- Preserve missing FIR coefficients through settings-only files and demo slots.
- Read current preset names in B118 as well as B119.
- Record remaining source, linking, DEQ and device-slot gaps in the parity audit.

## 0.7.3

- Linked metre/millisecond delay editing and whole-tile Trim interaction.
- Stereo/Bridge shows the current mode and asks for confirmation before switching.
- PC preset loading and saving use only native .sd files.
- Operating time from verified extended device identity appears in the header/details.
- Wink blinks the amplifier display with independently queried state; the header
  controls move left and use a waving-hand symbol for Wink.
- Live output and matrix mute writes use fresh settings and independent readback;
  physical tests restored the original settings byte-for-byte.

## 0.7.1

- Automatic CVR discovery on the wired amplifier LAN, alongside all demo devices.
- Live full-settings/status display with unknown readings kept explicit.
- Writable display lock and standby, verified with fresh device readback.
- Standby waits for asynchronous telemetry and native-state updates.
- Name appears before model; green/red points show connection state.
- Manual connection/disconnect buttons removed; native/JSON backups remain available.
- Correct status-zero framing for standby and load-mode commands.
- Output polarity and Low-Z/70V commands passively confirmed; 100V was unavailable
  on the tested controller/device combination.

## 0.7.0

- Independent input gain and schema-7/native preset preservation.
- Corrected active-low output/matrix mute and routing coordinates.
- Targeted input EQ global/Peak bypass, gain, frequency and Q commands.
- Sync-only native settings export without invented FIR blocks.

## 0.6.4

- Stereo/Bridge switches directly with one click; no popup or Apply step.
- In bridge mode, the second output of the pair (B or D) has greyed, disabled
  function tiles. The pair switch stays enabled to return to Stereo.
- Existing output editors for the follower close when bridge becomes active.
- Returning to Stereo restores editing without changing stored channel settings.


## 0.6.3

- Outputs uses equal-size function tiles with one shared heading/value layout.
- Stereo/Bridge controls at the end of every output row operate A/B or C/D
  together, independently of the per-output Low-Z/70V/100V selection.
- GitHub README includes a current local-demo Outputs screenshot, also bundled
  with the package so the README image remains available offline.


## 0.6.2

- Outputs Mode now selects Low-Z / 70V / 100V per output, using the dedicated
  load-mode command instead of the separate paired stereo/bridge command.
- Trim and symbol-only Mute now match the other output processing blocks.
- Mode is shared with MCP, device slots and JSON/native presets. Schema 6
  adds LoadMode; schemas 2–5 retain their current or original native mode.
- Existing bridge-coded native values are preserved without reinterpretation.
- Compact self-contained packages and integrated MCP remain unchanged.


## 0.6.1

- Smaller self-contained packages: one shared desktop/MCP runtime and no debug
  symbols in downloads. The integrated `--mcp` command stays unchanged.

- Outputs: stereo/bridge switching for each A/B or C/D pair.
- Per-output noise gate with On/Off and editable threshold (-100 to 0 dB).
- Peak/RMS limiter editors with enable, output-voltage thresholds and timing;
  RMS automatic timing and read-only device voltage limits.
- Revision-bound shared UI/MCP operations and complete preset restoration.
- Schema 5 JSON adds dynamics; earlier JSON remains readable. Supported native
  files retain bridge, gate and limiter values without changing opaque bytes.


## 0.6.0

- Outputs signal flow with independent graphical EQ/HP/LP and FIR editor.
- FIR import/export/remove, enable state and impulse/frequency/phase previews.
- Sixteen device presets, PC load/save in CVR .sd and app JSON, clear and reset.
- Native B118/B119 preservation of settings outside the editable controls.
- Larger Inputs environment and plain click-to-edit output gains.
- Shared UI/MCP support for output processing and preset operations.
- Synthetic and local-file validation; physical amplifier checks remain pending.


## 0.5.0

- Add HP/LP crossover cards, cutoff handles, bypass and a combined EQ response preview.
- Include all eleven reference choices: Butterworth 12/18/24/36/48, Bessel 12/24/48 and Linkwitz-Riley 12/24/48 dB/octave.
- Share crossover edits through Application, MCP, local models and confirmed CVR readback.
- Remove the two Main section footer hints and the EQ, FIR and Network tabs.
- Keep EQ editing under Inputs and manual amplifier connections through MCP.

## 0.4.0

- Check GitHub for native stable updates, verify SHA-256, stage and restart without extra software.
- Add direct platform download links and update instructions.
- Replace Main input gain entry boxes with plain text; use 0.5 dB arrow-key steps.
- Reuse existing delay/EQ windows instead of opening duplicates.
- Add graphical input EQ with draggable band handles and synchronized band controls.
- Preserve observed device filter types and avoid inventing responses for unknown types.
- Compact the routing matrix and remove its left output labels.

## 0.3.0

- Begin a new repository history; retire previous tags and releases.
- Add self-contained Windows ARM64 alongside Windows x64 and Apple Silicon.
- Integrate shared-session MCP into the desktop executable; no demo mode gate.
- Add explicit CVR UDP connections with validated framing and write readback.
- Give every two-channel model the same functions and input/output topology.
- Edit input delay in metres/ms, input EQ on/off and eight bands, and output gain.
- Preserve interface arrangement with uniform scaling and a minimum window size.
- Restore compact UI identity information; expand readable source headers.
- Shorten the README and update English development documentation.

Physical-amplifier validation remains pending. Native preset import, firmware
updates and filter-type editing are not implemented.
