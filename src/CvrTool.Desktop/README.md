# Desktop

`MainWindow` builds navigation and delegates actions to Application.
`ShellLayout` scales a fixed design uniformly; `DeviceInformation` is compact.
`MainPage`, `RoutingPage`, `OutputPage` and the analysis/network pages focus on
one view each. `InputProcessingEditors` edits delay in ms/metres and input EQ.
`Controls/` contains reusable gain readouts, meters, symbols, LEDs and dials.
`App` owns the shared session; external MCP writes trigger a UI refresh.
Source headers describe code responsibilities; they are not UI metadata.
