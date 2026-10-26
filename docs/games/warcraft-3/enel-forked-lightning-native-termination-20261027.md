# 에넬 (특별함) 갈래 번개 — 원본 e0KD 시전 종료 처리

기준: ORDR S2 2.323[C] / 악몽·이세계. 원본 W3X SHA-256
`0794996c9bc73eaa3020f20ebc45fb37cfa64a6e98ffe8aa0dfa3b356e77bc5c`.

## 원작에서 확인한 개별 스킬

- **에넬 특별함 S-18 / h00Q**
- 원본 `war3map.j`의 `Bnr`는 `EVENT_PLAYER_UNIT_ATTACKED`에서
  `BFi(1,100)<=10`으로 **공격 선언 시 10%** 분기.
- 분기에 성공하면 원본 더미 `e0KD`를 소환하고 그 안의 `A086`
  (워크래프트 `ANfl`, forkedlightning)을 원래 공격 대상에게 지시.
- 원본 능력 객체에는 **최대 대상 4명**, 시전 사거리 1750, 영역 125가 기록돼 있다.
  `DataA=1500`은 *원본 객체 능력값*이지 방어·면역·버프 계산 후
  최종 HP 피해라고 단정할 수 없다.
- 별도 `Original323ScriptAttackEffectCoordinator`와
  `Original323VerifiedNativeMagicImpactAdapter`는 이 10% 분기,
  대상 선정·마법 면역 및 검증된 최종 HP를 처리하는 코드이다.

## 이번에 새로 구현한, 실제 시전에 필요한 단계

공격 선언 후 `e0KD`가 시전됐으나 최종 HP 타격 없이 **원본 주문이
실패하거나 더미가 사라지는 경우**를 안전하게 처리하는 기능을 추가했다.

1. `IOriginal323DummySpellTerminationWitness`가 *실제로 관측한*
   Warcraft 더미 유닛의 주문 거절, 소멸 또는 무피해 시전 완료를
   `Original323ObservedDummyCastEnd`로 제공한다.
2. 원본 게임 지문, `e0KD` 신원, `A086` 능력, 고유 시전 ID,
   종료 시각 및 실제 종료 이유가 동일할 때만 기존 보류 시전을 종료한다.
3. **첫 공격 대상이 죽었다는 사실, 1초 경과, 임의 제한 시간**만으로
   시전을 종료하지 않는다. 이미 날아간 원본 번개가 다른 대상에 닿을 수 있다.
4. 실제 원본 면역으로 **대상은 선정됐지만 HP 피해가 0**인 경우에는
   그 선택을 원본 4인 제한에 계산한 다음 무피해 종료를 허용한다.
5. 이미 피해가 발생했다면 `OriginalCompletedWithoutDamage`나
   `OriginalOrderRejected`라고 거짓 기록할 수 없다. 대신 실제 더미
   소멸이 확인된 경우 이전 피해를 그대로 유지하고 종료 가능.
6. 종료된 시전은 다시 열 수 없으며 같은 시전 ID로 지연 피해를
   이중 처리할 수 없다.
7. 원본 시전 종료는 추가 공격·마법 피해·기절·`BFi` 난수를
   발생시키지 않으며, 원본 주문 실패를 성공 효과로 가장하지 않는다.

### Unity 런타임에서 호출

기존 `OrdrCharacterCombatController.ConfigureOriginal323DeferredDummySpellWorld`
에 원본 종료 증인(witness)을 전달하는 3인자 오버로드를 추가했다.

실제 `A04Q`와 마법저항을 검증하는
`ConfigureOriginal323VerifiedNativeMagicDummyWorld`에도
5인자 오버로드가 생겼다. 기존의 2/3/4인자 API는 그대로 유지한다.

원본 `e0KD` 유닛 주문 종료가 관측되면
`TryConfirmObservedOriginalDummyCastEnd(sourceCastId)`를 호출한다.
최종 HP 타격이 관측되면 기존의
`TryApplyObservedOriginalDummySpellImpact(sourceCastId, out damage)`를 사용한다.

## 회귀 시나리오

`Original323DeferredDummySpellTests`에 추가:

- 에넬 10% 발동으로 미해결 `A086` 시전이 생겼지만, HP 타격 전에
  관측된 e0KD 소멸로 정리되고 재타격은 차단되는지
- 원본 마법 면역 대상이 **HP를 잃지 않으면서도 네이티브 선정 대상에
  남는지**, 해당 시전 종료에 추가 원본 난수가 발생하지 않는지
- 다른 능력 ID/더미 유닛 ID/게임 지문/시각/가상 근거를 보내면
  시전 종료가 거절되는지
- 이미 1회 번개 피해를 준 시전은 잘못된 '무피해 완료'를 거절하고
  이후 **실제로 관측된 더미 소멸**로 종료되는지
- 별도 원본 종료 증인 없이 멋대로 진행한 모바일 시간만으로
  시전이 사라지지 않는지

테스트 fixture의 몬스터 HP, 타격 피해 40/77, 원본 관측
문구는 **TEST_ONLY 테스트 환경**이다. 실제 소스 검증 포트로
자동 전환하는 코드가 아니므로 이 값은 출시 버전의 피해가 아니다.

## 남은 미완성

- 게임 월드에서 실시간 Warcraft `e0KD` 네이티브 소멸·완료를
  확인하는 **실전 종료 증인 구현**이 아직 없다.
- `ANfl`의 워크래프트 원래 대상선정 순서·마법면역·저항·최종 HP
  산식과 실제 타격 시각은 기존의 독립 네이티브 검증 포트가 필요하다.
- Unity CI 및 단말 플레이 통과 확인은 별도 필요하다.
- 원본 맵/유닛 공격력/스킬 확률/쿨타임은 변경하지 않았으며 APK 미생성.
