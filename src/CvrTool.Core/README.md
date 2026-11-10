# Core

Immutable channel, routing, input DSP, device, telemetry and preset records.
Two-/four-channel profiles share functions. `IAmplifierBackend` separates
application validation from local state, shared-session IPC and physical peers.
Nullable measurements mean unknown; `IsDemo` identifies local simulation.
`VerifiedOnHardware` remains false until physical validation is performed.
