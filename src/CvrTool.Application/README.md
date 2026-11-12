# Application

`AmplifierService` validates shared channel, routing and DSP plans. `DeviceService`
handles device names and operational controls; presets use schema 7.
`Demo*Backend` owns local models, including explicit unknown FIR states.
`CvrSnapshotBackend` owns the observed live bulk profile and verified limited
writes. `CvrLanBackend` is the older individual-query profile; its general
physical compatibility is unverified. Both preserve unrelated native bytes.
`ConnectionBackend` routes devices to their owner and performs slow connection
construction outside its registry lock. `Session/` hosts current-user IPC;
Desktop and MCP use the same state and socket owner.
`NativePresetCodec` preserves supported .sd files. `NativePresetMetadata` exposes
read-only annotations that lack editable controls. File analysis stays local.

`ObservedInputCrossovers` shares the restricted BW-12/BW-24 live policy with
the editor; `CvrSnapshotMain` writes HP/LP slots 0/9 without replacing EQ blocks.
