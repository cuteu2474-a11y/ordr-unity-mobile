# 스토리 건물 14종 — 원본 객체 기반 개발 경로

스토리는 이동 몬스터가 아닌 **고정 건물 공격 목표**로 취급한다.
원본 `data/original-2323C-monster-object-stats.json`의 story-labeled 14개 객체를
`Original323StoryBuildingCatalog`에서 엄격하게 검증한다.
원본 rawcode 순서와 실제 스토리 순서는 다르므로 stage로 명시적으로 매핑한다.

`Original323StoryBuildingPracticeWorld`는 명시적인 연습용 호출로
기존 `CharacterCombatRuntime`에 `CharacterEnemyKind.Story` 적을 생성하고,
건물 파괴/제거를 구분한다. 스토리 건물은 경로 이동을 수행하지 않는다.
원본 기본 체력·방어력·fort 방어 유형만 적용하며 실제 악몽+이세계 최종
스탯, 스턴 저항 A08Q 활성 상태, 건물 대상 스킬 판정은 아직 검증되지 않았다.

**미완료:** 실제 UI에서 수동 파견, 파견·복귀 조건, 스토리 해금 순서,
실전 스폰 위치·시점, 보상/중복 보상 방지, 실전 건물 타깃 필터와
모바일 장시간 플레이 검증. 연습용 건물 파괴는 보상을 지급하지 않는다.
기존 스토리 파견 포트와 공통 전투 엔진을 재사용하고, 원본 확인 없이
새 지갑·피해 공식·보상 지급기를 만들지 않는다.
