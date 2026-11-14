#!/usr/bin/env python3
"""One-time, license-reviewed, immutable-source Unity font asset import.

The workflow calls this from a GitHub-hosted runner because the GitHub
connector supports UTF-8 source edits but cannot transfer TTF/OTF binaries.
No system fonts are read, no proprietary Microsoft font is downloaded.
"""

from __future__ import annotations

import hashlib
import os
from pathlib import Path
from tempfile import NamedTemporaryFile
from urllib.request import Request, urlopen

DESTINATION = Path(__file__).resolve().parents[1] / "Assets/Resources/Fonts"

MAPLE_REVISION = "73d0a6b839137a3e84186a962168ea0e245df548"
PRETENDARD_REVISION = "7aeb0698819be2b4097dae8ec8fe6a795e5cf3ae"

# Git's blob object SHA-1 verifies exact unmodified upstream bytes.
# Source licences: Nexon MapleStory free redistributable unmodified;
# Pretendard SIL OFL 1.1; see Assets/Resources/Fonts/README.md.
ASSETS = (
    (
        "Maplestory-Bold.ttf",
        f"https://raw.githubusercontent.com/fonts-archive/Maplestory/{MAPLE_REVISION}/Maplestory-Bold.ttf",
        "7b6ebd851b477173b5146d0f0317fb9146e2e418",
        1394116,
        b"\x00\x01\x00\x00",
    ),
    (
        "Maplestory-Light.ttf",
        f"https://raw.githubusercontent.com/fonts-archive/Maplestory/{MAPLE_REVISION}/Maplestory-Light.ttf",
        "f5b385346735ec73f1249675191cf73285f8d736",
        1357656,
        b"\x00\x01\x00\x00",
    ),
    (
        "Pretendard-Regular.otf",
        f"https://raw.githubusercontent.com/orioncactus/pretendard/{PRETENDARD_REVISION}/packages/pretendard/dist/public/static/Pretendard-Regular.otf",
        "08bf4cfc2164a0bff74a4bf844128d3b843bbf87",
        1574352,
        b"OTTO",
    ),
    (
        "Pretendard-OFL.txt",
        f"https://raw.githubusercontent.com/orioncactus/pretendard/{PRETENDARD_REVISION}/LICENSE",
        "160421b6039671e5c85e1646bee4167844a5d69c",
        4816,
        b"Copyright",
    ),
)


def git_blob_hash(content: bytes) -> str:
    header = f"blob {len(content)}\0".encode("ascii")
    return hashlib.sha1(header + content).hexdigest()


def install(name: str, url: str, blob_sha: str, size: int, signature: bytes) -> None:
    request = Request(url, headers={"User-Agent": "ORDR-Font-Sync/1.0"})
    with urlopen(request, timeout=60) as response:
        if response.status != 200:
            raise RuntimeError(f"{name}: HTTP {response.status}")
        content = response.read(size + 1)

    if len(content) != size or not content.startswith(signature):
        raise ValueError(f"{name}: upstream length/type changed, refusing import")
    actual_sha = git_blob_hash(content)
    if actual_sha != blob_sha:
        raise ValueError(f"{name}: upstream Git blob SHA mismatch: {actual_sha}")

    destination = DESTINATION / name
    if destination.exists() and destination.read_bytes() == content:
        print(f"UNCHANGED {destination.relative_to(DESTINATION.parent.parent.parent)}")
        return

    with NamedTemporaryFile(
        mode="wb", dir=DESTINATION, prefix=".ordr-font-", delete=False
    ) as temp:
        tmp_name = temp.name
        temp.write(content)
        temp.flush()
        os.fsync(temp.fileno())
    try:
        os.replace(tmp_name, destination)
    finally:
        if os.path.exists(tmp_name):
            os.unlink(tmp_name)
    print(f"VERIFIED {name}: {size} bytes, blob={blob_sha}")


def main() -> None:
    DESTINATION.mkdir(parents=True, exist_ok=True)
    for item in ASSETS:
        install(*item)


if __name__ == "__main__":
    main()
