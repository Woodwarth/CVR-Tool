# Integrated MCP

Start the desktop app first. Configure the MCP client with the absolute desktop
executable path and `args: ["--mcp"]`. Windows executable:
`desktop/CvrTool.Desktop.exe`; Mac: `CVR Tool.app/Contents/MacOS/CvrTool.Desktop`.
From v0.6.1, the optional `CvrTool.Mcp` executable lives beside the desktop
executable (`desktop/` on Windows; `Contents/MacOS/` inside the Mac app). It
takes no extra arguments and shares the included runtime. Earlier packages
had a separate `mcp/` directory; update that path if using the optional apphost.
The recommended desktop path with `--mcp` is unchanged. No demo flag or
SDK/runtime is required.
Stdout contains only MCP messages; logs use stderr. MCP remains stdio only. The separately enabled Browser control server is a
local web UI; it is not an HTTP MCP endpoint.

For a headless owner, run either executable with `--host-session`. Start a
second process as the MCP client. `--session NAME` selects an independent named
session; use the same name for owner and clients. Current-user IPC carries
bounded JSON messages; only the owner holds UDP sockets and device state.
A second owner of the default session is rejected. `--demo` is retained only
for compatibility with isolated test clients and does not share GUI state.

## Tools

- `list_devices`, `get_device`, `get_telemetry`, `export_preset`.
- `connect_amplifier(address, localAddress, localPort=45454, remotePort=45455,
  timeoutMs=1000)`, `disconnect_amplifier(deviceId)`.
- `rename_device`, `get_device_controls`, `set_device_controls`. Physical device names accept 1–31 ASCII characters; local demo names accept 1–32 characters.
- `get_routing`, `plan_routing_change`, `apply_routing_change`.
- `plan_channel_change`, `apply_change` (gain, mute, delay, polarity).
- `get_input_processing`, `plan_input_processing`, `apply_input_processing`
  (gain, independent input mute, delay, EQ enable state, eight bands with Hz/dB/Q and enable state, plus
  highPass/lowPass objects with FrequencyHz, FilterType and Enabled).
  Crossover type IDs: 0 BW-12, 1 BE-12, 2 LR-12, 3 BW-18, 4 BW-24,
  5 BE-24, 6 LR-24, 7 BW-36, 8 BW-48, 9 BE-48, 10 LR-48.

- `get_output_processing`, `plan_output_processing`, `apply_output_processing`
  use the same EQ/crossover fields as inputs.
- `get_output_fir`, `set_output_fir`: 1â€“512 finite taps at 48 kHz.
- `set_output_fir_enabled`: bypass only; preserves coefficients, including unknown data.
- `get_device_presets`, `store_device_preset`, `recall_device_preset` (slots 1â€“16),
  `clear_device_presets`, `factory_reset`.
- `export_native_preset` returns base64 `.sd`; `load_preset_file` accepts base64
  native `.sd` files only. Bulk loads, slot writes/recalls, reset and clear require
  `confirmed=true`; the default refuses the operation.

- `get_output_dynamics`, `plan_output_dynamics`, `apply_output_dynamics` expose
  per-output `LoadMode` (0 Low-Z, 1 70V, 2 100V), `NoiseGateEnabled`/`NoiseGateDb` (-100..0), and
  `Rms`/`Peak` records. Thresholds use actual output volts, not dBFS.
  RMS AttackMs is 1..2000; ReleaseMultiplier is 2..32 times attack; Automatic
  selects device timing. Peak HoldMs is 0..2000; ReleaseMs is 20..1000.
  Enabled selects the limiter; MaximumVolts is read-only and bounds thresholds.
  LoadMode affects only the selected output. The Bridged field remains
  separate and affects an A/B or C/D pair, matching the end-of-row Stereo/Bridge
  switch in Outputs.
  In the Outputs UI, bridge followers B/D are greyed and cannot be edited;
  the pair switch stays active. This presentation does not add backend/MCP
  restrictions or copy settings between outputs.
  Native bridge-coded modes 3â€“5 are preserved when read, but cannot be requested
  as new LoadMode edits. Older JSON without LoadMode preserves the active mode.

Channel numbers start at 1. Physical IDs are `cvr:ADDRESS:PORT`; local models
are `demo-1004`, `demo-2002` and `demo-1502`. Local state is volatile. Exported
schema 6 JSON includes inputs, outputs, routing, output EQ/FIR and dynamics; schema 2â€“5
files remain readable. Native `.sd` supports B-series 106118/106119 layouts. Unknown readings are null, never fabricated.

Plans are read-only, single-use, expire after two minutes and carry the shared
device revision. Any session write invalidates earlier plans for that device.
Physical writes require independent readback. A failure can follow a partially
applied multi-parameter change; refresh before making a new plan. Use one
controller: the firmware has no compare-and-swap transaction and an external
controller can change settings between queries.

## Physical capabilities

The default observed profile exposes only independently evidenced write variants.
See [network capabilities](network.md). Input HP/LP cutoff/type/bypass supports
only evidenced BW-12 and BW-24 on the observed live profile; unverified stored
and requested types are rejected before writes. Input mute uses the nullable `Muted`
field; an unknown native flag is not treated as unmuted. FIR bypass can change
without reading or replacing coefficients. The legacy connection option
`ReadOnly=true` selects this restricted profile; individual capability flags
determine which edits are allowed. Unsupported variants are rejected by the
backend as well as disabled in the UI. Reset remains protected.

## Development

Build with .NET SDK 10, then run `python scripts/configure-mcp.py` to create
project-local Codex/Claude configurations. Existing unrelated entries are kept.
`python scripts/check-mcp.py Release` verifies two clients sharing one owner.
This checks stdio MCP; live tool loading by a specific AI client is separate.

`set_device_wink` enables/disables display blinking through the shared owner.
Live writes use a separate function-83 state query and preserve DSP settings.

`get_native_preset_metadata` reads preserved source/speaker annotations, separate
input mute, source alignment and configured load. Null means no native source.
