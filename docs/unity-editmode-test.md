# Unity 검사와 APK 제작

현재 설정의 기준은 [unity-editmode.yml](../.github/workflows/unity-editmode.yml), 편집기 버전은 [ProjectVersion.txt](../ProjectSettings/ProjectVersion.txt)의 `6000.3.26f1`입니다.

## 로컬 검사

저장소 루트에서 원본 JSON·감사 자료에 대한 비 Unity 회귀와 공백 검사를 실행합니다.

```sh
python3 -m unittest discover -s tests -q
git diff --check
```

Unity에서는 저장소 전체를 열고 `Assets/Scenes/MainGame.unity`와 Test Runner의 EditMode 검사를 사용합니다. 원본 `data/`는 `OrdrCharacterDataSync`가 Resources 캐시로 가져옵니다. `Assets/Resources/OrdrData/`를 수동 편집하지 않습니다.

로컬 Unity CLI를 사용할 경우 설치한 Unity 실행 파일로 다음을 실행합니다. 테스트 결과·로그 경로는 작업 환경에 맞게 정하며 자동 생성물은 커밋하지 않습니다.

```sh
Unity -batchmode -nographics -projectPath "$PWD" \
  -runTests -testPlatform EditMode \
  -testResults /tmp/ordr-editmode.xml -logFile /tmp/ordr-editmode.log
```

Unity 없이 실행한 공통 C# 컴파일/NUnit은 검사한 코드 범위만 보증합니다. 파일 읽기용 Unity 어댑터를 썼다면 그 사실을 기록하고 실제 Unity 엔진 검사와 구분합니다. 임시 Mono 도구·실행기가 새 환경에 있다고 가정하지 않습니다.

## 기존 GitHub Actions

| 조건·작업 | 동작 |
|---|---|
| main push | `check`: Python 회귀 → 라이선스 설정 확인 → Unity EditMode |
| 수동 workflow_dispatch | `check` 실행. 아래 APK 조건을 자동 만족하지 않음 |
| push 커밋 메시지에 `[apk-preview]` 포함 | `check`에 의존하는 `android-apk-preview` 실행 |
| 동시 실행 | 그룹 `ordr-unity-editmode`, `cancel-in-progress: false` |
| 검사 아티팩트 | `unity-editmode-results` |
| APK 아티팩트 | `ordr-mobile-android-development-apk`, `ORDR-Mobile-Development.apk` 및 패킹 증빙·검증 JSON·SHA-256, 보존 14일 |

CI에 `UNITY_LICENSE`, `UNITY_EMAIL`, `UNITY_PASSWORD` 설정이 필요합니다. 값이나 키 파일을 코드·문서·로그에 노출하지 않습니다. 라이선스 단계 실패를 게임 테스트 실패로 혼동하지 않습니다.

사용자가 요청한 APK는 이 기존 작업을 사용합니다. 새 워크플로·같은 실행의 중복 재시도·진행 중 작업 취소를 하지 않습니다. 문서만 바뀐 커밋에서 같은 게임 코드의 APK를 다시 만들 필요는 없습니다.

## Android 전용 제작과 다운로드

기존 APK job은 `Ordr.Mobile.Editor.OrdrAndroidDevelopmentBuild.Build`를 실행합니다. 앱 ID `com.ordr.mobile.development`, IL2CPP ARM64+ARMv7 단일 APK, 가로 화면, OBB 없음, 개발용 디버그 서명으로 고정합니다. CI의 `run_number`를 버전 코드로 사용합니다. 정식 서명 키는 필요하지 않으며 저장소에 추가하지 않습니다. 서로 다른 CI 머신의 디버그 서명은 다를 수 있으므로 이전 개발용 APK에 덮어 설치가 실패할 수 있습니다. 기존 앱 삭제는 데이터 삭제이므로 임의 자동화하지 않습니다.

Unity Android Build Support가 설치된 로컬 환경에서는 플랫폼을 Android로 바꾼 뒤 **ORDR → Android 개발용 APK 제작**을 사용하거나 다음 명령을 실행합니다.

```sh
Unity -batchmode -nographics -quit -projectPath "$PWD" -buildTarget Android \
  -executeMethod Ordr.Mobile.Editor.OrdrAndroidDevelopmentBuild.Build \
  -ordrBuildVersion 0.1.0 -ordrVersionCode 1 -logFile /tmp/ordr-android-build.log
```

빌드 전 20개 `data/` 파일을 동기화·검사합니다. 기본 씬을 빌드하고 실패 시 예외로 CI를 실패시키며 이전 APK를 새 성공 결과로 재사용하지 않습니다. 생성 뒤 빌더가 실제 `BuildReport.packedAssets`의 필수 20개 자산 패킹과 생성 APK 해시를 `.build.json`에 기록합니다. `tools/verify_android_apk.py`는 이 증빙과 importer의 전체 목록·실제 APK 해시를 대조하고 필수 네이티브 라이브러리·ZIP CRC와 실제 Android `apksigner verify`를 검사합니다. 내부 `resources.assets` 이름을 고정하지 않습니다. Unity의 [빌드 콘텐츠 문서](https://docs.unity3d.com/kr/current/Manual/build-content-output.html)도 출력 파일명의 버전별 변경 가능성을 명시합니다. 서명 확인은 실제 설치·게임 플레이 검사와 별개입니다.

CI에서는 Unity 컨테이너가 만든 APK·패킹 증빙을 `$RUNNER_TEMP/ordr-apk-package`로 복사하고 그 복사본을 검증합니다. 쓰기 권한이 있는 이 폴더에 검증 JSON·SHA-256을 생성하고 폴더 전체를 첨부합니다. 컨테이너의 원본 `build/Android`에 결과 파일을 쓰지 않습니다.

성공한 Actions 실행의 **Artifacts → ordr-mobile-android-development-apk**에서 ZIP을 내려받아 풀면 APK, `.build.json`, `.apk.sha256`, `.verification.json`이 있습니다. 생성 APK의 실제 SHA-256과 소스 커밋을 확인할 수 있습니다. 빌드가 실패하거나 진행 중이면 이 결과물이 있다고 안내하지 않습니다. 현재 게임은 10R/20R 개발용 연습이며 전체 저장·65R·전체 스킬은 미완성입니다.

## 결과 해석과 조회

- 검사 설정이 존재하거나 실행이 시작된 것은 통과 증거가 아닙니다.
- 모델 전투·TEST_ONLY 승인·가상 HP/난수를 사용한 연동 검사는 원작 최종 피해/면역·실기 플레이 승인과 다릅니다.
- **푸시 후 Actions 테스트 실행 시작을 확인하면 결과를 기다리지 않고 응답을 종료합니다. 결과는 사용자의 별도 요청 때만 확인합니다.** 대기 상태는 대기라고 보고합니다.
- APK 제공 시 결과물이 어느 게임 코드에서 만들어졌는지 확인합니다. 제작 요청, APK 생성, 설치, 실제 조작·전투 확인을 따로 기록합니다.
- 기준 게임 코드의 과거 검사 범위는 [현재 상태](START_HERE.md), 원작 검증 경계는 [PARITY](PARITY.md)를 참고합니다.

## 2026-10-29 APK가 보이지 않았던 실행

`ee5cd0d`의 [실행 40838453130](https://github.com/cuteu2474-a11y/ordr-unity-mobile/actions/runs/40838453130)에서 Unity Android 단계는 약 30MB APK를 생성했지만 후속 고정 리소스 파일명 검사가 실패해 APK 첨부가 건너뛰어졌습니다. 남아 있는 아티팩트는 EditMode 결과뿐입니다. 이번 수정은 실제 Unity 패킹 증빙과 APK 해시를 사용합니다. 수정 후 다운로드 성공이나 기기 플레이를 미리 통과로 표시하지 않습니다.

`c84ae4d`의 [실행 40883799065](https://github.com/cuteu2474-a11y/ordr-unity-mobile/actions/runs/40883799065)에서는 패킹 증빙/해시/CRC/CPU와 실제 v2 서명까지 통과했으나 검증 JSON을 컨테이너 출력 옆에 쓰다 권한 오류가 발생했습니다. 이 때문에 APK 아티팩트는 첨부되지 않았습니다. 현재 워크플로는 runner 임시 폴더의 복사본을 사용합니다. 검증 생략이나 권한 전체 개방으로 처리하지 않습니다.
