#!/usr/bin/env python3
"""Recount live fusion authorization and group all original unresolved recipes.

This is a source-linked engineering inventory, not permission to craft. Read
real allowances from the C# code and quantities from the pinned JASS registry.
"""
import argparse
from collections import Counter
import json
from pathlib import Path
import re

ROOT = Path(__file__).resolve().parents[1]
REVIEWED_NAMES = ('Reviewed', 'ReviewedSpecial', 'ReviewedRare',
                  'ReviewedLegendary', 'ReviewedHidden', 'ReviewedEternal',
                  'ReviewedRestricted')
CONDITION_GROUPS = {
    'ITEM': 'item-payment',
    'SKPT': 'trait-counter', 'CHCT': 'trait-counter', 'IFCT': 'trait-counter',
    'GREN': 'transformation-effect', 'PICK': 'special-target-or-pick',
    'RDUN': 'round-state', 'RMAX': 'round-state',
    'KING': 'top-tier-route-or-callback-audit',
    'SPEC': 'special-native-result',
}
NATIVE_UNVERIFIED = {'A19Z', 'A1A1', 'A1A0'}
CHAT_STATE_EFFECT = {'YY15'}


def build(data, port):
    source = json.loads((data / 'original-jass-fusion-registry.json').read_text())
    raw = port.read_text()
    groups = {}
    enabled = set()
    for name in REVIEWED_NAMES:
        match = re.search(r'string\[,] ' + name + r' = \{(.*?)\n        \};', raw, re.S)
        if not match:
            raise ValueError('Missing live allowlist: ' + name)
        matches = re.findall(r'\{ "([A-Z0-9]{4})", "(\w{4})", "([A-Z]+-\d+)" \}',
                             match[1])
        if not matches:
            raise ValueError('Empty executable grade: ' + name)
        abilities = [row[0] for row in matches]
        if len(abilities) != len(set(abilities)) or not enabled.isdisjoint(abilities):
            raise ValueError('Duplicated or overlapping executable recipe identifiers')
        groups[name] = abilities
        enabled.update(abilities)
    objects = json.loads((data / 'original-2323C-map-base-stats.json').read_text())
    verified = {r['originalRawcode']: r['catalogId'] for r in objects['catalogEntries']
                if r.get('originalRawcode')}
    names = {r['rawcode']: r['mapName'] for r in objects['mapObjects']}
    hidden = json.loads((data / 'original-2323C-hidden-material-swaps.json').read_text())
    ch8 = hidden['functions']['CH8']['source']
    ch8_results = set()
    for value in re.findall(r'GetUnitTypeId\(iI\)==\$([0-9A-F]{8})', ch8):
        ch8_results.add(bytes.fromhex(value).decode('ascii'))
    all_rows = [r for r in source['registrations'] if r['abilityRawcode'] != 'TEST']
    all_ids = {r['abilityRawcode'] for r in all_rows}
    if len(all_rows) != len(all_ids) or not enabled.issubset(all_ids):
        raise ValueError('Source registry and executable allowlist disagree')
    blocked = []
    by_reason = Counter()
    for recipe in all_rows:
        ability = recipe['abilityRawcode']
        if ability in enabled:
            continue
        conditions = recipe['conditions']
        needed = {c['rawcode'] for c in conditions if c['kind'] == 'UNIT'}
        results = {c['rawcode'] for c in recipe['results'] if c['kind'] == 'UNIT'}
        unmapped = sorted((needed | results) - set(verified))
        reasons = set()
        if unmapped:
            reasons.add('unmapped-original-unit')
        for item in conditions:
            if item['kind'] == 'UBAN':
                # CNn/CNm's ban table is populated ONLY by 변질 mode (th[tl]).
                # The target is fixed to 이세계 mode (th[tn]), so UBAN does
                # not block any otherwise verified recipe in this build.
                continue
            if item['kind'] in CONDITION_GROUPS:
                reasons.add(CONDITION_GROUPS[item['kind']])
            elif item['kind'] not in {'UNIT', 'GOLD', 'WOOD', 'UPUN'}:
                reasons.add('unknown-original-condition')
        if any(c['rawcode'] in ch8_results for c in recipe['results']):
            reasons.add('creation-followup-CH8')
        if ability in NATIVE_UNVERIFIED:
            reasons.add('native-cast-AOsf')
        if ability in CHAT_STATE_EFFECT:
            reasons.add('chat-player-state-effect')
        if not any(c['kind'] == 'UNIT' for c in conditions):
            reasons.add('no-unit-payment-in-registration')
        if not reasons:
            reasons.add('source-callback-or-native-effect-needs-audit')
        by_reason.update(reasons)
        blocked.append({
            'abilityRawcode': ability,
            'resultRawcodes': sorted(results),
            'resultNames': [names.get(code) for code in sorted(results)],
            'unmappedUnitRawcodes': unmapped,
            'originalConditionKinds': sorted({c['kind'] for c in conditions}),
            'sourceGoldCost': sum(c['count'] for c in conditions if c['kind'] == 'GOLD'),
            'sourceWoodCost': sum(c['count'] for c in conditions if c['kind'] == 'WOOD'),
            'reasonCodes': sorted(reasons),
        })
    if len(blocked) + len(enabled) != len(all_rows):
        raise ValueError('Missing or duplicated source recipes')
    return {
        'format': 'ordr-fusion-blockers-v1',
        'sourceW3xSha256': objects['sourceW3xSha256'],
        'sourceJassSha256': source['sourceSha256'],
        'executionSource': str(port.relative_to(ROOT)),
        'limits': [
            'Reason codes are independently accumulated and their counts overlap.',
            'Only C# explicitly reviewed recipes are executable; registry entries are not authorization.',
            'Verified object identities do not prove the missing unit can be obtained in gameplay.',
            'GOLD/WOOD are source costs, while UPUN is presentation-only, never a consumed unit.',
            'Unknown callbacks or native effects stay locked even when material IDs are mapped.',
            'Original UBAN is a 변질-mode-only ban; 이세계 mode never activates its draw. UBAN is retained in originalConditionKinds but is not a blocking reason.',
        ],
        'summary': {'sourceRecipesExcludingTest': len(all_rows),
                    'liveExecutableRecipes': len(enabled),
                    'blockedRecipes': len(blocked),
                    'liveExecutableByGrade': {key: len(value) for key, value in groups.items()},
                    'blockedByReason': dict(sorted(by_reason.items()))},
        'blocked': blocked,
    }


if __name__ == '__main__':
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument('--data', type=Path, default=ROOT / 'data')
    p.add_argument('--port', type=Path,
                   default=ROOT / 'Assets/Scripts/Mobile/Original323CommonFusionPort.cs')
    p.add_argument('--output', type=Path,
                   default=ROOT / 'data/original-fusion-unconnected-review.json')
    p.add_argument('--verify', action='store_true')
    args = p.parse_args()
    value = json.dumps(build(args.data, args.port), ensure_ascii=False, indent=2) + '\n'
    if args.verify:
        if not args.output.exists() or args.output.read_text() != value:
            raise SystemExit('Committed source fusion blocker inventory is stale')
    else:
        args.output.write_text(value)
