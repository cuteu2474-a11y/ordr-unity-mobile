# 특별함·희귀함 직접 공격형 스킬: 원본 시전→시간차 타격

원랜디 S2 2.323[C] · 2026-10-26

## 이번 개발 범위

기존 `CharacterCombatRuntime.SpecialRareObservedSkills.cs`는 한 번 관측된
원본 스킬 사건(최종 HP 피해와 기절)을 한 번의 타격 묶음으로 적용할 수 있었다.
이번에 추가한 `Original323SpecialRareNativeSequenceWorld`는 **시전과
시전 이후 여러 시각에 발생하는 각 네이티브 충돌·주기 타격을 분리해**
이미 만들어진 실제 `CombatResolution` 피해·기절 경로로 보낸다.

- 전투 시뮬레이션의 현재 시각에 원본 Warcraft/JASS **실제 시전**
  `Original323ObservedSpecialRareNativeCast`가 독립 확인돼야 시전 등록 가능.
- 원본 W3X SHA-256, 원본 유닛 4자리 ID, 유닛 등급(특별함/희귀함),
  원본 능력 ID 소유 여부, **현재 살아 있는 시전자**를 검증한다.
- 해당 시전의 `ObservedNativeImpactCount`는 원본 소스가 확인한 **시전당
  충돌 횟수**이며 모바일에서 추측한 광역 수, 스킬 주기나 발동 확률이 아니다.
- 각 타격은 원본 관측 순번 1..N, 현재 전투 시각, 같은 원본 시전자·능력,
  독립적인 원본 시전 ID를 검증한 후, **실제 적 HP와 기절**에 반영한다.
  한 대상이 나중 시점에 다시 맞는 것도 구현할 수 있다.
- 각 타격의 원본 `SourceEventId`는 런타임 중복 검사로 보호하고,
  실패한 충돌은 타격 순번을 소비하지 않는다.
- 타격을 검사하는 검증 공급자와 실제 HP 계산기가 서로 **다른 타격 사건을
  가져오는 TOCTOU 오류**가 없도록 같은 원본 영수증 객체인지 추가 검증.
- 원본 소유 관계만 등록되고 실제 시전·충돌 증거가 없으면 **아무 스킬도
  자동 발동하지 않는다**. 공격력·확률·쿨타임·대상·원본 맵을 변경하지 않는다.

## 게임 연결

`OrdrCharacterCombatController`에서:

1. `ConfigureVerifiedOriginalSpecialRareNativeImpacts(actualImpactWitness)`
2. `ConfigureVerifiedOriginalSpecialRareSequences(actualCastSequenceWitness)`
3. 전투 런타임 생성
4. 원본 시전 확인 시 `SpecialRareNativeSequence.TryRegisterCast(sourceCastId)`
5. 타격 관측 시 `SpecialRareNativeSequence.TryResolveNextImpact(sourceCastId)`

이는 Unity 캐릭터/적 `CharacterCombatRuntime`과 연결되는 **실제 코드 경로**다.
다만 현재 원본 워크래프트의 실제 시전·충돌 데이터 공급자는 미완성이다.
따라서 아직 개별 유닛이 원작과 동일한 발동 주기·효과로
자동 싸운다고 단정할 수 없다.

## 캐릭터별 회귀 테스트

`Original323SpecialRareNativeSequenceTests` 9개:
- **에넬 (특별함, h00Q / A084):** 동일 적을 시간차로 반복 타격,
  서로 다른 적 2개에게도 각각의 최종 HP 적용, 완료 후 중복 차단
- **도플라밍고 (희귀함, h01L / A057):** 첫 타격의 기절 유지와
  후속 피해, 자연 만료 테스트
- 원본 주문 순번 뒤집힘, 다른 캐릭터/능력으로 시전 위조,
  기존 더미 주문(A086)과의 이중 적용, 타깃 전장 위반,
  검증 전과 실제 적용 시 영수증 바꿔치기, 시전자 소멸,
  같은 원본 타격 ID 반복을 차단

**중요:** 시험의 '에넬이 세 차례 타격', '도플라밍고 후속 기절',
가상 피해 13·17·23 등은 실제 원랜디 원본 캐릭터 능력을 단정하는
값이 아닌 **TEST_ONLY 테스트 영수증**이다. 원본 JASS/W3A의
발동 조건·실제 마법 피해·시전 후속 효과가 검증되기 전까지
이 가상 피해·기절 수치는 게임에 들어가지 않는다.

## 남은 핵심 문제

- 특별함 33종·희귀함 42종의 실제 능력별 JASS trigger와
  원본 W3A 효과, 주문 발동 확률·타깃·최종 피해를 전수 추출해야 한다.
- 원본 충돌 영수증 생성기를 실전 월드에 실제 등록해야 한다.
- 시전자 사망 후 잔존하는 원본 투사체는 현재 런타임이
  살아 있는 시전자 신원을 요구하므로 보수적으로 보류한다.
  해당 상황의 원본 투사체 소유·기여 규칙 검증과 별도 연결이 필요하다.
- Unity EditMode 시험 **소스는 추가했지만 테스트 실행/CI 통과 확인은 별도**다.
  iOS/Android APK는 제작하지 않았다.
