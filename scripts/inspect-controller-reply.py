"""Decode reassembled controller reply bodies for offline capture comparison."""
import argparse
import json
import struct


def decode(function, body):
    size = len(body)
    if function == 59 and size in (512, 768, 1280):
        return {"slot_count": size // 32, "slots": [
            {"number": i // 32 + 1, "name_bytes": body[i:i + 32].rstrip(b"\0").hex(),
             "name_preview": body[i:i + 32].rstrip(b"\0").decode("ascii", "backslashreplace")}
            for i in range(0, size, 32)]}
    if function == 52 and size in (99, 141):
        return {"bands": list(struct.iter_unpack("<bfffB", body[:-1])), "bypass": body[-1]}
    if function == 53 and size == 21:
        attack, hold, release, knee, *points, gain, bypass = struct.unpack("<3hB3f2B", body)
        return dict(attack=attack, hold=hold, release=release, knee=knee, points=points, gain=gain, bypass=bypass)
    if function == 53 and size == 105:
        return {"attack": struct.unpack_from("<4h", body), "hold": struct.unpack_from("<4h", body, 8),
                "release": struct.unpack_from("<4h", body, 16), "knees": list(body[24:28]),
                "points": list(struct.iter_unpack("<4f", body[28:92])),
                "frequencies": struct.unpack_from("<3f", body, 92), "bypass": body[104]}
    if function == 79 and size == 1:
        return {"analog_type": body[0]}
    if function == 80 and size == 4:
        return dict(zip(("first", "second", "backup", "threshold"), struct.unpack("<BBBb", body)))
    if function == 81 and size == 40:
        return {"modes": list(body[:4]), "sensitivity": list(body[4:8]),
                "loads": struct.unpack_from("<4f", body, 8), "voltages": struct.unpack_from("<4f", body, 24)}
    if function == 43 and size in (2048, 2080):
        return {"name_bytes": body[:-2048].hex(), "taps": struct.unpack("<512f", body[-2048:])}
    raise ValueError(f"Unsupported reply layout: function {function}, {size} bytes")


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("function", type=int)
    parser.add_argument("payload_hex", help="Reassembled body only, without transport or message headers")
    args = parser.parse_args()
    try:
        print(json.dumps(decode(args.function, bytes.fromhex(args.payload_hex)), indent=2))
    except ValueError as error:
        parser.error(str(error))
