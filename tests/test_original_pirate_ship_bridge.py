"""Pirate-ship identity, random-Wisp branch, and execution locks are source-linked."""
import json
from pathlib import Path
import re
import unittest

ROOT = Path(__file__).resolve().parents[1]
DATA = ROOT / "data"


def load(name):
    return json.loads((DATA / name).read_text(encoding="utf-8"))


class OriginalPirateShipBridgeTests(unittest.TestCase):
    def test_source_object_and_catalog_are_exactly_one_pirate_ship(self):
        data = load("original-2323C-map-base-stats.json")
        units = load("units.json")
        native = load("original-2323C-unit-object-stats.json")
        ship = next(r for r in data["catalogEntries"] if r["catalogId"] == "PS-01")
        origin = next(r for r in data["mapObjects"] if r["rawcode"] == "h060")
        slk = next(r for r in native["entries"] if r["rawcode"] == "h060")
        catalog = next(r for r in units["entries"] if r["id"] == "PS-01")
        self.assertEqual("0794996c9bc73eaa3020f20ebc45fb37cfa64a6e98ffe8aa0dfa3b356e77bc5",
                         data["sourceW3xSha256"])
        self.assertEqual("해적선", catalog["grade"])
        self.assertEqual("original-W3X-only", catalog["catalogEvidenceOrigin"])
        self.assertIsNone(catalog["gameRawCode"])
        self.assertEqual("verified-source-wisp-pirate-ship", ship["mappingStatus"])
        self.assertEqual("h060", ship["originalRawcode"])
        for field in ("sourceMapName", "originalAbilityList"):
            self.assertEqual(ship[field], origin[{"sourceMapName": "mapName",
                                                   "originalAbilityList": "originalAbilityList"}[field]])
        for field in ("attack1", "attack2", "baseStats"):
            self.assertEqual(ship[field], origin[field])
        self.assertEqual("510", slk["originalObjectFields"]["weapons"]["dmgplus1"])
        self.assertEqual(512, ship["attack1"]["minimumBaseDamage"])
        self.assertEqual(518, ship["attack1"]["maximumBaseDamage"])
        self.assertEqual(0.9, ship["attack1"]["attackCooldownSeconds"])
        self.assertIs(ship["combatParityVerified"], False)

    def test_one_source_ship_is_a_real_unit_not_an_extra_common_draw(self):
        source = (ROOT / "Assets/Scripts/Combat/Original323SourceCommonWispWorld.cs").read_text()
        service = (ROOT / "Assets/Scripts/Combat/OriginalWispCombatRewardWorld.cs").read_text()
        self.assertIn('PirateShipRawcode = "h060"', source)
        self.assertIn('PirateShipCatalogId = "PS-01"', source)
        self.assertIn("if (rare <= 24)", source)
        self.assertIn("Draw(1, 10000)", source)
        self.assertIn("SpawnCharacter(PirateShipCatalogId,", source)
        self.assertIn("shipDrawCount++;", source)
        self.assertIn("owner.pending = null;", source)
        self.assertIn("owner.combat.RollbackUncommittedSpawn(createdId)", source)
        self.assertIn("shared.Wallet.TryExchange(", service)
        self.assertIn("transaction.Commit();", service)
        self.assertIn("candidate.Unit.Grade != \"해적선\"", service)
        self.assertEqual(9, len(re.findall(r'"h00[1-9]"', source)))

    def test_hidden_uses_are_now_resolvable_but_not_silently_authorized(self):
        inventory = load("original-fusion-unconnected-review.json")
        rows = {r["abilityRawcode"]: r for r in inventory["blocked"]}
        self.assertEqual((265, 143, 122),
                         tuple(inventory["summary"][x] for x in
                               ("sourceRecipesExcludingTest", "liveExecutableRecipes",
                                "blockedRecipes")))
        for ability in ("YY04", "YY07", "YY08", "YY09", "YY12"):
            self.assertNotIn(ability, rows,
                             "Pure source chat callbacks now use the common atomic fusion engine")
        self.assertIn("YY01", rows)
        self.assertNotIn("h060", rows["YY01"]["unmappedUnitRawcodes"])
        self.assertIn("creation-followup-CH8", rows["YY01"]["reasonCodes"])
        self.assertIn("no-unit-payment-in-registration", rows["YY26"]["reasonCodes"])
        self.assertEqual(47, inventory["summary"]["blockedByReason"]["unmapped-original-unit"])


if __name__ == "__main__":
    unittest.main()
