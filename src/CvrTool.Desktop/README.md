# Desktop

`MainWindow` builds navigation and delegates actions to Application.
`ShellLayout` scales a fixed design uniformly; `DeviceInformation` is compact.
`MainPage`, `RoutingPage`, `OutputPage` and `PresetPage` each focus on one view.
`InputProcessingEditors` edits delay in ms/metres; `InputEqualizerEditor` combines
eight bands, high-/low-pass controls and a live draft curve. EQ/FIR/Network
tabs are removed; manual connections remain available through MCP.
`Controls/` contains reusable gain readouts, meters, symbols, LEDs and dials.
`App` owns the shared session; external MCP writes trigger a UI refresh.
Source headers describe code responsibilities; they are not UI metadata.

`NativePresetInformation` displays preserved annotations read-only. Live editors
can show cached delay/dynamics/FIR state while their Apply controls stay disabled;
unknown FIR coefficients produce no curve. Offline operational/mute writes are
disabled until fresh telemetry returns.

`BrowserControlWindow` shows a locally generated pairing QR containing the complete private connection link. `BrowserConnectionQrCode` draws managed QR modules with a quiet zone, clears its payload when stopped and uses no external QR service or platform-specific image renderer.
