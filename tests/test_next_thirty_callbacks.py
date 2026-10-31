import hashlib
import json
import sys
from pathlib import Path
from tempfile import TemporaryDirectory

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / "tools"))
import audit_next_thirty_callbacks as audit


def test_callback_registration_ignores_comments_and_strings():
    raw = "h021".encode("ascii").hex().upper()
    source = ('// call w5($' + raw + ',function Fake)\n'
              'set msg = "call w5($' + raw + ',function Fake)"\n'
              'call w5($' + raw + ',function Real)\n')
    assert audit.extract_registrations(source) == {"h021": {"Real"}}


def test_source_gate_does_not_promote_tooltips_to_executable_skills():
    review = json.loads(audit.REVIEW.read_text(encoding="utf-8"))
    assert len(review["entries"]) == 30
    assert review["runtimeAuthorization"] is False
    assert all(row["sourceVerified"] is False for row in review["entries"])


def test_original_sha_mismatch_is_rejected():
    review = json.loads(audit.REVIEW.read_text(encoding="utf-8"))
    with TemporaryDirectory() as folder:
        source = Path(folder) / "war3map.j"
        source.write_text("function fake takes nothing returns nothing\nendfunction\n")
        try:
            audit.inspect(source, review)
        except ValueError as exc:
            assert "SHA mismatch" in str(exc)
        else:
            raise AssertionError("Unpinned JASS was accepted")
