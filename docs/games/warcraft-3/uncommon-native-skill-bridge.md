# ORDR 2.323[C] 안흔함 14종 능력·전투 연결

## 근거

- 원본 W3X SHA-256: `0794996c9bc73eaa3020f20ebc45fb37cfa64a6e98ffe8aa0dfa3b356e77bc5c`
- `Units/AbilityData.slk`: `23e9eb93d805a63cc76bfb617189331aef1e40c0c0ec69860bd7399a3bef2eda`
- `Units/CampaignAbilityStrings.txt`: `1e6d9f5ccd22f67ad0edf5264bce77aa6b944032881b81ab7051f6bebf97b979`
- 맵의 전체 유닛 능력 목록: `data/original-2323C-map-base-stats.json`
- 조합의 UNIT/UPUN 실제 조건: `data/original-jass-fusion-registry.json`

## 고유 전투 능력 (원본 객체·문구 기준)

| 유닛 | 능력 코드 | 유형 및 원본 능력 수치 | 실행 비고 |
|---|---|---|---|
| 로빈 U-01 | A07M | 꽃꽃 열매, 동시 공격 3명 | 다중 타깃 실제 탄도/적격 판정 후 적용 |
| 브룩 U-03 | A07P | 잠의 노래, 10% 기절 0.3초(영웅 0.15초) | 네이티브 Bash 전체 타깃 검증 필요 |
| 블루노 U-04 | A07I | 육식, 15% 1.5배 크리티컬; 람각 10% 직선 마법 150 | **서로 다른 두 확률 트리거**, 추가 직선 피해의 JASS/네이티브 경로 검증 필요 |
| 에이스 U-05 | A07R | 이글이글, 10% 단일 물리 200 | 원본 추가 피해의 공격·방어 계산 순서 확인 필요 |
| 타시기 U-09 | A07Q | 시구레, 20% 단일 물리 200 + 기절 0.7초(영웅 0.35초) | 추가 피해·기절 모두 승인 필요 |
| 하찌 U-12 | A07S | 어인, 20% 단일 물리 150 + 기절 0.1초(영웅 0.05초) | 추가 피해·기절 모두 승인 필요 |
| 후쿠로 U-13 | A07I | 블루노와 같은 육식·람각 코드 | 별도 캐릭터 인스턴스로 확률·쿨타임 관리 |

나머지 안흔함 7종(U-02, U-06, U-07, U-08, U-10, U-11, U-14)은 객체 목록에서 위 고유 전투 능력 코드를 갖고 있지 않다. **이는 전체 숨은 스킬/JASS 기능 부재의 증명이 아니다.**

## UI·원본 조합을 전투와 구분

- `A07Y`: 창고 이동 (전투 패시브 아님)
- `A0JE`: 같은 유닛 모으기 (전투 패시브 아님)
- `A0B8`: 안흔함 판매, 랜덤위습 50%·목재 20% (전투 패시브 아님; 독립 경제 처리 필요)
- `A029`: 좀비 3 + 나미 1 + UPUN `h05K` ⇒ `h010`; 전투 기술이 아닌 CWR 조합
- `Avul`: 워크래프트 객체 능력. 무적/타깃 자격은 전투 시스템에서 별도 대조해야 한다.
- 다른 `A00*`, `A01*`, `A0N*`, `A1A*` 계열은 rawcode *접두어로* 임의 분류하지 않는다. 실제 원본 `CWR` 등록식과 개별 대조한다.

## 런타임 연결 정책

1. `Original323UncommonAbilityCatalog`가 원본 14종의 W3X rawcode 및 능력 순서를 검증한다.
2. `OrdrCharacterCombatController`가 카탈로그를 월드에 자동 연결하며 유닛 탭에서 고유 스킬 증거를 표시한다.
3. 원본 완전효과 실행 포트 `IOriginalUncommonVerifiedProcWorld`가 없으면 **난수·스턴·피해를 발생시키지 않는다.**
4. 포트가 등록될 때만 `CharacterCombatMapping`이 원본 확률을 `CombatSkillTriggerEngine`의 OnHit 트리거로 등록한다. 대상 선검증은 주사위 소모 이전에 수행한다.
5. 입증된 평타가 실제로 적중한 후 자동 발동하며, 효과 전체를 포트에 위임한다. **툴팁만 읽고 임의 최종 피해를 발생시키는 코드는 금지한다.**
6. `ConfigureVerifiedUncommonNativeSkills(port)`는 전투 런타임을 만들기 전에 호출해야 한다. 초기에 포트 미연결로 실행한 경우 게임 중 임의로 활성화할 수 없도록 한다.
7. 테스트의 가상 원본 타격·가상 추가효과는 실기 검증이 아니다. 런타임의 실제 2.323[C] 피해량과 원본 스킬 재현 완료를 뜻하지 않는다.

남은 작업: 확정된 네이티브 능력의 추가 피해 판정/영웅 구분/스턴 면역/멀티타깃 후보 수집/람각 직선 지오메트리/실제 조합·판매 월드 구현 및 W3X 실전 대조.
## A07P / 브룩 잠의 노래: 기절 효과 구현 단계

- `Original323UncommonBashWorld`에서 원본 `AHbh`의 `DataA1=10`, `DataB1=0`, `DataC1=0`, `Dur1=0.3`, `HeroDur1=0.15`, `BuffID1=BPSE`와 대조한 기절을 `CombatStatusEngine`에 적용한다.
- 실제 적의 **Warcraft 영웅 여부·기절 면역 여부·출처**가 별도로 검증되지 않은 경우 스킬 난수조차 사용하지 않는다. 모바일 `Boss` 또는 `Story` 적이라는 사실만으로 영웅으로 분류하지 않는다.
- `OrdrCharacterCombatController.ConfigureVerifiedOriginalBrookBash(verifiedTargetRules)`를 원본 피해 월드 구성 전에 호출해야 활성화된다. 출처 공급자가 없는 기본 상태에서는 미활성화된다.
- 기절 상태에서는 `TryMoveEnemy`가 이동을 거부하고, 공통 타임라인이 `Dur1` 또는 `HeroDur1`만큼 흐르면 해제된다.
- 타시기·하찌의 추가 물리 피해+기절, 에이스의 추가 물리 피해, 블루노·후쿠로의 람각/크리티컬 및 로빈의 3명 동시 공격은 **별도** 효과 실행기를 요구한다. 브룩용 실행기를 이들 능력에 재사용하지 않는다.
- 현재 검증은 합성 원본 평타와 테스트용 적 분류 공급자를 통한 EditMode 경로 테스트다. 완전한 원본 워크래프트 엔진의 타격/면역/모션 판정과 실기 비교를 뜻하지 않는다.


## 에이스·타시기·하찌: 네이티브 AHbh 추가 피해/기절 결합

원본 W3X의 Units/AbilityData.slk에서 직접 확인한 값이다.

| 소유자 | 능력 rawcode | 부모 능력 | 확률 | 원본 추가 피해 필드 | 일반 기절 | 영웅 기절 | SLK 허용 대상 |
|---|---|---|---:|---:|---:|---:|---|
| 에이스 h00K | A07R | AHbh | 10% | 200 | 0 | 0 | air,enemies,ground |
| 타시기 h00E | A07Q | AHbh | 20% | 200 | 0.7초 | 0.35초 | air,enemies,neutral,ground |
| 하찌 h00F | A07S | AHbh | 20% | 150 | 0.1초 | 0.05초 | air,enemies,neutral,ground |

- 해당 3개 rawcode는 원본 war3map.j에서 직접 호출하는 JASS 코드가 검색되지 않았으며, 오브젝트의 AHbh 기반 능력이다. 객체 필드 200/150을 **최종 HP 감소량과 동일시하지 않는다**.
- Original323UncommonBonusBashWorld는 위 세 능력에만 적용되며, 실제 적 영웅/면역 증거(IOriginal323UncommonBashTargetRules)와 추가 물리 피해의 **최종 HP 계산 증거**(IOriginal323UncommonBonusDamageRules)를 모두 요구한다. 하나라도 없으면 해당 스킬은 발동 난수를 소비하지 않는다.
- 대상 정책은 에이스의 neutral 제외를 직접 검증해야 한다. 이름·전장·보스 여부만으로 원본 enemies 대상 여부를 임의 추정하면 안 된다.
- 계산 정책 TryResolveFinalBonus(...)은 공격 전 사전검사와 스킬 발동 직전 재검사에서 사용되므로 무상태·무난수·무부작용이어야 한다. 테스트의 47 HP 가상 완료 계산은 **실게임 값이 아니다**.
- TryCommitOriginalUncommonBonus는 원본 평타로 발생한 스킬 발동 처리 도중에만 CombatResolution.TryApplySkillDamage를 호출할 수 있다. 카운터·처치 참여·스킬 피해 이벤트를 공통 시스템과 공유한다. 타시기·하찌의 기절은 **실제 피해 적용 후에도 살아있는 적**에만 부여한다.
- OrdrCharacterCombatController.ConfigureVerifiedOriginalUncommonBashBundle(targetRules, bonusRules)로 브룩+에이스+타시기+하찌를 함께 활성화한다. Unity 전투 런타임 **초기화 전에** 승인해야 한다.
- 크리티컬(원본 공격 1.5배)과 람각(직선 마법), 로빈 다중 타격은 별도 실행 설계가 필요하다. 이 포트가 해당 스킬을 임의 승인하지 않는다.

**현 시점 제한:** 가상 방어 계산을 실기로 오인하게 만드는 코드가 없다. 3개 AHbh 능력의 기본 확률·피해 필드·기절·전장 공격 시퀀스는 연결했으나, 원본 몬스터의 대상 유형 및 검증된 최종 HP 계산기 주입이 끝나기 전에는 라이브 전투 효과가 차단된다. 현재 OnHit 스킬 루프는 기본 공격 피해 이후 실행되므로, 한 방에 죽는 적에 대한 네이티브 Bash 타이밍과의 완전 동일성은 아직 검증되지 않았다. **실기 파리티는 미완료**.
