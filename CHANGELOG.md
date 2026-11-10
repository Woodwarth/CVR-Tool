# Changelog

## 0.5.0

- Add HP/LP crossover cards, cutoff handles, bypass and a combined EQ response preview.
- Include all eleven reference choices: Butterworth 12/18/24/36/48, Bessel 12/24/48 and Linkwitz-Riley 12/24/48 dB/octave.
- Share crossover edits through Application, MCP, local models and confirmed CVR readback.
- Remove the two Main section footer hints and the EQ, FIR and Network tabs.
- Keep EQ editing under Inputs and manual amplifier connections through MCP.

## 0.4.0

- Check GitHub for native stable updates, verify SHA-256, stage and restart without extra software.
- Add direct platform download links and update instructions.
- Replace Main input gain entry boxes with plain text; use 0.5 dB arrow-key steps.
- Reuse existing delay/EQ windows instead of opening duplicates.
- Add graphical input EQ with draggable band handles and synchronized band controls.
- Preserve observed device filter types and avoid inventing responses for unknown types.
- Compact the routing matrix and remove its left output labels.

## 0.3.0

- Begin a new repository history; retire previous tags and releases.
- Add self-contained Windows ARM64 alongside Windows x64 and Apple Silicon.
- Integrate shared-session MCP into the desktop executable; no demo mode gate.
- Add explicit CVR UDP connections with validated framing and write readback.
- Give every two-channel model the same functions and input/output topology.
- Edit input delay in metres/ms, input EQ on/off and eight bands, and output gain.
- Preserve interface arrangement with uniform scaling and a minimum window size.
- Restore compact UI identity information; expand readable source headers.
- Shorten the README and update English development documentation.

Physical-amplifier validation remains pending. Native preset import, firmware
updates and filter-type editing are not implemented.
