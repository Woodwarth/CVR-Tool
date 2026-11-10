# MCP

`McpHost` runs stdio tools, or owns a headless shared session with `--host-session`.
Default clients use `SessionBackend`; no demo flag is required.
Tool files are grouped by device, channel, routing, input DSP, connection and
preset operations. They delegate behavior to Application. Desktop supports
`--mcp` using the same host. Stdout is protocol-only; diagnostics use stderr.
See `docs/mcp.md` for release and development configuration.
