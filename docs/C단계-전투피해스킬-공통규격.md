# C단계 — 전투·스킬 공통 규격 (S2 2.323[C], 악몽 이세계모드)

> **기록 해석 기준:** 이 문서는 단계별 조사·구현 기록입니다. 본문의 미완료·미실행·다음 작업과 정책은 해당 작성 시점 기준이며, 현재 운영·전체 현황은 [최신 인수인계](새채팅-인수인계.md)의 ‘현재 작업 기준’을 우선합니다. 특히 과거 테스트 보류, 사거리 유지, 프로젝트/원본 수치 미확보 설명을 현재 상태로 적용하지 않습니다. 원본 규칙의 근거와 미검증 한계는 계속 구분합니다.

## 이번 단계의 목표

캐릭터별 코드 수백 개를 하드코딩하기 전, 물리·마법 피해 및 스킬의 발동·범위·효과를 독립적인 데이터로 정의한다. **게임의 실제 원본 전투 공식과 각 캐릭터의 수치는 아직 확정되지 않았다.**

## 출처 및 원본 검증

- 2.323 도감 물리 가이드: https://ordr.kr/guides/physical
- 2.323 도감 마법 가이드: https://ordr.kr/guides/magic
- 2.323 도감 방깎 가이드: https://ordr.kr/guides/armor-reduction
- 2.323 도감 스턴 가이드: https://ordr.kr/guides/stun
- 도감: 마법 피해는 마법방어력의 영향을 받는다. 물리 조합은 방깎, 마법 조합은 마방깎·증폭에 의존한다.
- 도감: 폭발형 피해 증폭은 마법 피해 증폭과 **별도 집계**된다. 폭발형은 마법 피해와 동일한 단일 타입으로 합치지 않는다.
- 도감: 방깎 오라와 스킬 발동형 방깎, 아머브레이크 누적 효과가 구분된다.
- 도감: 평타 발동과 광역 스턴 등은 유닛마다 다르며 특성강화 시 스킬이 변경될 수 있다.
- 원본 맵: `ORDR_S2_2.323[C](2).w3x`, SHA-256 `0794996c9bc73eaa3020f20ebc45fb37cfa64a6e98ffe8aa0dfa3b356e77bc5c`
- 원본 JASS: `extracted_war3map.j`, SHA-256 `96201e80edec1b2764f85818760306c6b2f0becd390ba4d63f41c86f3dfaff6d`
  - 약 3591~3608행: 맵 전용 난수 생성기 및 실수 구간 난수 사용
  - 8224~8230행: 스크립트 피해 공통 함수 `Bcb`가 `UnitDamageTarget` 호출
  - 10943행 외 다수: `ATTACK_TYPE_CHAOS` + `DAMAGE_TYPE_UNIVERSAL` 조합
  - 15000행대 외 다수: `ATTACK_TYPE_NORMAL` + `DAMAGE_TYPE_UNIVERSAL` 조합
  - **주의:** 워크래프트 원시 공격/피해 타입이 단순히 '물딜/마딜'과 1:1로 대응된다고 단정할 수 없다.

## 구현된 타입

`Assets/Scripts/Combat/CombatDataSchema.cs`
- `DamageSchool`: Physical / Magical / ScriptDefined
- `DamageTraits`: Explosive (물리·마법 피해 계열과 독립적인 플래그)
- `SkillTriggerKind`: OnAttack / OnHit / Periodic / VerifiedCondition / PassiveAura
- `SkillTargetArea`: Single / CircleAroundTarget / EntireEligibleZone
- `SkillEffectKind`: Damage, ArmorReduction, MagicResistanceReduction, ArmorBreakStack, MovementSlowPercent, Stun, AttackSpeedBuff, AttackPowerBuff, MagicDamageAmplification, ExplosiveDamageAmplification, AllDamageAmplification, HealthRegeneration
- `CombatSkillSpec`: 원본 rawcode, 발동 확률(백만분율), 쿨타임, 주기, 범위, 효과와 근거
- `UnitCombatSpec`: 원본 유닛 rawcode, 평타 스펙, 개별 스킬 목록과 근거
- 0~100만 범위 외 발동 확률, 음수 피해/쿨타임, 범위 반경 오류, 중복 스킬 rawcode를 거부한다.
- `SkillEffectSpec.Magnitude`는 *원본 명시 단위 그대로* 보관한다. 효과마다 임의의 퍼센트 공식이나 방어력 공식으로 변환하지 않는다.

`Assets/Scripts/Combat/CombatSkillTriggerEngine.cs`
- 같은 유닛 rawcode라도 **유닛 인스턴스별** 발동·쿨타임을 분리
- 이벤트 순번으로 같은 타격의 중복 확률 추첨을 방지
- 성공 시 쿨타임, 주기 스킬의 첫 주기 대기 및 재대기
- 발동 확률 난수 공급원 `ICombatProcRandom` 외부 주입
- 원본 JASS와 동일한 RNG임은 미검증이다.

`Assets/Scripts/Combat/CombatAttackPipeline.cs`
- 기존 `CombatScheduler`, `CombatResolution`, `CombatSkillTriggerEngine` 연결
- 공격 대상 후보 중 실제 세션에 살아 있는 적을 선별
- 공격력 표시값과 피해 계열/폭발 플래그를 `IOriginalDamageResolver`에 전달
- 주입된 **최종 피해량**만 체력에 반영하고, 처치/쿨타임/OnAttack·OnHit 발동 상태를 갱신
- 계산기가 주입되지 않으면 인스턴스를 만들 수 없음: 원작 피해 공식을 임의로 대신하지 않는다.

`Assets/Scripts/Combat/CombatStatusEngine.cs`
- 마방깎/암브/마법·폭발 증폭/공속·공격력 버프/체력 재생 등의 효과 분류 추가
- 효과를 기록하는 **데이터 계층**이며, 원작 중첩·지속 갱신·최종 저항 계산은 아직 미구현

## 반드시 남겨둔 작업

1. **원본 JASS 대조**: 개별 유닛의 피해 공식, 공격속도, 적 방어 타입과 감소 적용 순서 확인
2. **피해 계산기 구현**: 원작의 물리·마법·폭발 조건, 특성강화 및 반올림 규칙 검증 후 `IOriginalDamageResolver` 구현
3. **실제 스킬 효과 실행**: 발동한 스킬의 광역/단일 대상 판정, 추가 피해·디버프·버프 적용과 재시전 처리
4. **캐릭터 데이터 등록**: 유닛별 rawcode/스킬/수치/특성강화 전후 데이터 수집과 연결
5. **런타임 통합**: 시계 진행/유닛 소환·판매/웨이브/캐릭터 조합/UI/애니메이션
6. **Unity 검사 및 APK**: 저장소는 아직 Unity 프로젝트 자체가 완결되지 않아 컴파일·실기 테스트를 수행하지 않았다.

## 작성한 NUnit 테스트

- `CombatDataAndSkillTriggerTests.cs`: 10개 (계열/폭발 분리, 확률 경계, 개별 쿨타임, 이벤트 중복, 주기형/오라 구분)
- `CombatAttackPipelineTests.cs`: 8개 (대상, 공격, 처치, 발동, 쿨타임, 잘못된 최종 피해, 소멸 타깃 제외)

**현재 검증**: GitHub 파일 등록·재조회, 소스의 정적 검사. Unity 컴파일 및 NUnit 실제 실행은 **미수행**, 따라서 테스트 통과라 주장하지 않는다.

## 정책

- 도감과 원본 근거가 확보되지 않은 상태에서 **폭발형=마법, 고정 피해=순수 피해, 확률/쿨타임 일정** 같은 가정을 입력하지 않는다.
- 위 NUnit 스펙에 사용된 원시 공격력과 스킬 수치는 **테스트용 가상 값**이다.
- GitHub `main` 브랜치만 사용하며 새 Actions 워크플로를 추가하지 않는다.
