#!/usr/bin/env python3
"""Verify twenty object ability records against the exact ORDR 2.323[C] W3X.

This audit marks confirmed CWR fusion registrations separately from other
object abilities. Fusion registration does not prove exclusive usage.
A native ability base class or a JASS literal reference
ALONE never proves the precise gameplay effect or event activation.

Requires mpyq, the original pinned map, and the existing static inventory.
The original binary is never copied into the Git repository.
"""
import argparse
from collections import Counter
import hashlib
import json
from pathlib import Path
import re

from audit_attack_skill_batch import functions, inspect_function
from build_original_map_base_stats import open_archive
from extract_original_helper_abilities import read_objects
from extract_original_unit_slk_stats import parse_sylk_bytes


ROOT = Path(__file__).resolve().parents[1]
SOURCE = ROOT / "data/original-2323C-next-20-skill-inventory.json"
OUT = ROOT / "data/original-2323C-next-20-native-source-audit.json"
REPORT = ROOT / "docs/다음-20명-원본-JASS-능력-분석.md"
W3X_SHA = "0794996c9bc73eaa3020f20ebc45fb37cfa64a6e98ffe8aa0dfa3b356e77bc5c"
JASS_SHA = "96201e80edec1b2764f85818760306c6b2f0becd390ba4d63f41c86f3dfaff6d"
ABILITY_SHA = "23e9eb93d805a63cc76bfb617189331aef1e40c0c0ec69860bd7399a3bef2eda"
OWNER_SHA = "fbd47be22d1482d63df15d70d7a8fa38f7f26b288443a5d74aa056104aef139d"
W3A_SHA = "aa938f02c22884f38c5e31bcd6229a35e917a8fab0128331fdab88980cfd50e0"
GAMEPLAY_FIELDS = ("levels", "code", "targs1", "Cast1", "Dur1", "HeroDur1",
                   "Cool1", "Area1", "Rng1", "Cost1", "DataA1", "DataB1",
                   "DataC1", "DataD1", "UnitID1", "BuffID1")
SHARED_SYSTEM_CODES = {"A0JE", "A07Y", "A0BA", "Avul"}
HELPER_IDS = ("e0KD", "h0C5", "e0KG", "e00T", "e00Q", "e00R")


def checked(raw, expected, name):
    actual = hashlib.sha256(raw).hexdigest()
    if actual != expected:
        raise ValueError("Original source hash mismatch " + name + ": " + actual)
    return raw


def main_audit(w3x):
    checked(w3x.read_bytes(), W3X_SHA, "W3X")
    with open_archive(w3x) as archive:
        jass_bytes = checked(archive.read_file("war3map.j"), JASS_SHA, "JASS")
        ability_bytes = checked(archive.read_file(r"Units\AbilityData.slk"), ABILITY_SHA,
                                "AbilityData")
        owner_bytes = checked(archive.read_file(r"Units\UnitAbilities.slk"), OWNER_SHA,
                              "UnitAbilities")
        w3a_bytes = checked(archive.read_file("war3map.w3a"), W3A_SHA, "W3A")
    abilities = parse_sylk_bytes(ability_bytes)
    owners = parse_sylk_bytes(owner_bytes)
    overrides = {}
    for table, old, new, fields in read_objects(w3a_bytes):
        key = new if new.strip("\x00") else old
        overrides.setdefault(key, []).extend({
            "field": field, "type": typ, "level": level,
            "dataPointer": ptr, "value": value,
            "originalObjectId": old, "section": table,
        } for field, typ, level, ptr, value in fields)

    source = json.loads(SOURCE.read_text(encoding="utf-8"))
    fusions = json.loads((ROOT / "data/original-jass-fusion-registry.json")
                         .read_text(encoding="utf-8"))
    fusion_ids = {x["abilityRawcode"] for x in fusions["registrations"]}
    functions_by_name = functions(jass_bytes.decode("utf-8-sig").replace("\r\n", "\n"))
    registration = {}
    for name, (_, clean, line) in functions_by_name.items():
        for match in re.finditer(r"call w5\(\$([0-9A-Fa-f]{8}),function (\w+)\)", clean):
            raw = bytes.fromhex(match.group(1)).decode("ascii", errors="replace")
            registration.setdefault(raw, []).append({"function": match.group(2), "line": line})

    unique_codes = {code for row in source["units"]
                    for code in row["objectAbilityRawcodesInMapOrder"]}
    direct_references = {}
    for code in sorted(unique_codes):
        literal = "$" + code.encode("ascii").hex().upper()
        pattern = re.compile(r"(?<![0-9A-F])" + re.escape(literal) + r"(?![0-9A-F])", re.I)
        hits = [{"function": name, "functionStartsAtLine": line,
                 "rawcodeLiteralMatches": len(pattern.findall(clean))}
                for name, (_, clean, line) in functions_by_name.items()
                if pattern.search(clean)]
        direct_references[code] = hits

    rows = []
    category_counts = Counter()
    for unit in source["units"]:
        raw = unit["originalUnitRawcode"]
        from_map = owners[raw]["abilList"].split(",")
        listed = unit["objectAbilityRawcodesInMapOrder"]
        if from_map != listed:
            raise ValueError("Object owner abilities changed " + unit["catalogId"])
        results = []
        for code in listed:
            original = abilities.get(code)
            if original is None:
                raise ValueError("Missing ability object " + code)
            if code in fusion_ids:
                category = "CWR_FUSION_REGISTERED_COMBAT_USE_NOT_VERIFIED"
            elif code in SHARED_SYSTEM_CODES:
                category = "SHARED_SYSTEM_OR_NATIVE_NOT_UNIT_SKILL_VERIFIED"
            else:
                category = "NATIVE_ABILITY_NEEDS_TRIGGER_AND_EFFECT_REVIEW"
            category_counts[category] += 1
            direct = direct_references[code]
            results.append({
                "abilityRawcode": code,
                "classification": category,
                "warcraftNativeBaseAbility": original.get("code"),
                "originalSlkLevelOneFields": {k: original[k] for k in GAMEPLAY_FIELDS
                                              if k in original and original[k] not in ("", "_")},
                "war3mapW3aOverrides": overrides.get(code, []),
                "jassLiteralReferenceFunctionCount": len(direct),
                "jassLiteralReferences": direct[:12],
                "jassLiteralReferencesTruncated": len(direct) > 12,
                "confirmedFinalNativeEffect": False,
            })
        callback_rows = registration.get(raw, [])
        callback_evidence = []
        for callback in callback_rows:
            name = callback["function"]
            inspected = inspect_function(name, functions_by_name)
            _, clean, _ = functions_by_name[name]
            chance = re.search(r"BFi\(1,100\)\s*<=\s*(\d+)", clean)
            if chance is None:
                raise ValueError("Attack callback chance requires manual audit: " + name)
            callback_evidence.append({
                "function": name,
                "sourceLine": inspected["line"],
                "attackEventChancePercent": int(chance.group(1)),
                "sourceOperations": inspected["sourceOperations"],
                "directCustomDependencies": inspected["customDependencies"],
                "indirectSourceEffectsUnverified": True,
            })
        rows.append({
            "catalogId": unit["catalogId"],
            "name": unit["name"],
            "originalUnitRawcode": raw,
            "w5AttackCallbacksRegistered": callback_rows,
            "w5AttackCallbackEvidence": callback_evidence,
            "tooltipSkills": unit["tooltipSkillsNotJassVerified"],
            "abilities": results,
            "originalFullSkillBehaviorVerified": False,
        })
    helper_rows = []
    for raw in HELPER_IDS:
        helper = owners[raw]
        codes = helper["abilList"].split(",")
        helper_rows.append({
            "unitRawcode": raw,
            "nativeAbilityRows": [{
                "rawcode": code,
                "base": abilities[code]["code"] if code in abilities else None,
                "fields": ({k: abilities[code][k] for k in GAMEPLAY_FIELDS
                            if k in abilities[code] and abilities[code][k] not in ("", "_")}
                           if code in abilities else {}),
            } for code in codes],
        })
    result = {
        "format": "ordr-next-20-source-jass-native-ability-audit-v1",
        "sourceW3xSha256": W3X_SHA,
        "sourceJassSha256": JASS_SHA,
        "sourceAbilityDataSha256": ABILITY_SHA,
        "sourceUnitAbilitiesSha256": OWNER_SHA,
        "sourceW3aSha256": W3A_SHA,
        "runtimeAuthorization": False,
        "caveat": "Native base code/data and literal JASS references do not establish Warcraft built-in effect, automatic activation or retail parity.",
        "summary": {
            "units": len(rows),
            "objectAbilityEntries": sum(len(x["abilities"]) for x in rows),
            "cwrRegisteredAbilityEntries": category_counts["CWR_FUSION_REGISTERED_COMBAT_USE_NOT_VERIFIED"],
            "sharedSystemOrNativeEntries": category_counts["SHARED_SYSTEM_OR_NATIVE_NOT_UNIT_SKILL_VERIFIED"],
            "nativeAbilityLeadsStillNeedingRuntimeReview": category_counts["NATIVE_ABILITY_NEEDS_TRIGGER_AND_EFFECT_REVIEW"],
            "w5AttackCallbackOwners": sum(bool(x["w5AttackCallbacksRegistered"]) for x in rows),
            "fullOriginalSkillImplementationsApproved": 0,
        },
        "units": rows,
        "attackDummySourceAbilities": helper_rows,
    }
    return result


def create_report(audit):
    summary = audit["summary"]
    lines = [
        "# 다음 20명 원본 JASS·네이티브 능력 재추출 결과",
        "",
        "> 공개된 `ORDR_S2_2.323[C].w3x`를 다운로드하고 원본 저장소의 고정 SHA와 "
        "**완전히 일치하는 파일**에서 JASS, `AbilityData.slk`, `UnitAbilities.slk`, `war3map.w3a`를 다시 추출했습니다.",
        "",
        "- 원본 맵 SHA256: `" + W3X_SHA + "`",
        "- 원본 JASS SHA256: `" + JASS_SHA + "`",
        "- 원본 유닛: **20명**, 보유 능력 ID: **" + str(summary["objectAbilityEntries"]) + "개**",
        "- JASS `w5` 공격 선언 콜백이 직접 등록된 대상: **" + str(summary["w5AttackCallbackOwners"]) + "명**",
        "- 원본 조합(CWR) 등록식에 사용된 능력 ID: **" +
        str(summary["cwrRegisteredAbilityEntries"]) + "개** (다른 용도 존재 여부 별도)",
        "- 아직 원작 자동 발동·네이티브 효과가 검증되지 않은 나머지 능력 ID는 "
        "원본 스킬 완료로 계산하지 않습니다.",
        "",
        "| ID | 유닛 | 원본 객체 | 원작 능력 ID → 기본 능력형 (종류) | w5 공격 콜백 |",
        "|---|---|---|---|---|",
    ]
    for row in audit["units"]:
        parts = []
        for ab in row["abilities"]:
            kind = ("조합" if ab["classification"].startswith("CWR_FUSION") else
                    "공통" if ab["classification"].startswith("SHARED") else "검토")
            parts.append(f'`{ab["abilityRawcode"]}`→`{ab["warcraftNativeBaseAbility"]}`({kind})')
        callbacks = ", ".join("`" + x["function"] + "`" for x in row["w5AttackCallbacksRegistered"]) or "없음"
        lines.append(f'| {row["catalogId"]} | {row["name"]} | `{row["originalUnitRawcode"]}` | {", ".join(parts)} | {callbacks} |')
    lines += [
        "",
        "**분류 의미:** `조합`은 원본 CWR 제작 등록에 포함된 능력(전투 겸용 여부 미확인), `공통`은 유닛 스킬로 "
        "증명되지 않은 시스템/네이티브 코드, `검토`는 해당 유닛에 존재하지만 시전·자동 발동 "
        "및 효과를 확인해야 할 코드입니다. `w5 없음`은 *공격 선언 등록이 없음*이지 "
        "스킬 자체가 없다는 뜻이 아닙니다.",
        "",
        "## 검토 대상 네이티브 능력의 원본 수치",
        "",
        "> 아래는 SLK 필드값의 **직접 추출**입니다. 빈칸은 해당 필드가 기록되지 않았다는 뜻이며 "
        "0·효과 없음으로 해석하지 않습니다. `DataA1` 등은 기본 능력형마다 의미가 다릅니다.",
        "",
        "| 유닛 | 능력 / 기본형 | DataA1 | DataB1 | 지속 Dur1 | 쿨 Cool1 | 범위 Area1 | 생성 UnitID1 |",
        "|---|---|---:|---:|---:|---:|---:|---|",
    ]
    for row in audit["units"]:
        for ability in row["abilities"]:
            if ability["classification"] != "NATIVE_ABILITY_NEEDS_TRIGGER_AND_EFFECT_REVIEW":
                continue
            fields = ability["originalSlkLevelOneFields"]
            values = [fields.get(k, "—") for k in
                      ("DataA1", "DataB1", "Dur1", "Cool1", "Area1", "UnitID1")]
            lines.append(f'| {row["name"]} | `{ability["abilityRawcode"]}` / '
                         f'`{ability["warcraftNativeBaseAbility"]}` | ' +
                         " | ".join(str(x) for x in values) + " |")
    lines += [
        "",
        "## S-05 트라팔가 로우 — 원본 능력 데이터로 수치 교차검증",
        "",
    ]
    law = next(x for x in audit["units"] if x["catalogId"] == "S-05")
    speed = next(x for x in law["abilities"] if x["abilityRawcode"] == "A08B")
    fields = speed["originalSlkLevelOneFields"]
    lines += [
        "- 원본 능력: `A08B` / 기본 능력 `" + str(speed["warcraftNativeBaseAbility"]) + "`",
        "- `DataA1=" + str(fields.get("DataA1")) + "`, `Dur1=" + str(fields.get("Dur1")) +
        "`, `Cool1=" + str(fields.get("Cool1")) + "`, `Rng1=" + str(fields.get("Rng1")) + "`",
        "- 도감의 공격속도 +100% / 7초 / 재사용 50초와 원본 데이터가 "
        "**수치상 대응**합니다. 다만 대상 선택 자동화·버프 중첩의 최종 엔진 효과는 별도 확인이 필요합니다.",
        "",
        "## 공격 선언 JASS 콜백 6명 — 실제 분기",
        "",
        "| 유닛 | 원본 콜백 | 공격 선언 발동 확률 | JASS에서 확인한 처리 |",
        "|---|---|---:|---|",
    ]
    callback_meanings = {
        "S-08": "보조 유닛 `e0KD` 생성 → `fanofknives` 주문(네이티브 피해·대상 추가 검증)",
        "S-12": "보조 유닛 `h0C5` 생성 → `A08O` 추가, 1.2초 유지(네이티브 투사체 검증)",
        "S-15": "별도 `BoB` 상태기계 실행 → 0.29/0.45초 단계, 반경 450에 `Bcb` 3,000 및 특정 대상 추가 5,000 분기",
        "S-18": "보조 유닛 `e0KD` 생성 → 공격받은 적에 `forkedlightning` 주문",
        "S-19": "보조 유닛 `e0KG` 생성 → 적 방향에 `carrionswarm` 주문",
        "S-22": "보조 유닛 `e00T` 2개 생성 및 적 위치 반경 450에 `Bcb` 1,000 피해",
    }
    for row in audit["units"]:
        if not row["w5AttackCallbackEvidence"]:
            continue
        item = row["w5AttackCallbackEvidence"][0]
        lines.append(f'| {row["name"]} ({row["catalogId"]}) | `{item["function"]}` | '
                     f'{item["attackEventChancePercent"]}% | {callback_meanings[row["catalogId"]]} |')
    lines += [
        "",
        "위 6명의 **JASS 분기와 직접 명령**은 원본으로 확인했습니다. "
        "그러나 보조 유닛의 주문 피해, 원본 엔진 타깃·면역·사건 순서 및 실제 완료 여부까지 "
        "이 표로 승인하지 않습니다. 특히 스쿼드의 `BoB`는 간접 트리거와 시간 상태를 유지해야 합니다.",
        "",
        "## 공격에 사용하는 보조 유닛의 원본 능력",
        "",
        "| 보조 유닛 | 원본 등록 능력(능력형) |",
        "|---|---|",
    ]
    for helper in audit["attackDummySourceAbilities"]:
        abilities = ", ".join(
            "`" + a["rawcode"] + "`/`" + str(a["base"]) + "`"
            for a in helper["nativeAbilityRows"])
        lines.append(f'| `{helper["unitRawcode"]}` | {abilities} |')
    lines += [
        "",
        "## 다음 분석 단계",
        "",
        "1. `검토` 코드별 네이티브 기본 능력의 실제 발동 조건/보조 소환물·버프 ID "
        "및 `war3map.w3a` 레벨별 오버라이드를 역추적합니다.",
        "2. JASS의 각 능력 코드 직접 참조 함수와 상위 호출자를 추적합니다. "
        "직접 참조가 없으면 네이티브 자동 발동인지, 무효/조합용인지 별도 분리합니다.",
        "3. 확정된 기능만 스킬 실행기와 연결하고 전체 대상의 피해·상태·소환·정산을 통합 검증합니다.",
        "",
        "재현: `PYTHONPATH=<mpyq 설치 경로>:tools python3 tools/audit_next_20_native_skill_source.py "
        "--w3x <고정 원본 W3X 경로>`.",
        "",
    ]
    return "\n".join(lines)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--w3x", type=Path, required=True)
    parser.add_argument("--verify", action="store_true")
    args = parser.parse_args()
    data = main_audit(args.w3x)
    outputs = ((OUT, json.dumps(data, ensure_ascii=False, indent=2) + "\n"),
               (REPORT, create_report(data)))
    for path, contents in outputs:
        if args.verify:
            if not path.exists() or path.read_text(encoding="utf-8") != contents:
                raise SystemExit("Source audit output stale: " + str(path))
        else:
            path.write_text(contents, encoding="utf-8")
    print(json.dumps(data["summary"], ensure_ascii=False))


if __name__ == "__main__":
    main()
