#!/usr/bin/env python3
"""Inject a personal portrait ZIP into an ignored Unity Resources folder.

The image ZIP must live outside this PUBLIC Git repository. Unity imports these
PNGs as Sprite assets via OrdrPrivateCharacterPortraitImporter before building.
"""

import argparse
import json
import re
import struct
import sys
import zipfile
from pathlib import Path

PNG_SIGNATURE = b"\x89PNG\r\n\x1a\n"
RESOURCE_PATH = Path("Assets/Resources/CharacterPortraits")
MAX_FILE_SIZE = 4 * 1024 * 1024
IMAGE_NAME = re.compile(r"^([A-Z]{1,3})-?(\d{1,2})\.png$")


def catalog_image_names() -> dict[str, str]:
    """Map image basenames (C1 / TR24) to stable catalog IDs (C-01 / TR-24)."""
    root = Path(__file__).resolve().parents[1]
    lookup = {}
    for filename in ("data/units.json", "data/wiki-unit-catalog-extension.json"):
        for entry in json.loads((root / filename).read_text(encoding="utf-8"))["entries"]:
            unit_id = entry["id"]
            prefix, number = unit_id.split("-")
            lookup[f"{prefix}{int(number)}"] = unit_id
    return lookup


def validate_png(data: bytes, name: str) -> None:
    if len(data) < 33 or len(data) > MAX_FILE_SIZE:
        raise ValueError(f"{name}: invalid PNG file size")
    if not data.startswith(PNG_SIGNATURE) or data[12:16] != b"IHDR":
        raise ValueError(f"{name}: not a PNG with an IHDR header")
    if struct.unpack(">I", data[8:12])[0] != 13:
        raise ValueError(f"{name}: invalid PNG IHDR length")
    width, height = struct.unpack(">II", data[16:24])
    if not (1 <= width <= 2048 and 1 <= height <= 2048):
        raise ValueError(f"{name}: invalid image dimensions")


def read_portraits(source: Path) -> dict[str, bytes]:
    """Read any known catalog images without extracting arbitrary ZIP paths."""
    names = catalog_image_names()
    images = {}
    if source.is_dir():
        entries = ((path.name, path.read_bytes()) for path in source.iterdir()
                   if path.is_file() and not path.is_symlink() and path.suffix == ".png")
        for filename, data in entries:
            match = IMAGE_NAME.fullmatch(filename)
            if match is None:
                continue
            unit_id = names.get(f"{match.group(1)}{int(match.group(2))}")
            if unit_id is None:
                continue
            if unit_id in images:
                raise ValueError(f"Duplicate portrait ID: {unit_id}")
            images[unit_id] = data
    else:
        with zipfile.ZipFile(source) as archive:
            for item in archive.infolist():
                # Only filenames at the ZIP root; never expand archive paths.
                if item.is_dir() or "/" in item.filename or "\\" in item.filename:
                    continue
                match = IMAGE_NAME.fullmatch(item.filename)
                if match is None:
                    continue
                unit_id = names.get(f"{match.group(1)}{int(match.group(2))}")
                if unit_id is None:
                    continue
                if unit_id in images:
                    raise ValueError(f"Duplicate portrait ID: {unit_id}")
                images[unit_id] = archive.read(item)
    if not images:
        raise ValueError("No known catalog portrait PNG files found")
    for unit_id, image in images.items():
        validate_png(image, unit_id)
    return images


def inject(source: Path, project_root: Path) -> list[Path]:
    source = source.resolve()
    project_root = project_root.resolve()
    if source == project_root or project_root in source.parents:
        raise ValueError("Keep the source ZIP/directory outside the public Git repository")
    ignore_file = project_root / ".gitignore"
    if not ignore_file.is_file() or "Assets/Resources/CharacterPortraits/" not in ignore_file.read_text():
        raise ValueError("Public repo must ignore Assets/Resources/CharacterPortraits/ first")
    images = read_portraits(source)  # Validate the entire supplied batch first.
    target = project_root / RESOURCE_PATH
    target.mkdir(parents=True, exist_ok=True)
    destinations = []
    for unit_id, image in images.items():
        destination = target / f"{unit_id}.png"
        if not destination.exists() or destination.read_bytes() != image:
            destination.write_bytes(image)
        destinations.append(destination)
    return destinations


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--source", required=True, type=Path, help="Private ZIP or directory outside Git repo")
    parser.add_argument("--project-root", type=Path, default=Path(__file__).resolve().parents[1])
    args = parser.parse_args()
    try:
        destinations = inject(args.source, args.project_root)
    except (OSError, ValueError, KeyError, zipfile.BadZipFile) as exc:
        print(f"Private portrait import failed: {exc}", file=sys.stderr)
        return 1
    print(f"Ready for a PRIVATE Unity APK build: {len(destinations)} portraits injected (ignored by Git)")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
