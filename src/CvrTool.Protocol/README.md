# Protocol

`NetworkTarget` validates literal unicast IPv4 peers without DNS or scanning.
`CvrWire` implements packed frames, bounded fragment headers and checksums.
`CvrReplyAssembler` bounds interleaved replies and rejects corrupt/conflicting
fragments while retaining valid announcements. `CvrUdpClient` serializes exchanges, acknowledges/reassembles replies and times
out without retrying writes. Application owns semantic validation/readback.
The older `LoopbackUdpTransport` remains a separate loopback-only test utility.
See `docs/protocol-evidence.md` for observed mappings and validation limits.
