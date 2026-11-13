# 카툰풍 해적 UI 원본 에셋 (v1)

2026-11-13 ChatGPT 라이브러리에 보관된 원본 PNG 6개를 **변환·압축·리사이즈 없이** 가져온 보관용 묶음입니다. 기존 게임 UI 이미지나 코드는 삭제·교체하지 않았습니다.

| 파일 | 라이브러리 원본 | 용도 |
| --- | --- | --- |
| `button-wide-teal-gold.png` | 금빛 테두리의 청록색 UI 버튼.png | 일반 메뉴·조합 등 공통 와이드 버튼 |
| `button-wide-orange-gold.png` | 황금 테두리 주황 버튼.png | 강화·확인 등 강조 버튼 |
| `button-square-teal-gold.png` | 청록색 금빛 테두리 아이콘 버튼.png | 아이콘 전용 정사각 버튼 |
| `panel-parchment-wide.png` | 장식 모서리의 와이드 양피지 패널.png | 안내·상세 정보용 패널 |
| `reference-common-panels-states.png` | 공통 패널과 버튼 상태 보드.png | 팝업 및 기본·눌림·선택·비활성 상태 참고 시트 |
| `reference-comic-adventure-pirate.png` | 코믹 어드벤처 해적 UI 보드.png | 전체 콘셉트·버튼·색상 조합 참고 시트 |

**적용 상태:** 이 디렉터리는 원본 이미지 보관 경로(`art/`)입니다. `Assets/Resources/OrdrArt/Ui/`에 임포트하여 실제 UI에 연결하는 작업과 APK 빌드 검증은 별도입니다.

적용 시 가로 버튼·양피지 패널은 적절한 9-slice 또는 스프라이트 분할을 검토하고, 버튼 글자·재화·레벨·선택 상태는 런타임 텍스트 및 로직으로 처리합니다. `reference-*` 이미지는 전체 UI 예시 보드이므로 그대로 버튼 스프라이트처럼 사용하지 않습니다.
