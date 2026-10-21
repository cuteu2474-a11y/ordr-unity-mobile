# Unity 6.3 EditMode 테스트 실행

GitHub `main`의 **Actions → Unity EditMode 검증 (수동 실행) → Run workflow**에서
필요할 때만 한 번 실행합니다. 소스 변경마다 자동 실행하거나 기존 실행을 취소하지 않습니다.

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
