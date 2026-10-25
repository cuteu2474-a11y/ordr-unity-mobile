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


## 블루노·후쿠로 A07I: 육식과 람각의 서로 다른 타격 파이프라인

- 원본 유닛 rawcode는 블루노 `h00M`, 후쿠로 `h00A`이며 모두 `A07I`를 보유한다. 기존 추출 자료에 기록된 육식 크리티컬은 확률 **15%**, 원시 평타 **1.5배**다. 람각은 독립 확률 **10%**, 직선 마법 원본 표시값 **150**이다.
- **육식 크리티컬**: `Original323UncommonCriticalHitSource`는 원본 공격 주사위가 구해진 뒤, 방어 타입/방어력/HP 변환이 일어나기 *전에* 크리티컬을 적용한다. 기존의 공격 적중 이후 `OnHit` 표식에 크리티컬을 넣는 경로는 차단한다. 결과 `Ordr323VerifiedDamageHit.RawDamage`에 1.5배가 반영되고, 원래 공격 타입·저항 적용 범위·증거가 그대로 전달된다.
- 크리티컬 대상/계산 순서를 입증하는 `IOriginal323UncommonCriticalPrehitRules` 없이는 크리티컬 난수를 소모하지 않는다. `OrdrCharacterCombatController.ConfigureVerifiedOriginalUncommonCritical(verifiedRules)`를 **원본 피해 런타임 생성 전** 연결해야 한다. 별도의 `A07I:rankyaku` 주사위를 크리티컬 주사위로 대체해서는 안 된다.
- **람각**: `Original323UncommonRankyakuWorld`는 `IOriginal323RankyakuVerifiedRules`가 인정한 원본 직선 교차 판정, 대상 구분, 마법 저항/방어력, 최종 HP 변환을 통과한 결과 `Original323RankyakuLineHit`만 소비한다. 150을 사거리·반경·확정 HP 피해량으로 임의 적용하지 않는다.
- 다중 대상에 대해서는 전투 엔진 `TryCommitOriginalUncommonLine`이 대상 중복, 다른 전장, 사망, 근거 누락을 **일괄 사전 검증**하고, 성공할 때만 각 대상의 HP 감소와 처치/기여 이벤트를 동일 `CombatResolution`에 기록한다. 대상 하나라도 미검증이면 전부 적용 보류한다.
- 독립적인 다른 안흔함 효과를 함께 쓰려면 `Original323UncommonEffectRouter`로 명시적으로 묶을 수 있다. 타시기·하찌·에이스와 람각을 동시에 연결하는 `ConfigureVerifiedOriginalUncommonEffectBundle`도 추가되었다. 이 설정은 육식 크리티컬을 자동 승인하지 않는다.
- Unit-test의 가상 영웅/갑옷/경로/최종 HP 계산기와 항상 0을 반환하는 난수는 **실게임의 근거가 아니다**. 현재 기본 런타임에는 실제 몬스터 원본 경로·마법 저항·명중/최종 HP 반올림의 전수 검증이 여전히 부족하므로 스킬 완성 판정은 보류한다.


## 로빈 A07M — 원본 꽃꽃 열매 다중 대상 평타 연결

- 원본 W3X의 로빈 `h00O`은 `A07M` 능력을 보유한다. 능력 목록에 붙은 3명 동시 타격 근거를 `Original323UncommonAbilityCatalog`에서 `U-01 / A07M:multi3 / NativeTargetCount=3 / 100%`로 고정한다.
- `Original323RobinMultiTargetWorld`는 **주 평타 1회 + 별도로 확인된 추가 적 최대 2명**의 형태로 연결된다. 주 평타는 기존 `CombatAttackPipeline`만이 처리하므로 동일 표적 중복 가산·중복 쿨타임은 금지한다.
- `IOriginal323RobinVerifiedVolleyRules.CanAttempt`는 주 공격 전에 원본 공격 자격을 읽기 전용으로 증명해야 한다. `TryResolveAdditionalHits`도 대상 순서, 투사체 시점, 개별 주사위·방어력·피해 변환 및 마스크를 **각 타격별 최종 HP와 근거**로 공급해야 한다.
- 룰 공급자가 확인한 추가 후보는 0~2명이며, 없음도 유효하다. 추가 적이 존재하지 않을 때 원본 주 타격을 복제해 피해를 뻥튀기하지 않는다.
- `CharacterCombatRuntime.TryCommitOriginalRobinMultiTarget`는 **모든 추가 대상**의 ID 중복·주 표적과 중복·현재 생존·전장 구분·방어 타입·원본 기본 대상 필터를 먼저 검사한다. 어느 한 항목이라도 실패하면 추가 피해 전체를 보류한다.
- 각 추가 타격은 `CombatResolution.TryApplySkillDamage`를 거쳐 처치 수, 공격 기여·이벤트를 공유한다. 주 표적이 원본 기본 타격에서 죽어도 검증된 다른 적의 두 추가 타격은 살아있는 경우 각각 적용 가능하다.
- 모바일의 무제한 **평타 거리** 설정은 그대로 두되 다른 전장으로 A07M 타격을 확장하지 않는다. 람각(직선 범위), Bash(기절), 크리티컬(타격 전 배율)을 로빈의 별도 대상 타격과 합치지 않는다.
- 선택형 활성화: `OrdrCharacterCombatController.ConfigureVerifiedOriginalRobinMultiTarget(rules)` 또는 기존 브룩/Bash/람각까지 함께 사용할 때 `ConfigureVerifiedOriginalUncommonAllEffectBundle(...,verifiedRobin)`. 검증 룰이 없으면 원본 객체 수치를 그대로 보존하면서 추가 타격은 미승인 상태를 유지한다.

**현재 미검증:** 모바일에선 기본 타격 후 동기적으로 추가 타격을 적용한다. 실제 Warcraft 다중 투사체의 출발·적중 시간차, 두 번째 타격의 별도 공격 주사위 및 한 타격에 죽은 적의 후속 선택 등은 검증된 제공자가 필요하다. Unity EditMode에서 사용하는 각 추가 타격 45·61 HP 등은 테스트용 가상 수치이며 원본 피해량이 아니다. 별도 실기 대조 없이 3인 타격의 완전 동일성을 선언하지 않는다.
