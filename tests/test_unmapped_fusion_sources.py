"""Protect original-only material identities and all 47 still-unmapped recipes."""
import json
from pathlib import Path
import sys
import unittest

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / "tools"))
from audit_unmapped_fusion_sources import build


class SourceOnlyMaterialAuditTests(unittest.TestCase):
    def test_all_objects_from_pinned_registry_are_explained_without_aliases(self):
        audited = build(ROOT / "data")
        self.assertEqual(audited, json.loads((ROOT / "data/original-fusion-source-only-materials.json").read_text()))
        self.assertEqual(47, audited["summary"]["unmappedRecipeCount"])
        self.assertEqual(6, audited["summary"]["distinctSourceOnlyObjects"])
        self.assertEqual(49, audited["summary"]["sourceOnlyRoleOccurrences"])
        objects = {r["rawcode"]: r for r in audited["entries"]}
        self.assertEqual({"h04S", "h06G", "h05Y", "h0AN", "H08T", "h0C2"}, set(objects))
        self.assertEqual(30, len(objects["h04S"]["ingredientRecipes"]))
        self.assertEqual(14, len(objects["h06G"]["ingredientRecipes"]))
        self.assertEqual(["A0QG"], objects["h0C2"]["resultRecipes"])
        self.assertTrue(all(r["catalogUnitId"] is None for r in audited["entries"]))

    def test_source_only_recipes_stay_locked_until_gameplay_acquisition_and_effects(self):
        port = (ROOT / "Assets/Scripts/Mobile/Original323CommonFusionPort.cs").read_text()
        wisp = (ROOT / "Assets/Scripts/Combat/Original323SourceCommonWispWorld.cs").read_text()
        for rawcode in ('h04S', 'h06G'):
            self.assertNotIn('"' + rawcode + '"', port)
        self.assertIn('HasPendingPirateShip', wisp)
        self.assertIn('if (draw.PirateShip)', wisp)
        self.assertIn('return false;', wisp)
        preview = (ROOT / "Assets/Scripts/Mobile/OriginalJassReadOnlyFusionPort.cs").read_text()
        self.assertIn('OriginalFusionObjectPreviewCatalog', preview)
        self.assertIn('획득·보유 구현 미검증', preview)
        self.assertIn('카탈로그/생성 연결 미검증', preview)
        self.assertIn('ManualActionResult.Unavailable(', preview)


if __name__ == "__main__":
    unittest.main()
