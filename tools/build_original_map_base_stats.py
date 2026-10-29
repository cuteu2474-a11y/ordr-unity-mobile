#!/usr/bin/env python3
"""Build source-backed Warcraft object stats without guessing character identities.

Requires ``mpyq``. Example:

    python tools/build_original_map_base_stats.py \
      --w3x '/path/to/ORDR_S2_2.323[C].w3x'

The web character catalog and the original W3X have different names/coverage.
Only unique, normalized name+grade matches and nine explicitly checked common
rawcodes are joined. An unmatched character is NOT assigned a plausible ID.
"""

import argparse
from collections import Counter, defaultdict
from contextlib import contextmanager
import hashlib
import json
from pathlib import Path
import re
import sys
import unicodedata

from extract_original_unit_slk_stats import parse_sylk_bytes
from reconcile_ambiguous_original_map_objects import reconcile


PROJECT_ROOT = Path(__file__).resolve().parent.parent
DEFAULT_OUTPUT = PROJECT_ROOT / "data/original-2323C-map-base-stats.json"
EXPECTED_VERSION = "S2 2.323[C]"

# This is the map's literal suffix vocabulary, not a guessed equivalence
# between units of different grades.
GRADE_SUFFIX = {
    "흔함": "흔함", "안흔함": "안흔함", "특별함": "특별",
    "특수함": "특수", "희귀함": "희귀", "전설": "전설",
    "히든": "히든", "변화": "변화", "제한됨": "제한",
    "왜곡됨": "왜곡", "초월함": "초월", "영원한": "영원",
    "불멸의": "불멸", "세라핌": "세라핌",
    "랜덤": "랜덤전용", "신비함": "신비",
}

# Verified against original CampaignUnitStrings and UnitWeapons identifiers.
# Other variants of these names must not be silently confused with commons.
COMMON_RAWCODES = {
    "C-01": ("h003", "루피"),
    "C-02": ("h002", "조로"),
    "C-03": ("h001", "나미"),
    "C-04": ("h007", "우솝"),
    "C-05": ("h004", "상디"),
    "C-06": ("h008", "쵸파"),
    "C-07": ("h005", "버기"),
    "C-08": ("h009", "총병"),
    "C-09": ("h006", "칼병"),
}


class MPQOffsetStream:
    """Warcraft maps may prefix MPQ by a 512-byte HM3W map header."""

    def __init__(self, stream, offset):
        self.stream = stream
        self.offset = offset
        self.stream.seek(offset)

    def read(self, count=-1):
        return self.stream.read(count)

    def seek(self, position, whence=0):
        if whence == 0:
            return self.stream.seek(self.offset + position) - self.offset
        return self.stream.seek(position, whence) - self.offset

    def tell(self):
        return self.stream.tell() - self.offset


@contextmanager
def open_archive(path):
    try:
        import mpyq
    except ImportError as error:
        raise RuntimeError("mpyq is required to read the W3X archive") from error

    with path.open("rb") as stream:
        stream.seek(0)
        first = stream.read(4)
        if first == b"MPQ\x1a":
            offset = 0
        elif first == b"HM3W":
            stream.seek(512)
            if stream.read(4) != b"MPQ\x1a":
                raise ValueError("Warcraft map MPQ header not found at offset 512")
            offset = 512
        else:
            raise ValueError("Not a Warcraft map MPQ or HM3W W3X")
        yield mpyq.MPQArchive(MPQOffsetStream(stream, offset), listfile=False)


def sha256_bytes(value):
    return hashlib.sha256(value).hexdigest()


def sha256_file(path):
    hasher = hashlib.sha256()
    with path.open("rb") as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b""):
            hasher.update(chunk)
    return hasher.hexdigest()


def clean_name(name):
    return re.sub(r"\|c[0-9a-f]{8}|\|r", "", name, flags=re.IGNORECASE).strip()


def normalize_name(name):
    return re.sub(r"[^0-9a-z가-힣]", "", unicodedata.normalize("NFKC", name).lower())


def parse_names(data):
    names = {}
    rawcode = None
    for line in data.decode("utf-8-sig", errors="replace").splitlines():
        if line.startswith("[") and line.endswith("]"):
            rawcode = line[1:-1]
        elif rawcode is not None and line.startswith("Name="):
            if rawcode in names:
                raise ValueError("Duplicate unit name for rawcode " + rawcode)
            names[rawcode] = clean_name(line[5:])
    return names


def number(value):
    if value is None or value == "":
        return None
    result = float(value)
    if not result == result or result == float("inf") or result == float("-inf"):
        raise ValueError("Invalid object stat " + repr(value))
    return int(result) if result.is_integer() else result


def attack_stats(weapons, weapon_number, enabled):
    suffix = str(weapon_number)
    if not enabled:
        return None
    stats = {
        "attackCooldownSeconds": number(weapons.get("cool" + suffix)),
        "baseDamagePlus": number(weapons.get("dmgplus" + suffix)),
        "diceCount": number(weapons.get("dice" + suffix)),
        "diceSides": number(weapons.get("sides" + suffix)),
        "originalRange": number(weapons.get("rangeN" + suffix)),
        "originalAttackType": weapons.get("atkType" + suffix),
        "originalWeaponType": weapons.get("weapTp" + suffix),
        "attackPointSeconds": number(weapons.get("dmgpt" + suffix)),
    }
    base, dice, sides = (stats[k] for k in ("baseDamagePlus", "diceCount", "diceSides"))
    if (all(isinstance(x, int) for x in (base, dice, sides))
            and dice >= 0 and sides >= 1):
        stats["minimumBaseDamage"] = base + dice
        stats["maximumBaseDamage"] = base + dice * sides
    else:
        stats["minimumBaseDamage"] = None
        stats["maximumBaseDamage"] = None
    return stats


def object_stats(rawcode, name, weapons, balance, abilities):
    suffix = name.rsplit(" - ", 1)
    grade = suffix[-1] if len(suffix) == 2 else None
    attacks = number(weapons.get("weapsOn"))
    if attacks is None:
        attacks = 0
    if not isinstance(attacks, int) or attacks < 0 or attacks > 3:
        raise ValueError("Unexpected weapon flags for " + rawcode)
    result = {
        "rawcode": rawcode,
        "mapName": name,
        "mapGradeSuffix": grade,
        "originalWeaponFlags": attacks,
        "attack1": attack_stats(weapons, 1, bool(attacks & 1)),
        "attack2": attack_stats(weapons, 2, bool(attacks & 2)),
        "baseStats": {
            "hitPoints": number(balance.get("HP")),
            "mana": number(balance.get("manaN")),
            "healthRegeneration": number(balance.get("regenHP")),
            "manaRegeneration": number(balance.get("regenMana")),
            "armor": number(balance.get("def")),
            "armorType": balance.get("defType"),
            "movementSpeed": number(balance.get("spd")),
        },
        # Ability rawcodes are only evidence that an ability is attached.
        # They do not describe its triggers, targets or actual JASS behavior.
        "originalAbilityList": abilities.get("abilList"),
    }
    return result


def build(w3x, catalog):
    if catalog.get("gameVersion") != EXPECTED_VERSION:
        raise ValueError("Catalog is not S2 2.323[C]")
    version_sha = catalog.get("sourceW3xSha256")
    map_sha = sha256_file(w3x)
    expected = json.loads((PROJECT_ROOT / "data/character-master-database.json").read_text(
        encoding="utf-8"))["integrity"]["originalW3xSha256"]
    if map_sha != expected:
        raise ValueError("The supplied map SHA-256 differs from the original DB source")

    source_paths = {
        "weapons": "Units\\UnitWeapons.slk",
        "balance": "Units\\UnitBalance.slk",
        "abilities": "Units\\UnitAbilities.slk",
        "names": "Units\\CampaignUnitStrings.txt",
    }
    with open_archive(w3x) as archive:
        blobs = {key: archive.read_file(path) for key, path in source_paths.items()}
    for key, blob in blobs.items():
        if blob is None:
            raise ValueError("Missing Warcraft unit object file " + source_paths[key])

    weapons = parse_sylk_bytes(blobs["weapons"])
    balance = parse_sylk_bytes(blobs["balance"])
    abilities = parse_sylk_bytes(blobs["abilities"])
    names = parse_names(blobs["names"])
    if not set(weapons).issubset(names) or not set(weapons).issubset(balance):
        raise ValueError("Map object stats have weapon IDs without matching names/balance")
    objects = {
        code: object_stats(code, names[code], weapon,
                           balance[code], abilities.get(code, {}))
        for code, weapon in sorted(weapons.items())
    }

    by_name_grade = defaultdict(list)
    for code, obj in objects.items():
        label = obj["mapName"]
        if " - " in label:
            display = label.rsplit(" - ", 1)[0]
            by_name_grade[(obj["mapGradeSuffix"], normalize_name(display))].append(code)

    links = []
    used_rawcodes = set()
    for unit in catalog["entries"]:
        uid = unit["id"]
        if uid in COMMON_RAWCODES:
            code, expected_name = COMMON_RAWCODES[uid]
            obj = objects[code]
            if (unit["grade"] != "흔함" or unit["name"] != expected_name
                    or not obj["mapName"].endswith(" - 흔함")
                    or expected_name not in obj["mapName"]):
                raise ValueError("Common identity drift: " + uid)
            status = "verified-common-map-identity"
            candidates = [code]
        elif uid == "PS-01":
            # e0IX BIU can roll the source-only pirate ship. It has no
            # normal grade suffix and is not one of the nine common units.
            obj = objects.get("h060")
            if (unit["name"] != "해적선" or unit["grade"] != "해적선" or
                    obj is None or obj["mapName"] != "해적선" or
                    obj["mapGradeSuffix"] is not None or
                    obj["attack1"] is None or
                    obj["attack1"]["baseDamagePlus"] != 510 or
                    obj["attack1"]["diceCount"] != 2 or
                    obj["attack1"]["diceSides"] != 4 or
                    obj["attack1"]["attackCooldownSeconds"] != 0.9 or
                    obj["originalAbilityList"] != "A0BB,A0JE,Avul"):
                raise ValueError("Pinned source pirate-ship identity drift")
            code = "h060"
            status = "verified-source-wisp-pirate-ship"
            candidates = [code]
        else:
            expected_grade = GRADE_SUFFIX[unit["grade"]]
            candidates = by_name_grade.get(
                (expected_grade, normalize_name(unit["name"])), [])
            code = candidates[0] if len(candidates) == 1 else None
            status = ("unique-map-name-and-grade" if len(candidates) == 1 else
                      "ambiguous-name-and-grade" if candidates else "unmatched")
        if code:
            if code in used_rawcodes:
                raise ValueError("One original object mapped to multiple catalog IDs: " + code)
            used_rawcodes.add(code)
        links.append({
            "catalogId": uid,
            "catalogName": unit["name"],
            "grade": unit["grade"],
            "mappingStatus": status,
            "originalRawcode": code,
            "candidateRawcodes": list(candidates) if not code else [],
            "sourceMapName": objects[code]["mapName"] if code else None,
            "attack1": objects[code]["attack1"] if code else None,
            "attack2": objects[code]["attack2"] if code else None,
            "baseStats": objects[code]["baseStats"] if code else None,
            "originalAbilityList": objects[code]["originalAbilityList"] if code else None,
            "combatParityVerified": False,
        })

    status_count = Counter(link["mappingStatus"] for link in links)
    result = {
        "format": "ordr-source-map-base-stats-v1",
        "gameVersion": EXPECTED_VERSION,
        "sourceW3xSha256": map_sha,
        "unitObjectSources": {source_paths[key]: sha256_bytes(blob)
                              for key, blob in blobs.items()},
        "purpose": "All original unit object stats and conservatively linked catalog basics",
        "importantLimits": [
            "Raw Warcraft attack damage is dmgplus + dice roll; it is not final damage after upgrades, armor or JASS.",
            "Original range is retained for evidence; mobile attack range policy is independent.",
            "Matching rawcode by name+grade does not verify ability logic or effective combat parity.",
            "Ambiguous or missing catalog identities remain unassigned; zero must never mean unknown.",
        ],
        "summary": {
            "originalNamedObjects": len(objects),
            "catalogCharacters": len(links),
            "linkedCatalogCharacters": sum(bool(x["originalRawcode"]) for x in links),
            "unresolvedCatalogCharacters": sum(not x["originalRawcode"] for x in links),
            "mappingStatusCounts": dict(sorted(status_count.items())),
            "linkedWithAttack1": sum(x["attack1"] is not None for x in links),
            "gameplayParityVerified": 0,
        },
        "catalogEntries": links,
        "mapObjects": list(objects.values()),
    }
    # CWR/BGH fusion results resolve ambiguities, while BjB confirms catalog
    # aliases and named transcendent titles using the exact fusion rawcodes.
    read = lambda name: json.loads((PROJECT_ROOT / "data" / name).read_text(encoding="utf-8"))
    return reconcile(result, read("original-jass-fusion-registry.json"),
                     read("original-unit-rawcode-label-links.json"))


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--w3x", type=Path, required=True)
    parser.add_argument("--catalog", type=Path, default=PROJECT_ROOT / "data/units.json")
    parser.add_argument("--output", type=Path, default=DEFAULT_OUTPUT)
    parser.add_argument("--verify", action="store_true", help="Compare reproducible output, do not write")
    args = parser.parse_args()
    result = build(args.w3x, json.loads(args.catalog.read_text(encoding="utf-8")))
    encoded = json.dumps(result, ensure_ascii=False, indent=2) + "\n"
    if args.verify:
        if not args.output.exists() or args.output.read_text(encoding="utf-8") != encoded:
            raise ValueError("Generated stat catalog is missing or stale")
        print("PASS: reproducible map stats index, no file changed")
    else:
        args.output.parent.mkdir(parents=True, exist_ok=True)
        args.output.write_text(encoded, encoding="utf-8")
        print("Wrote", args.output)
    print(json.dumps(result["summary"], ensure_ascii=False, indent=2))


if __name__ == "__main__":
    try:
        main()
    except (ValueError, RuntimeError) as error:
        sys.exit(str(error))
