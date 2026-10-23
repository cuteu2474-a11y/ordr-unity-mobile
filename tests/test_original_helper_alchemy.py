"""CI-safe source-independent checks; local --w3x verifies actual JASS hash."""
import copy
import json
from pathlib import Path
import sys
import unittest

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / "tools"))
from extract_original_helper_alchemy import (  # noqa: E402
    DEFAULT_DATA, DEFAULT_FUSIONS, REQUIRED_JASS, inspect_source, MAP_SHA, JASS_SHA)


def synthetic_jass(profile):
    lines = ["function BjB takes nothing returns nothing"]
    for item in profile["entries"]:
        for ability in item["fusionAbilities"]:
            a = item["targetRawcode"].encode("latin-1").hex().upper()
            b = ability.encode("latin-1").hex().upper()
            lines.append("call Bi8($%s,$%s)" % (a, b))
    lines += ["endfunction", "function CWR takes nothing returns nothing"]
    registry = json.loads(DEFAULT_FUSIONS.read_text(encoding="utf-8"))
    code = lambda t: t.encode("latin-1").hex().upper()
    for item in registry["registrations"]:
        lines.append("call BGG($%s)" % code(item["abilityRawcode"]))
        for op, field in (("BGH", "results"), ("BGI", "conditions")):
            for r in item[field]:
                lines.append("call %s($%s,$%s,%d)" %
                             (op, code(r["kind"]), code(r["rawcode"]), r["count"]))
    lines += ["endfunction", "function CJY takes integer JG,integer hP,integer Hk returns nothing"]
    lines.extend(REQUIRED_JASS)
    lines += ["endfunction", "function CJZ takes nothing returns nothing",
              "call CJY(JG,hP,EK)", "endfunction"]
    return "\n".join(lines)


class OriginalHelperAlchemyExtractionTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.profile = json.loads(DEFAULT_DATA.read_text(encoding="utf-8"))
        cls.fusions = json.loads(DEFAULT_FUSIONS.read_text(encoding="utf-8"))
        cls.jass = synthetic_jass(cls.profile)

    def test_original_hashes_links_and_unit_only_refunds(self):
        extracted = inspect_source(self.jass, self.fusions)
        self.assertEqual(143, self.profile["existingFusionArchiveInterleavingMismatches"])
        self.assertEqual(0, extracted["existingFusionArchiveInterleavingMismatches"])
        extracted["existingFusionArchiveInterleavingMismatches"] = 143
        self.assertEqual(self.profile, extracted)
        self.assertEqual(MAP_SHA, extracted["sourceMapSha256"])
        self.assertEqual(JASS_SHA, extracted["sourceJassSha256"])
        self.assertEqual(265, extracted["targetCount"])
        a = {x["targetRawcode"]: x for x in extracted["entries"]}
        self.assertEqual([{"rawcode": "h001", "count": 1},
                          {"rawcode": "h004", "count": 1}],
                         a["h00O"]["refundUnits"])
        self.assertEqual(["A006"], a["h00O"]["fusionAbilities"])
        self.assertGreater(a["h00O"]["ignoredNonUnitConditions"], 0)
        self.assertTrue(extracted["semantics"]["onlyBgiUnitConditionsAreRefunded"])
        self.assertFalse(extracted["semantics"]["includesTargetConsumptionAndManaTransaction"])

    def test_non_unit_instructions_are_never_granted(self):
        changed = copy.deepcopy(self.fusions)
        item = next(r for r in changed["registrations"]
                    if r["abilityRawcode"] == "A006")
        item["conditions"][0]["kind"] = "WOOD"
        with self.assertRaisesRegex(ValueError, "UNIT order differs"):
            inspect_source(self.jass, changed)

    def test_required_cjy_handling_and_complete_bi8_mapping_are_fail_closed(self):
        with self.assertRaisesRegex(ValueError, "CJY semantics"):
            inspect_source(self.jass.replace("if hT==$554E4954 then",
                                             "if hT==$574F4F44 then"), self.fusions)
        with self.assertRaisesRegex(ValueError, "registrations changed"):
            inspect_source(self.jass.replace("call Bi8($6830304F,$41303036)",
                                             "call Bi9($6830304F,\"fake\")"), self.fusions)
        wrong_hash = copy.deepcopy(self.fusions)
        wrong_hash["sourceSha256"] = "0" * 64
        with self.assertRaisesRegex(ValueError, "CWR W3X SHA"):
            inspect_source(self.jass, wrong_hash)


if __name__ == "__main__":
    unittest.main()
