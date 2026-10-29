#!/usr/bin/env python3
"""Audit source-backed TR/ET material swaps; never approve combat or acquisition.

Run with the pinned original war3map.j. Stored function bodies permit offline
regression of the review without distributing the map or accepting new sources.
"""
import argparse
import hashlib
import json
from pathlib import Path
import re

from extract_original_lower_grade_gameplay import JASS_SHA, MAP_SHA


REVIEWED = ('TR01', 'TR10', 'TR21', 'TR23', 'TR27', 'TR28', 'ET08')


def review_functions(functions, data, port):
    records = json.loads((data / 'original-jass-fusion-registry.json').read_text())
    objects = json.loads((data / 'original-2323C-map-base-stats.json').read_text())
    if records['sourceSha256'] != MAP_SHA or objects['sourceW3xSha256'] != MAP_SHA:
        raise ValueError('Pinned source object/registration mismatch')
    registry = {r['abilityRawcode']: r for r in records['registrations']}
    by_raw = {r['originalRawcode']: r for r in objects['catalogEntries']
              if r.get('originalRawcode')}
    block = port.read_text().split('string[,] ReviewedRestricted = {', 1)[1].split('};', 1)[0]
    declared = dict((a, (raw, cid)) for a, raw, cid in re.findall(
        r'\{ "([A-Z0-9]{4})", "(\w{4})", "([A-Z]+-\d+)" \}', block))
    used = {'CHy', 'CH8', 'CH5', 'CHi', 'Bjp', 'Bjm'}
    # Bjp's return value is discarded by TR01/ET08. It reads skin
    # entitlement only; its nested Bjm also just reads account flags.
    for name, local_names in (('Bjp', {'RT', 'TS', 'Zm', 'Zn'}), ('Bjm', set())):
        body = functions[name]
        for variable in re.findall(r'^set (\w+)(?:\[[^\n]*?\])?=', body, re.M):
            if variable not in local_names:
                raise ValueError('Skin query mutates shared state: ' + name)
        calls = set(re.findall(r'\bcall (\w+)\(', body))
        if calls - {'DisplayTimedTextToPlayer'}:
            raise ValueError('Skin query has a non-presentation call')
        nested = set(re.findall(r'\b(\w+)\(', body)) - calls
        if nested - {'GetOwningPlayer', 'GetPlayerId', 'GetUnitTypeId',
                     'LoadBoolean', 'StringHash', 'Bjm'}:
            raise ValueError('Unreviewed skin query dependency')
    # These are the source creation pipeline's exceptional material aliases.
    # H099 (TR09 Luffy) is not an ordinary rawcode swap, even though its
    # own chat handler only narrates. It must remain locked.
    if 'GetUnitTypeId(Cm)==$48303939 or GetUnitTypeId(Cm)==$48304232' not in functions['CHy']:
        raise ValueError('Source creation alias branch changed')
    if '"TR09"' in block:
        raise ValueError('TR09 alias transaction is not reviewed')
    recipes = []
    for ability in REVIEWED:
        row = registry[ability]
        if len(row['results']) != 1 or row['results'][0]['kind'] != 'UNIT' or row['results'][0]['count'] != 1:
            raise ValueError('Unexpected reviewed result')
        rawcode = row['results'][0]['rawcode']
        obj = by_raw[rawcode]
        if declared.get(ability) != (rawcode, obj['catalogId']):
            raise ValueError('Missing or mismatched live declaration: ' + ability)
        if obj['grade'] != ('초월함' if ability.startswith('TR') else '영원한'):
            raise ValueError('Reviewed result grade changed')
        if 'GetUnitTypeId(iI)==$' + rawcode.encode().hex().upper() in functions['CH8']:
            raise ValueError('Result requires a CH8 followup')
        ingredients = []
        for condition in row['conditions']:
            kind, raw, count = condition['kind'], condition['rawcode'], condition['count']
            if count < 1:
                raise ValueError('Invalid source count')
            if kind == 'UNIT':
                material = by_raw[raw]
                ingredients.append({'rawcode': raw, 'catalogId': material['catalogId'], 'count': count})
            elif kind in ('KING', 'UBAN'):
                if raw != rawcode or count != 1:
                    raise ValueError('Restriction targets another unit')
            elif kind == 'UPUN':
                pass
            elif kind == 'WOOD' and ability == 'ET08' and raw == 'WOOD' and count == 5:
                pass
            else:
                raise ValueError('Unreviewed source payment/state: ' + ability + '/' + kind)
        kinds = [c['kind'] for c in row['conditions']]
        if not ingredients or kinds.count('KING') != 1 or kinds.count('UBAN') != 1:
            raise ValueError('Missing source materials/restrictions')
        if ability.startswith('TR') and not any(c['rawcode'] == 'h04S' for c in ingredients):
            raise ValueError('Source Kuma material missing')
        invocation = r'if not (\w+)\(\$' + ability.encode().hex().upper() + r'\) then'
        matches = [(name, body, re.search(invocation, body)) for name, body in functions.items()
                   if re.search(invocation, body)]
        if len(matches) != 1:
            raise ValueError('Ambiguous source chat callback')
        handler, body, match = matches[0]
        wrapper = match[1]
        expected = '\n'.join([
            'function ' + wrapper + ' takes integer WR returns boolean',
            'call BIw()', 'set Q5=GetTriggerPlayer()', 'set WS=WR',
            'call BI1(GetPlayerStartLocationX(GetTriggerPlayer()),GetPlayerStartLocationY(GetTriggerPlayer()))',
            'return BI2()', 'endfunction'])
        if functions[wrapper] != expected:
            raise ValueError('Source wrapper has additional conditions/effects')
        # Full line grammar: unknown statements, global writes, nested calls,
        # helper/item creation and timers cannot silently pass this review.
        patterns = [r'function ' + handler + r' takes nothing returns nothing',
                    r'local integer JG=GetPlayerId\(GetTriggerPlayer\(\)\)',
                    r'local unit Yu', re.escape(match[0]), r'return', r'endif',
                    r'set Yu=FirstOfGroup\(Q6\)', r'set Yu=null',
                    r'call StartSound\(BHD\("[^"\n]+",null,127,1\.\*1\.,false,0,0\)\)',
                    r'call DisplayTimedTextToForce\(bj_FORCE_ALL_PLAYERS,10,"[^"\n]*"\)',
                    r'endfunction']
        if ability in ('TR01', 'ET08'):
            patterns.append(r'call Bjp\(Yu\)')
        for line in body.splitlines():
            if not any(re.fullmatch(pattern, line) for pattern in patterns):
                raise ValueError('Unreviewed callback statement: ' + ability + ': ' + line)
        commands = []
        for name, source in functions.items():
            found = re.findall(r'call Bcx\("([^"\n]+)",true,function ' + handler + r'\)', source)
            if found:
                commands.extend(found)
                used.add(name)
        if len(commands) != 2:
            raise ValueError('Missing source chat command registration')
        used.update((wrapper, handler))
        recipes.append({'ability': ability, 'result': rawcode, 'catalogId': obj['catalogId'],
                        'name': obj['catalogName'], 'grade': obj['grade'],
                        'ingredients': ingredients, 'registration': row,
                        'wrapper': wrapper, 'handler': handler, 'commands': commands,
                        'skinQueryOnly': ability in ('TR01', 'ET08')})
    return recipes, used


def audit(jass, data, port):
    raw = jass.read_bytes()
    if hashlib.sha256(raw).hexdigest() != JASS_SHA:
        raise ValueError('Original JASS fingerprint changed')
    source = raw.decode('utf-8-sig').replace('\r\n', '\n')
    functions = {m[1]: m[0] for m in re.finditer(
        r'^function (\w+) takes .*?^endfunction$', source, re.M | re.S)}
    recipes, used = review_functions(functions, data, port)
    return {'format': 'ordr-transcend-material-swaps-v1',
            'sourceW3xSha256': MAP_SHA, 'sourceJassSha256': JASS_SHA,
            'recipes': recipes, 'spawnAtPlayerZeroStart': {'x': -3776, 'y': 4480},
            'functions': {name: {'source': functions[name],
                                'sha256LF': hashlib.sha256(functions[name].encode()).hexdigest()}
                          for name in sorted(used)},
            'limitations': [
                'Material swap authorization only; acquisition and combat parity remain separate.',
                'TR01/ET08 discard the read-only Bjp skin check; skin/audio presentation is not ported.',
                'TR09 H099 uses CHy/BH0 alias H08T and remains locked.',
                'Global Q4 counters, navigation effects and quest callbacks are not certified by this callback audit.',
                'Kuma still requires an external settled source story event; no free acquisition is introduced.']}


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--jass', type=Path, required=True)
    parser.add_argument('--data', type=Path, default=Path('data'))
    parser.add_argument('--port', type=Path, default=Path('Assets/Scripts/Mobile/Original323CommonFusionPort.cs'))
    parser.add_argument('--output', type=Path, default=Path('data/original-2323C-transcend-material-swaps.json'))
    args = parser.parse_args()
    args.output.write_text(json.dumps(audit(args.jass, args.data, args.port), ensure_ascii=False, indent=2) + '\n')
