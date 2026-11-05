# 원랜디 Unity 모바일

ORDR S2 2.323[C]의 **악몽 + 이세계모드**를 Android 오프라인 싱글플레이로 옮기는 Unity 리메이크입니다. 현재는 개발 버전입니다.

## APK 받기

[APK 생성에 성공한 실행 열기](https://github.com/cuteu2474-a11y/ordr-unity-mobile/actions/runs/41941260044) → **Artifacts → ordr-mobile-android-development-apk** → ZIP 압축 해제 → `ORDR-Mobile-Development.apk`.

2026-11-05 확인 기준 APK는 **0.1.364**, 소스는 `53d3e240`입니다. Unity 검사 2,161건과 APK 데이터·CPU·서명 검증을 통과했습니다. 휴대폰 설치·조작·65라운드 전체 플레이는 확인하지 않았습니다. 아티팩트는 11월 19일 오전 10:01(한국 시간) 만료 예정입니다.

**이 APK에는 이후 13개 게임 코드 커밋이 들어 있지 않습니다.** 최신 코드와 APK의 차이, 최신 검사 실패는 [현재 상태](docs/START_HERE.md)를 확인하세요.

## 현재 구현 범위

- 유닛 준비 → 위습 소환·조합 → 독립 65라운드와 14개 스토리 건물 전투. 10R·20R 연습도 선택할 수 있습니다.
- 공유 재화와 실제 보유 유닛을 사용하는 조합·판매·분해·일부 도박 및 보상.
- 원본 객체 기반 평타와 일부 스킬·오라·소환·변신의 개발용 전투 모델.
- 가로 화면, 수동 전장 이동·확대, 일시정지와 앱 전환 시 논리 시간 정지.

전체 유닛 스킬, 일부 아이템·연구·획득 경로, 저장·복원과 원작 최종 전투 동등성은 미완성입니다.

## 문서 안내

| 목적 | 문서 |
|---|---|
| 지금 되는 기능·알려진 문제 | [현재 상태](docs/START_HERE.md) |
| 다음에 할 일 | [개발 작업](docs/PLAYABLE_ROADMAP.md) |
| Unity 실행·검사·APK 제작 | [실행과 APK](docs/unity-editmode-test.md) |
| 수정할 코드 찾기 | [코드 구조](docs/CODE_MAP.md) |
| 원본 자료·유닛 현황·기술 참고 | [문서 목차](docs/README.md) |
| 개발 에이전트 작업 규칙 | [AGENTS.md](AGENTS.md) |

Unity 버전은 **6000.3.26f1**, 시작 씬은 `Assets/Scenes/MainGame.unity`입니다. 원본 W3X와 개인용 이미지는 저장소에 포함하지 않습니다.
