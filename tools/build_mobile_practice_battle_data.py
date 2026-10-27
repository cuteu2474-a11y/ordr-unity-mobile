#!/usr/bin/env python3
"""Build an explicitly NON-RETAIL 10-round practice model from original data.
Never produces a verified-wave catalog or changes the original source files.
"""
import argparse
import hashlib
import json
from pathlib import Path
import re
from extract_original_unit_slk_stats import parse_sylk_bytes

MAP_SHA = '0794996c9bc73eaa3020f20ebc45fb37cfa64a6e98ffe8aa0dfa3b356e77bc5c'
JASS_SHA = '96201e80edec1b2764f85818760306c6b2f0becd390ba4d63f41c86f3dfaff6d'


def build(data, jass_path, balance_path):
    raw = jass_path.read_bytes()
    if hashlib.sha256(raw).hexdigest() != JASS_SHA:
        raise ValueError('Unexpected original JASS')
    source = raw.decode('utf-8-sig').replace('\r\n', '\n')
    models = json.loads((data / 'original-2323C-nightmare-monster-initial-model.json').read_text())
    if models['sourceW3xSha256'] != MAP_SHA or models['safeForVerifiedMonsterWaveRuntime'] is not False:
        raise ValueError('Must retain the unverified reference model classification')
    objects = json.loads((data / 'original-2323C-monster-object-stats.json').read_text())
    by_id = {x['rawcode']: x for x in objects['entries']}
    balance = parse_sylk_bytes(balance_path.read_bytes())
    entries = []
    for row in models['waveMonsterInitialStatModels']:
        if row['waveNumber'] > 10:
            continue
        rawcode = row['monsterRawcode']
        armor_type = by_id[rawcode]['warcraftArmorType']
        speed = float(balance[rawcode]['spd'])
        if speed <= 0:
            raise ValueError('Missing source movement speed')
        entries.append({'wave': row['waveNumber'], 'role': row['role'], 'rawcode': rawcode,
                        'name': row['originalMapName'], 'health': row['modeledInitialHitPoints'],
                        'armor': row['modeledInitialArmor'], 'armorType': armor_type,
                        'objectMovementSpeed': speed})
    if len(entries) != 10 or sorted(x['wave'] for x in entries) != list(range(1, 11)):
        raise ValueError('Incomplete 10-round source model')
    route = []
    for name in ['y', 'z', 'BB', 'BC']:
        match = re.search(r'^set ' + name + r'=Rect\(([^\n]+)\)$', source, re.M)
        x1, y1, x2, y2 = map(float, match.group(1).split(','))
        route.append({'x': (x1 + x2) / 2, 'y': (y1 + y2) / 2})
    for name, target in [('CSR', 'z'), ('CSU', 'BB'), ('CSX', 'BC'), ('CSa', 'y')]:
        body = re.search(r'^function ' + name + r' takes .*?^endfunction$', source, re.M | re.S).group(0)
        if f'GetRectCenterX({target})' not in body:
            raise ValueError('Original line route changed')
    difficulty = next(body for body in re.findall(r'elseif NE==NL then\n(.*?)\nendif', source, re.S) if 'set ia=70' in body)
    if 'set ia=70' not in difficulty or 'call Bcz(Player(EK),2)' not in source:
        raise ValueError('Round/Wisp source rules changed')
    return {'format': 'ordr-mobile-practice-model-v1', 'sourceW3xSha256': MAP_SHA,
            'sourceJassSha256': JASS_SHA, 'retailParityVerified': False,
            'safeForVerifiedMonsterWaveRuntime': False, 'lastRound': 10,
            'initialEnemyLimit': 70, 'roundRandomWisps': 2, 'routeCenters': route,
            'rounds': entries,
            'limitations': ['Reference-engine HP/armor model, not observed retail final stats.',
                           'Basic object attacks only; skills/auras/upgrades are not a full game recreation.',
                           'Route centers/raw movement speed approximate native region turns/collision.',
                           'Damage retains fractional remainder per target; retail float rounding unverified.',
                           'Practice completion at round 10 is not original round-65 clear.']}


def main():
    p = argparse.ArgumentParser()
    p.add_argument('--jass', required=True, type=Path)
    p.add_argument('--balance', required=True, type=Path)
    p.add_argument('--data', type=Path, default=Path('data'))
    p.add_argument('--output', type=Path, default=Path('data/mobile-practice-battle-model.json'))
    a = p.parse_args()
    result = build(a.data, a.jass, a.balance)
    a.output.write_text(json.dumps(result, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
    print('10-round practice source model written; retail verification remains false')


if __name__ == '__main__':
    main()
