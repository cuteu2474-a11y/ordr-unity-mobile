#!/usr/bin/env python3
"""Audit Rare material swaps and the Special Wisp pool; no final combat approval."""
import argparse
import hashlib
import json
from pathlib import Path
import re
from audit_original_special_fusions import audit as audit_special
from extract_jass_fusions import extract
from extract_original_unit_slk_stats import parse_sylk_bytes


def audit(jass, abilities_path, data, port_path):
    previous = audit_special(jass, abilities_path, data, port_path)
    text = jass.read_text(encoding='utf-8-sig')
    abilities = parse_sylk_bytes(abilities_path.read_bytes())
    objects = {r['originalRawcode']: r for r in json.loads(
        (data / 'original-2323C-map-base-stats.json').read_text())['catalogEntries'] if r.get('originalRawcode')}
    allowlist = port_path.read_text().split('string[,] ReviewedRare = {', 1)[1].split('};', 1)[0]
    reviewed = re.findall(r'\{ "(\w{4})", "(\w{4})", "(R-\d+)" \}', allowlist)
    if len(reviewed) != 41 or len({x[0] for x in reviewed}) != 41:
        raise ValueError('Expected 41 explicit Rare recipes')
    registry = {r['abilityRawcode']: r for r in extract(jass)['registrations']}
    creation = re.search(r'^function CH8 takes .*?^endfunction$', text, re.M | re.S).group(0)
    recipes = []
    for ability, rawcode, unit_id in reviewed:
        recipe = registry[ability]
        output = objects[rawcode]
        if output['catalogId'] != unit_id or output['grade'] != '희귀함' or recipe['results'] != [
                {'kind': 'UNIT', 'rawcode': rawcode, 'count': 1}]:
            raise ValueError('Changed source result: ' + ability)
        casters = []
        for condition in recipe['conditions']:
            if condition['kind'] == 'UPUN':
                continue
            if condition['kind'] != 'UNIT' or condition['count'] <= 0:
                raise ValueError('Unsupported source cost: ' + ability)
            material = objects[condition['rawcode']]
            if material['grade'] not in ('흔함', '안흔함', '특별함'):
                raise ValueError('Unreviewed material grade')
            if ability in material['originalAbilityList'].split(','):
                if 'A0TJ' in material['originalAbilityList'].split(','):
                    raise ValueError('Caster has another placement branch')
                casters.append(material['catalogId'])
        native = abilities[ability]
        if not casters or native.get('code') not in ('AHwe', 'Aroa') or native.get('targs1') != '_' or \
                float(native['Cast1']) != .06 or float(native['Cool1']) != 0 or \
                float(native['Dur1']) != 0 or float(native['HeroDur1']) != 0:
            raise ValueError('Unreviewed native cast: ' + ability)
        if 'GetUnitTypeId(iI)==$' + rawcode.encode().hex().upper() in creation:
            raise ValueError('Additional creation handler required')
        recipes.append({**recipe, 'resultCatalogId': unit_id, 'resultName': output['catalogName'],
                        'originalCasterCatalogIds': casters, 'nativeBaseAbility': native['code'],
                        'nativeCastSeconds': .06, 'nativeCooldownSeconds': 0})
    pools = json.loads((data / 'original-2323C-wisp-pools.json').read_text())
    special = next(x for x in pools['wisps'] if x['wispRawcode'] == 'e016')
    if len(special['branches']) != 1 or special['branches'][0]['chancePercent'] != 100 or \
            special['branches'][0]['bonusAbilityRawcode'] is not None:
        raise ValueError('Special Wisp has an unreviewed bonus/branch')
    entries = special['branches'][0]['entries']
    if len(entries) != 33 or any(x['weight'] != 1 or objects[x['rawcode']]['grade'] != '특별함' for x in entries):
        raise ValueError('Special Wisp source pool changed')
    functions = dict(previous['functions'])
    for name in ('CSB', 'CSC'):
        match = re.search(r'^function ' + name + r' takes .*?^endfunction$', text, re.M | re.S)
        functions[name] = {'line': text[:match.start()].count('\n') + 1,
                           'sha256LF': hashlib.sha256(match.group(0).encode()).hexdigest()}
    initializer = re.search(r'^function CSB takes .*?^endfunction$', text, re.M | re.S).group(0)
    native_pool = [(bytes.fromhex(raw).decode('ascii'), int(weight)) for raw, weight in re.findall(
        r'UnitPoolAddUnitType\(st,\$([0-9A-F]{8}),(\d+)\)', initializer)]
    if native_pool != [(row['rawcode'], row['weight']) for row in entries]:
        raise ValueError('Special Wisp JSON differs from source initialization')
    consumer = re.search(r'^function CSC takes .*?^endfunction$', text, re.M | re.S).group(0)
    for required in ('GetPlayerStartLocationX(Player(JG))', 'GetPlayerStartLocationY(Player(JG))',
                     'PlaceRandomUnit(st,Player(JG),EN,JS,bj_UNIT_FACING)',
                     'IssueImmediateOrder(bj_lastCreatedUnit,"holdposition")'):
        if required not in consumer:
            raise ValueError('Changed Special Wisp placement/creation')
    if 'call DefineStartLocation(0,-3776.,4480.)' not in text or 'call SetPlayerStartLocation(Player(0),0)' not in text:
        raise ValueError('Changed player-zero start')
    special_entries = []
    for entry in entries:
        row = objects[entry['rawcode']]
        if 'GetUnitTypeId(iI)==$' + entry['rawcode'].encode().hex().upper() in creation:
            raise ValueError('Special Wisp needs an additional creation handler')
        special_entries.append({**entry, 'catalogId': row['catalogId'], 'name': row['catalogName']})
    return {'format': 'ordr-rare-growth-v1', 'sourceW3xSha256': previous['sourceW3xSha256'],
            'sourceJassSha256': previous['sourceJassSha256'],
            'sourceAbilitySlkSha256': previous['sourceAbilitySlkSha256'],
            'functions': functions, 'recipes': recipes, 'excludedNativeCastAbilities': ['A1A0'],
            'specialWisp': {'originalRawcode': 'e016', 'positionPlayerZero': {'x': -3776, 'y': 4480},
                            'entries': special_entries},
            'retailCombatParityVerified': False,
            'limitations': ['Material swaps and Special Wisp pool/placement only, not full skills/native cast parity.',
                            'Rare/Legendary automatic Wisps remain queued; no A0YE substitute is created.',
                            'Basic attacks are enabled only in the separate development practice model.']}


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
    print('Audited', len(result['recipes']), 'Rare swaps and', len(result['specialWisp']['entries']), 'Special Wisp entries')


if __name__ == '__main__':
    main()
