"""Source identity and safety gates; synthetic fixtures are not live parity."""
import copy
import json
from pathlib import Path
import sys
import unittest

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / "tools"))
from extract_original_robin_multishot import inspect_source, section, MAP_SHA


def data(name):
    return json.loads((ROOT / "data" / name).read_text(encoding="utf-8"))


class RobinSourceTests(unittest.TestCase):
    def setUp(self):
        self.profile = data("original-2323C-robin-A07M-evidence.json")
        self.rows = self.profile["sourceRows"]

    def inspect(self, rows=None, jass="", overrides=()):
        r = rows or self.rows
        return inspect_source(r["ability"], r["weapon"], r["unitAbilities"],
                              r["strings"], jass, overrides)

    def test_source_rows_reproduce_profile_without_promoting_live_parity(self):
        self.assertEqual(self.profile, self.inspect())
        self.assertEqual(MAP_SHA, self.profile["sourceW3xSha256"])
        self.assertEqual(3, self.profile["totalNativeTargetCount"])
        self.assertEqual(2, self.profile["maximumAdditionalTargets"])
        self.assertEqual("1", self.rows["ability"]["DataC1"])
        self.assertFalse(self.profile["nativeAbilityChanceFieldPresent"])
        self.assertFalse(self.profile["safeForUnconditionalLiveCombat"])
        self.assertIn("primary-zero-damage-and-secondary-shot-behavior",
                      self.profile["unverified"])

    def test_independent_base_stat_catalog_agrees_with_weapon_and_owner(self):
        catalog = data("original-2323C-map-base-stats.json")
        # Use the committed linked-unit catalog, not extractor constants.
        entries = catalog["catalogEntries"]
        robin = next(x for x in entries if x.get("catalogId") == "U-01")
        self.assertEqual("h00O", robin["originalRawcode"])
        self.assertEqual(self.rows["unitAbilities"]["abilList"],
                         robin["originalAbilityList"])
        attack = robin["attack1"]
        for field, key in (("dmgplus1", "baseDamagePlus"),
                           ("dice1", "diceCount"), ("sides1", "diceSides"),
                           ("cool1", "attackCooldownSeconds"),
                           ("dmgpt1", "attackPointSeconds")):
            self.assertEqual(float(self.rows["weapon"][field]), attack[key])

    def test_modified_count_mask_parent_damage_or_cooldown_requires_review(self):
        for row, field, value in (
            ("weapon", "targCount1", "4"), ("ability", "DataC1", "3"),
            ("ability", "code", "AHbh"), ("ability", "targs1", "ground"),
            ("weapon", "cool1", "1.0"), ("weapon", "dmgplus1", "100"),
            ("strings", "Missilespeed", "99999"),
            ("unitAbilities", "abilList", "A07M"),
        ):
            with self.subTest(row=row, field=field):
                changed = copy.deepcopy(self.rows)
                changed[row][field] = value
                with self.assertRaisesRegex(ValueError, "source changed"):
                    self.inspect(changed)

    def test_new_direct_jass_reference_or_w3a_override_requires_review(self):
        for reference in ("'A07M'", "$4130374D", "$4130374d", "1093678925"):
            with self.subTest(reference=reference):
                with self.assertRaisesRegex(ValueError, "JASS reference"):
                    self.inspect(jass=reference)
        for old, new in (("Aroc", "A07M"), ("Aroc", ""), ("A07M", "")):
            with self.assertRaisesRegex(ValueError, "W3A override"):
                self.inspect(overrides=[("custom", old, new, [])])

    def test_only_requested_strings_section_is_read_with_crlf(self):
        text = "[OTHER]\r\nMissilespeed=99999\r\n[A07M]\r\n" + "\r\n".join(
            key + "=" + value for key, value in self.rows["strings"].items())
        text += "\r\n[NEXT]\r\nMissilespeed=1\r\n"
        self.assertEqual(self.rows["strings"], section(text, "A07M"))
        with self.assertRaisesRegex(ValueError, "Missing original"):
            section(text, "NONE")


if __name__ == "__main__":
    unittest.main()
