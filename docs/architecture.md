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
