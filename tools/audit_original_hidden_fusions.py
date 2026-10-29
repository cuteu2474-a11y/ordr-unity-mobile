#!/usr/bin/env python3
"""Audit reviewed Hidden chat-driven material swaps from the pinned original JASS.

No native cast is substituted: these commands directly call BI2. Results use
player zero's original start. Extra callbacks, CH8 effects and variant materials
remain excluded. This authorizes material transactions, not full combat parity.
"""
import argparse
import hashlib
import json
from pathlib import Path
import re

from extract_jass_fusions import extract
from extract_original_lower_grade_gameplay import JASS_SHA, MAP_SHA


def require_simple_chat_entry(ability, functions):
    invocation = re.compile(r'if not (\w+)\(\$' + ability.encode().hex().upper() + r'\) then')
    matches = [(name, body, invocation.search(body)) for name, body in functions.items()
               if invocation.search(body)]
    if len(matches) != 1:
        raise ValueError('Expected one Hidden chat handler: ' + ability)
    handler, body, match = matches[0]
    wrapper = match[1]
    expected = '\n'.join([
        'function ' + wrapper + ' takes integer WR returns boolean',
        'call BIw()', 'set Q5=GetTriggerPlayer()', 'set WS=WR',
        'call BI1(GetPlayerStartLocationX(GetTriggerPlayer()),GetPlayerStartLocationY(GetTriggerPlayer()))',
        'return BI2()', 'endfunction'])
    if functions.get(wrapper) != expected:
        raise ValueError('Hidden wrapper has additional state/conditions: ' + ability)
    expected_body = (r'function ' + handler + r' takes nothing returns nothing\n'
                     r'(?:local integer JG=GetPlayerId\(GetTriggerPlayer\(\)\)\n)?'
                     + re.escape(match[0]) + r'\nreturn\nendif\n'
                     r'call DisplayTimedTextToForce\(bj_FORCE_ALL_PLAYERS,10,"[^"\n]*"\)\n'
                     r'if GetLocalPlayer\(\)==GetTriggerPlayer\(\) then\n'
                     r'call StartSound\(XF\)\nendif\nendfunction')
    if not re.fullmatch(expected_body, body):
        raise ValueError('Hidden handler has additional state/effects: ' + ability)
    return wrapper, handler


def audit(jass, data, port):
    raw = jass.read_bytes()
    if hashlib.sha256(raw).hexdigest() != JASS_SHA:
        raise ValueError('Unexpected original JASS')
    text = raw.decode('utf-8-sig').replace('\r\n', '\n')
    functions = {m[1]: m[0] for m in re.finditer(
        r'^function (\w+) takes .*?^endfunction$', text, re.M | re.S)}
    source = json.loads((data / 'original-2323C-map-base-stats.json').read_text())
    if source['sourceW3xSha256'] != MAP_SHA:
        raise ValueError('Unexpected map objects')
    objects = {r['originalRawcode']: r for r in source['catalogEntries'] if r.get('originalRawcode')}
    registry = {r['abilityRawcode']: r for r in extract(jass)['registrations']}
    block = port.read_text().split('string[,] ReviewedHidden = {', 1)[1].split('};', 1)[0]
    reviewed = re.findall(r'\{ "(YY\d{2})", "(\w{4})", "(H-\d+)" \}', block)
    if len(reviewed) != 18 or len({r[0] for r in reviewed}) != 18:
        raise ValueError('Expected eighteen explicit Hidden recipes')
    for fragment in ('call DefineStartLocation(0,-3776.,4480.)',
                     'call SetPlayerStartLocation(Player(0),0)'):
        if fragment not in text:
            raise ValueError('Changed player-zero start')
    for name in ('CH5', 'CHi'):
        if '$5550554E' in functions[name]:
            raise ValueError('UPUN now has a non-presentation handler')
    recipes = []
    used = {'CH5', 'CHi', 'CHu', 'CHx', 'CHy', 'CH8', 'BIw', 'BI1', 'BI2'}
    for ability, result, catalog_id in reviewed:
        row = registry[ability]
        output = objects[result]
        if output['catalogId'] != catalog_id or output['grade'] != '히든' or row['results'] != [
                {'kind': 'UNIT', 'rawcode': result, 'count': 1}]:
            raise ValueError('Changed Hidden result: ' + ability)
        if 'GetUnitTypeId(iI)==$' + result.encode().hex().upper() in functions['CH8']:
            raise ValueError('Hidden result needs an additional creation handler')
        for condition in row['conditions']:
            if condition['kind'] == 'UPUN':
                continue
            if condition['kind'] != 'UNIT' or condition['count'] <= 0:
                raise ValueError('Unreviewed Hidden condition/cost')
            material = objects[condition['rawcode']]
            if not (material['grade'] in ('흔함', '안흔함', '특별함', '희귀함') or
                    (condition['rawcode'] == 'h060' and
                     material['catalogId'] == 'PS-01' and
                     material['grade'] == '해적선')):
                raise ValueError('Unreviewed Hidden material identity')
        wrapper, handler = require_simple_chat_entry(ability, functions)
        commands = re.findall(r'call Bcx\("([^"\n]+)",true,function ' + handler + r'\)', text)
        if len(commands) != 2:
            raise ValueError('Missing original Hidden command registration')
        used.update((wrapper, handler))
        recipes.append({**row, 'resultCatalogId': catalog_id, 'resultName': output['catalogName'],
                        'chatWrapper': wrapper, 'chatHandler': handler, 'commands': commands})
    evidence = {}
    for name in sorted(used):
        body = functions[name]
        evidence[name] = {'line': text[:text.index(body)].count('\n') + 1,
                          'sha256LF': hashlib.sha256(body.encode()).hexdigest(), 'source': body}
    return {'format': 'ordr-hidden-material-swaps-v1', 'sourceW3xSha256': MAP_SHA,
            'sourceJassSha256': JASS_SHA, 'positionPlayerZero': {'x': -3776, 'y': 4480},
            'recipes': recipes, 'functions': evidence, 'retailCombatParityVerified': False,
            'excludedHiddenAbilities': ['YY01', 'YY13', 'YY15', 'YY20'],
            'limitations': [
                'Eighteen source-reviewed chat-driven BI2 material swaps are connected, including five pirate-ship material recipes.',
                'YY15 resets XM in its chat callback; it is not a pure material swap.',
                'YY01/YY13/YY20 have creation follow-up effects and remain locked.',
                'Hidden skills, native combat and retail gameplay parity remain incomplete.']}


if __name__ == '__main__':
    parser = argparse.ArgumentParser()
    parser.add_argument('--jass', type=Path, required=True)
    parser.add_argument('--data', type=Path, default=Path('data'))
    parser.add_argument('--port', type=Path, default=Path('Assets/Scripts/Mobile/Original323CommonFusionPort.cs'))
    parser.add_argument('--output', type=Path, required=True)
    args = parser.parse_args()
    result = audit(args.jass, args.data, args.port)
    args.output.write_text(json.dumps(result, ensure_ascii=False, indent=2) + '\n')
    print('Audited', len(result['recipes']), 'Hidden chat-driven material swaps')
