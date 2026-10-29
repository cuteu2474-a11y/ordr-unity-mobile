#!/usr/bin/env python3
"""Audit the source's material-only ET01 chat transaction, not combat parity.

ET01 has a UI/sound callback after BI2; its game-state mutations are the
ordered UNIT/WOOD swap itself. Other ET registrations remain unapproved.
"""
import argparse
import hashlib
import json
from pathlib import Path
import re

from extract_original_lower_grade_gameplay import JASS_SHA, MAP_SHA


def audit(jass, data, port):
    raw = jass.read_bytes()
    if hashlib.sha256(raw).hexdigest() != JASS_SHA:
        raise ValueError('Original ET01 JASS fingerprint changed')
    source = raw.decode('utf-8-sig').replace('\r\n', '\n')
    functions = {m[1]: m[0] for m in re.finditer(
        r'^function (\w+) takes .*?^endfunction$', source, re.M | re.S)}
    records = json.loads((data / 'original-jass-fusion-registry.json').read_text())
    registry = {r['abilityRawcode']: r for r in records['registrations']}
    map_stats = json.loads((data / 'original-2323C-map-base-stats.json').read_text())
    if map_stats['sourceW3xSha256'] != MAP_SHA:
        raise ValueError('Original ET01 object map changed')
    objects = {r['originalRawcode']: r for r in map_stats['catalogEntries']
               if r.get('originalRawcode')}
    block = port.read_text().split('string[,] ReviewedEternal = {', 1)[1].split('};', 1)[0]
    entries = re.findall(r'\{ "(ET\d{2})", "(\w{4})", "(E-\d+)" \}', block)
    if entries != [('ET01', 'h057', 'E-06')]:
        raise ValueError('Unreviewed Eternal transaction allowlist')
    row = registry['ET01']
    if row['results'] != [{'kind': 'UNIT', 'rawcode': 'h057', 'count': 1}]:
        raise ValueError('ET01 result changed')
    if objects['h057']['catalogId'] != 'E-06' or objects['h057']['grade'] != '영원한':
        raise ValueError('ET01 result identity changed')
    if 'GetUnitTypeId(iI)==$68303537' in functions['CH8']:
        raise ValueError('ET01 has an unimplemented CH8 effect')
    if [c['kind'] for c in row['conditions']].count('WOOD') != 1:
        raise ValueError('ET01 wood instruction changed')
    ingredients = []
    for condition in row['conditions']:
        if condition['kind'] == 'WOOD':
            if condition['rawcode'] != 'WOOD' or condition['count'] <= 0:
                raise ValueError('Unverified ET01 wood instruction')
        elif condition['kind'] == 'UNIT' and condition['count'] > 0:
            object_row = objects.get(condition['rawcode'])
            if object_row is None or object_row['grade'] not in (
                    '흔함', '안흔함', '특별함', '희귀함', '전설', '히든', '변화', '제한됨'):
                raise ValueError('ET01 material needs an independently verified map identity')
            ingredients.append({'rawcode': condition['rawcode'],
                                'catalogId': object_row['catalogId'],
                                'count': condition['count']})
        else:
            raise ValueError('Unreviewed ET01 side condition')
    wrapper = '\n'.join([
        'function B6B takes integer WR returns boolean',
        'call BIw()', 'set Q5=GetTriggerPlayer()', 'set WS=WR',
        'call BI1(GetPlayerStartLocationX(GetTriggerPlayer()),GetPlayerStartLocationY(GetTriggerPlayer()))',
        'return BI2()', 'endfunction'])
    if functions.get('B6B') != wrapper:
        raise ValueError('ET01 no longer uses the source player start and BI2')
    callback = functions.get('B6C', '')
    expected_callback = '\n'.join([
        'function B6C takes nothing returns nothing',
        'local integer JG=GetPlayerId(GetTriggerPlayer())',
        'local integer EK=1',
        'if not B6B($45543031) then', 'return', 'endif',
        'call StartSound(BHD("SE\\\\Vivi.mp3",null,127,1.*1.,false,0,0))',
        'call DisplayTimedTextToForce(bj_FORCE_ALL_PLAYERS,10,"|CFFFFFA78네펠타리 비비 : |r|CFFC15AF4이가람, 나! 해적여왕이 될 거야!|r")',
        'loop', 'exitwhen EK>6', 'if I5[EK]==null then',
        'set I5[EK]=BHD("SE\\\\vivi_gunz"+I2S(EK)+".wav",null,127,1.,true,2000,2500)',
        'endif', 'set EK=EK+1', 'endloop', 'endfunction'])
    if callback != expected_callback:
        raise ValueError('ET01 chat callback has additional gameplay state changes')
    commands = re.findall(r'call Bcx\("([^"\n]+)",true,function B6C\)', functions['B6D'])
    if commands != ['파이러츠왕녀', 'vivi et']:
        raise ValueError('ET01 chat entry changed')
    for marker in ('call DefineStartLocation(0,-3776.,4480.)',
                   'call SetPlayerStartLocation(Player(0),0)'):
        if marker not in source:
            raise ValueError('Source player-zero position changed')
    return {'format': 'ordr-eternal-material-swaps-v1', 'sourceW3xSha256': MAP_SHA,
            'sourceJassSha256': JASS_SHA, 'recipe': {**row, 'resultCatalogId': 'E-06',
            'ingredients': ingredients, 'chatCommands': commands,
            'spawnAtPlayerZeroStart': {'x': -3776, 'y': 4480}},
            'sourceFunctionSha256LF': {name: hashlib.sha256(functions[name].encode()).hexdigest()
                                      for name in ('B6B', 'B6C', 'B6D')},
            'combatParityVerified': False,
            'limitations': ['ET01 audio initialization is presentation, not a material expense.',
                            'Actual Eternal combat skills/attacks and sound playback remain unverified.',
                            'No other ET recipes are enabled; item, KING and UBAN require live state.']}


if __name__ == '__main__':
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument('--jass', type=Path, required=True)
    p.add_argument('--data', type=Path, default=Path('data'))
    p.add_argument('--port', type=Path, default=Path('Assets/Scripts/Mobile/Original323CommonFusionPort.cs'))
    p.add_argument('--output', type=Path, default=Path('data/original-2323C-eternal-material-swaps.json'))
    args = p.parse_args()
    args.output.write_text(json.dumps(audit(args.jass, args.data, args.port),
                                     ensure_ascii=False, indent=2) + '\n')
