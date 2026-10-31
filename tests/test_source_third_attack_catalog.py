from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]


def test_source_third_attack_catalog_only_executes_verified_unconditional_damage():
    catalog = (ROOT / "Assets/Scripts/Combat/SourceModelThirdAttackCatalog.cs").read_text(encoding="utf-8")
    runtime = (ROOT / "Assets/Scripts/Combat/CharacterCombatRuntime.SourceModelAttackSkills.cs").read_text(encoding="utf-8")
    assert 'new SourceScriptAttackSpec("R-07", "h025", "Bos", 100, 10,' in catalog
    assert 'new SourceScriptAttackSpec("H-05", "h044", "Bri", 100, 14,' in catalog
    assert "2000, 1, 1, Ordr323AttackType.Chaos" in catalog
    assert "11500, 1, 1, Ordr323AttackType.Chaos" in catalog
    assert "Array.Empty<SourceScriptHit>()" in catalog
    assert "SourceModelThirdAttackCatalog.TryGet(rawcode, out script)" in runtime
