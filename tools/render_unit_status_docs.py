#!/usr/bin/env python3
"""Render the human unit index from the actual runtime-support audit."""
import argparse
import json
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]


def render(source):
    data = json.loads(source.read_text(encoding='utf-8'))
    entries, summary = data['entries'], data['summary']
    if len(entries) != summary['catalogEntries'] or len({r['catalogId'] for r in entries}) != len(entries):
        raise ValueError('Unit audit has missing/duplicate identities')
    def cell(value):
        return str(value).replace('|', '\\|').replace('\n', ' ')
    lines = [
        '# 유닛별 현재 연결 현황', '',
        '> 이 문서는 `data/unit-runtime-status.json`에서 자동 생성합니다. 직접 체크하거나 과거 도감 표에서 완료를 복사하지 않습니다.', '',
        '[현재 상태](START_HERE.md) · [다음 작업](PLAYABLE_ROADMAP.md) · [전체 실행 범위 JSON](../data/unit-runtime-status.json)', '',
        f"카탈로그 {summary['catalogEntries']}항목, 객체 평타 모델 {summary['objectBasicAttackModels']}항목, "
        f"모델 효과 보유 {summary['modelEffectOwners']}항목 / 효과 정의 {summary['connectedModelEffectDefinitions']}개, "
        f"수동 기능 {summary['connectedManualUnitFunctions']}개입니다. 전체 원작 검증 완료는 {summary['fullyVerifiedUnits']}항목입니다.", '',
        '‘평타 모델 있음’과 효과 수는 개발용 실행 범위입니다. 해당 유닛의 액티브·변신·소환물·연구·원작 동등성 전체 완료를 뜻하지 않습니다. 상세 효과명·툴팁 수·남은 작업은 JSON의 각 항목을 확인합니다. E-02는 객체 미매칭이며 X-05는 평타 없는 지원 기능을 별도로 연결해야 합니다.', '',
        '재생성: `python3 tools/render_unit_status_docs.py` · 정합성 확인: `python3 tools/render_unit_status_docs.py --verify`.', '',
        '| ID | 유닛 | 등급 | 원본 객체 | 객체 평타 모델 | 모델 효과 수 | 수동 기능 | 전체 원작 검증 |',
        '|---|---|---|---|---|---:|---|---|',
    ]
    for row in entries:
        manual = ' / '.join(f"{r['name']} {r['castSeconds']}초" for r in row['connectedManualFunctions']) or '없음'
        values = [row['catalogId'], row['name'], row['grade'], row['originalRawcode'] or '미매칭',
                  '있음' if row['objectBasicAttackModel'] else '없음', len(row['connectedModelEffects']),
                  manual, '완료' if row['fullUnitImplementationVerified'] else '대기']
        lines.append('| ' + ' | '.join(map(cell, values)) + ' |')
    return '\n'.join(lines) + '\n'


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--source', type=Path, default=ROOT / 'data/unit-runtime-status.json')
    parser.add_argument('--output', type=Path, default=ROOT / 'docs/캐릭터별-구현목록.md')
    parser.add_argument('--verify', action='store_true')
    args = parser.parse_args()
    content = render(args.source)
    if args.verify:
        if args.output.read_text(encoding='utf-8') != content:
            raise SystemExit('Unit status documentation is outdated; rerun tools/render_unit_status_docs.py')
        print('Unit status documentation matches the runtime audit')
    else:
        args.output.write_text(content, encoding='utf-8')
        print('Rendered unit status documentation')
