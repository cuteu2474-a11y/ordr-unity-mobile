# 스토리 요새 v1

`story-fortress-v1.png`: 스토리 전장 오른쪽의 공격 대상에 사용할 공통 요새 이미지 시안입니다. 내장 이미지 생성 도구로 만든 원본이며 바깥 배경은 투명합니다. 글자·이름·HP바·피격 이펙트는 포함하지 않습니다.

- 원본 비율을 유지해 작은 스토리 전장 높이에 맞춥니다. 발밑 기초부를 배치 기준으로 잡고 투명 여백을 고려합니다.
- 단계 이름과 실제 건물 HP는 게임에서 별도 표시합니다. 한 장을 여러 단계에 재사용합니다.
- 실제 피해 이벤트에 맞춰 번쩍임·짧은 흔들림을 표시하고, 파괴 시 페이드/축소합니다. 표시 애니메이션으로 피해를 다시 계산하지 않습니다.
- 기존 [OrdrStoryBattlefieldView.cs](../../Assets/Scripts/Unity/OrdrStoryBattlefieldView.cs)의 건물 표시를 교체하는 시안입니다. 건물 단계·원본 HP·공격 조건은 바꾸지 않습니다.
- 원본 PNG·사용법·생성 프롬프트를 GitHub에 보관했습니다. 원본은 1402×1122 RGBA PNG이며 모서리 알파 0을 확인했습니다. 게임 연결·Unity 실행·APK 제작은 하지 않았습니다.

전체 최종 프롬프트는 [prompt.txt](prompt.txt)에 있습니다. [checksums.json](checksums.json)에 원본 SHA-256·Git blob SHA를 기록했습니다.

다른 chat에서는 “`art/story-building-v1/README.md`를 읽고 요새 이미지를 사용해줘”라고 전달하시면 됩니다. 기존 적·공격 효과는 [적·이펙트 v1](../combat-v1/README.md)을 재사용합니다.
