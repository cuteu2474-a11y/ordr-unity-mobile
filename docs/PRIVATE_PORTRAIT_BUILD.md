# 개인용 캐릭터 초상화 빌드 (공개 GitHub / 비공개 이미지 분리)

공개 저장소에는 초상화 PNG·ZIP이 **없습니다**. 현재 개인용 비공개 묶음에는
흔함 C-01~C-09의 [ordr.kr](https://ordr.kr/) 도감 이미지 `C1.png`~`C9.png`만 들어갑니다.
원본 사이트의 `C8.png`와 `C9.png`는 같은 MARINE 이미지이므로 둘을 독립된 유닛 ID로
매핑하지만 실제 원본 그림은 같습니다. 추후 개인용 묶음에서 각 PNG를 교체할 수 있습니다.

## 개인 기기용 APK 생성

1. 개인용 초상화 ZIP을 **공개 저장소 밖**의 비공개 경로에 보관합니다.
   ZIP 루트에는 `C1.png`부터 `C9.png`까지 9개가 필요합니다.
2. 공개 Unity 프로젝트 루트에서 다음을 실행합니다.

   ```bash
   python3 tools/import_private_portraits.py --source /private/location/ORDR_Common_Private_Portraits.zip
   ```

3. 프로젝트를 Unity `6000.3.26f1`로 열어 이미지 9개가 Sprite로 임포트될 때까지
   기다립니다. `Assets/Resources/CharacterPortraits/C-01.png`~`C-09.png`가 있어야 합니다.
4. Unity Android 빌드(`Ordr.Mobile.Editor.OrdrAndroidDevelopmentBuild.Build`)를
   **개인 컴퓨터의 Unity Editor**에서 실행합니다. 이미지 9장은 `Resources.Load<Sprite>`에
   의해 APK 내부 리소스로 포함됩니다. 빌드 시 9장 누락·Sprite 임포트 실패·APK
   패킹 누락은 오류로 중단하고, `build/Android/ORDR-Mobile-Development.build.json`의
   `privatePortraitAssets`에 실제 포함 대상 9개를 기록합니다.

전장 아이콘, 보유 유닛 목록, 전체 도감과 상세 페이지에서 같은 ID 이미지가 표시됩니다.
해당 이미지가 없는 유닛은 기존 등급 색상의 원형 마커/텍스트가 유지됩니다.
원작 게임의 능력치·평타·소환/조합 규칙은 바꾸지 않습니다.

## 공개 저장소 보호 및 제한

- `.gitignore`는 `Assets/Resources/CharacterPortraits/` 전체(자동 생성 `.meta` 포함)를
  제외합니다. 이미지·ZIP을 공개 커밋/PR/이슈/릴리스/Actions 아티팩트에 첨부하지 마세요.
- GitHub Actions의 공개 CI에는 비공개 ZIP을 주입하지 않으므로 **공개 빌드의 APK는
  여전히 기본 마커**를 사용합니다. 개인 초상화가 포함된 APK는 위의 로컬 비공개
  빌드를 진행해야 하며 공개 CI 실행 성공만으로 포함을 증명할 수 없습니다.
- ZIP에서 파일을 읽을 때 `C1`~`C9` 이름만 허용하고 9개 모두 PNG 형식/크기 확인 후
  반영합니다. 파일이 누락/손상된 경우 부분 주입하지 않습니다.
- 개인용 비공개 사용과 이미지 저작권 허락은 별개의 문제입니다. 이미지 재배포 여부는
  원본 권리자 정책을 따르세요.
