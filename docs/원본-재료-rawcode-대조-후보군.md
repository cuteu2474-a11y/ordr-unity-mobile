# 원본 재료 rawcode 후보군 — 확정 매핑과 구분

> **자료의 위치:** 본문은 해당 기능의 원본 근거와 작성 당시 구현 기록입니다. 현재 실행 범위는 [START_HERE](START_HERE.md), 다음 작업은 [PLAYABLE_ROADMAP](PLAYABLE_ROADMAP.md), 운영 정책은 [AGENTS](../AGENTS.md)를 우선합니다. 본문의 과거 APK/테스트 보류·사거리·다음 작업을 현재 지시로 복사하지 않습니다.

> **기록 해석 기준:** 이 문서는 단계별 조사·구현 기록입니다. 본문의 미완료·미실행·다음 작업과 정책은 해당 작성 시점 기준이며, 현재 운영·전체 현황은 [최신 인수인계](새채팅-인수인계.md)의 ‘현재 작업 기준’을 우선합니다. 특히 과거 테스트 보류, 사거리 유지, 프로젝트/원본 수치 미확보 설명을 현재 상태로 적용하지 않습니다. 원본 규칙의 근거와 미검증 한계는 계속 구분합니다.

기준: ORDR S2 2.323[C] 악몽·이세계모드
작업 브랜치: `main`
자료: `data/original-jass-fusion-registry.json`, `data/original-unit-rawcode-label-links.json`, `data/verified-mobile-fusion-recipes.json`

## 정량 결과

- 원본 CWR 조합 등록 **266건**(테스트용 `TEST` 1건 포함).
- 기존 JASS 이름·능력 연동 **81종** 유지. 원본 객체의 최종 검증은 별개.
- 미매핑 재료 rawcode 기존 **163종** 유지. 새로 **확정한 객체 매핑은 0종**.
- 수집된 웹 도감 조합 **7건** 중 결과 rawcode를 기존 JASS 이름 연동으로 확인할 수 있는 **2개 조합**에 대해, 재료 수량과 GOLD/WOOD 합계가 일치하는 **순서 없는 후보군**을 기록.
- 이 2건의 후보군 안에 **서로 다른 재료 rawcode 7종**. 7종 모두 rawcode↔특정 카탈로그 ID를 **개별적으로 확정한 것이 아니다**.
- 나머지 5개 웹 조합은 결과 rawcode가 기존 81종 연결에 없어 이번 단계에서 JASS와 직접 교차 확인하지 못함.

## 교차 대응이 가능한 두 개의 조합

### 시류 (히든), H-14

원본 JASS: `YY14`, 결과 `h03J`
CWR 재료 rawcode 집합: `{h026, h01S, h01Z}`, 각 1개
웹 도감 재료 카탈로그 집합: `{R-25, R-17, R-09}`, 각 1개
GOLD/WOOD 추가 합계는 양쪽 자료에서 0/0이며 원본 추가 조건은 `UPUN/h0CJ/1`이다.

**금지되는 추론:** `h026=R-25`, `h01S=R-17`, `h01Z=R-09`로 위치 순서대로 대입하는 것. 조합식의 나열 순서는 유닛 ID의 1대1 근거가 아니다.

### 베르고 (히든), H-10

원본 JASS: `YY03`, 결과 `h03W`
CWR 재료 rawcode 집합: `{h02I, h01W, h014, h00Z}`, 각 1개
웹 도감 재료 카탈로그 집합: `{R-12, R-21, S-12, S-20}`, 각 1개
GOLD/WOOD 추가 합계는 양쪽 자료에서 0/0이며 원본 추가 조건은 `UPUN/H09E/1`, `UPUN/h040/1`이다.

**금지되는 추론:** `h02I=R-12`처럼 해당 목록끼리 순서상 같은 자리라는 이유로 매핑하는 것.

## 저장 데이터 / 도구

- `tools/reconcile_mobile_fusion_jass_candidates.py`: 7개 웹 레시피와 266개 CWR 등록식을 대조하고 **순서 없는 후보군**만 만든다. **게임 테스트가 아니며 조합 실행을 허용하지 않는다.**
- `data/original-jass-web-recipe-candidate-groups.json`: 결과 7개 행, 2개 후보군, 원본 조건/비용과 도감 출처.
- `tools/extract_w3u_fusion_unit_names.py`: **실제 원본** `war3map.w3u`를 확보하면 이름 필드와 선택적 `war3map.wts` 문자열을 추출해 후보군을 개별 검증할 수 있는 근거 자료를 작성한다.
- `OriginalVerifiedUnitObjectMap`: 원본 객체 개별 검수 및 SHA-256 증빙이 기록된 항목만 제작 가능 후보로 승인하는 로더.

## 같은 맵의 조건으로만 실제 제작 허용

- `OriginalJassFusionCatalog`은 `conditionsInOriginalOrder = true`와 원본 맵의 확정 SHA-256을 강제한다.
- `OriginalUnitRawcodeLabelCatalog`도 조합 레지스트리와 동일한 원본 맵 지문을 확인한다.
- `OriginalJassFusionOrderedWorldAdapter`는 거래 잠금 상태에서 **각 재료 인스턴스의 실제 Warcraft 객체 rawcode를 읽어 요구 rawcode와 직접 대조**한 뒤에만 소비 단계로 이동한다.
- 재료 인스턴스 중복 소비 및 조건 불일치, 예외 상황에서는 조합이 차단된다.
- **실제 원본 객체 데이터, CHu/CHx 후속 효과, 게임 월드의 독점 거래 세션 구현체는 아직 없다. 따라서 실제 조합 가능 수는 계속 0건이다.**

사용자 요청대로 전체 게임 기능 구현 전까지 Unity 컴파일, NUnit, 기기 시험, APK 빌드 및 GitHub Actions 실행/생성은 보류한다.
