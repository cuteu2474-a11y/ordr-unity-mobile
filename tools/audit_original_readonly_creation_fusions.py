#!/usr/bin/env python3
"""Audit two CH8 recipes whose only creation callback is a discarded skin query.

The original JASS/W3X remain external. This approves a material swap, not a
skin system, finished character skills or original retail combat parity.
"""
import argparse
import hashlib
import json
from pathlib import Path
import re

from build_original_map_base_stats import open_archive
from extract_original_lower_grade_gameplay import JASS_SHA, MAP_SHA
from extract_original_unit_slk_stats import parse_sylk_bytes

REVIEWED = {'A02D': ('h02T', 'L-11', '전설'),
            'YY13': ('h055', 'H-13', '히든')}
NATIVE_FIELDS = ('code', 'targs1', 'Cast1', 'Cool1', 'Dur1', 'HeroDur1')


def review(functions, native, registry, objects, port, pinned):
    # The full creation hook is pinned; merely looking for a Bjp substring
    # would miss additional player-state, helper or item effects.
    for name in ('CH8', 'Bjp', 'Bjm'):
        if functions[name] != pinned['functions'][name]['source']:
            raise ValueError('Source creation/skin query changed: ' + name)
    for raw, _, _ in REVIEWED.values():
        hex_raw = raw.encode('ascii').hex().upper()
        branch = (f'if GetUnitTypeId(iI)==${hex_raw} then\n'
                  'call Bjp(iI)\nset iI=null\nreturn\nendif')
        if functions['CH8'].count(branch) != 1:
            raise ValueError('Creation branch has gameplay effects: ' + raw)
    wrapper = '\n'.join([
        'function BrG takes integer WR returns boolean',
        'call BIw()', 'set Q5=GetTriggerPlayer()', 'set WS=WR',
        'call BI1(GetPlayerStartLocationX(GetTriggerPlayer()),GetPlayerStartLocationY(GetTriggerPlayer()))',
        'return BI2()', 'endfunction'])
    if functions['BrG'] != wrapper:
        raise ValueError('Hidden material swap wrapper changed')
    chat_lines = [
        'function BrH takes nothing returns nothing',
        'local integer JG=GetPlayerId(GetTriggerPlayer())',
        'if not BrG($59593133) then', 'return', 'endif',
        None,
        'if GetLocalPlayer()==GetTriggerPlayer() then',
        'call StartSound(XF)', 'endif', 'endfunction']
    actual = functions['BrH'].splitlines()
    if len(actual) != len(chat_lines) or any(
            expected is not None and actual[i] != expected
            for i, expected in enumerate(chat_lines)) or not re.fullmatch(
            r'call DisplayTimedTextToForce\(bj_FORCE_ALL_PLAYERS,10,"[^"\n]*"\)', actual[5]):
        raise ValueError('Hidden post-commit chat action changed')
    commands = re.findall(r'call Bcx\("([^"\n]+)",true,function BrH\)', functions['BrJ'])
    if commands != ['스튜시조합', 'stussy']:
        raise ValueError('Hidden chat commands changed')
    block = port.read_text().split('string[,] ReviewedLegendary = {', 1)[1]
    legendary = block.split('};', 1)[0]
    hidden = port.read_text().split('string[,] ReviewedHidden = {', 1)[1].split('};', 1)[0]
    reviewed = {}
    for ability, (raw, cid, grade) in REVIEWED.items():
        declared = legendary if grade == '전설' else hidden
        if len(re.findall(r'\{ "' + ability + r'", "' + raw + r'", "' + cid + r'" \}', declared)) != 1:
            raise ValueError('Not bound to live fusion authorization: ' + ability)
        source = registry[ability]
        if source['results'] != [dict(kind='UNIT', rawcode=raw, count=1)]:
            raise ValueError('Original result changed: ' + ability)
        output = objects[raw]
        if output['catalogId'] != cid or output['grade'] != grade:
            raise ValueError('Original result identity changed: ' + ability)
        caster = []
        materials = []
        for condition in source['conditions']:
            kind, mat_raw, amount = (condition[k] for k in ('kind', 'rawcode', 'count'))
            if amount < 1:
                raise ValueError('Invalid source quantity')
            if kind == 'UNIT':
                original = objects[mat_raw]
                if original['grade'] not in ('흔함', '안흔함', '특별함', '희귀함'):
                    raise ValueError('Unreviewed material grade')
                materials.append(dict(rawcode=mat_raw, catalogId=original['catalogId'], count=amount))
                if ability in original['originalAbilityList'].split(','):
                    if 'A0TJ' in original['originalAbilityList'].split(','):
                        raise ValueError('Caster has another source placement branch')
                    caster.append(original['catalogId'])
            elif kind == 'UPUN':
                continue  # Presentation only, not another material or grant.
            elif ability == 'A02D' and kind == 'WOOD' and mat_raw == 'WOOD' and amount == 3:
                continue
            else:
                raise ValueError('Unreviewed payment or state: ' + ability + '/' + kind)
        if not materials or (ability == 'A02D' and caster != ['R-08']) or (ability == 'YY13' and caster):
            raise ValueError('Original caster/material linkage changed')
        if ability == 'A02D':
            fields = {key: native.get(key) for key in NATIVE_FIELDS}
            if fields != dict(code='AHwe', targs1='_', Cast1='0.06', Cool1='0.',
                              Dur1='0.', HeroDur1='0.'):
                raise ValueError('Native legendary no-target cast changed')
            if sum(c['kind'] == 'WOOD' for c in source['conditions']) != 1:
                raise ValueError('Legendary wood cost changed')
        else:
            if any(c['kind'] not in ('UNIT', 'UPUN') for c in source['conditions']):
                raise ValueError('Hidden chat payment changed')
        reviewed[ability] = dict(resultRawcode=raw, catalogId=cid, grade=grade,
                                  sourceRegistration=source, materials=materials,
                                  originalCasterCatalogIds=caster)
    return reviewed


def audit(jass, w3x, data, port):
    if hashlib.sha256(jass.read_bytes()).hexdigest() != JASS_SHA or \
            hashlib.sha256(w3x.read_bytes()).hexdigest() != MAP_SHA:
        raise ValueError('Pinned original source fingerprint changed')
    source = jass.read_text(encoding='utf-8-sig').replace('\r\n', '\n')
    functions = {name: body for body, name in re.findall(
        r'(^function (\w+) takes .*?^endfunction$)', source, re.M | re.S)}
    selected = {n: functions[n] for n in ('CH8', 'Bjp', 'Bjm', 'BrG', 'BrH', 'BrJ')}
    registry_data = json.loads((data / 'original-jass-fusion-registry.json').read_text())
    objects_data = json.loads((data / 'original-2323C-map-base-stats.json').read_text())
    if registry_data['sourceSha256'] != MAP_SHA or objects_data['sourceW3xSha256'] != MAP_SHA:
        raise ValueError('Pinned registry/object evidence changed')
    registry = {r['abilityRawcode']: r for r in registry_data['registrations']}
    objects = {r['originalRawcode']: r for r in objects_data['catalogEntries'] if r.get('originalRawcode')}
    with open_archive(w3x) as archive:
        native = parse_sylk_bytes(archive.read_file('Units\\AbilityData.slk'))['A02D']
    pinned = json.loads((data / 'original-2323C-transcend-material-swaps.json').read_text())
    approved = review(selected, native, registry, objects, port, pinned)
    return {'format': 'ordr-readonly-creation-fusions-v1', 'sourceW3xSha256': MAP_SHA,
            'sourceJassSha256': JASS_SHA, 'reviewed': approved,
            'legendaryNativeFields': {k: native.get(k) for k in NATIVE_FIELDS},
            'originalHiddenChatCommands': ['스튜시조합', 'stussy'],
            'functions': {n: dict(source=body, sha256LF=hashlib.sha256(body.encode()).hexdigest())
                          for n, body in selected.items()},
            'limits': ['Only discarded read-only skin entitlement queries are omitted.',
                       'No source cost, additional unit, player state or item is waived.',
                       'Material swap is not retail character skill/combat parity.']}


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--jass', type=Path, required=True)
    parser.add_argument('--w3x', type=Path, required=True)
    parser.add_argument('--data', type=Path, default=Path('data'))
    parser.add_argument('--port', type=Path, default=Path('Assets/Scripts/Mobile/Original323CommonFusionPort.cs'))
    parser.add_argument('--output', type=Path, default=Path('data/original-2323C-readonly-creation-fusions.json'))
    args = parser.parse_args()
    args.output.write_text(json.dumps(audit(args.jass, args.w3x, args.data, args.port),
                                      ensure_ascii=False, indent=2) + '\n')
