#!/usr/bin/env python3
"""Audit individual character skills against code, not tooltip registration.

This is a conservative static inventory, NOT a Unity execution/parity test.
An absent string or a registered dispatcher never proves a skill is missing or
fully implemented. Explicit witnesses must name both a handler and its caller.
"""
from __future__ import annotations

import argparse
from collections import Counter, defaultdict
import csv
import hashlib
import io
import json
from pathlib import Path
import re
import sys

ROOT = Path(__file__).resolve().parents[1]
DATA = ROOT / "data"
DOCS = ROOT / "docs"
PROFILE = DATA / "character-skill-profiles.json"
UNITS = DATA / "unit-runtime-status.json"
WITNESSES = DATA / "character-skill-execution-witnesses.json"
OUT_JSON = DATA / "character-skill-code-coverage.json"
OUT_CSV = DATA / "character-skill-code-coverage.csv"
OUT_MD = DOCS / "스킬-구현-현황.md"

STATUS_KOREAN = {
    "model_witness": "🟦 코드 근거(미검증)",
    "partial_witness": "🟨 일부 효과 근거",
    "registration_only": "🔎 등록·호출 검토",
    "unresolved": "⚪ 실행 경로 미확정",
}


def read_json(path: Path):
    return json.loads(path.read_text(encoding="utf-8"))


def source_inventory():
    scripts = ROOT / "Assets" / "Scripts"
    if not scripts.is_dir():
        raise FileNotFoundError("Assets/Scripts 소스 전체가 필요합니다. 일부 파일만으로 전수 감사를 하지 않습니다.")
    result = {}
    for path in sorted(scripts.rglob("*.cs")):
        result[path.relative_to(ROOT).as_posix()] = path.read_text(encoding="utf-8")
    if len(result) < 250 or "Assets/Scripts/Combat/CharacterCombatRuntime.cs" not in result:
        raise ValueError("게임 C# 소스가 일부만 있습니다. 전체 저장소에서 감사표를 생성하세요.")
    return result


def skill_map(profiles):
    skills = {}
    seen_units = set()
    for unit in profiles["profiles"]:
        unit_id = unit["unitId"]
        if unit_id in seen_units:
            raise ValueError("캐릭터 ID 중복: " + unit_id)
        seen_units.add(unit_id)
        for index, skill in enumerate(unit.get("skills", [])):
            key = f"{unit_id}:skill:{index}"
            if key in skills:
                raise ValueError("기술 ID 중복: " + key)
            skills[key] = (unit_id, index, skill)
    if not skills:
        raise ValueError("기술 프로필이 비어 있습니다.")
    return skills


def validate_witnesses(document, skills, sources):
    if document.get("schemaVersion") != 1:
        raise ValueError("실행 증거 스키마 버전이 다릅니다.")
    validated = defaultdict(list)
    for row in document.get("witnesses", []):
        skill_id = row["skillId"]
        if skill_id not in skills:
            raise ValueError("도감에 없는 증거 ID: " + skill_id)
        unit_id, _, skill = skills[skill_id]
        path, entry = row["handlerFile"], row["entryFile"]
        if path not in sources or entry not in sources:
            raise ValueError("실행 증거 파일 누락: " + skill_id)
        if not path.startswith("Assets/Scripts/") or not entry.startswith("Assets/Scripts/"):
            raise ValueError("실행 증거는 게임 소스 파일이어야 합니다: " + skill_id)
        handler = row["handlerSymbol"]
        signature = re.compile(
            r"\b(?:public|private|internal|protected)\s+"
            r"(?:static\s+)?(?:[\w<>?,\[\]]+\s+)+" + re.escape(handler) + r"\s*\(")
        if not signature.search(sources[path]):
            raise ValueError(f"핸들러 선언 없음: {skill_id} / {handler}")
        if row["entryCall"] not in sources[entry]:
            raise ValueError(f"호출부 단절: {skill_id} / {entry}")
        tokens = row.get("identityTokens", [])
        if not tokens or not all(token in sources[path] for token in tokens):
            raise ValueError("유닛/스킬 근거 변경: " + skill_id)
        indexes = row.get("effectIndexes", [])
        if not indexes or len(indexes) != len(set(indexes)):
            raise ValueError("효과 인덱스 누락/중복: " + skill_id)
        if any(not isinstance(i, int) or i < 0 or i >= len(skill.get("components", []))
               for i in indexes):
            raise ValueError("도감 범위 밖 효과 인덱스: " + skill_id)
        if not row.get("note"):
            raise ValueError("근거의 한계 설명 누락: " + skill_id)
        validated[skill_id].append(row)
    return validated


def classify_source(path):
    name = Path(path).name
    if "Catalog" in name or "Registry" in name or "/Data/" in path:
        return "registration"
    if path.startswith("Assets/Scripts/Combat/") or path.startswith(
            "Assets/Scripts/Mobile/") or path.startswith("Assets/Scripts/Core/"):
        return "runtime_candidate"
    return "other_reference"


def audit(profiles, units, witness_document, sources):
    skills = skill_map(profiles)
    witnesses = validate_witnesses(witness_document, skills, sources)
    unit_data = {u["catalogId"]: u for u in units["entries"]}
    if not set(u for u, _, _ in skills.values()) <= set(unit_data):
        raise ValueError("도감과 유닛 현황 ID가 일치하지 않습니다.")

    digest = hashlib.sha256()
    for path in (PROFILE, UNITS, WITNESSES):
        digest.update(path.relative_to(ROOT).as_posix().encode("utf-8"))
        digest.update(path.read_bytes())
    for path, content in sorted(sources.items()):
        digest.update(path.encode("utf-8"))
        digest.update(content.encode("utf-8"))

    rows = []
    suspicious = []
    for skill_id, (unit_id, index, skill) in sorted(skills.items(), key=lambda x: (
            x[1][0], x[1][1])):
        name = skill["name"]
        explicit = witnesses.get(skill_id, [])
        covered = set()
        witnesses_for_effect = defaultdict(list)
        for item in explicit:
            for component in item["effectIndexes"]:
                covered.add(component)
                witnesses_for_effect[component].append(item["handlerSymbol"])
        repeated = {i: names for i, names in witnesses_for_effect.items()
                    if len(set(names)) > 1}
        if repeated:
            suspicious.append({"skillId": skill_id, "effectHandlers": repeated})

        # String evidence is a REVIEW HINT, never a declaration that the
        # method was reached or that the entire tooltip was implemented.
        exact_id = '"' + skill_id + '"'
        exact_name = '"' + name.replace('"', '\\"') + '"'
        references = []
        for path, content in sources.items():
            if exact_id in content or exact_name in content:
                references.append({"file": path, "kind": classify_source(path),
                                   "matching": ("skill_id" if exact_id in content
                                                else "skill_name")})
        references.sort(key=lambda r: (r["kind"], r["file"]))
        count = len(skill.get("components", []))
        if count > 0 and len(covered) == count:
            status = "model_witness"
        elif covered:
            status = "partial_witness"
        elif references:
            status = "registration_only"
        else:
            status = "unresolved"
        effects = [
            {"index": i, "type": component.get("type", "unknown"),
             "evidence": "code_witness" if i in covered else "needs_review",
             "note": "코드 연결 증거이며 실행·원작 결과 검증이 아님" if i in covered
                     else "미구현 확정 아님: 구체 실행 경로 조사 필요"}
            for i, component in enumerate(skill.get("components", []))]
        row = {
            "skillId": skill_id, "unitId": unit_id,
            "unitName": unit_data[unit_id]["name"],
            "grade": unit_data[unit_id].get("grade"),
            "unitRawcode": unit_data[unit_id].get("originalRawcode"),
            "name": name, "trigger": skill.get("trigger"),
            "status": status, "statusKo": STATUS_KOREAN[status],
            "coveredEffectCount": len(covered), "totalEffectCount": count,
            "effects": effects,
            "witnesses": [{"handlerFile": w["handlerFile"],
                           "handlerSymbol": w["handlerSymbol"],
                           "entryFile": w["entryFile"],
                           "entryCall": w["entryCall"],
                           "effectIndexes": w["effectIndexes"],
                           "note": w["note"]} for w in explicit],
            "sourceReferences": references,
            "potentialDuplicate": bool(repeated),
            "unityExecuted": False,
            "androidExecuted": False,
            "originalParityVerified": False,
        }
        rows.append(row)
    return {
        "schemaVersion": 1,
        "sourceVersion": profiles.get("gameVersion"),
        "sourceFingerprintSha256": digest.hexdigest(),
        "classificationPolicy": "fail_closed_static_audit_no_execution_proof",
        "summary": {
            "profileUnits": len({r["unitId"] for r in rows}),
            "skills": len(rows),
            "components": sum(r["totalEffectCount"] for r in rows),
            "validatedWitnesses": sum(len(r["witnesses"]) for r in rows),
            "statusCounts": dict(sorted(Counter(r["status"] for r in rows).items())),
            "possibleDuplicateEffects": len(suspicious),
            "unityExecutedSkills": 0, "androidExecutedSkills": 0,
            "originalParityVerifiedSkills": 0,
        },
        "possibleDuplicateEffects": suspicious,
        "skills": rows,
    }


def render_csv(result):
    buffer = io.StringIO(newline="")
    writer = csv.writer(buffer, lineterminator="\n")
    writer.writerow(["skill_id", "unit_id", "unit_name", "grade", "skill_name",
                     "trigger", "status", "covered_effects", "total_effects",
                     "handler_symbols", "runtime_reference_files", "verification"])
    for r in result["skills"]:
        writer.writerow([r["skillId"], r["unitId"], r["unitName"], r["grade"],
                         r["name"], r["trigger"], r["status"],
                         r["coveredEffectCount"], r["totalEffectCount"],
                         ", ".join(sorted({w["handlerSymbol"] for w in r["witnesses"]})),
                         ", ".join(sorted({x["file"] for x in r["sourceReferences"]
                                           if x["kind"] == "runtime_candidate"})),
                         "Unity/Android/원작 대조 미실시"])
    return buffer.getvalue()


def render_markdown(result):
    summary = result["summary"]
    counts = summary["statusCounts"]
    lines = [
        "# 캐릭터 스킬 구현 감사 — 자동 생성",
        "",
        "> 기술 이름 등록과 실행을 구분한 **정적 코드 감사표**입니다. 모든 `코드 근거`는 실제 기기·Unity 검증 또는 원작 일치를 뜻하지 않습니다.",
        "> 이 문서는 `python3 tools/audit_character_skill_coverage.py --write`에서 생성합니다. 직접 수정하지 마세요.",
        "",
        f"- **대상:** {summary['profileUnits']}개 캐릭터 프로필 / **{summary['skills']}개 기술** / {summary['components']}개 효과",
        f"- **실제 호출·효과 증거:** 수동 검토 후 경로를 지정한 {summary['validatedWitnesses']}개 등록(중복 가능). 코드 근거가 있는 기술도 실행 검증은 별도입니다.",
        f"- **자동 대조 지문:** `{result['sourceFingerprintSha256']}`. 매 실행 시 데이터·게임 소스 변경이 반영됩니다.",
        "",
        "| 코드 감사 분류 | 기술 수 | 의미 |",
        "|---|---:|---|",
        f"| 🟦 코드 근거(미검증) | {counts.get('model_witness', 0)} | 등록된 전체 효과에 대해 핸들러·호출부 정적 증거 있음 |",
        f"| 🟨 일부 효과 근거 | {counts.get('partial_witness', 0)} | 일부 효과의 코드 근거만 있음 |",
        f"| 🔎 등록·호출 검토 | {counts.get('registration_only', 0)} | ID/이름 발견; 실제 실행 여부 단정 금지 |",
        f"| ⚪ 실행 경로 미확정 | {counts.get('unresolved', 0)} | 자동 검색 근거 부족; **미구현 확정 아님** |",
        "",
        "**절대 혼동하지 마세요:** 🟦는 '완성'이 아니라 '코드 연결 증거'입니다. ⚪도 '기술 없음'이 아닙니다. 독립 네이티브/JASS 호출은 별도 검토해야 합니다.",
        "",
        "## 다음 조사 대상 — 수동/액티브 기술 중 실행 근거 미확정",
        "",
        "| ID | 캐릭터 | 기술 | 상태 |",
        "|---|---|---|---|",
    ]
    pending = [r for r in result["skills"]
               if r["trigger"] in ("manual", "active", "activeMana", "activeHealth")
               and r["status"] not in ("model_witness", "partial_witness")]
    for r in pending[:50]:
        lines.append(f"| `{r['skillId']}` | {r['unitName']} | {r['name']} | {r['statusKo']} |")
    lines += ["", f"전체 후보 {len(pending)}개는 아래 표와 CSV에서 확인합니다.", "",
              "## 모든 기술 — 개별 코드 감사", "",
              "| ID | 캐릭터 | 기술 | 효과 근거 | 분류 |",
              "|---|---|---|---:|---|"]
    for r in result["skills"]:
        lines.append(f"| `{r['skillId']}` | {r['unitName']} | {r['name']} | "
                     f"{r['coveredEffectCount']}/{r['totalEffectCount']} | {r['statusKo']} |")
    lines += ["", "## 후속 개발 시 절차", "",
              "1. 해당 ID를 [JSON 전수표](../data/character-skill-code-coverage.json)에서 찾고, 기존 `sourceReferences`와 `witnesses`를 확인합니다.",
              "2. 등록만 된 항목은 원본 함수·네이티브 호출·게임 상태까지 따라가며 기존 실행 여부를 먼저 확정합니다.",
              "3. 새 효과를 구현했다면 [실행 경로 증거](../data/character-skill-execution-witnesses.json)에 실제 핸들러·호출부·효과 인덱스를 기록합니다.",
              "4. 묶음 수정 완료 후 이 감사표를 재생성하고 `--verify`로 데이터·코드 동기화를 확인합니다. 실제 검사 실행은 프로젝트 승인 규칙에 따릅니다.",
              "", "[CSV 전수표](../data/character-skill-code-coverage.csv) · [기존 C03 콜백 검토표](스킬-누락-검토표.md)", ""]
    return "\n".join(lines)


def outputs(result):
    return {
        OUT_JSON: json.dumps(result, ensure_ascii=False, indent=2) + "\n",
        OUT_CSV: render_csv(result),
        OUT_MD: render_markdown(result),
    }


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    action = parser.add_mutually_exclusive_group()
    action.add_argument("--write", action="store_true", help="갱신된 JSON/CSV/Markdown 저장")
    action.add_argument("--verify", action="store_true", help="저장된 감사표가 최신 소스와 일치하는지 비교")
    action.add_argument("--inspect", metavar="SKILL_ID", help="해당 기술의 근거·미확정 부분을 출력")
    args = parser.parse_args()
    result = audit(read_json(PROFILE), read_json(UNITS),
                   read_json(WITNESSES), source_inventory())
    rendered = outputs(result)
    if args.inspect:
        row = next((x for x in result["skills"] if x["skillId"] == args.inspect), None)
        if row is None:
            print("존재하지 않는 기술 ID: " + args.inspect, file=sys.stderr)
            return 2
        print(json.dumps(row, ensure_ascii=False, indent=2))
        print("\n주의: 기록이 없다고 미구현이 아니며, 코드 근거도 실제 플레이 검증이 아닙니다.")
        return 0
    if args.write:
        for path, text in rendered.items():
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_text(text, encoding="utf-8", newline="")
    elif args.verify:
        stale = [p.relative_to(ROOT).as_posix() for p, text in rendered.items()
                 if not p.is_file() or p.read_text(encoding="utf-8") != text]
        if stale:
            print("스킬 감사표가 최신 소스와 다릅니다:", ", ".join(stale), file=sys.stderr)
            return 1
    print(json.dumps(result["summary"], ensure_ascii=False, sort_keys=True))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
