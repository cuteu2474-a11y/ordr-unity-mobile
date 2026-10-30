#!/usr/bin/env python3
"""Build source-derived standalone enemy simulations, without external live witnesses.

The 20-round training model and 65-round standalone model are intentionally
distinct from the unimplemented Warcraft-client-parity approval path.
"""
import argparse
import hashlib
import json
from pathlib import Path
import re
from extract_original_unit_slk_stats import parse_sylk_bytes

MAP_SHA = '0794996c9bc73eaa3020f20ebc45fb37cfa64a6e98ffe8aa0dfa3b356e77bc5c'
JASS_SHA = '96201e80edec1b2764f85818760306c6b2f0becd390ba4d63f41c86f3dfaff6d'


def build(data, jass_path, balance_path, last_round=20):
    if last_round not in (20, 65):
        raise ValueError('Supported source-derived wave catalogs end at 20 or 65')
    raw = jass_path.read_bytes()
    if hashlib.sha256(raw).hexdigest() != JASS_SHA:
        raise ValueError('Unexpected original JASS')
    source = raw.decode('utf-8-sig').replace('\r\n', '\n')
    models = json.loads((data / 'original-2323C-nightmare-monster-initial-model.json').read_text())
    if models['sourceW3xSha256'] != MAP_SHA or models['safeForVerifiedMonsterWaveRuntime'] is not False:
        raise ValueError('Must retain the unverified reference model classification')
    objects = json.loads((data / 'original-2323C-monster-object-stats.json').read_text())
    by_id = {x['rawcode']: x for x in objects['entries']}
    waves = json.loads((data / 'original-2323C-monster-wave-script-evidence.json').read_text())
    if waves['sourceW3xSha256'] != MAP_SHA or waves['sourceWar3mapJSha256'] != JASS_SHA:
        raise ValueError('Wave identities do not match the source map')
    expected = {
        (wave['waveNumber'], spawn['role']): spawn['rawcode']
        for wave in waves['waves'] if wave['waveNumber'] <= last_round
        for spawn in wave['normalAndBossSpawns']
    }
    balance = parse_sylk_bytes(balance_path.read_bytes())
    initial_magic_level = None
    if last_round == 65:
        tech = json.loads((data / 'original-2323C-nightmare-monster-tech-evidence.json').read_text())
        abilities = json.loads((data / 'original-2323C-monster-object-abilities.json').read_text())
        if (tech['sourceW3xSha256'] != MAP_SHA or tech['sourceWar3mapJSha256'] != JASS_SHA
                or abilities['sourceW3xSha256'] != MAP_SHA
                or abilities['sourceWar3mapJSha256'] != JASS_SHA):
            raise ValueError('Monster A04Q source hashes disagree')
        initial_magic_level = tech['nightmareMonsterMagicDefense']['initialA04QAbilityLevel']
        if initial_magic_level != 16:
            raise ValueError('Nightmare initial A04Q level changed')
        object_abilities = {x['rawcode']: set(x['objectBaseAbilities']) for x in abilities['entries']}
        if any('A04Q' not in object_abilities.get(rawcode, ()) for rawcode in expected.values()):
            raise ValueError('Wave monster lacks original A04Q base ability')
    entries = []
    for row in models['waveMonsterInitialStatModels']:
        if row['waveNumber'] > last_round:
            continue
        rawcode = row['monsterRawcode']
        key = (row['waveNumber'], row['role'])
        if expected.get(key) != rawcode:
            raise ValueError('Source wave identity disagrees with monster stat model: ' + str(key))
        armor_type = by_id[rawcode]['warcraftArmorType']
        speed = float(balance[rawcode]['spd'])
        if speed <= 0:
            raise ValueError('Missing source movement speed')
        entries.append({'wave': row['waveNumber'], 'role': row['role'], 'rawcode': rawcode,
                        'name': row['originalMapName'], 'health': row['modeledInitialHitPoints'],
                        'armor': row['modeledInitialArmor'], 'armorType': armor_type,
                        'objectMovementSpeed': speed})
    found = {(row['wave'], row['role']) for row in entries}
    if len(entries) != len(expected) or found != set(expected):
        raise ValueError('Incomplete or duplicate source-derived enemy wave models')
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
    result = {'format': ('ordr-mobile-practice-model-v1' if last_round == 20 else
                       'ordr-mobile-standalone-source-model-v1'), 'sourceW3xSha256': MAP_SHA,
            'sourceJassSha256': JASS_SHA, 'retailParityVerified': False,
            'safeForVerifiedMonsterWaveRuntime': False, 'lastRound': last_round,
            'initialEnemyLimit': 70, 'roundRandomWisps': 2, 'routeCenters': route,
            'rounds': entries,
            'limitations': ['Reference-engine HP/armor model, not observed retail final stats.',
                           'Basic object attacks only; skills/auras/upgrades are not a full game recreation.',
                           'Route centers/raw movement speed approximate native region turns/collision.',
                           'Damage retains fractional remainder per target; retail float rounding unverified.',
                           'Source-derived simulation is not a verified original-client parity result.']}
    if last_round == 65:
        result['initialMagicAbilityLevel'] = initial_magic_level
    return result


def main():
    p = argparse.ArgumentParser()
    p.add_argument('--jass', required=True, type=Path)
    p.add_argument('--balance', required=True, type=Path)
    p.add_argument('--data', type=Path, default=Path('data'))
    p.add_argument('--output', type=Path, default=Path('data/mobile-practice-battle-model.json'))
    p.add_argument('--last-round', type=int, choices=(20, 65), default=20)
    a = p.parse_args()
    result = build(a.data, a.jass, a.balance, a.last_round)
    a.output.write_text(json.dumps(result, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
    print(str(a.last_round) + '-round source-derived monster model written; original-client parity remains unverified')


if __name__ == '__main__':
    main()
