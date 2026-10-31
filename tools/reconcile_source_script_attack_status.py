#!/usr/bin/env python3
"""Sync the implemented 20 offline attack callbacks into the runtime index.

The code source of truth is SourceModelScriptAttackCatalog.cs; the pinned
JASS audit supplies identities and BFi thresholds. This updater never marks
Warcraft retail/native completion or grants an execution authorization.
"""
import argparse
import json
import re
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]


def chance(callback):
    found = [re.search(r'BFi\(1,(\d+)\)\s*<=\s*(\d+)', line)
             for line in callback['sourceOperations']]
    matches = [match for match in found if match]
    if len(matches) != 1:
        raise ValueError('Expected exactly one BFi branch: ' + callback['function'])
    denominator, threshold = map(int, matches[0].groups())
    if denominator not in (100, 1000) or threshold <= 0 or threshold > denominator:
        raise ValueError('Invalid original branch threshold')
    return f'{threshold * 100 / denominator:g}'


def update(data, audit):
    rows = {row['catalogId']: row for row in data['entries']}
    if audit['unitCount'] != 20 or audit['runtimeAuthorization'] or len(audit['units']) != 20:
        raise ValueError('Attack audit is not the expected pinned 20-source input')
    if not Path(ROOT / 'Assets/Scripts/Combat/SourceModelScriptAttackCatalog.cs').read_text().count(
            'Row(') == 21:  # one constructor helper and twenty entries
        raise ValueError('Review the source model catalog before publishing a new count')
    for source in audit['units']:
        row = rows[source['catalogId']]
        if row['originalRawcode'] != source['rawcode']:
            raise ValueError('Source unit rawcode mismatch')
        label = ('공격 선언 기술 · ' + chance(source['callback']) +
                 '% 발동 · 연습 피해/범위 모델, 보조 주문·면역·원작 동등성 미검증')
        effects = row['connectedModelEffects']
        if not any(x.startswith('공격 선언 기술 · ') for x in effects):
            effects.append(label)
        elif effects[-1] != label:
            raise ValueError('Unexpected stale source attack description')
    data['summary']['modelEffectOwners'] = sum(bool(x['connectedModelEffects']) for x in data['entries'])
    data['summary']['connectedModelEffectDefinitions'] = sum(len(x['connectedModelEffects']) for x in data['entries'])
    data['allUnitsComplete'] = False
    return data


if __name__ == '__main__':
    p = argparse.ArgumentParser()
    p.add_argument('--verify', action='store_true')
    a = p.parse_args()
    target = ROOT / 'data/unit-runtime-status.json'
    current = target.read_text()
    result = json.dumps(update(json.loads(current), json.loads(
        (ROOT / 'data/original-2323C-attack-skill-batch.json').read_text())),
        ensure_ascii=False, indent=2) + '\n'
    if a.verify:
        if current != result:
            raise SystemExit('Attack script runtime status is stale')
    else:
        target.write_text(result)
    print('20 source attack descriptions reconciled (MODEL_ONLY)')
