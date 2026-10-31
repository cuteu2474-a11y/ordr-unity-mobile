from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]


def test_fifth_attack_catalog_has_source_pinned_damage_packets():
    source = (ROOT / "Assets/Scripts/Combat/SourceModelFifthAttackCatalog.cs").read_text(encoding="utf-8")
    runtime = (ROOT / "Assets/Scripts/Combat/CharacterCombatRuntime.SourceModelAttackSkills.cs").read_text(encoding="utf-8")
    assert 'new SourceScriptAttackSpec("H-13", "h055", "BrI", 100, 14,' in source
    assert 'Circle(315, 100000)' in source
    assert 'new SourceScriptAttackSpec("TR-28", "H08Y", "BzH", 100, 10,' in source
    assert 'Circle(400, 400000, .8, 1.5)' in source
    assert 'Direct(200000, .8, 1.5)' in source
    assert 'SourceModelFifthAttackCatalog.TryGet(rawcode, out script)' in runtime
    assert 'independent 8% aY deferred' in source
