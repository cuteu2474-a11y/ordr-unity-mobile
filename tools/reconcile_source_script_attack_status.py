#!/usr/bin/env python3
"""Sync the 20 original + 6 new w5 models and four native follow-ups.

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


def update(data, audit, next_audit):
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
    new_catalog = (ROOT / 'Assets/Scripts/Combat/SourceModelNextAttackCatalog.cs').read_text()
    if new_catalog.count('new SourceScriptAttackSpec(') != 6 or \
            next_audit['runtimeAuthorization'] or \
            next_audit['sourceJassSha256'] != audit['sourceJassSha256']:
        raise ValueError('Next native attack source/model count mismatch')
    registered = 0
    for source in next_audit['units']:
        callbacks = source['w5AttackCallbackEvidence']
        if not callbacks:
            continue
        if len(callbacks) != 1:
            raise ValueError('Unexpected additional callback')
        registered += 1
        key = source['catalogId']
        row = rows[key]
        if source['originalUnitRawcode'] != row['originalRawcode']:
            raise ValueError('Next unit rawcode mismatch')
        cb = callbacks[0]
        label = (f"공격 선언 기술 · {cb['attackEventChancePercent']:g}% 발동 · "
                 '연습 피해/범위 모델, 보조 주문·면역·원작 동등성 미검증')
        if label not in row['connectedModelEffects']:
            row['connectedModelEffects'].append(label)
    if registered != 6:
        raise ValueError('Expected six original w5 callbacks')
    native = {
        'S-05': 'A08B 언홀리 프렌지 · 자신 공속 +100% / 7초 / 50초 재사용 · 연습 자기 시전 모델, 원작 자동 대상 미검증',
        'S-09': 'A087 블랙 애로우 · 일반 적 처치 시 h00H 좀비 1기 / 10초 · 연습 소환 모델, 원본 조건 미검증',
        'S-20': 'A0EG 리퀴드 파이어 · 타격 시 150/초 3초 화상 모델 · 원작 틱·면역·중첩 미검증',
        'S-28': 'A0EA 피닉스 파이어 · 0.2초마다 600 범위 150 피해 · 연습 자동 타깃 모델, 원본 타이밍 미검증',
    }
    for key, label in native.items():
        if label not in rows[key]['connectedModelEffects']:
            rows[key]['connectedModelEffects'].append(label)
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
        (ROOT / 'data/original-2323C-attack-skill-batch.json').read_text()),
        json.loads((ROOT / 'data/original-2323C-next-20-native-source-audit.json').read_text())),
        ensure_ascii=False, indent=2) + '\n'
    if a.verify:
        if current != result:
            raise SystemExit('Attack script runtime status is stale')
    else:
        target.write_text(result)
    print('26 source attack and 4 native follow-up descriptions reconciled (MODEL_ONLY)')
