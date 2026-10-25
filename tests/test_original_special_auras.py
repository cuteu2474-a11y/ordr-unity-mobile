import copy
import json
from pathlib import Path
import sys
import unittest

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / "tools"))
from extract_original_special_auras import inspect_source, MAP_SHA


class SpecialAuraSourceTests(unittest.TestCase):
    def setUp(self):
        self.report = json.loads((ROOT / "data/original-2323C-special-six-aura-evidence.json").read_text())
        self.abilities, self.owners, self.weapons, self.strings, self.overrides = {}, {}, {}, [], []
        for row in self.report["entries"]:
            code, unit = row["abilityRawcode"], row["unitRawcode"]
            self.owners[unit] = {"abilList": row["originalAbilityList"]}
            self.weapons[unit] = row["objectWeaponFields"]
            source = row["abilitySource"]
            if code == "A0EM":
                self.overrides.append(("custom", "ACac", "A0EM",
                                       [tuple(x) for x in source["modifications"]]))
            else:
                self.abilities[code] = source["fields"]
                self.strings.append("[" + code + "]\n" + "\n".join(
                    k + "=" + v for k, v in source["strings"].items()))

    def inspect(self):
        return inspect_source(self.abilities, self.owners, self.weapons,
                              "\n".join(self.strings), self.overrides)

    def test_evidence_reproduces_and_does_not_claim_unconditional_live_parity(self):
        self.assertEqual(self.report["entries"], self.inspect())
        self.assertEqual(MAP_SHA, self.report["sourceW3xSha256"])
        for key in ("liveTargetEligibilityVerified", "nativeStackingAndLingerVerified",
                    "nativeAttackBuffFormulaVerified", "safeForUnconditionalLiveCombat"):
            self.assertIs(False, self.report[key])

    def test_original_owner_mapping_and_native_weapon_stats_agree(self):
        catalog = json.loads((ROOT / "data/original-2323C-map-base-stats.json").read_text())
        for row in self.report["entries"]:
            mapped = next(r for r in catalog["catalogEntries"] if r["catalogId"] == row["catalogUnitId"])
            self.assertEqual(mapped["originalRawcode"], row["unitRawcode"])
            self.assertEqual(mapped["originalAbilityList"], row["originalAbilityList"])
            for raw, field in (("dmgplus1", "baseDamagePlus"), ("dice1", "diceCount"),
                               ("sides1", "diceSides"), ("cool1", "attackCooldownSeconds")):
                self.assertEqual(float(row["objectWeaponFields"][raw]), mapped["attack1"][field])

    def test_crocodile_radius_and_chopper_w3a_are_not_defaulted(self):
        crocodile = next(r for r in self.report["entries"] if r["catalogUnitId"] == "S-29")
        self.assertEqual(850, crocodile["radius"])
        guard = next(r for r in self.report["entries"] if r["catalogUnitId"] == "S-25")
        self.assertEqual("war3map.w3a", guard["abilitySource"]["table"])
        self.assertEqual(.07000000029802322, guard["signedRawObjectValue"])
        self.abilities["A08E"]["Area1"] = "800."
        with self.assertRaisesRegex(ValueError, "SLK aura field"):
            self.inspect()

    def test_missing_guard_override_or_modified_target_mask_is_rejected(self):
        original = copy.deepcopy(self.overrides)
        self.overrides.clear()
        with self.assertRaisesRegex(ValueError, "W3A record"):
            self.inspect()
        self.overrides = original
        fields = self.overrides[0][3]
        i = next(i for i, field in enumerate(fields) if field[0] == "atar")
        fields[i] = (*fields[i][:4], "air,ground")
        with self.assertRaisesRegex(ValueError, "A0EM field"):
            self.inspect()


if __name__ == "__main__":
    unittest.main()
