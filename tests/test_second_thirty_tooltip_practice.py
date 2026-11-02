import json
import re
import unittest
from pathlib import Path


ROOT = Path(__file__).resolve().parents[1]
SOURCE = ROOT / 'Assets/Scripts/Combat/SourceModelNextThirtySkillCatalog.cs'


def rows():
    text = SOURCE.read_text()
    entries = text.split('private static readonly SourceModelNextThirtyUnit[] Entries = {', 1)[1]
    entries = entries.split('\n        };', 1)[0]
    definitions = entries.split('new SourceModelNextThirtyUnit(')[1:]
    return [re.findall(r'"([^"]+)"', definition) for definition in definitions]


def supported(skill):
    trigger = skill.get('trigger')
    if trigger not in ('onAttack', 'onEveryAttack', 'activeMana', 'activeHealth'):
        return False
    active = trigger in ('activeMana', 'activeHealth')
    valid_targets = ('area', 'single') if active else ('area', 'single', 'casterArea')
    if skill.get('target') not in valid_targets:
        return False
    if skill['target'] in ('area', 'casterArea') and skill.get('radius', 0) <= 0:
        return False
    if trigger == 'onAttack' and not 0 < skill.get('chancePercent', 0) <= 100:
        return False
    if trigger in ('onEveryAttack', 'activeMana', 'activeHealth') and 'chancePercent' in skill:
        return False
    allowed = {'name', 'trigger', 'target', 'radius', 'chancePercent', 'components'}
    if active:
        allowed |= {'manaCost', 'healthCost', 'cooldownSeconds', 'absoluteCooldownSeconds'}
        cost = 'manaCost' if trigger == 'activeMana' else 'healthCost'
        if skill.get(cost, 0) <= 0:
            return False
    if set(skill) - allowed:
        return False
    if not skill.get('components'):
        return False
    for component in skill['components']:
        if component['type'] in ('physical', 'magical', 'fixed'):
            if set(component) - {
                'type', 'amount', 'multiplierRange', 'amountPerHit', 'hits',
                'basis', 'percent', 'scope', 'onlyTargetKinds',
                'plusTargetMaxHealthPercent', 'plusTargetMissingHealthPercent',
                'extraTargetMaxHealthPercent', 'extraBossMaxHealthPercent',
                'extraEnragedMaxHealthPercent', 'extraAmount',
                'singlePrimaryTargetExtraPercent'
            }:
                return False
            quantities = ('amount', 'amountPerHit', 'plusTargetMaxHealthPercent',
                          'plusTargetMissingHealthPercent', 'extraTargetMaxHealthPercent',
                          'extraBossMaxHealthPercent', 'extraEnragedMaxHealthPercent', 'extraAmount')
            if any(component[x] <= 0 for x in quantities if x in component):
                return False
            if not any(x in component for x in quantities) and 'basis' not in component:
                return False
            if ('amountPerHit' in component) != ('hits' in component):
                return False
            if 'hits' in component and not (1 <= component['hits'] <= 64 and
                                            int(component['hits']) == component['hits']):
                return False
            if 'basis' in component and (component['basis'] != 'targetMaxHealth' or
                                        component.get('percent', 0) <= 0):
                return False
            if 'basis' not in component and 'percent' in component:
                return False
            if 'singlePrimaryTargetExtraPercent' in component and (
                'amount' not in component or component['singlePrimaryTargetExtraPercent'] <= 0
            ):
                return False
            if component.get('scope') not in (None, 'singlePrimaryTarget'):
                return False
            if 'onlyTargetKinds' in component and (
                not component['onlyTargetKinds'] or
                set(component['onlyTargetKinds']) - {'line', 'boss', 'enraged', 'story'}
            ):
                return False
            if 'multiplierRange' in component:
                value = component['multiplierRange']
                if len(value) != 2 or not 0 < value[0] <= value[1]:
                    return False
        elif component['type'] == 'stun':
            if set(component) - {'type', 'durationSeconds', 'scope', 'ignoresResistance'}:
                return False
            if component.get('durationSeconds', 0) <= 0:
                return False
            if component.get('scope') not in (None, 'singlePrimaryTarget'):
                return False
        else:
            return False
    return True


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

    def test_approved_packets_are_whole_and_source_declared(self):
        attacks = active = 0
        for unit_id, _, *names in self.rows:
            by_name = {skill['name']: skill for skill in self.profiles[unit_id]['skills']}
            for name in names:
                self.assertTrue(supported(by_name[name]), (unit_id, name))
                if by_name[name]['trigger'] in ('activeMana', 'activeHealth'):
                    active += 1
                else:
                    attacks += 1
        self.assertEqual(29, attacks)
        self.assertEqual(8, active,
                         'I-06 Big Mom was approved in the additional 13-active batch')

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
        for unit_id in ('TR-23', 'TR-30', 'I-11'):
            self.assertEqual([], by_id[unit_id])
        self.assertEqual(['대자재천신'], by_id['I-06'],
                         'Big Mom has one complete sourced health-cost packet, not a form or summon')

    def test_same_named_complete_packets_have_distinct_unit_permissions(self):
        by_id = {unit_id: names for unit_id, _, *names in self.rows}
        name = '체력 소모 광역 공격'
        for unit_id in ('TR-04', 'I-11'):
            skill = next(s for s in self.profiles[unit_id]['skills'] if s['name'] == name)
            self.assertTrue(supported(skill), unit_id)
        self.assertIn(name, by_id['TR-04'])
        self.assertNotIn(name, by_id['I-11'])


if __name__ == '__main__':
    unittest.main()
