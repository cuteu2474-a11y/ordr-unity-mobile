# 카툰풍 해적 UI 원본 에셋 (v1)

2026-11-13 ChatGPT 라이브러리에 보관된 원본 PNG 6개를 **변환·압축·리사이즈 없이** 가져온 보관용 묶음입니다. 원본 6개는 `art/`에 그대로 보존합니다. 그중 실사용 가능한 4개를 Unity `Resources`와 공통 UI 테마에 연결했으며, 레퍼런스 보드 2개는 참조용입니다.

| 파일 | 라이브러리 원본 | 용도 |
| --- | --- | --- |
| `button-wide-teal-gold.png` | 금빛 테두리의 청록색 UI 버튼.png | 일반 메뉴·조합 등 공통 와이드 버튼 |
| `button-wide-orange-gold.png` | 황금 테두리 주황 버튼.png | 강화·확인 등 강조 버튼 |
| `button-square-teal-gold.png` | 청록색 금빛 테두리 아이콘 버튼.png | 아이콘 전용 정사각 버튼 |
| `panel-parchment-wide.png` | 장식 모서리의 와이드 양피지 패널.png | 안내·상세 정보용 패널 |
| `reference-common-panels-states.png` | 공통 패널과 버튼 상태 보드.png | 팝업 및 기본·눌림·선택·비활성 상태 참고 시트 |
| `reference-comic-adventure-pirate.png` | 코믹 어드벤처 해적 UI 보드.png | 전체 콘셉트·버튼·색상 조합 참고 시트 |

**적용 상태(2026-11-13):** `Assets/Resources/OrdrArt/CartoonUi/`에 원본 PNG 4종의 동일 Git blob을 등록하고 `OrdrCartoonUiArtwork` → `OrdrMobileUiTheme`로 연결했습니다. 청록색은 일반 메뉴·기능·스킬, 주황색은 주요 실행·선택, 정사각 버튼은 닫기·페이지, 양피지는 확인창·도움말에 사용합니다. 두 레퍼런스 보드는 APK에 포함하지 않습니다. **코드·리소스 연결 상태이며 Unity/Android 실행 검증은 아직 하지 않았습니다.**

**공통 렌더링:** `Read/Write` 임포트한 원본 PNG에서 투명 생성 여백을 최초 로딩에만 정리해 384px(가로형)/128px(사각)/512px(양피지) 이내로 축소·캐시합니다. GUIStyle 9-slice로 외곽 금색 테두리를 보호하며 기본·hover·눌림 상태의 색상 밝기를 자동 처리하고, 리소스 누락 시 기존 IMGUI 배경을 그대로 사용합니다. 양피지는 기존 흰색 동적 텍스트 대비를 위해 어두운 갈색 톤으로 렌더링합니다. 버튼 글자·수량·비용·레벨은 PNG가 아닌 런타임 텍스트입니다. 원본 파일은 손대지 않았습니다.

**남은 검증:** Unity EditMode의 `OrdrCartoonUiArtworkTests`로 리소스·알파 트리밍·캐시·9분할·상태 분기를 확인하고, 실제 Android 가로 화면에서 버튼 크기·글자 대비·팝업 스크롤·터치 영역을 검사해야 합니다. 전장/로스터/조합/거래 로직은 변경하지 않았습니다.
