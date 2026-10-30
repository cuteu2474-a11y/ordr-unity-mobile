#!/usr/bin/env python3
"""Extract war3map.j from an owned Warcraft III MPQ/W3X map.

Standard library only; the original binary is never modified or committed.
Usage: python3 tools/extract_mpq_war3map_j.py map.w3x --output /tmp/war3map.j --verify-2323c
"""
import argparse
import bz2
import hashlib
from pathlib import Path
import struct
import zlib

MAP_SHA = "0794996c9bc73eaa3020f20ebc45fb37cfa64a6e98ffe8aa0dfa3b356e77bc5c"
JASS_SHA = "96201e80edec1b2764f85818760306c6b2f0becd390ba4d63f41c86f3dfaff6d"
MASK = 0xFFFFFFFF


def make_crypt_table():
    table = [0] * 1280
    seed = 0x00100001
    for i in range(256):
        for j in range(5):
            seed = (seed * 125 + 3) % 0x2AAAAB
            high = (seed & 0xFFFF) << 16
            seed = (seed * 125 + 3) % 0x2AAAAB
            table[j * 256 + i] = high | (seed & 0xFFFF)
    return table


CRYPT = make_crypt_table()


def mpq_hash(name, mode):
    seed1, seed2 = 0x7FED7FED, 0xEEEEEEEE
    for char in name.replace("/", "\\").upper().encode("ascii"):
        seed1 = (CRYPT[mode * 256 + char] ^ (seed1 + seed2)) & MASK
        seed2 = (char + seed1 + seed2 + (seed2 << 5) + 3) & MASK
    return seed1


def decrypt_table(data, key):
    if len(data) % 4:
        raise ValueError("Incomplete MPQ table")
    seed1, seed2 = key, 0xEEEEEEEE
    result = bytearray(len(data))
    for index in range(0, len(data), 4):
        value = struct.unpack_from("<I", data, index)[0]
        seed2 = (seed2 + CRYPT[1024 + (seed1 & 255)]) & MASK
        plain = (value ^ (seed1 + seed2)) & MASK
        seed1 = (((~seed1 & MASK) << 21) + 0x11111111 | (seed1 >> 11)) & MASK
        seed2 = (plain + seed2 + (seed2 << 5) + 3) & MASK
        struct.pack_into("<I", result, index, plain)
    return result


def checksum(path):
    sha = hashlib.sha256()
    with path.open("rb") as src:
        for chunk in iter(lambda: src.read(1 << 20), b""):
            sha.update(chunk)
    return sha.hexdigest()


def decompress_sector(data, expected_size):
    if len(data) == expected_size:
        return data
    if not data:
        raise ValueError("Empty compressed sector")
    if data[0] == 0x02:
        result = zlib.decompress(data[1:])
    elif data[0] == 0x10:
        result = bz2.decompress(data[1:])
    else:
        raise ValueError("Unsupported MPQ compression mask: 0x%02x" % data[0])
    if len(result) != expected_size:
        raise ValueError("Invalid decompressed sector length")
    return result


def extract_jass(path):
    with path.open("rb") as handle:
        prefix = handle.read(2 << 20)
        base = prefix.find(b"MPQ\x1a")
        if base < 0:
            raise ValueError("MPQ header not found")
        handle.seek(base)
        header = handle.read(32)
        if len(header) != 32:
            raise ValueError("Invalid MPQ header")
        magic, header_size, archive_size, version, shift, hash_offset, \
            block_offset, hash_count, block_count = struct.unpack("<4s2I2H4I", header)
        if magic != b"MPQ\x1a" or version != 0 or header_size < 32 or \
                hash_count < 1 or block_count < 1 or \
                base + archive_size > path.stat().st_size:
            raise ValueError("Unsupported or truncated MPQ archive")
        handle.seek(base + hash_offset)
        hashes = decrypt_table(handle.read(hash_count * 16),
                               mpq_hash("(hash table)", 3))
        handle.seek(base + block_offset)
        blocks = decrypt_table(handle.read(block_count * 16),
                               mpq_hash("(block table)", 3))
        hash_a = mpq_hash("war3map.j", 1)
        hash_b = mpq_hash("war3map.j", 2)
        matches = []
        for index in range(hash_count):
            a, b, _, _, block_index = struct.unpack_from("<2I2HI", hashes, 16 * index)
            if a == hash_a and b == hash_b and block_index < block_count:
                matches.append(block_index)
        if len(matches) != 1:
            raise ValueError("Expected one war3map.j; found %d" % len(matches))
        offset, packed, unpacked, flags = struct.unpack_from("<4I",
                                                             blocks, 16 * matches[0])
        if not (flags & 0x80000000) or flags & 0x00010000 or \
                base + offset + packed > path.stat().st_size:
            raise ValueError("Missing, encrypted or truncated war3map.j")
        handle.seek(base + offset)
        content = handle.read(packed)
        if not (flags & 0x200):
            return content
        if flags & 0x01000000:
            return decompress_sector(content, unpacked)
        sector_size = 512 << shift
        num_sectors = (unpacked + sector_size - 1) // sector_size
        table_size = 4 * (num_sectors + 1)
        if len(content) < table_size:
            raise ValueError("Truncated MPQ sector offsets")
        offsets = struct.unpack_from("<%dI" % (num_sectors + 1), content)
        if offsets[0] < table_size or offsets[-1] > packed or \
                any(offsets[i] > offsets[i + 1] for i in range(num_sectors)):
            raise ValueError("Invalid MPQ sector offsets")
        output = bytearray()
        for i in range(num_sectors):
            start, end = offsets[i], offsets[i + 1]
            expected = min(sector_size, unpacked - len(output))
            output.extend(decompress_sector(content[start:end], expected))
        if len(output) != unpacked:
            raise ValueError("Incorrect war3map.j length")
        return bytes(output)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("map", type=Path)
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--verify-2323c", action="store_true",
                        help="require the exact original map and JASS checksums")
    options = parser.parse_args()
    if options.verify_2323c and checksum(options.map) != MAP_SHA:
        raise ValueError("This is not the pinned ORDR S2 2.323[C] map")
    jass = extract_jass(options.map)
    digest = hashlib.sha256(jass).hexdigest()
    if options.verify_2323c and digest != JASS_SHA:
        raise ValueError("Extracted JASS does not match the pinned original")
    options.output.parent.mkdir(parents=True, exist_ok=True)
    options.output.write_bytes(jass)
    print("war3map.j bytes=%d sha256=%s" % (len(jass), digest))


if __name__ == "__main__":
    main()
