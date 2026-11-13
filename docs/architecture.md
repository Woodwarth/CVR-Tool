# Automation architecture

Desktop and MCP share the Application service and one connection owner.
The AI orchestrates separate tools: it can later run measurements through REW,
interpret results and plan/apply amplifier settings through CVR MCP. There is
no direct REW-to-CVR data channel. Measurement orchestration, REW integration
and automatic tuning have not yet been implemented.

Keep measurement acquisition separate from amplifier control. Measurements
must carry sample rate, units and channel identity; device writes retain
preview, validation, revision and readback behavior. Never treat a suggested
filter as an applied or measured result. This roadmap belongs in development
notes, not the product README.

## Browser control for the next build

Requested: an embedded local-network web server so an iPad can control the
amplifiers in Safari without installing an app. Implemented in 0.7.4. The desktop offers start/stop and shows its private
browser connection URL. The server binds only to the selected local interface.

The responsive English UI must call the existing Application service through
the same session owner as Desktop and MCP. It must not open another amplifier
UDP socket or weaken revision checks, independent readback, unknown-state
handling, or restrictions on unverified operations/reset. Browser assets must
ship with the self-contained Windows and Mac builds and work without a CDN or
internet connection. Keep this LAN service separate from stdio MCP; it does
not change the existing MCP transport contract. HTTP integration checks cover access keys, host/origin restrictions, shared
edits and lifecycle. Chrome touch tests cover tablet/phone layouts and
delay/EQ/routing/mode edits. Physical iPad/Safari validation remains pending.

Browser control currently exposes input/output gain/mute/delay, routing,
input EQ and supported crossovers, output polarity/mode/gate/FIR bypass,
operational controls and native preset export. Unverified live operations
remain protected. Limiter/bridge editing and FIR coefficient import remain
in Desktop; reset is unavailable through the web server.

## Local browser-control changes after 0.7.5

Browser control now reserves one logical page session. Every authenticated API
request carries a random per-page X-CVR-Client identifier; a second page receives
423 without device access. The owner's heartbeat renews a twelve-second lease.
Explicit disconnect releases it, pagehide attempts release, and an abandoned
owner expires. Parallel HTTP requests from the same page remain valid. The
write queue rechecks ownership before applying, so a released page cannot leave
queued commands for a new owner. This does not restrict MCP or Desktop sessions.

Input range events update their readouts immediately and coalesce pending values
per channel. One gain request runs at a time, at a bounded cadence, with the same
fresh revision/readback checks as all other clients. The last position is flushed
on release; disconnected, failed, superseded-device and expired-access queues
are discarded. Reconnection never replays them. Other commands report that the gain operation must finish first instead of
silently dropping an edit.

Desktop's header shows Off, On, or Connected even when the pairing dialog is
closed. Status changes distinguish the listener from its occupied 0/1 slot.
Shutdown runs the host's asynchronous drain away from the UI context.
