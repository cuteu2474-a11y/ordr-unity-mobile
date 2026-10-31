"""Pinned W3X native-object receipts: never confuse them with retail parity."""
import json
from pathlib import Path
import re
import unittest


ROOT = Path(__file__).resolve().parents[1]
DATA = ROOT / "data"


class LegacyTenNativeFollowupTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.source = json.loads((DATA / "original-2323C-legacy-10-native-followups.json").read_text())
        cls.rows = {row["catalogId"]: row for row in cls.source["units"]}

    def test_ten_original_callback_identities_are_pinned_not_runtime_authorizations(self):
        attack = json.loads((DATA / "original-2323C-attack-skill-batch.json").read_text())
        index = {row["catalogId"]: row for row in attack["units"]}
        self.assertEqual(10, len(self.rows))
        self.assertEqual(attack["sourceJassSha256"], self.source["sourceJassSha256"])
        self.assertEqual("MODEL_ONLY", self.source["runtimeAuthorization"])
        self.assertFalse(self.source["verifiedFullWarcraftNativeParity"])
        for uid, row in self.rows.items():
            self.assertFalse(row["verifiedFinalWarcraftOutput"])
            self.assertEqual(index[uid]["rawcode"], row["ownerRawcode"])
            self.assertEqual(index[uid]["callback"]["function"], row["sourceCallback"])

    def test_all_five_inferno_followups_are_zero_damage_stuns_with_correct_radius(self):
        expected = {
            "R-13": ("A14X", 425, .9, .18),
            "R-26": ("A14Y", 405, .95, .19),
            "R-30": ("A150", 485, 1.15, .23),
            "R-31": ("A151", 500, 1.2, .24),
            "H-16": ("A156", 415, 1.7, .34),
        }
        catalog = (ROOT / "Assets/Scripts/Combat/SourceModelLegacyNativeFollowupCatalog.cs").read_text()
        self.assertEqual(5, catalog.count("new SourceModelInfernoFollowup("))
        for uid, (raw, radius, stun, hero) in expected.items():
            row = self.rows[uid]
            fields = row["nativeAbilityFields"]
            self.assertEqual(raw, row["nativeAbilityRawcode"])
            self.assertEqual("AUin", fields["code"])
            self.assertEqual(0, float(fields["DataA1"]))
            self.assertEqual(.01, float(fields["DataB1"]))
            self.assertEqual(radius, float(fields["Area1"]))
            self.assertEqual(stun, float(fields["Dur1"]))
            self.assertEqual(hero, float(fields["HeroDur1"]))
            self.assertEqual("h0C5", fields["UnitID1"])
            self.assertIn(raw, catalog)
        # R-26 creates e01L but orders the separate h0C5 + A14Y cast.
        self.assertIn("A14Y", self.rows["R-26"]["nativeAbilityRawcode"])

    def test_nami_cracker_rankyaku_and_sentinel_have_distinct_native_outcomes(self):
        nami = self.rows["S-03"]["nativeAbilityFields"]
        self.assertEqual("AOae", nami["code"])
        self.assertEqual(-.05, float(nami["DataA1"]))
        self.assertEqual(0, float(nami["DataB1"]))
        self.assertEqual(450, float(nami["Area1"]))
        self.assertEqual(["Aloc", "Avul"], self.rows["L-33"]["dummyAbilityRawcodes"])
        for uid in ("U-04", "U-13"):
            rankyaku = self.rows[uid]
            self.assertEqual("A0VS", rankyaku["nativeAbilityRawcode"])
            self.assertEqual(200, float(rankyaku["nativeAbilityFields"]["Area1"]))
            self.assertTrue(any("carrionswarm" in op and ",Id)" in op
                                for op in rankyaku["sourceJassOperations"]))
        sentinel = self.rows["RN-04"]["nativeAbilityFields"]
        self.assertEqual("Afae", sentinel["code"])
        self.assertEqual(50, float(sentinel["DataA1"]))
        self.assertEqual(60, float(sentinel["Dur1"]))
        self.assertEqual(15, self.source["sourceSentinelA04RIncrease"])
        self.assertEqual(1, self.source["waveSourceA04RInitialLevel"])
        self.assertEqual(26, self.source["sourceA04RLevelMax"])
        self.assertEqual(68, self.source["waveTypesWithA04RCount"])
        self.assertEqual(68, len(set(self.source["waveTypesWithA04R"])))

    def test_legacy_runtime_has_no_guessed_second_damage_for_visual_or_inferno(self):
        scripts = (ROOT / "Assets/Scripts/Combat/SourceModelScriptAttackCatalog.cs").read_text()
        runtime = (ROOT / "Assets/Scripts/Combat/CharacterCombatRuntime.SourceModelLegacyFollowups.cs").read_text()
        self.assertIn("e04X has only Aloc/Avul", scripts)
        self.assertIn("spec.NormalStunSeconds", runtime)
        self.assertIn("PracticeSourceA04RLevel", runtime)
        self.assertIn("SentinelFaerieDuration", runtime)
        self.assertFalse(re.search(r"new SourceScriptHit\([^\n]*A14[XY]", runtime))


if __name__ == "__main__":
    unittest.main()
