# Unity 실행·검사와 APK

Unity **6000.3.26f1** · 기본 씬 `Assets/Scenes/MainGame.unity` · [현재 워크플로](../.github/workflows/unity-editmode.yml).

최신 확인 결과와 APK 버전은 [현재 상태](START_HERE.md) 한 곳에서 관리합니다. APK 생성·서명 검증과 실제 기기 플레이 검증은 별개입니다.

## APK 다운로드와 설치

1. [확인된 APK 생성 실행](https://github.com/cuteu2474-a11y/ordr-unity-mobile/actions/runs/41941260044)을 엽니다. GitHub 로그인이 필요할 수 있습니다.
2. **Artifacts → ordr-mobile-android-development-apk**를 내려받습니다.
3. ZIP을 풀고 `ORDR-Mobile-Development.apk`를 Android 기기에서 설치합니다.

ZIP에는 APK, `.build.json` 패킹 증빙, `.apk.sha256` 체크섬, `.verification.json` 검증 결과가 있습니다. 보존 기간은 생성 후 14일입니다.

앱 ID는 `com.ordr.mobile.development`, IL2CPP ARM64+ARMv7 단일 APK, 가로 화면, OBB 없음, 개발용 디버그 서명입니다. 버전은 `0.1.<Actions run_number>`이고 버전 코드도 해당 실행 번호입니다. 다른 빌드 머신의 서명 차이로 덮어 설치가 실패할 수 있습니다. 기존 앱 삭제는 데이터 삭제이므로 자동으로 처리하지 않습니다.

게임 진행은 오프라인을 목표로 합니다. 캐릭터 사진 다운로드에는 인터넷을 사용하며, 사진이 없으면 대체 그림을 표시합니다.

## Unity에서 실행 (Windows)

1. Unity Hub를 설치하고 **Unity 6.3 LTS (6000.3.26f1)** 에디터를 설치합니다. APK를 직접 제작할 경우 해당 버전의 **Android Build Support**도 설치합니다.
2. GitHub의 `cuteu2474-a11y/ordr-unity-mobile` 저장소에서 **main 전체**를 내려받아 압축을 풀거나 복제합니다.
3. Unity Hub에서 **Add project from disk**를 선택하고 `Assets`, `Packages`, `ProjectSettings`, `data`가 들어 있는 **저장소 최상위 폴더**를 지정합니다. `Assets`만 선택하지 않습니다.
4. 에디터에서 `Assets/Scenes/MainGame.unity`를 연 뒤 **Play**로 개발 버전을 확인합니다.
5. 도감·전투 데이터가 보이지 않으면 상단 **ORDR → 캐릭터 데이터 · 위키 확장 · 스킬 동기화** 메뉴를 실행합니다.

`data/`는 Editor의 `OrdrCharacterDataSync`가 `Assets/Resources/OrdrData/`로 자동 동기화합니다. 생성된 캐시를 직접 수정하거나 커밋하지 않습니다. Unity에서 실행했다고 해서 전체 라운드·APK·실기 검증까지 통과한 것은 아닙니다.

## Editor 전용 3라운드 체험

**개발용 가상 전투**를 빠르게 확인하는 기능입니다. 정식 악몽·이세계 플레이나 Android APK가 아니며, 원본 라운드·몬스터 수치를 검증한 결과도 아닙니다.

1. Unity 6000.3.26f1에서 프로젝트 전체를 연 뒤 상단 **ORDR → 첫 라운드 직접 체험 (Unity Editor 전용)**을 선택하고 가상 데이터 사용을 승인합니다.
2. `MainGame.unity`가 Play 모드로 열리고 **1라운드와 루피(C-01) 1기**가 자동 생성됩니다. 이 체험에는 **연습용 랜덤위습 총 5개(루피 사용 후 4개)**가 제공됩니다. 원본 시작 지급도 **5개**지만, 이 체험에서 루피를 고정 소환하는 흐름과 가상 전투 입력은 정식 게임 시작 절차와 다릅니다.
3. 자동 공격·몬스터 이동·처치·골드 표시와 **1x/2x/4x 배속·일시정지·드래그/줌**을 확인합니다. 연습용 타이머는 라운드당 약 **35초**로, 생존 적이 있어도 **1→2→3라운드**가 진행되고 4라운드 이후에는 진행하지 않습니다.
4. 종료 시 Unity 상단 **Play**를 끕니다. Editor 전용 타이머와 배속이 해제되어 1배로 복구됩니다.

| 가상 연습 입력 | 실제 의미 |
|---|---|
| 1~3라운드의 원본 JASS 몬스터 ID·종류·0.5초 출현 시각 | 일부 원본 식별/출현 자료를 재사용할 뿐 정식 웨이브 동등성은 아님 |
| 몬스터 체력 **55/75/95**, 방어 **0**, 가상 2구간 경로(**총 512×192 좌표 변화**), 이동속도 **40 월드 단위/초** | 원본 체력·면역·속도·좌표가 아닌 Editor 연습용 값 |
| 첫 타격 지연 **0.35초**, 임시 물리 공격 허가·피해 절사, 모든 처치의 연습 골드 지급 | 골드 금액은 기존 라운드 계산식을 쓰지만 원본 확정 피해/처치 판정은 아님 |
| 고정 루피 1기·별도 난수 시드·35초 라운드 타이머 | 정식 원본 RNG·시작 지급·라운드 시작 증거와 분리 |

경로 끝 도달은 **처치·이탈·골드 획득으로 처리하지 않습니다.** 일시정지 중 이동하지 않으며 기절 중에는 경로 이동기가 정지 판정을 적용합니다. 체험 종료 후 3라운드의 생존 몬스터 이동은 관찰할 수 있습니다.

이 입력은 [`Assets/Editor/OrdrFirstWavePreview.cs`](../Assets/Editor/OrdrFirstWavePreview.cs)에만 있으며 **보통의 Unity Play·APK 빌드에서는 자동 연결되지 않습니다.** Editor 전용 경고가 화면에 표시됩니다. 원본 전투의 최종 HP·마법 면역·글로벌 난수·마법 피해 소수점·스토리/상점/조합·전체 스킬·65라운드와 기기 동작은 이 체험으로 검증되지 않습니다. 메뉴가 보이지 않으면 Unity Console 오류를 확인합니다. CI 컴파일 성공도 실제 Editor 조작 성공을 의미하지 않습니다.

## GitHub 검사와 제작 조건

| 조건 | 동작 |
|---|---|
| main push(코드·데이터·설정 변경 포함) | Python 회귀 → Unity 라이선스 확인 → Unity EditMode |
| 문서만 바뀐 main push | 자동 검사 생략(`docs/**`, Markdown·MDX). 코드·데이터와 함께 바뀌면 실행 |
| 수동 실행, build_apk=false | 위 검사만 실행 |
| push 커밋 메시지에 `[apk-preview]` | 위 검사가 성공하면 APK 제작·검증·첨부 |
| 수동 실행, build_apk=true | 위 검사가 성공하면 APK 제작·검증·첨부 |
| 동시 실행 | `ordr-unity-editmode` 그룹, 진행 중 실행 자동 취소 없음 |

CI는 `UNITY_LICENSE`, `UNITY_EMAIL`, `UNITY_PASSWORD` 설정이 필요합니다. 실제 값·키 파일은 로그나 저장소에 기록하지 않습니다. 라이선스 실패와 게임 검사 실패를 구분합니다.

**운영 규칙:** 기존 워크플로만 사용합니다. 에이전트는 명시적 요청이 있을 때 검사·APK 빌드·Actions 조회를 수행합니다. APK 요청은 기존 push의 태그 조건을 사용하며 문서 수정만으로 재제작하지 않습니다. 푸시 뒤 자동 검사를 기다리거나 별도 수동 실행하지 않습니다. [AGENTS.md](../AGENTS.md).

## 허가받은 로컬 검사 명령

저장소 루트에서 실행합니다.

```sh
python3 -m unittest discover -s tests -q
git diff --check
```

Unity CLI의 실행 파일과 결과 경로는 현재 환경에 맞춥니다.

```sh
Unity -batchmode -nographics -projectPath "<repository>" \
  -runTests -testPlatform EditMode \
  -testResults "<results.xml>" -logFile "<editmode.log>"
```

Unity 메뉴 **ORDR → Android 개발용 APK 제작** 또는 CLI로 빌드합니다.

```sh
Unity -batchmode -nographics -quit -projectPath "<repository>" -buildTarget Android \
  -executeMethod Ordr.Mobile.Editor.OrdrAndroidDevelopmentBuild.Build \
  -ordrBuildVersion 0.1.0 -ordrVersionCode 1 -logFile "<android-build.log>"
```

Unity 없이 수행한 C# 컴파일·NUnit은 해당 코드 범위만 확인합니다. Unity 엔진 검사로 보고하지 않습니다. 전체 로컬 도구가 항상 설치돼 있다고 가정하지 않습니다.

## APK 검증 방식

빌더는 현재 importer의 필수 데이터 목록을 동기화하고, 실제 `BuildReport.packedAssets`에 모두 포함됐는지 확인해 APK 해시와 함께 증빙을 만듭니다. 내부 `resources.assets` 파일명은 고정하지 않습니다.

`tools/verify_android_apk.py`는 목록·패킹 증빙·실제 SHA-256·ZIP CRC·CPU별 네이티브 라이브러리와 Android `apksigner verify` 결과를 확인합니다. CI는 Unity 컨테이너 출력을 runner 임시 폴더로 복사한 뒤 검증하고, 그 폴더 전체를 첨부합니다.

검증 성공은 패키지 구조와 서명이 확인됐다는 뜻입니다. 기기 설치·화면 조작·전투·성능은 해당 커밋과 기기를 명시해 별도로 기록합니다.
