#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""Check 2.323[C] source-preserving character DB. No Unity/game test."""
import hashlib
import json
from collections import Counter
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent

def load(p):
    return json.loads((ROOT / p).read_text(encoding="utf8"))

def require(ok, why):
    if not ok:
        raise AssertionError(why)

def blob_sha(path):
    raw = (ROOT / path).read_bytes()
    return hashlib.sha1(b"blob " + str(len(raw)).encode() + b"\0" + raw).hexdigest()

def validate():
    m = load("data/character-master-database.json")
    audit = load("data/character-database-audit.json")
    rawindex = load("data/original-2323C-rawcode-index.json")
    require(m["gameVersion"] == "S2 2.323[C]", "version drift")
    require(m["mode"] == "nightmare-otherworld", "mode drift")
    require(len(m["gradeFiles"]) == 16, "grade source count")
    for file, sha in m["integrity"]["sourceBlobShas"].items():
        require(blob_sha(file) == sha, "source modified without DB regeneration: " + file)
    catalog = {r["id"]:r for r in load("data/units.json")["entries"]}
    profiles = {r["unitId"]:r for r in
                load("data/character-skill-profiles.json")["profiles"]}
    require(len(catalog) == 271 and len(profiles) == 200, "source duplicate/drift")
    links = {}
    for row in load("data/original-unit-rawcode-label-links.json")["mappings"]:
        links.setdefault(row["catalogUnitId"], []).append(row)
    patches = {x["unitId"]:x for x in
               load("data/original-stat-evidence.json")["verifiedFromPages"]}
    recipes = {}
    for row in load("data/verified-mobile-fusion-recipes.json")["recipes"]:
        recipes.setdefault(row["resultUnitId"],[]).append(row)
    combat = {}
    for row in load("data/original-2323C-character-defense-skill-links.json")["links"]:
        combat.setdefault(row["unitId"],[]).append(row)

    allrows, filegroups = [], {}
    for group in m["gradeFiles"]:
        path = group["path"]
        require(path.startswith("data/characters/by-grade/"), "untrusted path")
        source = load(path)
        rows = source["characters"]
        require(source["gameVersion"] == m["gameVersion"], "wrong grade version")
        require(group["catalogCount"] == len(rows), "grade count mismatch")
        require(group["profiles"] == sum(x["profile"] is not None for x in rows),
                "grade profile count")
        require(group["skills"] == sum(len(x["profile"]["skills"]) if x["profile"] else 0
                                       for x in rows), "grade skills count")
        filegroups[path] = {x["id"] for x in rows}
        allrows.extend(rows)

    ids = [x["id"] for x in allrows]
    require(len(ids)==271 and len(set(ids))==271, "missing/duplicate unit")
    require(set(ids)==set(catalog), "catalog names/IDs not matched")
    require(len(m["characters"])==271, "manifest missing units")
    indexed = set()
    for item in m["characters"]:
        uid=item["id"]
        require(uid in filegroups[item["recordFile"]], "wrong recordFile")
        require(uid not in indexed, "duplicate manifest record")
        indexed.add(uid)
    for row in allrows:
        uid=row["id"]
        require(row["catalog"]==catalog[uid], uid+" catalog mismatch")
        require(row["profile"]==profiles.get(uid), uid+" tooltip details mismatch")
        require(row["evidence"]["originalRawcodeVerified"] is None,
                uid+" original unit identity must not be fabricated")
        require(row["completeness"]["allowedForParityCombat"] is False,
                uid+" false combat completion")
        require(all(v is None for k,v in row["coreCombat"].items()
                    if k != "numericValuesFromPatchAreNotCombatVerified"),
                uid+" unjustified final combat data")
        c=row["evidence"]["originalRawcodeCandidates"]
        require(len(c)==len(links.get(uid,[])), uid+" candidate count")
        for a,b in zip(c,links.get(uid,[])):
            require(a["rawcode"]==b["rawcode"] and
                    a["evidenceAbilityRawcode"]==b["abilityRawcode"],
                    uid+" original JASS candidate mismatch")
        require(row["evidence"]["patchStatObservations"]==
                (patches[uid]["observations"] if uid in patches else []),
                uid+" patch source drift")
        require(row["evidence"]["catalogFusionRecipes"]==recipes.get(uid,[]),
                uid+" fusion recipe drift")
        require(row["evidence"]["originalBattleAbilities"]==combat.get(uid,[]),
                uid+" JASS effect mismatch")

    derived={
      "catalogCharacters":len(allrows),
      "withTooltipProfiles":sum(bool(r["profile"]) for r in allrows),
      "withoutTooltipProfiles":sum(not r["profile"] for r in allrows),
      "totalSkillEntries":sum(len(r["profile"]["skills"]) if r["profile"] else 0
                             for r in allrows),
      "totalPassiveEntries":sum(len(r["profile"]["passives"]) if r["profile"] else 0
                               for r in allrows),
      "jassLabelCandidateLinks":sum(bool(r["evidence"]["originalRawcodeCandidates"])
                                    for r in allrows),
      "originalRawcodesVerifiedAgainstWarcraftUnitData":0,
      "verifiedOriginalBasicAttacks":0,
      "withPatchStatObservations":sum(bool(r["evidence"]["patchStatObservations"])
                                       for r in allrows),
      "sourceCorrelatedDefenseSkills":sum(len(r["evidence"]["originalBattleAbilities"])
                                          for r in allrows),
      "withWebRecipes":sum(bool(r["evidence"]["catalogFusionRecipes"])
                            for r in allrows),
      "originalJassFusionRegistrations":len(
        load("data/original-jass-fusion-registry.json")["registrations"]),
      "distinctJassFusionResultRawcodes":len({
        t["rawcode"] for reg in
        load("data/original-jass-fusion-registry.json")["registrations"]
        for t in reg["results"] if t["kind"]=="UNIT"}),
      "distinctJassFusionIngredientOrResultRawcodes":len(rawindex["entries"]),
      "unmatchedOriginalRawcodes":sum(not r["candidateCatalogIds"]
                                      for r in rawindex["entries"]),
      "allowedForOriginalParityCombat":0
    }
    require(m["summary"]==derived and audit["summary"]==derived,
            "master/audit mismatch")
    require(len(audit["researchQueue"])==271, "missing backlog rows")
    for grade, n in Counter(x["grade"] for x in allrows).items():
        require(audit["byGrade"][grade]["catalog"]==n, grade+" grade drift")
    require(len(rawindex["entries"])==280, "JASS rawcode index mismatch")
    return derived

if __name__=="__main__":
    summary=validate()
    print("PASS: source data inventory only, not game parity")
    print("catalog:",summary["catalogCharacters"],
          "profiles:",summary["withTooltipProfiles"],
          "skills:",summary["totalSkillEntries"],
          "passives:",summary["totalPassiveEntries"])
    print("verified gameplay-ready characters:",summary["allowedForOriginalParityCombat"])
