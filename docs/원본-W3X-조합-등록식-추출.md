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

## 실제 조합 구현을 막는 미확인 부분

1. **원본 유닛 rawcode와 Unity 캐릭터 ID 대조**: `h00K`와 `L-21` 등은 동일 ID가 아님. 워크래프트 오브젝트 테이블에서 이름/등급을 대조한 후 매핑
2. **모든 BGI 조건의 의미와 순서**: `GOLD`, `WOOD`, `IFCT`, `KING`, `UBAN`, `SPEC`, `GREN` 등 원본 함수·게임 난이도 조건 검증
3. **제작 트리거의 원자적 처리**: 실행 직전에 소유 재료/골드/목재/아이템/변경 횟수 재확인, 지불과 생성, 실패 시 롤백
4. **모바일 수동 조합 정책**: 실제 유닛/재화 소비는 반드시 유저가 `조합 확정`을 탭해야 시작. 자동 재료 선택은 편의 기능일 뿐
5. **특수 트리거의 원작 분기**: 무조건 제작되거나 무료 제작되는 상태를 허용하지 않음

## 완료 상태 해석

**완료:** 소스 스크립트에서 조합 등록식 266건을 찾아, 압축 자료와 원본 순서 보존 변환기, C# 조회 경로까지 GitHub `main`에 저장.

**미완료:** 전체 265건을 실제 게임에서 조합·소비 가능한 상태로 구현한 것은 아님. 원본 유닛 매핑, 특수 조건, 재화 처리, 조합 UI 연동 및 실제 Unity 컴파일·기기 테스트는 아직 필요. 사용자 요청에 따라 전체 게임 기능 완성 전 테스트와 APK 빌드는 보류.
