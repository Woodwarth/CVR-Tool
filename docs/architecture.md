# Session architecture

The desktop app owns amplifier discovery and the UDP connection. Desktop controls, browser requests and MCP tools use the same application service. This keeps edits, revision checks and device readback consistent across clients.

## Browser access

The desktop serves the browser UI on the selected LAN interfaces. Scan the QR code or open the connection link to pair one browser session. The server checks ownership again before each queued write. Disconnecting drops pending edits.

Dragging an input fader updates its readout immediately. The browser coalesces intermediate values and sends the final position on release.

## Measurement roadmap

A future MCP client could run measurements through REW and use CVR Tool to apply amplifier settings. The two applications would exchange no data directly.
