# UI 바탕 이미지 v1

회색 원본 3종을 **게임 UI 코드에 연결했습니다(2026-11-13). Unity/Android 실제 화면 확인은 아직 하지 않았습니다.**

| 원본 | Resources 경로 | 실제 연결 위치 |
|---|---|---|
| [button-square-v1.png](button-square-v1.png) | `OrdrArt/Ui/SquareButton` | 닫기·이전/다음 페이지 버튼 |
| [popup-panel-v1.png](popup-panel-v1.png) | `OrdrArt/Ui/PopupPanel` | 상점 구매·선택 판매·흔함 판매·분해 확인 영역 |
| [speech-bubble-v1.png](speech-bubble-v1.png) | `OrdrArt/Ui/SpeechBubble` | 하단 안내·연구/강화 길게 누르기 설명·도박 설명 |

[가로 공통 버튼](../button-v1/README.md)과 함께 `OrdrMobileUiTheme` 및 `OrdrMobileTouchPanel`에 연결합니다. 오른쪽 상세 패널의 바깥 테두리는 기존 정책대로 장식 없이 유지합니다. 글자·가격·확률·수량은 별도 UI 텍스트입니다.

## 공통 처리

- 원본은 `art/ui`에 그대로 보관하고 같은 PNG를 `Assets/Resources/OrdrArt/Ui`에 포함합니다.
- `OrdrMobileUiArtwork`가 최초 로딩 때 알파 경계를 찾아 투명 생성 여백을 정리하고 비율을 유지한 작은 표면으로 캐시합니다. 임포트 Read/Write는 이 단계에 필요하며 원본 리소스는 준비 후 해제합니다.
- 색상·hover·눌림 밝기를 같은 원본에서 만듭니다. GPU 표면의 CPU 복사본은 해제하고 반복 OnGUI 로딩을 피합니다. 누락된 이미지에는 기존 스타일을 유지합니다.
- 9분할 테두리와 말풍선 왼쪽/아래 꼬리 구간을 고정하고 텍스트에 별도 안쪽 여백을 둡니다. PNG 원본 자체는 수정하지 않습니다.
- 기존 버튼 콜백·확인 정책·비용·재고·보상 규칙을 유지합니다.

## 생성·검증

생성 프롬프트는 [prompts.json](prompts.json), 원본 크기·해시는 [image-metadata.json](image-metadata.json)에 있습니다.

리소스/임포터/스타일/거래 호출 변경 범위를 정적으로 검토했습니다. `OrdrBattlefieldArtTests`에 이미지 4종의 로딩·알파·캐시·분할·버튼 상태 검사 소스를 추가했습니다. **Unity/NUnit/Android 실행·APK 제작은 이번 작업에서 수행하지 않았습니다.** 실제 해상도별 모서리·말풍선 꼬리·텍스트 대비·스크롤 확인은 Q03에 남겨 둡니다.
