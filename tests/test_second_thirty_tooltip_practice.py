import json
import re
import unittest
from pathlib import Path


ROOT = Path(__file__).resolve().parents[1]
SOURCE = ROOT / 'Assets/Scripts/Combat/SourceModelNextThirtySkillCatalog.cs'


def rows():
    text = SOURCE.read_text()
    definitions = re.findall(r'new SourceModelNextThirtyUnit\(([^\n;]*)\)', text)
    return [re.findall(r'"([^"]+)"', definition) for definition in definitions]


def supported(skill):
    if skill.get('trigger') not in ('onAttack', 'onEveryAttack'):
        return False
    if skill.get('target') not in ('area', 'single'):
        return False
    if skill['target'] == 'area' and skill.get('radius', 0) <= 0:
        return False
    if skill['trigger'] == 'onAttack' and not 0 < skill.get('chancePercent', 0) <= 100:
        return False
    if skill['trigger'] == 'onEveryAttack' and 'chancePercent' in skill:
        return False
    if set(skill) - {'name', 'trigger', 'target', 'radius', 'chancePercent', 'components'}:
        return False
    if not skill.get('components'):
        return False
    damage = False
    for component in skill['components']:
        if component['type'] in ('physical', 'magical'):
            if set(component) - {'type', 'amount', 'multiplierRange'}:
                return False
            if component.get('amount', 0) <= 0:
                return False
            if 'multiplierRange' in component:
                value = component['multiplierRange']
                if len(value) != 2 or not 0 < value[0] <= value[1]:
                    return False
            damage = True
        elif component['type'] == 'stun':
            if set(component) - {'type', 'durationSeconds', 'scope', 'ignoresResistance'}:
                return False
            if component.get('durationSeconds', 0) <= 0:
                return False
            if component.get('scope') not in (None, 'singlePrimaryTarget'):
                return False
        else:
            return False
    return damage


class SecondThirtyTooltipPracticeTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.rows = rows()
        cls.status = {entry['catalogId']: entry for entry in json.loads(
            (ROOT / 'data/unit-runtime-status.json').read_text())['entries']}
        cls.profiles = {entry['unitId']: entry for entry in json.loads(
            (ROOT / 'data/character-skill-profiles.json').read_text())['profiles']}

    def test_exact_thirty_distinct_original_object_identities(self):
        self.assertEqual(30, len(self.rows))
        self.assertEqual(30, len({entry[0] for entry in self.rows}))
        self.assertEqual(30, len({entry[1] for entry in self.rows}))
        previous = {entry['id'] for entry in json.loads((ROOT /
            'data/original-2323C-next-30-runtime-coverage.json').read_text())['units']}
        for unit_id, rawcode, *_ in self.rows:
            self.assertNotIn(unit_id, previous)
            self.assertEqual(rawcode, self.status[unit_id]['originalRawcode'])
            self.assertIn(unit_id, self.profiles)

    def test_sixteen_approved_packets_are_whole_and_source_declared(self):
        count = 0
        for unit_id, _, *names in self.rows:
            by_name = {skill['name']: skill for skill in self.profiles[unit_id]['skills']}
            for name in names:
                self.assertTrue(supported(by_name[name]), (unit_id, name))
                count += 1
        self.assertEqual(16, count)

    def test_legacy_source_callbacks_are_not_double_activated(self):
        by_id = {unit_id: names for unit_id, _, *names in self.rows}
        self.assertEqual([], by_id['L-33'])
        self.assertEqual([], by_id['H-16'])
        runtime = (ROOT / 'Assets/Scripts/Combat/'
                   'CharacterCombatRuntime.SourceModelAttackSkills.cs').read_text()
        self.assertLess(runtime.index('if (batch != null)'),
                        runtime.index('if (nextThirty != null)'))

    def test_unresolved_form_or_summon_skills_are_never_auto_approved(self):
        by_id = {unit_id: names for unit_id, _, *names in self.rows}
        for unit_id in ('TR-23', 'TR-30', 'I-06', 'I-11', 'L-03'):
            self.assertEqual([], by_id[unit_id])


if __name__ == '__main__':
    unittest.main()
