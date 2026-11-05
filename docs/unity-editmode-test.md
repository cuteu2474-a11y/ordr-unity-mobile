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

## GitHub 검사와 제작 조건

| 조건 | 동작 |
|---|---|
| main push | Python 회귀 → Unity 라이선스 확인 → Unity EditMode |
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
