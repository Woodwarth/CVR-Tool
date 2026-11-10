# Application

`AmplifierService` validates channel, routing and input DSP plans. `DeviceService`
handles names and controls; `PresetService` exports schema 3 snapshots.
`Demo*Backend` stores local models. `CvrLanBackend` and `CvrLanDsp` read/write
explicit CVR peers, preserving unedited bytes and checking readback.
`ConnectionBackend` routes devices to their owner. `Session/` hosts current-user
IPC and its typed backend client. UI and MCP share this state.
`NetworkSetupService` stores manual targets; file analyzers remain read-only.
