import json
from pathlib import Path
import sys
import unittest

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / "tools"))
from audit_original_all_unit_auras import derive, literal, utility_registrations, value


class AllUnitAuraAuditTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.audit = json.loads((ROOT / "data/original-2323C-all-unit-aura-audit.json").read_text(encoding="utf-8"))
        cls.map = json.loads((ROOT / "data/original-2323C-map-base-stats.json").read_text(encoding="utf-8"))

    def test_every_catalog_unit_has_explicit_scope_and_unknown_is_not_empty(self):
        self.assertEqual(272, self.audit["summary"]["catalogUnitsScanned"])
        self.assertEqual([r["catalogId"] for r in self.map["catalogEntries"]],
                         [r["catalogUnitId"] for r in self.audit["catalogUnits"]])
        self.assertEqual(116, sum(bool(r["objectAuraCodes"]) for r in self.audit["catalogUnits"]))
        unknown = [r["catalogUnitId"] for r in self.audit["catalogUnits"] if r["objectAuraCodes"] is None]
        self.assertEqual(["E-02"], unknown)
        self.assertFalse(self.audit["safeForUnconditionalLiveCombat"])

    def test_native_inheritance_and_flat_vs_percent_damage_are_distinguished(self):
        objects = {r["abilityRawcode"]: r for r in self.audit["nativeAuraObjects"]}
        self.assertEqual(230, len(objects))
        self.assertEqual({"AHad": 79, "AOac": 36, "AOae": 115}, self.audit["summary"]["byNativeRoot"])
        self.assertEqual(["A0EM", "ACac", "AOac"], objects["A0EM"]["nativeParentChain"])
        guard = objects["A0EM"]["levels"][0]["fields"]
        flat = objects["A0O6"]["levels"][0]["fields"]
        self.assertEqual(0, guard["Ear4"])
        self.assertEqual(1, flat["Ear4"])
        self.assertEqual(5000, flat["Cac1"])
        self.assertEqual(.07000000029802322, guard["Cac1"])
        self.assertIn("공격력 1만 증가", objects["A0NL"]["levels"][0]["fields"]["aub1"])
        self.assertIsNone(objects["A08N"]["levels"][0]["fields"]["DataB"])

    def test_shared_buff_ids_are_not_blindly_promoted_to_stacking(self):
        groups = {r["buffRawcode"]: r for r in self.audit["sharedBuffGroups"]}
        self.assertEqual(26, len(groups))
        self.assertEqual(["A0EM", "A0EN"], groups["B00V"]["abilityRawcodes"])
        self.assertTrue(all(not r["combatStackingVerified"] for r in groups.values()))

    def test_original_movement_bounds_and_hud_separation_are_preserved(self):
        self.assertEqual({"MinUnitSpeed": 89, "MaxUnitSpeed": 490}, self.audit["originalMovementBounds"])
        self.assertFalse(self.audit["movementFormulaAndClampOrderVerified"])
        utility = self.audit["utilityRegistrations"]
        self.assertTrue(utility["notACombatStackingFormula"])
        self.assertEqual(246, len(utility["entries"]))
        self.assertEqual(2, len(utility["dynamicCallsNotExpanded"]))

    def test_registration_expansion_and_dynamic_inputs_are_not_guessed(self):
        jass = """function Test takes nothing returns nothing
call BH9($4130384E,$42303131,1,1,-3.)
call BIB($41303848,0,2,3,5.,2.)
call BIC($41303845,$42303031,2,1,5.,$68303141)
call BH9(variable,0,1,1,-5.)
endfunction"""
        report = utility_registrations(jass)
        self.assertEqual([-3, 5, 7, 9, 5], [r["signedValue"] for r in report["entries"]])
        self.assertEqual("h01A", report["entries"][-1]["ownerRestriction"])
        self.assertEqual(1, len(report["dynamicCallsNotExpanded"]))
        self.assertIsNone(value("_"))
        self.assertIsNone(value(None))
        self.assertEqual(-3, value("-3."))
        with self.assertRaises(ValueError):
            value("nan")
        with self.assertRaises(ValueError):
            literal("GetUnitTypeId(unit)")


if __name__ == "__main__":
    unittest.main()
