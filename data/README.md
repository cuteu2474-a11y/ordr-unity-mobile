# 데이터 출처와 생성 안내

`data/`는 원본 분석 근거와 Unity Resources 생성 입력입니다. 기능 상태는 [현재 상태](../docs/START_HERE.md), 완료 판정은 [검증 기준](../docs/PARITY.md)에서 확인합니다.

## 자료를 읽는 순서

| 자료 | 용도 |
|---|---|
| `units.json`, `wiki-unit-catalog-extension.json` | 기본 카탈로그와 확장/변신/지원 객체. 모두 독립 전투 유닛이라는 뜻은 아님 |
| `character-skill-profiles.json` | 도감 툴팁. 실제 호출·조건·피해 계산의 완전한 실행 명세와 구분 |
| `original-2323C-map-base-stats.json`, `original-2323C-unit-object-stats.json` | 원본 rawcode·기본 공격·HP/MP/재생 등 객체 근거 |
| `original-jass-fusion-registry.json` | 원본 CWR 등록식 266건. 일반 미리보기 265건과 TEST 1건이며 전부 제작 허가는 아님 |
| `original-2323C-legendary-growth.json` | 전설 조합의 원본 재료·목재 소비·보류 조건을 확인하는 감사 자료 ([생성/대조 도구](../tools/audit_original_legendary_growth.py)) |
| `original-2323C-*.json`, `source-model-*.json` | 원본 추출·버전/객체 대조·구현 근거·보류 사유. 파일 존재만으로 실행 승인하지 않음 |
| `unit-runtime-status.json` | C# 모델 지원 내보내기를 합친 저장된 유닛 실행 범위 |
| `character-runtime-coverage.json` | 기본 277개 + 확장 11개 = 288항목. 지원 객체·변신체를 포함한 집계이며 완성 유닛 수가 아님 |
| `mobile-standalone-enemy-model.json` | APK에서 사용하는 65R 웨이브 모델. 원작 최종 실측 동등성은 미검증 |

과거 10R/20R 모델 `mobile-practice-battle-model.json`은 저수준 회귀 검사에만 사용하는 자료이며, Unity Resources/APK 필수 목록에서는 제외했습니다. 게임에는 65R 단일 시작만 남습니다.

유닛 표는 `unit-runtime-status.json`의 스냅샷입니다. 두 현황 JSON은 집계 범위가 다르며 원작 완성 유닛 수로 합산하지 않습니다. 프로필 rawcode가 비어 있어도 별도 객체 매핑을 확인합니다. 이름·등급·태그만으로 피해 계열이나 재료를 추정하지 않습니다.

## 변경 순서

1. 원본 버전과 고정 SHA를 [작업 규칙](../AGENTS.md)에서 확인합니다. W3X는 읽기만 합니다.
2. 해당 추출 도구·원본 JASS/SLK/W3A·기존 JSON을 대조합니다. 맵의 누락 필드와 기본 게임 부모 상속을 구분하고 미확인 값은 `null`로 보존합니다.
3. `data/`와 관련 실행 코드를 함께 갱신합니다. Editor가 `Assets/Resources/OrdrData/`를 동기화하므로 캐시를 직접 편집·커밋하지 않습니다.
4. 실행 범위가 바뀌면 실제 C# 지원 내보내기·현황 JSON·생성 문서를 갱신합니다. 검사 실행은 사용자 요청에 따릅니다.

## 대표 생성 도구

| 대상 | 도구 | 입력 |
|---|---|---|
| 원본 객체 | [build_original_map_base_stats.py](../tools/build_original_map_base_stats.py) | 원본 W3X·카탈로그 |
| 순수 패시브 | [extract_original_native_passives.py](../tools/extract_original_native_passives.py) | 맵 능력/유닛 SLK·객체 JSON |
| 독립 65R 모델 | [build_mobile_practice_battle_data.py](../tools/build_mobile_practice_battle_data.py) `--last-round 65` | 원본 JASS·UnitBalance.slk·초기 몬스터 자료 |
| 실제 C# 지원 TSV | [export_source_model_unit_support.cs](../tools/export_source_model_unit_support.cs) | 현재 C# 어셈블리·data 디렉터리 |
| 유닛 실행 범위 | [build_unit_runtime_status.py](../tools/build_unit_runtime_status.py) | 위 TSV·원본/도감/분해 자료 |
| 유닛 표 | [render_unit_status_docs.py](../tools/render_unit_status_docs.py) | 실행 범위 JSON |
| 확장 카탈로그 집계 | [build_character_runtime_coverage.py](../tools/build_character_runtime_coverage.py) | 현재 카탈로그·저장된 실행 범위·원본/도감 JSON |
| 돈 도박 상점 | [extract_original_money_gambling_shop.py](../tools/extract_original_money_gambling_shop.py) | 원본 W3X |

**연구·강화소 단계별 수치:** 현재 저장소에는 2.323[C] W3X 원본과 각 단계의 비용·효과를 내보낸 전용 증거표/추출기가 없습니다. [현재 확인된 4개 연구·18개 강화 목록과 원본 추출 순서](../docs/전체-아이템-시설-목록.md)를 따릅니다. 원본 `Units/UpgradeData.slk`·문자열·있다면 `war3map.w3q` 수정 정보·JASS 실행 사건을 함께 대조해야 하며, **테스트 fixture·툴팁만으로 가격/전투 효과를 승인하지 않습니다.**

도구별 입력과 옵션은 소스를 확인하고, 실행 허가가 있으면 `--help`를 사용합니다. 원본·외부 도구가 없는 환경에서 가짜 입력이나 임시 완료 플래그를 만들지 않습니다. 도박 상점 재고 모델과 R009 해금·유닛 도박의 승인 범위도 구분합니다.

실제 TSV를 준비한 뒤 사용하는 유닛 표 생성 예:

```sh
python3 tools/build_unit_runtime_status.py --runtime-tsv /path/to/unit-support.tsv
python3 tools/render_unit_status_docs.py
python3 tools/render_unit_status_docs.py --verify
```

표 생성기는 기존 JSON을 렌더링하며 지원 범위나 원작 승인 플래그를 새로 판정하지 않습니다.

확장 집계는 `python3 tools/build_character_runtime_coverage.py --verify`로 파일 변경 없이 확인합니다. 불일치 시 옵션 없이 재생성하고 diff를 검토합니다. TW-01/h0BR 보상 상자는 비전투 지원 객체이며 집계 증가가 평타·스킬·획득 권한을 승인하지 않습니다. C# 실행 지원 변경은 집계 전에 실제 지원 TSV와 `unit-runtime-status.json` 갱신이 필요합니다.

## 저장소에 넣지 않는 입력

원본 W3X/MPQ·임시 추출 파일·개인용 도감 이미지·APK/서명 키·계정 정보는 포함하지 않습니다. 원본 입력이 필요한 작업에만 접근을 요청합니다. [개인용 이미지](../docs/PRIVATE_PORTRAIT_BUILD.md) · [검사와 APK](../docs/unity-editmode-test.md).
