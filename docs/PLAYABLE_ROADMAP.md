# 다음 개발 작업

정리일: 2026-10-28 · 게임 코드 기준 `3cc8997` · 현재 실행 순서를 관리하는 유일한 작업목록입니다.

현재 기능 범위는 [START_HERE](START_HERE.md), 재사용할 구현은 [CODE_MAP](CODE_MAP.md)을 먼저 확인합니다. 과거 P0~P5 및 A~K 항목의 미체크 표시는 당시 전체 완성 기준이므로 이미 연결된 기능을 다시 만들라는 지시가 아닙니다. 기존 ID와 근거는 [P0~P5 보존본](archive/PLAYABLE_ROADMAP-20261028.md), [133개 상세 항목 보존본](archive/개발-작업목록-20261028.md)에 남겼습니다.

## 이미 연결된 범위

| 기능 묶음 | 현재 상태 | 후속 작업 |
|---|---|---|
| 준비·위습·공유 로스터 | 기본 APK 연결 | 해적선/상위 위습·기기 확인 |
| 116개 실제 조합·지원 유닛 판매 | 연결 | 제외된 제작·추가 생성·특수 조건 |
| 10R/20R 출현·이동·평타·보상·승패 | 개발용 모델 연결 | 전체 원작 동작·65R·실기 확인 |
| 치명타/기절 26개·로빈·일부 오라 | 개발용 모델 및 별도 승인 경로 | 남은 복합 효과·면역·중첩·시점 |
| 갱 뱃지·버기 마기탄 분해 | 실제 교체·취소·롤백 연결 | UI 문구 정정·기기 확인 |
| 개인 미션·일시정지·진행 기록 | 일부/공통 경로 연결 | 전체 사건·전체 저장·장시간 검증 |

‘연결’은 전체 원작 검증 완료가 아닙니다. 소스 모델, 로컬 검사, Unity/기기 검증을 별도 기록합니다.

## N0. 먼저 고칠 확인된 표기 오류

상태: **미수정**. 실제 생성 대상은 정확하지만 안내 문자열이 틀립니다.

- 파일: [Original323UnitDecompositionService.cs](../Assets/Scripts/Mobile/Original323UnitDecompositionService.cs)의 `Description`, `Complete`.
- 갱 뱃지 결과의 마지막 항목은 `C-08 / h009 / 총병`입니다. 현재 문자열 두 곳의 ‘우솝’을 ‘총병’으로 고칩니다. 우솝은 `C-04 / h007`이며 실제 생성 ID를 우솝으로 바꾸면 안 됩니다.
- 원본 자료: [분해 결과](../data/original-2323C-unit-decomposition.json), [객체 매핑](../data/original-2323C-map-base-stats.json). 기존 [분해 검사](../Assets/Tests/EditMode/Original323UnitDecompositionTests.cs)는 실제 `C-08` 생성을 검사합니다.
- 완료 기준: 결과 ID·수량·좌표·시전 시간은 유지하고, 미리보기와 완료 문구가 보유 목록의 실제 유닛 이름과 일치합니다. 이번 문서 정리에서는 게임 코드를 수정하지 않았습니다.

## N1. 안흔함의 남은 전투 효과

최우선 기능 묶음입니다. 로빈·브룩·육식 크리티컬을 새로 만들지 않습니다.

| 유닛 | 기본 APK 연습의 현재 범위 | 다음 연결 |
|---|---|---|
| 로빈 U-01 | 같은 전장 최대 3명 모델 | 원본 추가 대상 순서·미사일/시점 대조 |
| 브룩 U-03 | 일반 몬스터 기절 | 보스 저항·현재 타깃 면역 검증 |
| 블루노 U-04 / 후쿠로 U-13 | 육식 크리티컬 | 람각의 공격 선언·직선·실제 주문 피해 |
| 에이스 U-05 | 객체 평타 | 추가 물리 피해 200 전체 경로 |
| 타시기 U-09 | 객체 평타 | 추가 물리 피해 200 + 기절 전체 경로 |
| 하찌 U-12 | 객체 평타 | 추가 물리 피해 150 + 기절 전체 경로 |

기존 파일:

- [Original323UncommonAbilityCatalog.cs](../Assets/Scripts/Combat/Original323UncommonAbilityCatalog.cs): 소유·확률·배율/추가 피해/기절 수치.
- [Original323UncommonBonusBashWorld.cs](../Assets/Scripts/Combat/Original323UncommonBonusBashWorld.cs): 추가 피해/기절 실행 계약. 현재 인터페이스를 실제 피해·타깃 공급자와 연결하는 작업이 남았습니다.
- [Original323UncommonRankyakuWorld.cs](../Assets/Scripts/Combat/Original323UncommonRankyakuWorld.cs), [Original323ScriptAttackEffectCoordinator.cs](../Assets/Scripts/Combat/Original323ScriptAttackEffectCoordinator.cs), [Original323NativeDummySpellWorld.cs](../Assets/Scripts/Combat/Original323NativeDummySpellWorld.cs): 람각·공격 선언·더미 주문 기반.
- [원본 공격 선언 자료](../data/original-2323C-attacked-dummy-spells.json), [더미 능력 수치](../data/original-2323C-dummy-native-object-abilities.json), `SourceModelUnitEffects`와 `OrdrCharacterCombatController`의 구성 지점.

주의: 블루노/후쿠로의 네이티브 치명타 15%와 JASS 람각 10%는 서로 다른 사건·난수 경로입니다. 합친 확률 한 번이나 명중 후 이벤트로 대체하지 않습니다. ‘추가 200’은 최종 HP 200이 아닙니다. 네이티브 공격 타입·방어/면역·시점·정수화를 추측하지 않습니다.

완료 기준: 실제 소환한 해당 유닛이 필요한 사건에서 한 번만 발동하고, 정확한 대상·피해/상태·기절 종료·처치/골드로 이어집니다. 원본 근거 없는 공급자를 가상 승인으로 채우지 않습니다. 필요한 근거가 없으면 막힌 필드/함수와 확보할 입력을 기록하고 이 묶음 안의 독립 구현을 계속합니다. 관련 기존 전투·거래 회귀를 묶어 검사하고 전체 유닛 현황을 갱신합니다.

## N2. 특별함·희귀함 및 상위 유닛 스킬

- 입력 순서: [유닛 현황 JSON](../data/unit-runtime-status.json) → 객체 소유 능력 → 해당 JASS 등록/실행 함수 → 도감 항목 대조.
- 기존 [SpecialRareNativeSequenceWorld](../Assets/Scripts/Combat/Original323SpecialRareNativeSequenceWorld.cs), [SpecialRareObservedSkills](../Assets/Scripts/Combat/CharacterCombatRuntime.SpecialRareObservedSkills.cs), 네이티브 패시브·오라·방어 링크를 재사용합니다. 이 코드가 있다고 실제 모든 관측/타격 공급자가 연결된 것은 아닙니다.
- 원본 연속 시전·딜레이·소환·변신·능력 레벨·연구·자원 효과를 유닛별로 한 묶음씩 완성합니다. 554개 툴팁을 그대로 일반 피해 스킬로 변환하지 않습니다.
- E-02의 니카 재료 경로와 X-05의 평타 없는 지원 기능은 별도로 추적합니다. [니카 동일성 근거](니카-초월별-영원동일성-20261023.md), [모바일 확정 정책](특별함-모바일-확정정책-20261023.md)을 확인합니다.
- 완료 기준: 한 유닛의 연결/보류 효과·원본 함수·실행 검사·기기 확인 여부를 구분하고 무료 소환·임의 재화·중복 발동 없이 현재 세션에서 동작합니다.

## N3. 남은 제작·위습·수동 경제

- [Original323CommonFusionPort](../Assets/Scripts/Mobile/Original323CommonFusionPort.cs)의 116개 검수 목록과 기존 `OriginalJassFusionPlanner`/거래 코드를 먼저 확인합니다.
- 보류된 특별함 A19Z/A1A1, 희귀함 A1A0은 네이티브 AOsf 생성·시전 전체 동작을 확인해야 합니다. 전설 8개는 CH8 추가 생성 처리나 미매핑 변형 재료가 남아 있습니다. 기존 [전설 감사 자료](../data/original-2323C-legendary-growth.json)의 제외 목록을 사용합니다.
- [Original323AutomaticUncommonWisps](../Assets/Scripts/Combat/Original323AutomaticUncommonWisps.cs)는 이름과 달리 특별함도 처리합니다. 희귀함·전설 풀과 추가 능력/생성 동작은 아직 연결하지 않습니다.
- 흔함 소환의 해적선 `h060` 보류 결과를 재추첨하거나 임의 흔함으로 바꾸지 않습니다. 상점·도박은 기존 공유 지갑·수동 행동 서비스에 실제 결과를 연결합니다.
- 완료 기준: 정확한 보유 인스턴스/조건·비용·결과 전체가 한 번만 반영되고, 실패·재시도·오래된 선택에서 상태가 보존됩니다. 미리보기 개수로 실제 제작/지급 수를 늘리지 않습니다.

## N4. 65라운드·스토리·연구·항법

- 65R 출현 원본 자료와 `CombatWaveDirector`·기존 몬스터 관측/승인 경로를 재사용합니다. 20R 연습을 65R로 늘리기만 해서는 원작 완성이 아닙니다.
- 최종 HP·방어·면역·보스 저항과 조건부 광폭화, 수동 스토리 파견·크립 출전·보상, 연구·도움소·항법의 현재 세션 연결을 구현합니다.
- 완료 기준: 해당 기능의 실제 조건·효과·대상·비용·복귀/종료와 미션 사건이 연결되고 다른 전장으로 공격/보상이 새지 않습니다. 조건을 모르면 임의 타이머나 빈 전장을 원본 사건으로 만들지 않습니다.

## N5. 전체 저장·기기 플레이·성능

- [기존 미션/공유 경제 컴포넌트](CODE_MAP.md)를 활용하되 전체 게임 저장은 새로 통합해야 합니다. 유닛·라운드·출현 중인 적·논리 시간·공격/시전·상태효과·난수·거래/보상 장부를 함께 복원합니다.
- 첫 기기 확인은 준비 → 소환 → 실제 조합 → 공격/처치 → 골드/라운드 → 분해·취소 → 보스 → 종료 순서로 진행합니다. 일시정지/앱 전환과 재시작을 포함합니다.
- 성능은 같은 편대·라운드·입력으로 먼저 측정합니다. [코드 구조의 최적화 주의점](CODE_MAP.md)을 따르고 난수 순서·공격 시점·처치·롤백을 바꾸는 최적화를 하지 않습니다.
- 완료 기준: 저장/복원 전후 상태 일치, 지원 기기의 설치·핵심 조작·장시간 동작 근거, 성능 개선 전후 측정값을 기록합니다. 테스트 통과를 실제 플레이 완료로 대신하지 않습니다.

## 작업을 끝낼 때 갱신할 것

1. 코드/자료에서 해당 기능이 연결됐는지 확인하고 기존 검사와 관련 사례를 묶어 실행합니다.
2. 이 목록의 해당 묶음 상태와 막힌 근거, [현재 상태](START_HERE.md), 유닛 변경 시 현황 JSON·[유닛별 표](캐릭터별-구현목록.md)를 갱신합니다.
3. 원본 수치/규칙을 바꿨다면 추출 근거와 변경 이유를 남깁니다. 미확인 값을 0/없음/승인됨으로 덮지 않습니다.
4. main에 반영하고 기존 Actions의 시작 상태만 확인한 뒤 종료합니다. CI 결과는 별도 요청 때만 확인합니다. 문서 전용 정리는 현재 APK를 취소하거나 중복 제작하지 않습니다.
