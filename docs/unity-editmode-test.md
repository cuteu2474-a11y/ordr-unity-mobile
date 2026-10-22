# Unity 6.3 EditMode 테스트 실행

현재 워크플로 이름은 **Unity EditMode 검증 (main 자동 + 수동)**입니다.

- `main`에 push하면 Python 회귀 검사와 Unity EditMode 검증이 자동 실행됩니다.
- 필요한 경우 Actions에서 해당 워크플로의 **Run workflow**로 수동 실행할 수 있습니다. 같은 변경의 중복 실행은 피합니다.
- 동시 실행 그룹은 `ordr-unity-editmode`, `cancel-in-progress: false`로 진행 중 실행을 자동 취소하지 않습니다.
- 테스트 실행 설정은 통과 증거가 아닙니다. 해당 커밋의 실행 결과를 확인해야 합니다.
- APK 제작은 이 워크플로의 작업이 아닙니다. 핵심 기능 완성 및 검증 후 진행합니다.

설정의 기준: [unity-editmode.yml](../.github/workflows/unity-editmode.yml).

## 선행 설정

- Unity 프로젝트 편집기 버전: `ProjectSettings/ProjectVersion.txt` 참조 (`6000.3.26f1`).
- `Packages/manifest.json`에 Unity Test Framework를 등록했습니다.
- 런타임과 테스트 어셈블리는 각각 `Assets/Scripts/Ordr.Mobile.Runtime.asmdef`,
  `Assets/Tests/EditMode/Ordr.Mobile.EditModeTests.asmdef`를 사용합니다.
- GitHub 저장소의 Actions Secrets에 `UNITY_LICENSE`, `UNITY_EMAIL`,
  `UNITY_PASSWORD`가 필요합니다. Unity Personal/Pro에 맞게 GameCI 라이선스를
  준비해야 하며, 계정 비밀번호나 라이선스 내용을 코드에 넣지 마세요.

## 실행 결과 해석

1. Python 자료 회귀 테스트가 먼저 실행됩니다(원본 맵 데이터 일치 여부).
2. 라이선스 값이 없으면 Unity 실행 전에 실패합니다. **Unity 테스트 실패가 아닙니다.**
3. Unity EditMode Test Runner는 여러 NUnit 테스트 및 `OriginalMapCommonNativeAttackBindingTests`를 실행합니다.
4. 테스트 결과는 `unity-editmode-results` 아티팩트에 기록됩니다.

`OriginalMapCommonNativeAttackBindingTests`에서 적 체력이 감소하는 시나리오는
`TEST_ONLY` 검증 공급자와 피해량 절사 정책을 주입한 **연동 테스트**입니다.
이 테스트가 통과해도 2.323[C]의 JASS 공격 경로, 대상 방어력, 소수점 정수화
정책, 스킬 발동 등이 원작과 동일하다는 의미는 아닙니다. 실제 게임 전투에 이
검증 공급자를 사용하지 않습니다. 원본에서 각 조건이 검증되면 별도로 연결합니다.
