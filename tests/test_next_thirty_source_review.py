import json
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]

def test_next_thirty_review_matches_source_catalogs():
    review = json.loads((ROOT / "data/original-2323C-next-30-source-review.json").read_text(encoding="utf-8"))
    status = json.loads((ROOT / "data/unit-runtime-status.json").read_text(encoding="utf-8"))
    profiles = json.loads((ROOT / "data/character-skill-profiles.json").read_text(encoding="utf-8"))
    units = {row["catalogId"]: row for row in status["entries"]}
    skills = {row["unitId"]: row for row in profiles["profiles"]}
    assert review["runtimeAuthorization"] is False
    assert review["unitCount"] == len(review["entries"]) == 30
    assert len({row["id"] for row in review["entries"]}) == 30
    for row in review["entries"]:
        unit, profile = units[row["id"]], skills[row["id"]]
        assert row["name"] == unit["name"]
        assert row["grade"] == unit["grade"]
        assert row["rawcode"] == unit["originalRawcode"]
        assert row["documentedSkills"] == len(profile["skills"])
        assert row["connectedEffects"] == len(unit["connectedModelEffects"]) == 0
        assert row["sourceVerified"] is False
        assert sorted(row["triggerTypes"]) == sorted(set(s["trigger"] for s in profile["skills"]))
