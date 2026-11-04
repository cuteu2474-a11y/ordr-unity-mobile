# 개인용 캐릭터 초상화 빌드 (공개 GitHub / 비공개 이미지 분리)

공개 저장소에는 제3자 초상화 PNG·ZIP이 **없습니다**. 대신 게임 화면은
[ordr.kr](https://ordr.kr/character) 도감에 해당 이미지가 있으면 HTTPS로 필요한
캐릭터 사진만 요청하고, Android 기기의 앱 저장 공간에 저장해 다음 화면·실행에서 재사용합니다.
사진이 아직 내려오지 않았거나 원본에 없는 캐릭터는 기존 사각형 등급 그림을 표시합니다.
다운로드는 게임 실행 중에만 최대 3건씩 진행하며 전투/획득/조합 기능은 건드리지 않습니다.

네트워크 없이 처음부터 초상화를 쓰고 싶은 **개인용 APK**는 다음 방법으로
사진을 앱 안에 미리 넣을 수 있습니다. 미리 넣은 사진이 다운로드 사진보다 우선합니다.

## 개인 기기용 APK 생성

1. 사용 권한이 있는 개인용 초상화 PNG 또는 ZIP을 **공개 저장소 밖**에 보관합니다.
   ZIP 루트에는 `C1.png`, `TR24.png`, `E9.png` 등의 도감 파일 이름을 씁니다.
   `C-01.png`, `TR-24.png` 등 ID 형태도 허용합니다. 287개 유닛 중 원하는
   일부만 넣어도 됩니다. 등록되지 않은 이름과 ZIP 내부 경로는 읽지 않습니다.
2. 공개 Unity 프로젝트 루트에서 다음을 실행합니다.

   ```bash
   python3 tools/import_private_portraits.py --source /private/location/ORDR_Common_Private_Portraits.zip
   ```

3. 프로젝트를 Unity `6000.3.26f1`로 열어 해당 PNG들이 Sprite로 임포트될 때까지
   기다립니다. 예: `Assets/Resources/CharacterPortraits/TR-24.png`.
4. Unity Android 빌드(`Ordr.Mobile.Editor.OrdrAndroidDevelopmentBuild.Build`)를
   **개인 컴퓨터의 Unity Editor**에서 실행합니다. 제공한 사진은 `Resources.Load<Sprite>`로
   APK 내부에 포함됩니다. Sprite 임포트나 APK 패킹 누락은 오류로 중단하며,
   `build/Android/ORDR-Mobile-Development.build.json`의 `privatePortraitAssets`에
   실제 포함된 사진 목록을 기록합니다.

전장 아이콘과 보유 유닛 목록·상세 화면은 같은 유닛 ID의 이미지를 사용합니다.
전장에 이미 등장한 유닛도 이미지 다운로드가 끝나면 즉시 아이콘이 교체됩니다.
이미지가 없는 유닛은 기존 등급 사각 아이콘을 유지합니다.
원작 게임의 능력치·평타·소환/조합 규칙은 바꾸지 않습니다.

## 공개 저장소 보호 및 제한

- `.gitignore`는 `Assets/Resources/CharacterPortraits/` 전체(자동 생성 `.meta` 포함)를
  제외합니다. 이미지·ZIP을 공개 커밋/PR/이슈/릴리스/Actions 아티팩트에 첨부하지 마세요.
- 공개 GitHub Actions APK는 개인 초상화를 포함하지 않고, 실행 시 원본 사이트에서
  사진을 불러오는 방식입니다. 앱에서 원본 서버 접속이 불가능하면 기본 아이콘이 유지됩니다.
  개인 초상화가 **APK 내부**에 포함되었는지는 개인 빌드의 manifest로 확인해야 합니다.
- ZIP은 정상 도감 ID 이름·PNG 구조·4MB 용량·최대 2048px 크기를 검증합니다.
  잘못된 이미지가 하나라도 있으면 해당 묶음은 쓰지 않습니다. 없는 캐릭터의 이미지를
  임의로 채우지 않으며, ZIP 내부 파일 경로는 추출하지 않습니다.
- 개인용 비공개 사용과 이미지 저작권 허락은 별개의 문제입니다. 이미지 재배포 여부는
  원본 권리자 정책을 따르세요.
