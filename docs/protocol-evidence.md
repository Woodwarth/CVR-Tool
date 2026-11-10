# Implemented CVR protocol facts

Evidence: local static inspection of the installed AMP Controller and offline
comparison with the private display-lock capture. No manufacturer source,
binary, device identifiers or capture is included in this repository. The
implementation is independently written from the observed wire layout.
Physical hardware compatibility remains unverified.

## Framing

Outer header, ten packed bytes: little-endian magic 55555 (offset 0), model
short (2), fragment count (4), last-fragment length short (5), one-based fragment
index (7), data/ACK byte (8), padding (9). Fragments contain at most 450 bytes.
ACK copies the received header with offset 8 set to 1. Padding is preserved.
The decoder bounds counts to 64 and validates exact datagram lengths.

Inner header: 0x55, function, status, channel, segment, four little-endian link
bits, input/output side byte. Link is a channel mask, not a transaction ID.
Status 0 is a response, 1 a write, 2 a request. Trailer: big-endian total inner
length (two bytes), then sum of preceding inner bytes modulo 256.
Channels and segments are zero-based on the wire.

## Supported mappings

| Function | Operation | Body |
| --- | --- | --- |
| 0 | Basic identity read | 75/79 bytes; model 0..31, name 32..63, analog inputs at 71, outputs at 73 |
| 6 | Telemetry read | Known layout sizes; see network notes |
| 9 | Output gain | Little-endian float |
| 10 | Output mute | Byte 0/1 |
| 12 | Routing | Float gain + mute byte; channel=input, segment=output, side=0 |
| 14 | Delay | Float milliseconds; side 0=input, 1=output |
| 15 | Standby | Byte 0/1; other states rejected |
| 17 | Display lock | Byte 0/1; command matches captured frame |
| 18 | Output polarity | Byte 0/1 |
| 52 | Input filter block | 141 bytes |
| 60 | Device name write | 32 padded bytes; read back through function 0 |

The filter block contains ten packed 14-byte records and one global bypass
byte. Each record: signed type, float gain, float frequency, float Q, reset
byte. Records 1..8 are EQ; first/last are crossovers and are preserved.
Negative type encodes disabled as -type-1; global bypass 0 means enabled.
No filter type is inferred from screenshot appearance.

The test suite includes a synthetic display-lock fixture and an independently
built UDP peer, readback rejection and timeouts. These demonstrate software
behavior, not physical-amplifier compatibility or persistence after power loss.
