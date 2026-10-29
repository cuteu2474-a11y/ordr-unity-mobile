#!/usr/bin/env python3
"""Reproduce owned-material fusion review from pinned original input files."""
import argparse
import hashlib
import json
from pathlib import Path
import re

from extract_original_lower_grade_gameplay import JASS_SHA, MAP_SHA
from extract_original_native_passives import slk_rows, ABILITY_SHA


def review(functions, native, registry, objects):
    expected = '\n'.join([
        'function CFl takes nothing returns boolean', 'call BIw()',
        'set Q5=GetOwningPlayer(GetSpellAbilityUnit())',
        'call BI1(GetPlayerStartLocationX(GetOwningPlayer(GetSpellAbilityUnit())),GetPlayerStartLocationY(GetOwningPlayer(GetSpellAbilityUnit())))',
        'set WS=GetSpellAbilityId()', 'call BIz(GetSpellAbilityUnit())',
        'return BI2()', 'endfunction'])
    if functions['CFl'] != expected:
        raise ValueError('Tatsumaki caster/position/BI2 wrapper changed')
    allowed = [r'function CFm takes nothing returns nothing', r'local integer JG',
               r'local unit Yu', r'if not CFl\(\) then', r'return', r'endif',
               r'set Yu=FirstOfGroup\(Q6\)', r'set JG=GetPlayerId\(GetOwningPlayer\(Yu\)\)',
               r'set Yu=null', r'endfunction',
               r'call DisplayTimedTextToForce\(bj_FORCE_ALL_PLAYERS,10,"[^"\n]*"\)',
               r'call StartSound\(BHD\("[^"\n]*",null,127,1\.\*1\.,false,0,0\)\)']
    if any(not any(re.fullmatch(p, line) for p in allowed)
           for line in functions['CFm'].splitlines()):
        raise ValueError('Tatsumaki has an unreviewed callback effect')
    if 'call BC5(KH,$41313243,function CFm)' not in functions['CFu']:
        raise ValueError('Tatsumaki native cast handler registration changed')
    if 'GetUnitTypeId(iI)==$68304243' in functions['CH8']:
        raise ValueError('Tatsumaki now requires additional creation effects')
    if objects['h0BC']['catalogId'] != 'MY-12':
        raise ValueError('Tatsumaki result identity changed')
    row = registry['A12C']
    casters = [objects[c['rawcode']]['catalogId'] for c in row['conditions']
               if c['kind'] == 'UNIT' and 'A12C' in objects[c['rawcode']]['originalAbilityList'].split(',')]
    if casters != ['RN-08']:
        raise ValueError('Original Tatsumaki caster ownership changed')
    if row['results'] != [dict(kind='UNIT', rawcode='h0BC', count=1)]:
        raise ValueError('Tatsumaki result count changed')
    if [c for c in row['conditions'] if c['kind'] != 'UNIT'] != [
            dict(kind='GOLD', rawcode='GOLD', count=10000),
            dict(kind='WOOD', rawcode='WOOD', count=7),
            dict(kind='KING', rawcode='h0BC', count=1)]:
        raise ValueError('Tatsumaki costs/restrictions changed')
    fields = {key: native[key] for key in ('code', 'levels', 'targs1', 'Cast1', 'Dur1',
                                         'HeroDur1', 'Cool1', 'Area1', 'Rng1', 'UnitID1')}
    if fields != dict(code='AHwe', levels='1', targs1='_', Cast1='0.06', Dur1='0.',
                      HeroDur1='0.', Cool1='0.', Area1='0.', Rng1='0.0', UnitID1='hwat'):
        raise ValueError('Unreviewed native fusion ability fields')
    return fields, casters


def audit(jass, abilities, data):
    raw = jass.read_bytes()
    if hashlib.sha256(raw).hexdigest() != JASS_SHA:
        raise ValueError('Original JASS fingerprint changed')
    source = raw.decode('utf-8-sig').replace('\r\n', '\n')
    functions = {m[1]: m[0] for m in re.finditer(r'^function (\w+) takes .*?^endfunction$', source, re.M | re.S)}
    registry = json.loads((data / 'original-jass-fusion-registry.json').read_text())
    original = json.loads((data / 'original-2323C-map-base-stats.json').read_text())
    if registry['sourceSha256'] != MAP_SHA or original['sourceW3xSha256'] != MAP_SHA:
        raise ValueError('Original map object/registration fingerprint changed')
    objects = {r['originalRawcode']: r for r in original['catalogEntries'] if r.get('originalRawcode')}
    registrations = {r['abilityRawcode']: r for r in registry['registrations']}
    native, casters = review(functions, slk_rows(abilities, ABILITY_SHA)['A12C'], registrations, objects)
    return {'format': 'ordr-source-owned-material-fusions-v1', 'sourceW3xSha256': MAP_SHA,
            'sourceJassSha256': JASS_SHA, 'sourceAbilitySlkSha256': ABILITY_SHA,
            'recipe': registrations['A12C'], 'resultCatalogId': 'MY-12',
            'casterCatalogIds': casters, 'nativeFields': native,
            'spawnAtPlayerZeroStart': {'x': -3776, 'y': 4480},
            'functions': {n: {'sha256LF': hashlib.sha256(functions[n].encode()).hexdigest(), 'source': functions[n]}
                          for n in ('CFl', 'CFm', 'CFu', 'CH8', 'CHy', 'BH0', 'BH1')},
            'materialGroups': {'H08T': {'rawcodes': ['H099', 'H0B2'], 'catalogIds': ['TR-08', 'TR-14']}},
            'acquisitionPriority': 'deferred by user; craft from owned materials; no normal start grants added',
            'combatParityVerified': False}


if __name__ == '__main__':
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument('--jass', type=Path, required=True)
    p.add_argument('--ability-slk', type=Path, required=True)
    p.add_argument('--data', type=Path, default=Path('data'))
    p.add_argument('--output', type=Path, default=Path('data/original-2323C-owned-material-fusions.json'))
    a = p.parse_args()
    a.output.write_text(json.dumps(audit(a.jass, a.ability_slk, a.data), ensure_ascii=False, indent=2) + '\n')
