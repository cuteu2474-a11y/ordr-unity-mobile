"""Non-Unity regression checks for S2 2.323[C] map-only base stats."""

import json
from pathlib import Path
import sys
import unittest
from copy import deepcopy


ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / "tools"))

from build_original_map_base_stats import (  # noqa: E402
    COMMON_RAWCODES, attack_stats, clean_name, normalize_name,
)
from classify_character_skills import family  # noqa: E402
from reconcile_ambiguous_original_map_objects import (  # noqa: E402
    REVIEWED_MAP_NAME_ALIASES, reconcile,
)


class WarcraftObjectAttackTests(unittest.TestCase):
    def test_luffy_damage_dice_are_not_tooltip_damage(self):
        data = attack_stats({"cool1": "1.02", "dmgplus1": "28",
                             "dice1": "1", "sides1": "3"}, 1, True)
        self.assertEqual(data["minimumBaseDamage"], 29)
        self.assertEqual(data["maximumBaseDamage"], 31)
        self.assertEqual(data["attackCooldownSeconds"], 1.02)

    def test_unavailable_attack_is_none(self):
        self.assertIsNone(attack_stats({}, 1, False))
        missing = attack_stats({"cool1": "1.1"}, 1, True)
        self.assertIsNone(missing["minimumBaseDamage"])
        self.assertIsNone(missing["maximumBaseDamage"])

    def test_map_label_normalization(self):
        self.assertEqual(clean_name("|CFFFF0000몽키.D.루피|r"), "몽키.D.루피")
        self.assertEqual(normalize_name("바질 호킨스"), normalize_name("바질호킨스"))


class GeneratedDataTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.source = json.loads((ROOT / "data/original-2323C-map-base-stats.json").read_text(
            encoding="utf-8"))
        cls.skills = json.loads((ROOT / "data/original-2323C-skill-research-queue.json").read_text(
            encoding="utf-8"))
        cls.registry = json.loads((ROOT / "data/original-jass-fusion-registry.json").read_text(
            encoding="utf-8"))
        cls.labels = json.loads((ROOT / "data/original-unit-rawcode-label-links.json").read_text(
            encoding="utf-8"))
        cls.defense_links = json.loads((
            ROOT / "data/original-2323C-character-defense-skill-links.json"
        ).read_text(encoding="utf-8"))
        cls.profiles = json.loads((ROOT / "data/character-skill-profiles.json").read_text(
            encoding="utf-8"))

    def test_full_source_index_and_partial_verified_join(self):
        report = self.source["summary"]
        self.assertEqual(report["originalNamedObjects"], 1493)
        self.assertEqual(report["catalogCharacters"], 272)
        self.assertEqual(report["linkedCatalogCharacters"], 271)
        self.assertEqual(report["unresolvedCatalogCharacters"], 1)
        self.assertEqual(report["linkedWithAttack1"], 270)
        self.assertEqual(report["mappingStatusCounts"]["reviewed-map-name-alias-and-grade"], 54)
        self.assertEqual(report["mappingStatusCounts"]["verified-jass-fusion-result"], 6)
        self.assertEqual(report["mappingStatusCounts"]["verified-jass-label-and-fusion-result"], 49)
        self.assertEqual(report["gameplayParityVerified"], 0)
        rows = self.source["catalogEntries"]
        self.assertEqual(len({r["catalogId"] for r in rows}), 272)
        linked = [r for r in rows if r["originalRawcode"]]
        self.assertEqual(len({r["originalRawcode"] for r in linked}), len(linked))
        self.assertTrue(all(r["combatParityVerified"] is False for r in rows))
        for row in rows:
            if not row["originalRawcode"]:
                self.assertIsNone(row["attack1"])
                self.assertIsNone(row["baseStats"])
                self.assertIsNone(row["sourceMapName"])
        self.assertEqual([row["catalogId"] for row in rows if not row["originalRawcode"]],
                         ["E-02"])

    def test_54_reviewed_aliases_use_pinned_names_and_actual_object_stats(self):
        rows = {row["catalogId"]: row for row in self.source["catalogEntries"]}
        objects = {obj["rawcode"]: obj for obj in self.source["mapObjects"]}
        self.assertEqual(len(REVIEWED_MAP_NAME_ALIASES), 54)
        for catalog_id, (code, original_name) in REVIEWED_MAP_NAME_ALIASES.items():
            row = rows[catalog_id]
            obj = objects[code]
            self.assertEqual(row["mappingStatus"], "reviewed-map-name-alias-and-grade")
            self.assertEqual(row["originalRawcode"], code)
            self.assertEqual(row["sourceMapName"], original_name)
            self.assertEqual(obj["mapName"], original_name)
            self.assertEqual(row["attack1"], obj["attack1"])
            self.assertEqual(row["attack2"], obj["attack2"])
            self.assertEqual(row["baseStats"], obj["baseStats"])
            self.assertEqual(row["originalAbilityList"], obj["originalAbilityList"])
            self.assertIs(row["combatParityVerified"], False)
        self.assertEqual(reconcile(deepcopy(self.source), self.registry, self.labels),
                         self.source)

    def test_reviewed_alias_fails_closed_on_name_grade_or_rawcode_drift(self):
        for field, invalid, expected_error in (
            ("mapName", "니코 로빈 임시 - 안흔함", "source"),
            ("mapGradeSuffix", "전설", "grade"),
        ):
            before = deepcopy(self.source)
            original = next(o for o in before["mapObjects"] if o["rawcode"] == "h00O")
            original[field] = invalid
            with self.assertRaisesRegex(ValueError, expected_error):
                reconcile(before, self.registry, self.labels)

        before = deepcopy(self.source)
        row = next(r for r in before["catalogEntries"] if r["catalogId"] == "U-01")
        row["originalRawcode"] = "h00K"
        with self.assertRaisesRegex(ValueError, "drifted"):
            reconcile(before, self.registry, self.labels)

    def test_six_ambiguous_units_are_resolved_using_jass_fusion_results(self):
        expected = {
            "E-03": ("h0CJ", "ET04", ["h0C7"]),
            "LI-03": ("h08O", "A011", ["h09Q"]),
            "D-04": ("h048", "IF02", ["h000"]),
            "E-09": ("h067", "ET07", ["h0AF"]),
            "I-07": ("h04E", "IM06", ["h0BM"]),
            "I-11": ("h07M", "IM11", ["h0AD", "h0BW"]),
        }
        rows = {row["catalogId"]: row for row in self.source["catalogEntries"]}
        results = {(reg["abilityRawcode"], result["rawcode"])
                   for reg in self.registry["registrations"]
                   for result in reg["results"] if result["kind"] == "UNIT"}
        labels = {row["catalogUnitId"]: row for row in self.labels["mappings"]}
        for unit_id, (code, ability, alternatives) in expected.items():
            row = rows[unit_id]
            self.assertEqual(row["mappingStatus"], "verified-jass-fusion-result")
            self.assertEqual(row["originalRawcode"], code)
            self.assertEqual(row["alternateRawcodes"], alternatives)
            self.assertIn(ability, row["sourceJassFusionAbilityRawcodes"])
            self.assertIn((ability, code), results)
            if unit_id in labels:
                self.assertEqual(labels[unit_id]["rawcode"], code)
                self.assertEqual(labels[unit_id]["abilityRawcode"], ability)
            self.assertFalse(row["combatParityVerified"])
        self.assertEqual(reconcile(deepcopy(self.source), self.registry, self.labels),
                         self.source)

    def test_ryuma_helper_is_replaced_only_by_original_et04_result(self):
        source = deepcopy(self.source)
        row = next(r for r in source["catalogEntries"] if r["catalogId"] == "E-03")
        obj = next(o for o in source["mapObjects"] if o["rawcode"] == "h0C7")
        row.update({
            "mappingStatus": "unique-map-name-and-grade",
            "originalRawcode": "h0C7",
            "candidateRawcodes": [],
            "sourceMapName": obj["mapName"],
            "attack1": obj["attack1"],
            "attack2": obj["attack2"],
            "baseStats": obj["baseStats"],
            "originalAbilityList": obj["originalAbilityList"],
        })
        row.pop("alternateRawcodes")
        row.pop("sourceJassFusionAbilityRawcodes")
        self.assertEqual(reconcile(source, self.registry, self.labels), self.source)

        bad = deepcopy(self.source)
        next(o for o in bad["mapObjects"] if o["rawcode"] == "h0CJ")["attack1"] = None
        with self.assertRaisesRegex(ValueError, "E-03 original helper"):
            reconcile(bad, self.registry, self.labels)

    def test_49_different_named_units_join_only_when_jass_label_and_result_agree(self):
        rows = {row["catalogId"]: row for row in self.source["catalogEntries"]}
        objects = {obj["rawcode"]: obj for obj in self.source["mapObjects"]}
        fusion_results = {(r["abilityRawcode"], obj["rawcode"])
                          for r in self.registry["registrations"]
                          for obj in r["results"]
                          if obj["kind"] == "UNIT" and obj["count"] == 1}
        labels = {row["catalogUnitId"]: row for row in self.labels["mappings"]}
        linked = [row for row in rows.values()
                  if row["mappingStatus"] == "verified-jass-label-and-fusion-result"]
        self.assertEqual(len(linked), 49)
        self.assertEqual(len([row for row in linked if row["catalogId"].startswith("TR-")]), 30)
        for row in linked:
            label = labels[row["catalogId"]]
            obj = objects[row["originalRawcode"]]
            self.assertEqual(row["originalRawcode"], label["rawcode"])
            self.assertIn((label["abilityRawcode"], label["rawcode"]), fusion_results)
            self.assertEqual(row["sourceJassFusionAbilityRawcodes"],
                             [label["abilityRawcode"]])
            self.assertEqual(row["sourceMapName"], obj["mapName"])
            self.assertEqual(row["attack1"], obj["attack1"])
            self.assertFalse(row["combatParityVerified"])
        self.assertEqual(rows["TR-08"]["originalRawcode"], "H099")
        self.assertEqual(rows["TR-20"]["originalRawcode"], "H09F")
        self.assertEqual(rows["E-01"]["sourceMapName"], "태양의 신")

    def test_jass_label_reconciliation_rejects_wrong_grade_and_skips_wrong_fusion(self):
        before = deepcopy(self.source)
        row = next(r for r in before["catalogEntries"] if r["catalogId"] == "H-01")
        row["mappingStatus"] = "unmatched"
        row["originalRawcode"] = None
        original = next(o for o in before["mapObjects"] if o["rawcode"] == "h043")
        original["mapGradeSuffix"] = "전설"
        with self.assertRaisesRegex(ValueError, "JASS label conflicts with map grade"):
            reconcile(before, self.registry, self.labels)

        before = deepcopy(self.source)
        row = next(r for r in before["catalogEntries"] if r["catalogId"] == "TR-08")
        row["mappingStatus"] = "unmatched"
        row["originalRawcode"] = None
        labels = deepcopy(self.labels)
        next(l for l in labels["mappings"] if l["catalogUnitId"] == "TR-08")[
            "abilityRawcode"] = "XXXX"
        after = reconcile(before, self.registry, labels)
        row = next(r for r in after["catalogEntries"] if r["catalogId"] == "TR-08")
        self.assertEqual(row["mappingStatus"], "unmatched")
        self.assertIsNone(row["originalRawcode"])

    def test_reconcile_rejects_conflicting_jass_label(self):
        before = deepcopy(self.source)
        row = next(row for row in before["catalogEntries"] if row["catalogId"] == "E-09")
        row["mappingStatus"] = "ambiguous-name-and-grade"
        row["originalRawcode"] = None
        row["candidateRawcodes"] = ["h067", "h0AF"]
        labels = deepcopy(self.labels)
        for entry in labels["mappings"]:
            if entry["catalogUnitId"] == "E-09":
                entry["rawcode"] = "h0AF"
        with self.assertRaisesRegex(ValueError, "JASS fusion/label conflict"):
            reconcile(before, self.registry, labels)

    def test_common_nine_match_original_map_damage_and_interval(self):
        report = {x["catalogId"]: x for x in self.source["catalogEntries"]}
        # These are *map object* damage ranges, not actual damage against an enemy.
        expected = {
            "C-01": (29, 31, 1.02), "C-02": (31, 33, 1.03),
            "C-03": (26, 30, 0.98), "C-04": (25, 27, 0.84),
            "C-05": (23, 25, 0.77), "C-06": (22, 24, 0.57),
            "C-07": (29, 31, 0.99), "C-08": (30, 32, 0.96),
            "C-09": (28, 30, 0.94),
        }
        self.assertEqual(len(COMMON_RAWCODES), 9)
        for uid, (min_dmg, max_dmg, interval) in expected.items():
            unit = report[uid]
            self.assertEqual(unit["mappingStatus"], "verified-common-map-identity")
            self.assertEqual(unit["originalRawcode"], COMMON_RAWCODES[uid][0])
            attack = unit["attack1"]
            self.assertEqual(attack["minimumBaseDamage"], min_dmg)
            self.assertEqual(attack["maximumBaseDamage"], max_dmg)
            self.assertAlmostEqual(attack["attackCooldownSeconds"], interval)

    def test_common_nine_attack_preview_has_source_timing_and_is_not_verified_damage(self):
        entries = {row["catalogId"]: row for row in self.source["catalogEntries"]}
        for uid in COMMON_RAWCODES:
            row = entries[uid]
            attack = row["attack1"]
            self.assertIsNotNone(attack["attackPointSeconds"])
            self.assertGreaterEqual(attack["attackPointSeconds"], 0)
            self.assertLess(attack["attackPointSeconds"], attack["attackCooldownSeconds"])
            self.assertFalse(row["combatParityVerified"])

    def test_common_native_object_dice_and_attack_type_are_preserved(self):
        rows = {row["catalogId"]: row for row in self.source["catalogEntries"]}
        expected_types = {
            "C-01": "siege", "C-02": "pierce", "C-03": "normal",
            "C-04": "normal", "C-05": "siege", "C-06": "normal",
            "C-07": "pierce", "C-08": "siege", "C-09": "pierce",
        }
        for catalog_id, expected_type in expected_types.items():
            attack = rows[catalog_id]["attack1"]
            self.assertEqual(attack["originalAttackType"], expected_type)
            self.assertGreater(attack["diceCount"], 0)
            self.assertGreater(attack["diceSides"], 0)
            self.assertEqual(attack["minimumBaseDamage"],
                             attack["baseDamagePlus"] + attack["diceCount"])
            self.assertEqual(attack["maximumBaseDamage"],
                             attack["baseDamagePlus"] + attack["diceCount"] * attack["diceSides"])

    def test_skill_queue_preserves_uncertainty(self):
        s = self.skills["summary"]
        self.assertEqual(s["tooltipProfiles"], 200)
        self.assertEqual(s["skillEntries"], 554)
        self.assertEqual(s["passiveEntries"], 295)
        self.assertEqual(s["originalJassSkillsVerified"], 0)
        self.assertEqual(len(self.skills["skills"]), 554)
        self.assertEqual(sum(x["skillCount"] for x in self.skills["templateGroups"]), 554)
        self.assertEqual(len(set(x["skillId"] for x in self.skills["skills"])), 554)
        self.assertTrue(all(x["originalJassSkillRawcode"] is None for x in self.skills["skills"]))

    def test_jass_defense_links_match_original_rawcodes_and_exact_tooltip_components(self):
        original = {r["catalogId"]: r for r in self.source["catalogEntries"]}
        profiles = {r["unitId"]: r for r in self.profiles["profiles"]}
        links = self.defense_links["links"]
        self.assertEqual(self.defense_links["sourceMapSha256"],
                         self.source["sourceW3xSha256"])
        self.assertEqual(len(links), 11)
        self.assertEqual(len({r["unitId"] for r in links}), 9)
        distinct_skills = set()
        for link in links:
            uid = link["unitId"]
            ordinal = link["skillOrdinal"]
            self.assertEqual(original[uid]["originalRawcode"], link["unitRawcode"])
            self.assertIn(original[uid]["mappingStatus"], (
                "unique-map-name-and-grade", "reviewed-map-name-alias-and-grade",
                "verified-jass-label-and-fusion-result",
            ))
            skill = profiles[uid]["skills"][ordinal]
            self.assertEqual(skill["name"], link["skillName"])
            self.assertEqual(skill["trigger"], link["trigger"])
            field = "stacks" if link["type"] == "armorBreak" else "percent"
            self.assertTrue(any(component["type"] == link["type"] and
                                component[field] == link["value"]
                                for component in skill["components"]))
            self.assertIn(link["ability"], {"A04M", "A04Q"})
            self.assertNotIn((uid, ordinal, link["type"]), distinct_skills)
            distinct_skills.add((uid, ordinal, link["type"]))

    def test_verified_jass_proc_chances_and_mana_thresholds_are_not_guessed(self):
        expected_chances = {
            ("E-10", 1): 11, ("I-09", 3): 10, ("L-23", 0): 5,
            ("L-23", 1): 10, ("L-37", 0): 14,
        }
        expected_mana = {
            ("H-10", 0): 100, ("H-09", 0): 150, ("I-03", 0): 115,
            ("L-20", 0): 50, ("L-07", 0): 115,
        }
        profiles = {r["unitId"]: r for r in self.profiles["profiles"]}
        for link in self.defense_links["links"]:
            key = (link["unitId"], link["skillOrdinal"])
            skill = profiles[key[0]]["skills"][key[1]]
            self.assertEqual(link.get("verifiedProcChancePercent"),
                             expected_chances.get(key))
            self.assertEqual(link.get("verifiedManaThreshold"),
                             expected_mana.get(key))
            if key in expected_chances:
                self.assertEqual(skill["chancePercent"], expected_chances[key])
            if key in expected_mana:
                self.assertEqual(skill["manaCost"], expected_mana[key])
        # H-10 on-attack metadata is intentionally omitted until a JASS
        # probability has been independently verified; its tooltip is not proof.
        self.assertNotIn(("H-10", 1), expected_chances)

    def test_trigger_families_do_not_invent_special_behavior(self):
        self.assertEqual(family("onAttack"), "attack-triggered")
        self.assertEqual(family("manual"), "active-or-automatic-mobile")
        self.assertEqual(family("periodic"), "periodic")
        self.assertEqual(family("onSkillOrAttack"), "special-condition-needs-analysis")


if __name__ == "__main__":
    unittest.main()
