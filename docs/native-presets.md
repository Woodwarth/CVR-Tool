# Native presets (.sd)

The Presets page loads/saves original CVR `.sd` and CVR Tool JSON files. There
is no raw text view. Native support is intentionally limited to the evidenced
B-series firmware headers 106118 and 106119, with two or four channels. Unknown
versions, inconsistent lengths, wrong models and invalid values are rejected.

The native codec reads a 2232/2687-byte sync block, four fixed 515-byte records,
and optional 2048/2080-byte FIR blocks per active output plus the 16-byte footer.
EQ, delays, gains, routing, polarity, mute and FIR map to shared application state.
Bridge pairs, RMS/peak limiter settings and gate bypass are decoded explicitly.
B119 additionally stores signed gate thresholds; B118 has no threshold field.
Source bytes retain unknown settings, load data and unused channels.
Imported native files can therefore be saved without dropping opaque parameters.

JSON schema 7 stores independent input gain alongside output dynamics, output EQ/FIR,
optional original native bytes, input processing, routing and channel settings;
schemas 2–6 remain readable. Missing legacy input gains are recovered from
original native bytes when present, otherwise the active input gains are retained.
Native input gain is float32 at channel offset 369; output trim uses offset 44.
JSON without native bytes preserves the connected device's current unknown settings.
Local-model exports synthesize a B119 header; they are not proof of compatibility
with a physical amplifier. A physical load also checks the connected header before
writing and requires independent sync/FIR readback. Failed multi-block writes may
be partial; refresh the device before retrying.

Presets on a device use sixteen slots. Save replaces one slot; Load replaces active
processing. Clear removes all slots while retaining active processing. Factory
Reset restores device settings and may clear presets. The desktop asks explicitly
before these operations; MCP requires `confirmed=true`. Local-model slots are
volatile and do not survive application restart. Export to PC for persistence.

Private sample files are never distributed. Synthetic codec/network checks and
exact local sample round-trips do not replace physical firmware/power-cycle tests.

Per-output load mode is the byte at channel offset 367: 0 Low-Z, 1 70V,
2 100V. These are distinct from the paired bridge flags at sync offsets 34/35.
Native bridge-coded values 3–5 remain readable and round-trip unchanged; the
Mode editor offers only Low-Z / 70V / 100V. Schema 5 JSON without LoadMode
recovers it from its original native bytes, or preserves the active setting
when no native data exists. No bridge change is inferred from a load-mode edit.

Native output mute at channel offset 48 is active-low: 0 muted, 1 unmuted.
App JSON continues to express `Muted: true` for a muted output. Earlier exports
used inverted native output mute semantics; importing native bytes now follows
the observed CVR convention. A separate controlled matrix test also establishes active-low route flags:
0 muted, 1 enabled. Native route flags now follow that convention; App JSON
retains true=muted. The controlled off-diagonal test and independent full sync establish output-major
native route storage: output record `36 + (output-1)*515`, route gain at
`24 + (input-1)*5`, followed by its active-low flag. Both import and export
now follow this order; earlier native route mapping was transposed.
