import importlib.util
import json
from pathlib import Path
import tempfile
import unittest

ROOT = Path(__file__).resolve().parents[1]
spec = importlib.util.spec_from_file_location('attack_batch', ROOT / 'tools/audit_attack_skill_batch.py')
audit = importlib.util.module_from_spec(spec)
spec.loader.exec_module(audit)


class AttackBatchAuditTests(unittest.TestCase):
    def test_comments_and_strings_cannot_invent_dependencies(self):
        source = '''function Actual takes nothing returns nothing
call Helper(BFi(1,100))
call DisplayTextToPlayer(null,0,0,"call Fake() function Ghost")
// call AnotherFake()
endfunction
function Helper takes integer n returns nothing
endfunction
'''
        table = audit.functions(source)
        row = audit.inspect_function('Actual', table)
        self.assertEqual(['Helper'], row['customDependencies'])
        self.assertIn('BFi', row['externalDependencies'])
        self.assertNotIn('Fake', row['externalDependencies'])
        self.assertNotIn('AnotherFake', row['externalDependencies'])
        self.assertNotIn('Ghost', row['externalDependencies'])

    def test_wrong_original_source_is_rejected_before_parsing(self):
        with tempfile.TemporaryDirectory() as folder:
            path = Path(folder) / 'war3map.j'
            path.write_text('function fake takes nothing returns nothing\nendfunction')
            with self.assertRaisesRegex(ValueError, 'SHA mismatch'):
                audit.build(path, {})

    def test_batch_keeps_current_object_identity_and_no_runtime_approval(self):
        data = json.loads((ROOT / 'data/original-2323C-attack-skill-batch.json').read_text())
        base = json.loads((ROOT / 'data/original-2323C-map-base-stats.json').read_text())
        objects = {x['catalogId']: x for x in base['catalogEntries']}
        self.assertEqual(set(audit.TARGETS), {x['catalogId'] for x in data['units']})
        self.assertEqual(len(audit.TARGETS), len(data['units']))
        self.assertFalse(data['runtimeAuthorization'])
        for row in data['units']:
            self.assertEqual(objects[row['catalogId']]['originalRawcode'], row['rawcode'])
            self.assertFalse(row['runtimeConnectedByThisAudit'])
            self.assertEqual(set(row['callback']['customDependencies']),
                             {x['function'] for x in row['directCustomDependencies']})
            self.assertEqual(64, len(row['callback']['sha256']))

    def test_rejects_duplicate_or_unterminated_function(self):
        body = 'function F takes nothing returns nothing\nendfunction\n'
        with self.assertRaisesRegex(ValueError, 'Duplicate'):
            audit.functions(body + body)
        with self.assertRaisesRegex(ValueError, 'Unterminated'):
            audit.functions('function F takes nothing returns nothing\n')
