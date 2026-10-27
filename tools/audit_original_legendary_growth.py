#!/usr/bin/env python3
"""Audit 31 Legendary material/WOOD swaps, excluding extra creation paths."""
import argparse
import json
from pathlib import Path
import re
from audit_original_rare_growth import audit as audit_rare
from extract_jass_fusions import extract
from extract_original_unit_slk_stats import parse_sylk_bytes


def audit(jass, abilities_path, data, port_path):
    previous = audit_rare(jass, abilities_path, data, port_path)
    text = jass.read_text(encoding='utf-8-sig')
    objects = {r['originalRawcode']: r for r in json.loads(
        (data / 'original-2323C-map-base-stats.json').read_text())['catalogEntries'] if r.get('originalRawcode')}
    abilities = parse_sylk_bytes(abilities_path.read_bytes())
    registry = {r['abilityRawcode']: r for r in extract(jass)['registrations']}
    allowlist = port_path.read_text().split('string[,] ReviewedLegendary = {', 1)[1].split('};', 1)[0]
    reviewed = re.findall(r'\{ "(\w{4})", "(\w{4})", "(L-\d+)" \}', allowlist)
    if len(reviewed) != 31 or len({x[0] for x in reviewed}) != 31:
        raise ValueError('Expected 31 explicit Legendary recipes')
    creation = re.search(r'^function CH8 takes .*?^endfunction$', text, re.M | re.S).group(0)
    recipes = []
    for ability, rawcode, unit_id in reviewed:
        recipe = registry[ability]
        output = objects[rawcode]
        if output['catalogId'] != unit_id or output['grade'] != '전설' or recipe['results'] != [
                {'kind': 'UNIT', 'rawcode': rawcode, 'count': 1}]:
            raise ValueError('Changed Legendary result: ' + ability)
        wood = [r for r in recipe['conditions'] if r['kind'] == 'WOOD']
        if wood != [{'kind': 'WOOD', 'rawcode': 'WOOD', 'count': 3}]:
            raise ValueError('Unreviewed Legendary cost')
        casters = []
        for row in recipe['conditions']:
            if row['kind'] in ('UPUN', 'WOOD'):
                continue
            if row['kind'] != 'UNIT' or row['count'] <= 0:
                raise ValueError('Unreviewed condition')
            material = objects[row['rawcode']]
            if material['grade'] not in ('흔함', '안흔함', '특별함', '희귀함'):
                raise ValueError('Unreviewed material grade')
            if ability in material['originalAbilityList'].split(','):
                if 'A0TJ' in material['originalAbilityList'].split(','):
                    raise ValueError('Different caster placement branch')
                casters.append(material['catalogId'])
        native = abilities[ability]
        if not casters or native.get('code') not in ('AHwe', 'Aroa') or native.get('targs1') != '_' or \
                float(native['Cast1']) != .06 or float(native['Cool1']) != 0 or \
                float(native['Dur1']) != 0 or float(native['HeroDur1']) != 0:
            raise ValueError('Unreviewed native cast: ' + ability)
        if 'GetUnitTypeId(iI)==$' + rawcode.encode().hex().upper() in creation:
            raise ValueError('Additional creation handler needed')
        recipes.append({**recipe, 'resultCatalogId': unit_id, 'resultName': output['catalogName'],
                        'originalCasterCatalogIds': casters, 'woodCost': 3,
                        'nativeBaseAbility': native['code'], 'nativeCastSeconds': .06,
                        'nativeCooldownSeconds': 0})
    return {'format': 'ordr-legendary-growth-v1', 'sourceW3xSha256': previous['sourceW3xSha256'],
            'sourceJassSha256': previous['sourceJassSha256'],
            'sourceAbilitySlkSha256': previous['sourceAbilitySlkSha256'],
            'functions': previous['functions'], 'recipes': recipes,
            'excludedAbilities': ['A00O', 'A02D', 'A025', 'A0UG', 'A01S', 'A02Q', 'A02G', 'A113'],
            'retailCombatParityVerified': False,
            'limitations': ['Only reviewed UNIT/WOOD/UPUN material transactions are enabled.',
                            'Extra CH8 creation effects and unmapped material variants remain locked.',
                            'Skills, auras, native splash and dynamic upgrades are unverified in practice.',
                            'Legendary automatic Wisp pool is still unconnected.']}


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
    print('Audited', len(result['recipes']), 'Legendary material/WOOD swaps')


if __name__ == '__main__':
    main()
