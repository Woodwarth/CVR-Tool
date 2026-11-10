# Native presets (.sd)

The local inspector is read-only. It reports file size, SHA-256, a model hint
and recognized text markers; it does not decode or import amplifier parameters.
JSON export is a separate schema 3 application snapshot, not a native .sd file.

Private DSP-1004 and DSP-1502 exports were inspected locally. They remain
outside source control along with screenshots and FIR reference files. Model
text or screenshot appearance does not establish binary parameter encoding.

Before implementing native import, compare controlled single-parameter exports
and establish all field widths, byte order, checksums, versions and limits.
Reject unknown formats and model mismatches. Never copy unverified bytes to
hardware or publish private reference files.
