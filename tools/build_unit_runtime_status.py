#!/usr/bin/env python3
"""Reconcile actual C# model support with the entire catalog, not completion claims.

Use export_source_model_unit_support.cs against the current Core/Combat/Data/
Mobile assembly to create the TSV. A tooltip or a review matrix never grants
runtime authority. Native model support and final original parity stay separate.
"""
import argparse
import json
from pathlib import Path


def build(data, runtime_tsv):
    map_data = json.loads((data / 'original-2323C-map-base-stats.json').read_text())
    profiles = {r['unitId']: r for r in json.loads((data / 'character-skill-profiles.json').read_text())['profiles']}
    source = {r['catalogId']: r for r in map_data['catalogEntries']}
    actual = {}
    for line in runtime_tsv.read_text().splitlines():
        row = line.split('\t')
        if len(row) != 3 or row[0] in actual or row[1] not in ('0', '1'):
            raise ValueError('Malformed or duplicate runtime export')
        actual[row[0]] = {'objectBasicAttackModel': row[1] == '1',
                          'connectedModelEffects': row[2].split('|') if row[2] else []}
    if set(actual) != set(source):
        raise ValueError('Runtime export does not match the full current catalog')
    manual = {r['catalogId']: r for r in json.loads((data / 'original-2323C-unit-decomposition.json').read_text())['entries']}
    entries = []
    for unit_id, row in source.items():
        profile = profiles.get(unit_id, {})
        tasks = []
        if not row.get('originalRawcode'):
            tasks.append('원본 객체 미매칭')
        if unit_id == 'X-05':
            tasks.append('평타 없는 지원 유닛: 전용 기능 연결 필요')
        if profile.get('skills'):
            tasks.append('도감 스킬별 원본 함수·실행 순서 대응과 전투 연결 필요')
        if profile.get('passives'):
            tasks.append('나머지 패시브·변신·조건·연구 효과의 원본 대응 필요')
        tasks.append('원작 최종 피해·타깃·시점 동등성 미검증')
        entries.append({'catalogId': unit_id, 'name': row['catalogName'], 'grade': row['grade'],
                        'originalRawcode': row.get('originalRawcode'), **actual[unit_id],
                        'connectedManualFunctions': ([{'name': '유닛 분해', 'castSeconds': manual[unit_id]['castSeconds'],
                            'sourceFunction': manual[unit_id]['jassFunction']}] if unit_id in manual else []),
                        'documentedSkills': len(profile.get('skills', [])),
                        'documentedPassives': len(profile.get('passives', [])),
                        'fullUnitImplementationVerified': False, 'remainingWork': tasks})
    return {'format': 'ordr-unit-runtime-status-v1', 'sourceW3xSha256': map_data['sourceW3xSha256'],
            'allUnitsComplete': False,
            'summary': {'catalogEntries': len(entries),
                        'objectBasicAttackModels': sum(r['objectBasicAttackModel'] for r in entries),
                        'modelEffectOwners': sum(bool(r['connectedModelEffects']) for r in entries),
                        'connectedModelEffectDefinitions': sum(len(r['connectedModelEffects']) for r in entries),
                        'documentedSkillEntries': sum(r['documentedSkills'] for r in entries),
                        'documentedPassiveEntries': sum(r['documentedPassives'] for r in entries),
                        'fullyVerifiedUnits': 0,
                        'connectedManualUnitFunctions': len(manual)},
            'limits': ['The C# runtime export describes development model capability, not retail parity.',
                       'Object basic attack models omit unverified hero attributes/dynamic upgrades.',
                       'Identical aura duplicates collapse; unresolved mixed aura identities are deferred.',
                       'Movement uses the explicit practice reference model; native collisions/Arsk are unverified.',
                       'Skills in the tooltip inventory are not automatically executed.'],
            'entries': entries}


if __name__ == '__main__':
    p = argparse.ArgumentParser()
    p.add_argument('--data', default=Path('data'), type=Path)
    p.add_argument('--runtime-tsv', required=True, type=Path)
    p.add_argument('--output', default=Path('data/unit-runtime-status.json'), type=Path)
    a = p.parse_args()
    a.output.write_text(json.dumps(build(a.data, a.runtime_tsv), ensure_ascii=False, indent=2) + '\n')
