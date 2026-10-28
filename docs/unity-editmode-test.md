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
| APK 아티팩트 | `ordr-mobile-android-development-apk`, `build/Android/**/*.apk`, 보존 14일 |

CI에 `UNITY_LICENSE`, `UNITY_EMAIL`, `UNITY_PASSWORD` 설정이 필요합니다. 값이나 키 파일을 코드·문서·로그에 노출하지 않습니다. 라이선스 단계 실패를 게임 테스트 실패로 혼동하지 않습니다.

사용자가 요청한 APK는 이 기존 작업을 사용합니다. 새 워크플로·같은 실행의 중복 재시도·진행 중 작업 취소를 하지 않습니다. 문서만 바뀐 커밋에서 같은 게임 코드의 APK를 다시 만들 필요는 없습니다.

## 결과 해석과 조회

- 검사 설정이 존재하거나 실행이 시작된 것은 통과 증거가 아닙니다.
- 모델 전투·TEST_ONLY 승인·가상 HP/난수를 사용한 연동 검사는 원작 최종 피해/면역·실기 플레이 승인과 다릅니다.
- **푸시 후 Actions 테스트 실행 시작을 확인하면 결과를 기다리지 않고 응답을 종료합니다. 결과는 사용자의 별도 요청 때만 확인합니다.** 대기 상태는 대기라고 보고합니다.
- APK 제공 시 결과물이 어느 게임 코드에서 만들어졌는지 확인합니다. 제작 요청, APK 생성, 설치, 실제 조작·전투 확인을 따로 기록합니다.
- 기준 게임 코드의 과거 검사 범위는 [현재 상태](START_HERE.md), 원작 검증 경계는 [PARITY](PARITY.md)를 참고합니다.
