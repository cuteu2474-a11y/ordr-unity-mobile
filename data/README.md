# 데이터 출처와 재생성

현재 구현 범위는 [START_HERE](../docs/START_HERE.md), 유닛별 실행 범위는 [unit-runtime-status.json](unit-runtime-status.json)에서 확인합니다. 이 폴더는 Unity Resources 생성의 입력입니다. 과거 데이터 안내는 [보존본](../docs/archive/data-README-20261028.md)에 있습니다.

## 파일의 역할

| 파일 | 용도·권한 |
|---|---|
| `units.json` | 272개 프로젝트 카탈로그 항목. 모두 별개의 원본 유닛이라는 뜻은 아님 |
| `character-skill-profiles.json` | 200개 도감 프로필, 스킬 554·패시브 295항목. 툴팁과 최종 실행 명세는 다름 |
| `original-2323C-map-base-stats.json` | 원본 객체 271개 연결·평타 객체 270개. E-02 미매칭, X-05 지원 기능 별도 |
| `unit-runtime-status.json` | 실제 C# 지원 내보내기 + 원본 매핑 + 툴팁을 합친 전체 실행 범위. 유닛 전체 원작 검증과 구분 |
| `mobile-practice-battle-model.json` | 1~20R 개발용 몬스터/경로 모델. 원작 최종 웨이브 승인 자료 아님 |
| `original-2323C-native-pure-passives.json` | 순수 치명타/기절 26개 객체 근거. 타깃 면역·원작 전체 검증은 별도 |
| `original-2323C-all-unit-aura-audit.json` | 원본 오라 소유·레벨·필드·미확인 사항. 전부 자동 실행되는 목록이 아님 |
| `practice-native-inherited-defaults.json` | 좁게 확인한 연습용 AHAd 모드 근거. 정식 자료의 미확인 필드를 일괄 덮지 않음 |
| `original-2323C-unit-decomposition.json` | 갱 뱃지/버기 마기탄 분해 시전·결과·원본 등록 근거. C-08은 총병 |
| `original-2323C-legendary-growth.json` 등 성장 감사 자료 | 실제 검수한 제작식과 보류 사유. 기본 제작 포트의 명시적 목록과 함께 확인 |
| `original-jass-fusion-registry.json` | CWR 원본 등록식 266건(TEST 1 포함). 일반 265개 미리보기를 전부 실행 허가하지 않음 |
| `original-fusion-object-reconciliation.json` 등 후보 자료 | 이름·재료·결과를 대조하는 검토 자료. 실제 소비 권한으로 자동 승격하지 않음 |
| `original-2323C-monster-*.json`, `original-2323C-wisp-pools.json` | 출현 객체·원본 일정·풀/분포 등 개별 근거. 실제 최종 적 상태/추가 생성 동작과 구분 |
| `original-2323C-missing-skill-profile-evidence.json` | 프로필 없는 72항목의 능력 코드 조사. 스킬 없음이나 구현 완료를 뜻하지 않음 |

프로필의 `gameRawCode=null`을 프로젝트 전체 rawcode 미확보로 해석하지 않습니다. 객체 매핑은 별도 자료를 사용합니다. 등급·이름·roleTags·damageSchoolHints만으로 공격 계열·피해 공식·조합 재료를 추정하지 않습니다. 니카의 두 재료 경로는 [동일성 근거](../docs/니카-초월별-영원동일성-20261023.md)를 확인합니다.

## 데이터 변경 순서

1. 원본 입력/출처와 버전을 확인합니다. 툴팁, 맵 SLK, 기본 게임 SLK, W3A 수정 기록을 구분합니다.
2. 해당 추출·감사 도구를 사용하고 변경된 필드와 보류 항목을 대조합니다. 원본 W3X는 읽기만 합니다.
3. `data/`를 갱신한 뒤 관련 Python 회귀와 C# 로더/실행 검사를 합니다. 단순 JSON 등록을 실제 게임 연결로 표시하지 않습니다.
4. 유닛 실행 범위가 바뀌면 실제 C# 내보내기를 다시 생성하고 현황 JSON·문서를 갱신합니다. Editor는 같은 `data/`에서 Resources를 동기화합니다.

## 대표 재생성 경로

각 도구는 `--help`로 현재 인수를 확인합니다. 입력의 고정 SHA는 [START_HERE](../docs/START_HERE.md)와 도구 내부에 있습니다. 외부 원본이 없는 환경에서는 기존 JSON 검사·코드 연결을 먼저 진행하며 파일을 만들어 원본인 것처럼 사용하지 않습니다.

| 대상 | 도구 | 필요한 입력 |
|---|---|---|
| 원본 객체 기본값 | [build_original_map_base_stats.py](../tools/build_original_map_base_stats.py) | 원본 W3X, 카탈로그. MPQ 추출용 `mpyq`가 필요한 경로 |
| 네이티브 순수 패시브 | [extract_original_native_passives.py](../tools/extract_original_native_passives.py) | 맵 AbilityData.slk·UnitAbilities.slk·객체 기본값 JSON |
| 로빈 3인 제한 | [extract_original_robin_multishot.py](../tools/extract_original_robin_multishot.py) | 원본 W3X의 UnitWeapons/능력·W3A 근거 |
| 20R 연습 자료 | [build_mobile_practice_battle_data.py](../tools/build_mobile_practice_battle_data.py) | 원본 war3map.j·UnitBalance.slk·기존 초기 몬스터 모델 |
| 전설 제작 감사 | [audit_original_legendary_growth.py](../tools/audit_original_legendary_growth.py) | 원본 JASS·AbilityData.slk·기존 포트/자료 |
| 유닛 분해 감사 | [extract_original_unit_decomposition.py](../tools/extract_original_unit_decomposition.py) | 원본 JASS·AbilityData.slk·war3map.w3a |
| 실제 모델 지원 TSV | [export_source_model_unit_support.cs](../tools/export_source_model_unit_support.cs) | 현재 C# 실행 어셈블리와 data 디렉터리. 인수: `data-directory output-tsv` |
| 전체 실행 범위 JSON | [build_unit_runtime_status.py](../tools/build_unit_runtime_status.py) | 위 C# 실행기가 만든 TSV + 현재 원본/도감/분해 자료 |
| 사람이 읽는 유닛 표 | [render_unit_status_docs.py](../tools/render_unit_status_docs.py) | 실행 범위 JSON. 모델 지원·원본 승인 판정을 새로 만들지 않음 |

예를 들어 현재 C# 어셈블리에 지원 내보내기 도구를 연결해 TSV를 만든 뒤 저장소 루트에서 실행합니다. 아래 TSV 경로는 실제 생성한 경로로 바꿉니다.

```sh
python3 tools/build_unit_runtime_status.py --runtime-tsv /path/to/unit-support.tsv
python3 tools/render_unit_status_docs.py
python3 tools/render_unit_status_docs.py --verify
python3 -m unittest discover -s tests -q
```

유닛 표 생성기는 JSON을 읽기만 하며 원본 수치·실행 지원·완료 플래그를 수정하지 않습니다. 실행 범위 JSON 자체를 코드를 확인하지 않고 수동으로 완료 처리하지 않습니다.

## 외부 자료와 검증 한계

원본 맵/MPQ, 임시 추출 파일, 로컬 컴파일 도구, APK·서명 키는 저장소에 포함하지 않습니다. 원본 입력이 필요한 작업에만 해당 파일 접근을 요청하고, 계정·서명 정보는 기록하지 않습니다. JSON에 남은 `null`, 보조 출처와 불확실성 메모는 임의 값으로 메우지 않습니다.

원본 규칙 검증은 [PARITY](../docs/PARITY.md), 검사/빌드 운영은 [실행 안내](../docs/unity-editmode-test.md)를 따릅니다. APK 제작 요청은 기존 워크플로의 `[apk-preview]` 조건을 사용하며 과거 ‘모든 기능 완성 전 APK 금지’를 현재 지시로 적용하지 않습니다.

## 일반/고급 돈 도박 상점 객체 재추출

`original-2323C-money-gambling-shop.json`은 고정 SHA의 원본 W3X에서 `h062` 판매 목록, `h06F`/`H0AZ`의 금액·재고·해금, `CJB`/`CJC` 보상 분기와 고급 누적 반환 졸업선을 추출합니다. 상속된 누락 필드는 `null`로 보존하며 0으로 검증됐다고 하지 않습니다. 기본 연습의 일반 돈 도박은 원본 지급 규칙과 실제 공유 지갑을 사용하지만 초기/충전 재고·전체 원본 난수 순서의 동등성은 미검증입니다.

```sh
python3 tools/extract_original_money_gambling_shop.py --w3x '/path/to/original.w3x'
```

원본 맵은 읽기만 하며 커밋하지 않습니다. 해당 자료를 고급 돈 도박의 R009 해금이나 유닛 도박 승인으로 사용하지 않습니다.
