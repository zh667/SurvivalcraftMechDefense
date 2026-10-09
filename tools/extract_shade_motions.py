"""Inventory the original Destroy Gunners Shade motion containers.

The PSMd payload is retained byte-for-byte in local/dguns/motions.  The
runtime catalog deliberately records source evidence and event coverage
separately: the Survivalcraft sample uses a retargeted event layer until a
full PSMd curve decoder is available.
"""
from __future__ import annotations

import argparse
import hashlib
import json
import struct
from pathlib import Path
import shutil


def read_motion(path: Path) -> dict:
    data = path.read_bytes()
    if not data.startswith(b"PSMd") or len(data) < 0x54:
        raise ValueError(f"not a PSMd motion: {path}")
    frames_a, frames_b = struct.unpack_from("<HH", data, 0x44)
    return {
        "id": int(path.stem),
        "file": path.name,
        "bytes": len(data),
        "sha256": hashlib.sha256(data).hexdigest(),
        "format": data[:4].decode("ascii"),
        "version": struct.unpack_from("<I", data, 4)[0],
        "frames": frames_a,
        "framesRepeat": frames_b,
        "headerSize": struct.unpack_from("<I", data, 8)[0],
        "sectionOffsets": list(struct.unpack_from("<9I", data, 8)),
    }


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--source", type=Path, default=Path("local/dguns/assets/grp.bin"))
    parser.add_argument("--raw-output", type=Path, default=Path("local/dguns/motions"))
    parser.add_argument("--catalog", type=Path, default=Path("Mod/Assets/Animations/catalog.json"))
    args = parser.parse_args()

    args.raw_output.mkdir(parents=True, exist_ok=True)
    rows = []
    for source in sorted(args.source.glob("*.bin")):
        if not source.read_bytes().startswith(b"PSMd"):
            continue
        raw = args.raw_output / f"{int(source.stem):04}.psmd"
        shutil.copyfile(source, raw)
        row = read_motion(source)
        row["rawFile"] = raw.as_posix()
        rows.append(row)

    # Grouping is based on the retained resource ordering, not a guessed name.
    groups = [
        {"name": "motion-set-0295", "ids": [295], "candidate": "utility"},
        {"name": "motion-set-0339", "ids": list(range(339, 349)), "candidate": "enemy"},
        {"name": "motion-set-0370", "ids": list(range(370, 373)), "candidate": "turret"},
        {"name": "motion-set-0411", "ids": list(range(411, 419)), "candidate": "mech"},
        {"name": "motion-set-0453", "ids": list(range(453, 457)), "candidate": "turret-or-support"},
    ]
    catalog = {
        "source": "Destroy Gunners Sigma 1.10 local grp.bin",
        "format": "PSMd",
        "rawCount": len(rows),
        "groups": groups,
        "motions": rows,
        "runtimePolicy": "retargeted-event-layer",
        "runtimeEvents": {
            "Turret": ["Deploy", "Idle", "Track", "Fire", "Hit", "Death"],
            "Mech": ["Deploy", "Idle", "Walk", "Fire_Rifle", "Fire_Missile", "Fire_Cannon", "Fire_Laser", "Fire_Grenade", "Reload", "Hit", "Death"],
            "Enemy": ["Deploy", "Idle", "Walk", "Fire", "Hit", "Death"],
        },
        "note": "Raw PSMd files are preserved and hashed. Event clips are retargeted to the extracted bind joints for this playable build; they are not claimed to be byte-level curve reconstruction.",
    }
    args.catalog.parent.mkdir(parents=True, exist_ok=True)
    args.catalog.write_text(json.dumps(catalog, indent=2), encoding="utf-8")
    print(f"Extracted {len(rows)} PSMd motion containers")


if __name__ == "__main__":
    main()
