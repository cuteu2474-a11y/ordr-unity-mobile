# 모바일 UI 폰트 및 배포 조건

이 폴더의 바이너리 파일은 Unity `Resources.Load<Font>("Fonts/<name>")`로 직접 불러옵니다. Windows의 설치 글꼴을 참조하지 않습니다.

## 메이플스토리체 (Maplestory-Bold.ttf, Maplestory-Light.ttf)
- 권리자: **㈜넥슨코리아**. 공식 출처: https://maplestory.nexon.com/Media/Font
- 용도: 게임 제목, 메뉴/탭/버튼, 큰 숫자와 강조 표시
- 넥슨 안내: 모든 개인/기업 사용자에게 무료 제공되며 자유로운 사용 및 배포 가능. **임의 수정·편집 금지**, 배포 원형 유지, 글꼴 단독 유료 판매 금지. 저작권 고지를 포함해 소프트웨어 번들/임베디드 사용 가능. 출처 표기 권장.
- 출처 표기: **이 프로젝트의 제목·메뉴 UI에는 넥슨 메이플스토리 서체가 사용됩니다.**
- 파일 출처: 공식 원본을 재배포하는 https://github.com/fonts-archive/Maplestory (Git revision `73d0a6b839137a3e84186a962168ea0e245df548`).
- `tools/sync_font_assets.py`가 파일을 재가공하지 않고 정확한 Git blob SHA-1과 바이트 수를 검증합니다.

## Pretendard (Pretendard-Regular.otf)
- 권리자/제작자: Kil Hyung-jin 및 라이선스 고지에 열거된 프로젝트.
- 용도: 설명, 안내문, 상태 표시, 작은 한글 텍스트 (맑은 고딕의 합법적인 대체 폰트)
- SIL Open Font License 1.1: 이 폴더의 `Pretendard-OFL.txt`에 저작권 및 전체 라이선스 포함.
- 공식 출처: https://github.com/orioncactus/pretendard (Git revision `7aeb0698819be2b4097dae8ec8fe6a795e5cf3ae`).

## 맑은 고딕을 넣지 않은 이유

Microsoft 맑은 고딕(Malgun Gothic)은 Windows에 설치되는 글꼴로, 별도의 소프트웨어/게임 재배포 라이선스가 없는 경우 APK 또는 GitHub에 글꼴 파일을 포함할 수 없습니다. 따라서 이 저장소에는 넣지 않습니다.

Microsoft 안내: https://learn.microsoft.com/en-us/typography/fonts/font-faq

## 적용 / 빌드

- `OrdrMobileUiTheme.ApplyBundledFonts()`는 `Assets/Resources/Fonts`에서 글꼴을 불러와 IMGUI 스타일에 할당합니다.
- 바이너리가 아직 GitHub에 없다면 기존 폰트가 유지됩니다. 동기화 완료 전에는 '적용 완료'로 표시하지 않습니다.
- 폰트 출처/버전이 변경되면 이 문서와 `tools/sync_font_assets.py`의 원본 해시를 함께 감사해야 합니다.
- 기존 Unity 빌드 설정/게임 규칙은 건드리지 않습니다.
