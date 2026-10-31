from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]


def test_fourth_attack_source_catalog_dispatch_and_bounds():
    catalog = (ROOT / "Assets/Scripts/Combat/SourceModelFourthAttackCatalog.cs").read_text(encoding="utf-8")
    runtime = (ROOT / "Assets/Scripts/Combat/CharacterCombatRuntime.SourceModelAttackSkills.cs").read_text(encoding="utf-8")
    assert 'new SourceScriptAttackSpec("L-16", "h034", "Btb", 400, 57,' in catalog
    assert 'Direct(40000, .8, 1.5)' in catalog
    assert 'Circle(315, 100000, .8, 1.5)' in catalog
    assert 'new SourceScriptAttackSpec("L-21", "h042", "Bt2", 100, 15,' in catalog
    assert 'new[] { Direct(400000) }' in catalog
    assert 'SourceModelFourthAttackCatalog.TryGet(rawcode, out script)' in runtime
    assert "independent 8% summon deferred" in catalog
