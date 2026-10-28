#!/usr/bin/env python3
"""Pin the two original voluntary unit conversions, not combat damage rules."""
import argparse
import hashlib
import json
import re
from pathlib import Path
from extract_original_native_passives import slk_rows, ABILITY_SHA, MAP_SHA
from extract_original_helper_abilities import read_objects

JASS_SHA = '96201e80edec1b2764f85818760306c6b2f0becd390ba4d63f41c86f3dfaff6d'
W3A_SHA = 'aa938f02c22884f38c5e31bcd6229a35e917a8fab0128331fdab88980cfd50e0'
SLK_POLICY_COMMIT = '82916514a12b7edb15252d42225cd8cc8ce61cfd'


def extract(jass, ability_slk, w3a, data):
    raw = jass.read_bytes()
    if hashlib.sha256(raw).hexdigest() != JASS_SHA:
        raise ValueError('Changed original JASS')
    text = raw.decode('utf-8-sig').replace('\r\n', '\n')
    abilities = slk_rows(ability_slk, ABILITY_SHA)
    overrides = w3a.read_bytes()
    if hashlib.sha256(overrides).hexdigest() != W3A_SHA:
        raise ValueError('Changed ability overlay')
    overridden_ids = {new if new.strip('\0') else old for _, old, new, _ in read_objects(overrides)}
    objects = {r['catalogId']: r for r in json.loads((data / 'original-2323C-map-base-stats.json').read_text())['catalogEntries']}
    def body(name):
        return re.search(r'^function ' + name + r' takes .*?^endfunction$', text, re.M | re.S).group(0)
    required = {
        'Bn2': ['local integer Pr=BFi(2,3)', 'local integer Vx=$68303035', 'call BdD(RN)',
                'exitwhen EK>=Pr', 'call BIY(JG,Vx)', 'set Vr=CreateUnit(Cl,Vx,EN,JS,bj_UNIT_FACING)'],
        'BoK': ['call BdD(RN)', 'set Vx=$68303041', 'set EN=GetRectCenterX(Lq[JG])',
                'set Vx=$68303035', 'set Vx=$68303039', 'call BIY(JG,Vx)'],
        'BDL': ['call TriggerRegisterAnyUnitEventBJ(Db,EVENT_PLAYER_UNIT_SPELL_EFFECT)'],
        'BDK': ['call TriggerEvaluate(LoadTriggerHandle(J6,KH,GetSpellAbilityId()))'],
        'BdD': ['call ShowUnit(Cm,false)', 'call KillUnit(Cm)'],
        'BIV': ['call SaveReal(Kn,Ko,$68303035,1536.)', 'call SaveReal(Kn,Ko,$68303039,1792.)'],
        'BIY': ['set Vu=GetRectCenterX(KO[JG])+Vs', 'set Vv=GetRectCenterY(KO[JG])+Vt'],
    }
    functions = {}
    for name, fragments in required.items():
        source = body(name)
        if not all(f in source for f in fragments):
            raise ValueError('Changed source conversion ' + name)
        functions[name] = hashlib.sha256(source.encode()).hexdigest()
    rows = []
    for uid, rawcode, code, function, duration, outputs in (
        ('S-02', 'h01C', 'A0EJ', 'BoK', 5, [('U-13', 'h00A', 1, -3760, 3904),
                                           ('C-07', 'h005', 1, -3232, 3232), ('C-08', 'h009', 1, -2976, 3232)]),
        ('S-11', 'h015', 'A0EB', 'Bn2', 2, [('C-07', 'h005', None, -3232, 3232)])):
        native = abilities[code]
        # These are complete, separately keyed SLK alias rows, not W3A patches
        # inheriting Aroa's template. w3x2lni's typed SLK reader normalizes an
        # absent integer to zero; its writer omits explicit integer zeroes.
        # Pin that interpretation and reject any overlay, not all unknown data.
        if code in overridden_ids or native.get('Cost1') not in (None, '0', '0.'):
            raise ValueError('Changed decomposition mana field or overlay')
        if objects[uid]['originalRawcode'] != rawcode or code not in objects[uid]['originalAbilityList'].split(','):
            raise ValueError('Changed conversion caster')
        if native['code'] != 'Aroa' or float(native['Cast1']) != duration or any(
                float(native[k]) != 0 for k in ('Cool1', 'Dur1', 'HeroDur1', 'DataA1', 'DataC1', 'DataD1')):
            raise ValueError('Changed native cast timing/effects')
        registration = f'call BC5(KH,${code.encode().hex().upper()},function {function})'
        if registration not in text:
            raise ValueError('Missing spell-effect registration')
        for out_id, out_raw, _, _, _ in outputs:
            if objects[out_id]['originalRawcode'] != out_raw:
                raise ValueError('Changed conversion result')
        rows.append({'catalogId': uid, 'name': objects[uid]['catalogName'], 'unitRawcode': rawcode,
                     'abilityRawcode': code, 'jassFunction': function, 'castSeconds': duration,
                     'resultCountRoll': {'min': 2, 'max': 3} if uid == 'S-11' else None,
                     'results': [dict(catalogId=i, rawcode=r, count=n, x=x, y=y) for i,r,n,x,y in outputs],
                     'goldCost': 0, 'woodCost': 0, 'nativeManaCost': 0, 'salePointsAwarded': 0})
    return {'format': 'ordr-source-unit-decomposition-v1', 'sourceW3xSha256': MAP_SHA,
            'sourceJassSha256': JASS_SHA, 'sourceAbilityDataSha256': ABILITY_SHA,
            'sourceW3aSha256': W3A_SHA, 'functions': functions, 'entries': rows,
            'slkIntegerZeroPolicy': {
                'scope': 'Standalone map SLK alias numeric Cost1 without a W3A overlay; not template inheritance',
                'sourceCommit': SLK_POLICY_COMMIT,
                'reader': f'https://github.com/sumneko/w3x2lni/blob/{SLK_POLICY_COMMIT}/script/core/slk/frontend_slk.lua',
                'writer': f'https://github.com/sumneko/w3x2lni/blob/{SLK_POLICY_COMMIT}/script/core/slk/backend_slk.lua'},
            'placementScope': 'player zero; existing fixed mobile layout, Vw=false',
            'retailCombatParityVerified': False}


if __name__ == '__main__':
    p = argparse.ArgumentParser()
    p.add_argument('--jass', type=Path, required=True)
    p.add_argument('--ability-slk', type=Path, required=True)
    p.add_argument('--w3a', type=Path, required=True)
    p.add_argument('--data', type=Path, default=Path('data'))
    p.add_argument('--output', type=Path, default=Path('data/original-2323C-unit-decomposition.json'))
    args = p.parse_args()
    args.output.write_text(json.dumps(extract(args.jass, args.ability_slk, args.w3a, args.data), ensure_ascii=False, indent=2) + '\n')
