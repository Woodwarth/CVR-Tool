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
