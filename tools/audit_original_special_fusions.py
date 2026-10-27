#!/usr/bin/env python3
"""Audit the explicit Special fusion allowlist against pinned original inputs.

The output approves mobile material swaps only, not skills/native cast parity.
The two AOsf variants are deliberately excluded from this AHwe/Aroa audit.
"""
import argparse
import hashlib
import json
from pathlib import Path
import re
from extract_jass_fusions import extract
from extract_original_unit_slk_stats import parse_sylk_bytes
from extract_original_lower_grade_gameplay import JASS_SHA, MAP_SHA


def audit(jass, abilities_path, data, port_path):
    raw = jass.read_bytes()
    if hashlib.sha256(raw).hexdigest() != JASS_SHA:
        raise ValueError('Unexpected original JASS')
    text = raw.decode('utf-8-sig').replace('\r\n', '\n')
    abilities = parse_sylk_bytes(abilities_path.read_bytes())
    catalog = json.loads((data / 'original-2323C-map-base-stats.json').read_text())
    if catalog['sourceW3xSha256'] != MAP_SHA:
        raise ValueError('Unexpected map objects')
    objects = {r['originalRawcode']: r for r in catalog['catalogEntries'] if r.get('originalRawcode')}
    source = port_path.read_text()
    allowlist = source.split('string[,] ReviewedSpecial = {', 1)[1].split('};', 1)[0]
    reviewed = re.findall(r'\{ "(\w{4})", "(\w{4})", "(S-\d+)" \}', allowlist)
    if len(reviewed) != 31 or len({x[0] for x in reviewed}) != 31:
        raise ValueError('Expected 31 explicit Special recipes')
    registry = {r['abilityRawcode']: r for r in extract(jass)['registrations']}
    functions = {}
    for name in ('CH5', 'CHi', 'CHu', 'CHx', 'CHy', 'CH8', 'CIB', 'CT1', 'CT2'):
        match = re.search(r'^function ' + name + r' takes .*?^endfunction$', text, re.M | re.S)
        body = match.group(0)
        functions[name] = {'line': text[:match.start()].count('\n') + 1,
                           'sha256LF': hashlib.sha256(body.encode()).hexdigest()}
        if name in ('CH5', 'CHi') and '$5550554E' in body:
            raise ValueError('UPUN is no longer presentation-only')
    recipes = []
    for ability, result, unit_id in reviewed:
        recipe = registry[ability]
        output = objects[result]
        if output['catalogId'] != unit_id or output['grade'] != '특별함' or recipe['results'] != [
                {'kind': 'UNIT', 'rawcode': result, 'count': 1}]:
            raise ValueError('Changed source result: ' + ability)
        casters = []
        for condition in recipe['conditions']:
            if condition['kind'] == 'UPUN':
                continue
            if condition['kind'] != 'UNIT' or condition['count'] <= 0:
                raise ValueError('Unsupported source cost: ' + ability)
            material = objects[condition['rawcode']]
            if material['grade'] not in ('흔함', '안흔함'):
                raise ValueError('Unreviewed material grade: ' + ability)
            if ability in material['originalAbilityList'].split(','):
                if 'A0TJ' in material['originalAbilityList'].split(','):
                    raise ValueError('Caster has a different source placement branch')
                casters.append(material['catalogId'])
        native = abilities[ability]
        if not casters or native.get('code') not in ('AHwe', 'Aroa') or native.get('targs1') != '_' or \
                float(native['Cast1']) != .06 or float(native['Cool1']) != 0 or \
                float(native['Dur1']) != 0 or float(native['HeroDur1']) != 0:
            raise ValueError('Unreviewed no-target native cast: ' + ability)
        # None of these Special outputs uses CH8's extra creation branches.
        if 'GetUnitTypeId(iI)==$' + result.encode().hex().upper() in re.search(
                r'^function CH8 takes .*?^endfunction$', text, re.M | re.S).group(0):
            raise ValueError('Special creation requires an additional CH8 handler')
        recipes.append({**recipe, 'resultCatalogId': unit_id,
                        'resultName': output['catalogName'], 'originalCasterCatalogIds': casters,
                        'nativeBaseAbility': native['code'], 'nativeCastSeconds': .06,
                        'nativeCooldownSeconds': 0})
    return {'format': 'ordr-special-material-swaps-v1', 'sourceW3xSha256': MAP_SHA,
            'sourceJassSha256': JASS_SHA,
            'sourceAbilitySlkSha256': hashlib.sha256(abilities_path.read_bytes()).hexdigest(),
            'recipes': recipes, 'functions': functions,
            'excludedNativeCastAbilities': ['A19Z', 'A1A1'],
            'retailCombatParityVerified': False,
            'limitations': ['Only material swap and original caster position are connected.',
                            'Native cast animation and AHwe/Aroa engine effects are not emulated.',
                            'Special basic attacks use the explicit practice model; skills/auras remain disabled.',
                            'A029 requires actual zombie materials; no zombie is granted for free.']}


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--jass', type=Path, required=True)
    parser.add_argument('--ability-slk', type=Path, required=True)
    parser.add_argument('--data', type=Path, default=Path('data'))
    parser.add_argument('--port', type=Path, default=Path('Assets/Scripts/Mobile/Original323CommonFusionPort.cs'))
    parser.add_argument('--output', type=Path, required=True)
    args = parser.parse_args()
    result = audit(args.jass, args.ability_slk, args.data, args.port)
    args.output.write_text(json.dumps(result, ensure_ascii=False, indent=2) + '\n')
    print('Audited', len(result['recipes']), 'Special material swaps')


if __name__ == '__main__':
    main()
