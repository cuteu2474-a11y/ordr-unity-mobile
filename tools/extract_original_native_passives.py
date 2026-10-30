#!/usr/bin/env python3
"""Extract source AHbh passives with a separate practice-only first-rank set.

Inputs are unmodified SLKs from the pinned user-owned 2.323[C] W3X.
Bonus-damage effects, multiple active bashes and missing fields remain excluded.
"""
import argparse
import hashlib
import json
import re
from decimal import Decimal
from pathlib import Path
from build_original_map_base_stats import open_archive
from extract_original_helper_abilities import read_objects

MAP_SHA = "0794996c9bc73eaa3020f20ebc45fb37cfa64a6e98ffe8aa0dfa3b356e77bc5c"
ABILITY_SHA = "23e9eb93d805a63cc76bfb617189331aef1e40c0c0ec69860bd7399a3bef2eda"
OWNERS_SHA = "fbd47be22d1482d63df15d70d7a8fa38f7f26b288443a5d74aa056104aef139d"
OVERRIDES_SHA = "aa938f02c22884f38c5e31bcd6229a35e917a8fab0128331fdab88980cfd50e0"


def slk_rows(path, expected_sha):
    data = path.read_bytes()
    if hashlib.sha256(data).hexdigest() != expected_sha:
        raise ValueError("원본 SLK 지문 불일치: " + str(path))
    cells = {}
    x = y = 0
    for line in data.decode("utf-8-sig").splitlines():
        if not line.startswith("C;"):
            continue
        value = None
        for part in re.split(r';(?=(?:[^\"]*\"[^\"]*\")*[^\"]*$)', line)[1:]:
            if part.startswith("X"):
                x = int(part[1:])
            elif part.startswith("Y"):
                y = int(part[1:])
            elif part.startswith("K"):
                value = part[1:]
                if value.startswith('"') and value.endswith('"'):
                    value = value[1:-1].replace('""', '"')
        if value is not None:
            cells[x, y] = value
    headers = {x: value for (x, y), value in cells.items() if y == 1}
    rows = {}
    for (x, y), value in cells.items():
        if y > 1:
            rows.setdefault(y, {})[headers[x]] = value
    return {row[headers[1]]: row for row in rows.values()}


def extract(ability_file, owner_file, base_file, w3x):
    abilities = slk_rows(ability_file, ABILITY_SHA)
    owners = slk_rows(owner_file, OWNERS_SHA)
    base = json.loads(base_file.read_text())
    if base["sourceW3xSha256"] != MAP_SHA:
        raise ValueError("맵 기본 객체 지문 불일치")
    if hashlib.sha256(w3x.read_bytes()).hexdigest() != MAP_SHA:
        raise ValueError("원본 W3X 지문 불일치")
    with open_archive(w3x) as archive:
        overrides = archive.read_file('war3map.w3a')
    if not overrides or hashlib.sha256(overrides).hexdigest() != OVERRIDES_SHA:
        raise ValueError("원본 능력 오버라이드 지문 불일치")
    # aub1 is an editor tooltip; it does not override gameplay fields.
    overridden = {new if new.strip('\0') else old
                  for _, old, new, fields in read_objects(overrides)
                  if any(field[0] != 'aub1' for field in fields)}
    entries = []
    practice_mixed = []
    practice_bonus = []
    for character in base["catalogEntries"]:
        raw = character.get("originalRawcode")
        if not raw:
            continue
        source_abilities = owners.get(raw, {}).get("abilList", "")
        if source_abilities != character.get("originalAbilityList"):
            raise ValueError("캐릭터/SLK 능력 소유 관계 불일치: " + raw)
        # Multiple active AHbh abilities require source-exact stacking/order.
        # Do not multiply or roll them independently by convenience.
        active_bashes = [code for code in source_abilities.split(",")
                         if abilities.get(code, {}).get("code") == "AHbh" and
                         Decimal(abilities[code].get("DataA1", "0")) > 0]
        if len(active_bashes) != 1:
            continue
        for ability in source_abilities.split(","):
            row = abilities.get(ability, {})
            if row.get("code") != "AHbh":
                continue
            if ability in overridden:
                continue
            levels = int(row.get("levels", "0"))
            if levels not in (1, 2, 3):
                continue
            fields = ("DataA1", "DataB1", "DataC1", "Dur1", "HeroDur1", "Cool1", "DataD1", "targs1")
            if any(field not in row or row[field] in ("_", "") for field in fields):
                continue
            chance, multiplier, bonus, duration, hero, cooldown, data_d = (
                Decimal(row[field]) for field in fields[:-1])
            if chance <= 0 or chance > 100 or bonus < 0 or cooldown != 0 or data_d != 0:
                continue
            if bonus > 0:
                # One single-level ability, without critical/multiple-bash
                # interaction. The damage school and final retail HP impact
                # are not proven by these object fields.
                if levels != 1 or multiplier != 0 or duration < 0 or hero < 0 or (duration == 0 and hero != 0):
                    continue
                mask_parts = set(row['targs1'].split(','))
                if 'air' not in mask_parts or not mask_parts <= {'air', 'ground', 'enemies', 'neutral'}:
                    continue
                scaled = chance * 10000
                if scaled != int(scaled):
                    raise ValueError("확률 정밀도 초과: " + ability)
                practice_bonus.append({
                    'catalogId': character['catalogId'], 'catalogName': character['catalogName'],
                    'originalUnitRawcode': raw, 'abilityRawcode': ability, 'level': 1,
                    'kind': 'bonus-stun' if duration > 0 else 'bonus',
                    'chancePerMillion': int(scaled), 'bonusDamage': float(bonus),
                    'normalStunSeconds': float(duration), 'heroStunSeconds': float(hero),
                    'targetMask': row['targs1'],
                    'sourceFields': {field: row[field] for field in fields},
                })
                continue
            kind = "critical" if multiplier > 1 and duration == 0 and hero == 0 else (
                "stun" if multiplier == 0 and duration > 0 and hero >= 0 else (
                "critical-stun" if multiplier > 1 and duration > 0 and hero >= 0 else None))
            if kind is None:
                continue
            scaled = chance * 10000
            if scaled != int(scaled):
                raise ValueError("확률 정밀도 초과: " + ability)
            entry = {
                "catalogId": character["catalogId"], "catalogName": character["catalogName"],
                "originalUnitRawcode": raw, "abilityRawcode": ability, "level": 1,
                "kind": kind, "chancePerMillion": int(scaled),
                "criticalMultiplier": float(multiplier), "normalStunSeconds": float(duration),
                "heroStunSeconds": float(hero), "targetMask": row["targs1"],
                "sourceFields": {field: row[field] for field in fields},
            }
            if levels == 1 and kind != "critical-stun":
                entries.append(entry)
            else:
                entry["declaredLevels"] = levels
                practice_mixed.append(entry)
    return {"format": "ordr-2323C-native-pure-passives-v1", "gameVersion": "S2 2.323[C]",
            "sourceW3xSha256": MAP_SHA, "abilityDataSha256": ABILITY_SHA,
            "unitAbilitiesSha256": OWNERS_SHA,
            "abilityOverridesSha256": OVERRIDES_SHA,
            "scope": "complete-single-level-pure-critical-or-stun-only; target eligibility remains live state",
            "entries": entries,
            "practiceFirstLevelEntries": practice_mixed,
            "practiceFirstLevelScope": "model only; rank upgrades and native simultaneous-effect parity unverified",
            "practiceBonusEntries": practice_bonus,
            "practiceBonusScope": "model only; reference attack-bonus pipeline; retail school, immunity and final HP parity unverified"}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--abilities", type=Path, required=True)
    parser.add_argument("--owners", type=Path, required=True)
    parser.add_argument("--base", type=Path, required=True)
    parser.add_argument("--w3x", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    result = extract(args.abilities, args.owners, args.base, args.w3x)
    args.output.write_text(json.dumps(result, ensure_ascii=False, indent=2) + "\n")
    print("원본 순수 기절/크리티컬 능력", len(result["entries"]), "개 추출")


if __name__ == "__main__":
    main()
