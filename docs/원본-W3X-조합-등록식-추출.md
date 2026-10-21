# 원본 W3X 조합 레지스트리 확보와 Unity 연동

기준: 원랜디 S2 2.323[C], 사용자 제공 W3X
브랜치: `main`
추출 지점: `war3map.j`, 함수 `CWR` (원본 60,315~62,418행)
맵 SHA-256: `0794996c9bc73eaa3020f20ebc45fb37cfa64a6e98ffe8aa0dfa3b356e77bc5c`

## 실제 확보된 내용

- `CWR`에 `BGG` 능력 ID별 등록식 **266건** 확인
- 그중 능력 rawcode `TEST` **1건**은 정상 게임 제작 조합으로 취급하지 않음. 비-TEST **265건**은 아직 게임에서 전부 실행 가능한 것으로 판단하지 않음
- `BGH`: 결과/생성 유닛 또는 결과 등록식
- `BGI`: **원본 순서가 중요한 검증/실행 조건 등록식**. `UNIT`, `GOLD`, `WOOD`, `ITEM`, `IFCT`, `SKPT`, `CHCT`, `RMAX`, `PICK`, `RDUN`, `SPEC`, `GREN`, `UPUN`, `UBAN`, `KING` 등이 있음
- `UBAN`은 밴 유닛 검사, `KING`은 항법/상위 유닛 제한 검사가 관련 JASS 코드 `CHg`, `CHh`에 연결되어 있음
- `UNIT` 조건 판정·자원 소비는 `CHu`, `CHx`에서 처리됨. 재료 개수만 맞춘다고 원작 조합이 허가되는 것은 아님
- 원본 태그 `UPUN`을 별도 결과 아이템이라고 오인하지 않도록 **모든 BGI를 조건으로 분류**하는 순서 보존 추출기를 추가함

## 등록한 코드와 데이터

- `data/original-jass-fusion-registry.br.b64`: 266건 조합 결과와 BGI 태그가 들어 있는 압축 조회 데이터. **빠른 조회용**이며 4열로 재분류하는 과정에서 전체 BGI 호출 순서는 보존되지 않음. 이 파일 자체로 게임 제작을 실행하면 안 됨
- `tools/extract_jass_fusions.py`: 원본 `war3map.j`에서 `BGG`, `BGH`, `BGI` 호출 순서 그대로 JSON으로 추출. Python 3 표준 라이브러리만 사용
- `Assets/Scripts/Mobile/OriginalJassFusionCatalog.cs`: 변환된 JSON을 Unity C#에서 읽고 4글자 rawcode/수량/중복 능력을 확인하는 **읽기 전용 카탈로그**
- `Assets/Scripts/Unity/OrdrCharacterCombatController.cs`: 원본 JSON이 제공되면 `OriginalJassFusions`로 조회
- `Assets/Editor/OrdrCharacterDataSync.cs`: 생성된 JSON을 `Assets/Resources/OrdrData/`로 동기화

## Windows에서 원본 조합 JSON을 준비하는 방법

원본 W3X에서 `war3map.j`를 먼저 추출한 뒤 프로젝트 루트에서:

```powershell
python tools/extract_jass_fusions.py --jass "war3map.j" --output "data/original-jass-fusion-registry.json"
```

이후 Unity Editor의 `ORDR > 캐릭터 데이터 271종 · 스킬 200종 동기화` 메뉴를 누르면 원본 추출 데이터가 리소스에 복사된다. 이 작업은 **데이터 적재만** 진행하며 소모·제작 트리거를 실행하지 않는다.

압축 조회 데이터만 복구하고 싶을 때에는 별도로 Python용 Brotli 라이브러리가 필요하지만, **원본 순서를 복구하려면 반드시 `war3map.j`에서 재추출**해야 한다.

## 2차 구현: 원본 이름·등급 대응 81종과 안전한 제작 승인 흐름

원본 JASS 함수 `BjB`에는 `Bi8(유닛 rawcode, 조합능력 rawcode)`와 `Bi9(유닛 rawcode, 표시 이름/검색 별칭)` 등록이 함께 존재한다. 이 이름·등급을 현재 271종 카탈로그와 교차 대조하여 **81종**을 연결했다.

| 카탈로그 분류 | 매칭 수 |
|---|---:|
| 초월함 TR | 30 |
| 히든 H | 22 |
| 불멸 I | 12 |
| 영원 E | 11 |
| 왜곡 D | 6 |
| **합계** | **81** |

- [원본 유닛 ID의 이름 대응표](../data/original-unit-rawcode-label-links.json): 4글자 Warcraft 유닛 rawcode, 프로젝트 내부 ID, JASS 조합 능력 rawcode를 한 레코드로 저장. **번호가 같아서 매핑한 것은 아니다.** 예: JASS `TR01`은 나미이며 프로젝트에서는 `TR-02`다.
- [Unity C# 대응표 로더](../Assets/Scripts/Mobile/OriginalUnitRawcodeLabelCatalog.cs): 271종 카탈로그에 실제 존재하는지 확인하고, 원본 CWR 결과 rawcode와 Bi8/Bi9 대응표의 결과가 일치하는지 교차 검사.
- [원본 조건 순서 보존 압축 자료](../data/original-jass-fusion-ordered.xz.b64): 이전 `.br.b64` 조회용 자료와 달리 `BGI` 조건 등장 순서를 유지한 **266건**의 원본 등록식. Python 표준 라이브러리 `lzma`로 복원.
- [압축 자료 JSON 복원기](../tools/unpack_ordered_jass_fusions.py): 별도 W3X 준비 없이 등록식 266건을 Unity용 JSON으로 복원. SHA-256 무결성 확인 포함.
- [원본 조합 준비 상태 판정기](../Assets/Scripts/Mobile/OriginalJassFusionPlanner.cs): UNIT 재료 원시 ID와 보유 개수, GOLD/WOOD 추가 소모, UBAN/KING 등 특수 태그의 외부 규칙 판정, 미확인 사유 기록.
- [알려진 원본 조건 핸들러](../Assets/Scripts/Mobile/OriginalJassKnownConditions.cs): JASS `CHg` 밴 유닛, `CHh` 항법/최상위 제한, `CH3` 라운드 제한, `UPUN` 전시용 등록 처리. ITEM/PICK/IFCT/SKPT/CHCT 등은 원작 조회/소모 로직이 완전히 연결되기 전에는 차단.
- [원본 제작 포트](../Assets/Scripts/Mobile/OriginalJassFusionPort.cs): 확정 직전 **현재 보유 재료·골드·목재·특수 조건·원본 시전 대상**을 재검사. 실제 재화 차감과 소환은 전체 성공/실패를 원자적으로 처리하는 `IOriginalJassFusionAtomicCommit` 구현체에서만 실행하도록 분리.
- Unity는 [컨트롤러](../Assets/Scripts/Unity/OrdrCharacterCombatController.cs)에서 `ConfigureOriginalJassCrafting`으로 외부 원본 게임 상태 서비스를 연결하며, 모바일 화면은 매핑된 결과의 원본 재료 및 부족/미검증 상태를 볼 수 있다.

**커버리지의 경계:** 현재 266건 중 1건(`TEST`)은 제외한다. 265건 중 이름·등급으로 결과 유닛까지 매핑한 등록식은 **81건**이며, 그중 **모든 재료 rawcode까지 확보된 등록식은 아직 0건**이다. 그러므로 원본 조합으로 재료 소비·완성 유닛 소환은 아직 활성화하지 않는다. 이 프로젝트의 271종 유닛 목록이 원본 전체를 완전히 포괄한다는 뜻도 아니다.

### Windows에서 Unity에 원본 조합 DB 연결

프로젝트 루트에서 다음 한 번 실행한다.

```powershell
python tools/unpack_ordered_jass_fusions.py
```

그러면 `data/original-jass-fusion-registry.json`가 생성된다. Unity Editor에서 `ORDR > 캐릭터 데이터 271종 · 스킬 200종 동기화`를 실행하면 원본 조합 데이터와 81종 대응표가 Resources에 들어간다. 이는 **조합 미리보기 및 규칙 상태 조회** 단계이며 원본 경제/소모는 실행하지 않는다.

실제 제작 활성화에는 별도로 검증된 `IVerifiedUnitRawcodeMap`, `IOriginalJassSpecialConditionEvaluator`, `IOriginalJassFusionResourceReader`, `IOriginalJassFusionCastPolicy`, `IOriginalJassFusionAtomicCommit` 구현체가 **모두 필요**하다. 원본 CHu/CHx의 `GetSpellTargetUnit()` 처리에 맞는 시전 대상 승인도 생략하지 않는다.

## 실제 조합 구현을 막는 미확인 부분

### 원본 재료 ID 대조 대기열 및 거래 연결 (2026-10-21)

`data/original-jass-fusion-registry.json`은 원본 순서를 보존하여 현재 `main`에 포함한다. `tools/build_fusion_rawcode_backlog.py`가 만든 `data/original-fusion-rawcode-backlog.json`은 CWR UNIT 재료의 각 출현 위치와 수량, BGH 결과의 출현 능력, 기존 BjB 이름 연동을 분리해 보존한다. 전체 고유 결과/재료 ID 280종 중 기존 이름 연동 81종, 매핑되지 않은 재료 ID 163종이다. 미매핑 재료의 정체는 원본 W3X 객체 정보 없이는 확정할 수 없다.

`OriginalJassFusionAtomicCoordinator`는 UI 조합 확정 뒤 세계 상태를 독점한 거래 범위에서 재료와 재화, 시전 대상, 특수 조건을 다시 검증하고 `TryApplyOriginalFusion`과 `Commit`을 순서대로 호출한다. `IOriginalJassFusionWorldTransaction.Dispose`는 미커밋 변경 전체를 롤백해야 한다. 이 실제 월드 거래 어댑터, CHu/CHx 후속 효과 및 특수 BGI 지불은 **미구현**이므로 현재 원본 조합은 계속 실행 차단 상태다. `OrdrCharacterCombatController.ConfigureOriginalJassCraftingWorld`는 향후 검증된 월드 구현을 연결하는 진입점이다. 테스트·컴파일·APK 빌드는 요청에 따라 진행하지 않았다.

1. **원본 유닛 rawcode와 Unity 캐릭터 ID 대조**: `h00K`와 `L-21` 등은 동일 ID가 아님. 워크래프트 오브젝트 테이블에서 이름/등급을 대조한 후 매핑
2. **모든 BGI 조건의 의미와 순서**: `GOLD`, `WOOD`, `IFCT`, `KING`, `UBAN`, `SPEC`, `GREN` 등 원본 함수·게임 난이도 조건 검증
3. **제작 트리거의 원자적 처리**: 실행 직전에 소유 재료/골드/목재/아이템/변경 횟수 재확인, 지불과 생성, 실패 시 롤백
4. **모바일 수동 조합 정책**: 실제 유닛/재화 소비는 반드시 유저가 `조합 확정`을 탭해야 시작. 자동 재료 선택은 편의 기능일 뿐
5. **특수 트리거의 원작 분기**: 무조건 제작되거나 무료 제작되는 상태를 허용하지 않음

## 완료 상태 해석

**완료:** 소스 스크립트에서 조합 등록식 266건을 찾아, 압축 자료와 원본 순서 보존 변환기, C# 조회 경로까지 GitHub `main`에 저장.

**미완료:** 전체 265건을 실제 게임에서 조합·소비 가능한 상태로 구현한 것은 아님. 원본 유닛 매핑, 특수 조건, 재화 처리, 조합 UI 연동 및 실제 Unity 컴파일·기기 테스트는 아직 필요. 사용자 요청에 따라 전체 게임 기능 완성 전 테스트와 APK 빌드는 보류.

## 순서형 거래 및 W3U 객체 이름 검수 도구 (2026-10-21)

### 실제 구현된 흐름

- `OriginalJassFusionPlanner`는 동일 재료 rawcode를 여러 번 요구하는 조합도 필요한 수량만큼 합산하고, rawcode별 **정확한 소유 인스턴스 ID 목록**을 기록한다.
- `OriginalJassFusionCommitOrder`는 원본 `CWR`의 `BGI` 순서와 `BGH` 결과, 각 재료 묶음, 골드·목재, 시전 대상을 보유한다.
- `OriginalJassFusionAtomicCoordinator`는 플레이어 상태를 잠근 후 주문을 새로 구성하고, 원본 조건 승인과 실제 소모가 동일 주문을 사용하도록 한다.
- `OriginalJassFusionOrderedWorldAdapter`는 거래 범위 안에서 모든 조건을 차례로 재검증하고, `UNIT` 인스턴스별 소비, `GOLD`·`WOOD` 차감, 나머지 원본 특수 태그의 효과, `BGH`·`CHu/CHx` 후속 효과까지 실행하도록 연결한다. 세계 상태는 **Commit 전까지 미확정**이어야 한다.
- 원본 특수 태그의 정확한 검사/효과와 BGH 결과 소환은 `IOriginalJassFusionOrderedSession`이 책임지며, 실제 월드의 검증·원자적 롤백 구현체는 아직 없다. `Unity ConfigureOriginalJassCraftingOrderedWorld` 진입점은 추가했지만 실제 게임에서 조합 가능 상태가 아니다. 재료 수량·비용만으로 제작 실행을 승인하지 않는다.

### rawcode 객체 근거 추출 (원본 파일을 확보한 경우에만)

기존 `data/original-fusion-rawcode-backlog.json`의 280종 고유 ID 중 미확인 원재료 ID 163종을 대상으로 원본 `war3map.w3u` 객체 이름을 수집한다. 원본 맵에서 `war3map.w3u`와 (존재할 때) `war3map.wts`를 추출한 뒤 아래 명령을 실행한다.

```powershell
python tools/extract_w3u_fusion_unit_names.py --w3u "war3map.w3u" --wts "war3map.wts" --output "data/original-w3u-fusion-unit-name-evidence.json"
```

`war3map.wts`가 없는 맵에서는 `--wts`를 생략할 수 있다. 이 추출기는 원본 객체 파일의 수정 내역에 있는 이름 필드(`unam`), `TRIGSTR_` 참조, 원본 rawcode와 도감 이름의 **문자열 일치 후보**를 기록한다. 객체 파일에 등장하지 않는 ID(기본 게임 객체 등)는 미확인으로 둔다. 같은 이름이 여러 등급에 있을 수도 있으므로 추출 결과는 **즉시 사용 가능한 제작용 ID 매핑이 아니다.** 해당 W3U가 이 버전 원본 W3X에서 왔는지도 확인해야 한다.

원본 객체와 카탈로그를 **개별적으로 확인한 뒤에만** `OriginalVerifiedUnitObjectMap.FromReviewedJson`이 읽는 승인 자료를 작성할 수 있다. JSON 최상위 필드는 `gameVersion: "S2 2.323[C]"`, `sourceJassSha256` (위 원본 값), `mappingEvidenceLevel: "manually-reviewed-original-object"`, `approvedMappings`이다. 각 승인 항목에는 `rawcode`, `catalogUnitId`, `sourceObjectFile`(`war3map.w3u` 또는 `Units/UnitData.slk`), `sourceObjectSha256`, `resolvedOriginalObjectName`, `manuallyReviewed: true`가 모두 필요하다. 이름/능력 관련 JASS 81종과 대응이 충돌하면 로더가 거부한다. 도감 조합식과 rawcode 순서만으로 임의 대응을 만들지 않는다.

### 현재 실제 검증 상태

- 원본 W3U 및 WTS 파일이 이번 작업 환경에 없어서 객체 대조 도구를 **실행하지 않았음**.
- **새로 확정한 객체 rawcode 매핑 0종**, 기존 JASS 이름 연동 81종 유지. 현재 원본 실제 제작 가능 조합 수는 계속 **0건**.
- `ITEM/IFCT/SKPT/CHCT/PICK/RDUN/SPEC/GREN` 등 원본 검사/지불/후속 효과를 담당하는 게임 월드 서비스가 미구현. 원본 무결성 확인, 실제 거래 롤백, Unity 프로젝트 기능 검증도 미완료.
- 사용자 요구에 따라 신규 테스트·게임 실행·Unity 컴파일·APK 제작·GitHub Actions 생성은 하지 않았음.
