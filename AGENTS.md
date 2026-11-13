# Project conventions

CVR Tool is a cross-platform amplifier controller built with .NET and Avalonia.
Keep interface text, documentation and source comments in English.
Use the shared Application service for Desktop, browser and MCP operations.
Serialize device I/O through the existing wired discovery and session owner.
Validate physical writes with readback. Test destructive operations on simulated devices.
Keep private device captures, presets, identifiers and manufacturer files outside the repository.
Document hardware evidence separately from synthetic test results; never infer unknown telemetry.
Keep module headers concise (at most eight lines). See docs/code-guide.md for build commands
and docs/validation.md for validation scope.
