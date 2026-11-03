using System;
using System.Collections.Generic;

namespace Ordr.Mobile.Combat
{
    // Explicit additional approval for OFFLINE model packets only. These
    // catalog skills are absent from the existing source-script callback and
    // next-thirty lists. Native object critical/stun skills already modeled
    // at the same probability were deliberately excluded, not duplicated.
    public sealed class SourceModelUnconnectedSkillRow
    {
        public string CatalogId { get; private set; }
        public string Rawcode { get; private set; }
        public IReadOnlyList<string> PracticeAttackSkills { get; private set; }
        public IReadOnlyList<string> PracticeActiveSkills { get; private set; }
        internal SourceModelUnconnectedSkillRow(string id, string rawcode,
            string[] attackSkills, string[] activeSkills)
        {
            CatalogId = id;
            Rawcode = rawcode;
            PracticeAttackSkills = Array.AsReadOnly(attackSkills);
            PracticeActiveSkills = Array.AsReadOnly(activeSkills);
        }
    }

    public static class SourceModelUnconnectedSkillCatalog
    {
        // Source-tooltip-complete *practice* casts only. No inferred JASS cooldown,
        // Warcraft resistance, or non-listed mixed effects are granted.
        private static readonly SourceModelUnconnectedSkillRow[] Entries = {
            new SourceModelUnconnectedSkillRow("MY-07", "h083",
                Array.Empty<string>(), new[] { "가스터 블래스터" }),
            new SourceModelUnconnectedSkillRow("MY-08", "h08M",
                new[] { "스타캐치 애로우" }, new[] { "요르 포저", "로이드 포저" }),
            new SourceModelUnconnectedSkillRow("L-32", "h030",
                Array.Empty<string>(), new[] { "우르스스 쇼크" }),
            new SourceModelUnconnectedSkillRow("TR-11", "H092",
                new[] { "불꽃 주먹", "넓은 주먹", "화염용왕" },
                new[] { "마나 범위 타격" }),
            new SourceModelUnconnectedSkillRow("TR-19", "H09B",
                new[] { "화염성", "저격", "치명타" }, new[] { "파이어 버드 스타" }),
            new SourceModelUnconnectedSkillRow("I-01", "h04C",
                new[] { "무장색 패기", "기본 공격 범위 추가" },
                new[] { "특제 대포환" }),
            new SourceModelUnconnectedSkillRow("MY-04", "h09W",
                new[] { "비뢰신 이의 단 나선환" },
                new[] { "나선섬광초륜무후삼식" }),
            new SourceModelUnconnectedSkillRow("MY-06", "h09N",
                Array.Empty<string>(), new[] { "이성의 창조물" }),
            // I-10 has no already-registered source attack callback here.
            // Its native object supplies the exact 160 mana maximum; only
            // this complete tooltip packet is approved for automatic casts.
            new SourceModelUnconnectedSkillRow("I-10", "h04G",
                new[] { "매 타격 스턴" }, new[] { "마나 광역 공격" }),
            // First composite-effect batch: reuse the same active resource,
            // lane, target and damage execution path as existing practice casts.
            new SourceModelUnconnectedSkillRow("I-09", "h04B",
                new[] { "범위 마법 스턴", "물리 마법 혼합" }, new[] { "마나 광역 스턴" }),
            new SourceModelUnconnectedSkillRow("L-28", "h03G",
                new[] { "단일 타격 스턴" }, new[] { "어인가라데" }),
            new SourceModelUnconnectedSkillRow("D-03", "h0CI",
                new[] { "빙귀", "역재" }, new[] { "브라키오 붐버" }),
            // Second composite batch: new complete active tooltip packets.
            new SourceModelUnconnectedSkillRow("SR-03", "h0A0",
                new[] { "단일 타격" }, new[] { "체력 소모 광역공격" }),
            new SourceModelUnconnectedSkillRow("TR-08", "H099",
                new[] { "평타 광역 연계", "기간트 개틀링" }, new[] { "레드호크" }),
            new SourceModelUnconnectedSkillRow("TR-29", "H08X",
                new[] { "중력장", "중력검" }, new[] { "씽크홀" }),
            new SourceModelUnconnectedSkillRow("LI-06", "h08Q",
                Array.Empty<string>(), new[] { "범위 광역 스턴" }),
            new SourceModelUnconnectedSkillRow("H-19", "h07L",
                new[] { "범위 마법·증폭", "라인 체력 비례" }, Array.Empty<string>()),
            new SourceModelUnconnectedSkillRow("L-24", "h02O", new[] { "산화 부지화" }, Array.Empty<string>()),
            new SourceModelUnconnectedSkillRow("L-35", "h087", new[] { "시간시간열매" }, Array.Empty<string>()),
            new SourceModelUnconnectedSkillRow("T-02", "h05W",
                new[] { "공작슬래셔", "공작스트링" }, Array.Empty<string>()),
            new SourceModelUnconnectedSkillRow("LI-01", "h05G",
                new[] { "핏빛 범위 공격", "광역 마법 공격", "단일 스턴",
                    "평타 단일 고정" }, Array.Empty<string>()),
            new SourceModelUnconnectedSkillRow("LI-02", "h05I",
                new[] { "단일 스턴", "광역 물리 둔화", "물리 연계" }, Array.Empty<string>()),
            new SourceModelUnconnectedSkillRow("LI-04", "h084", new[] { "광역 마법" }, Array.Empty<string>()),
            new SourceModelUnconnectedSkillRow("LI-07", "h05E",
                new[] { "낙뢰 4연타" }, new[] { "뇌영" }),
            new SourceModelUnconnectedSkillRow("D-01", "h04U",
                new[] { "범위 마법", "13타 고정피해" }, Array.Empty<string>()),
            new SourceModelUnconnectedSkillRow("TR-12", "H09H",
                new[] { "불꽃 스펙트럼", "무통 샷", "검은다리" },
                new[] { "유성소바 킥" }),
            new SourceModelUnconnectedSkillRow("TR-15", "H08U",
                new[] { "해류조정", "고대병기 포세이돈" }, new[] { "해왕류 다이브" }),
            new SourceModelUnconnectedSkillRow("TR-16", "H097",
                new[] { "얼음얼음 열매", "페잔트백", "아이스 사브르", "아이스 에이지" },
                Array.Empty<string>()),
            new SourceModelUnconnectedSkillRow("TR-20", "H09F",
                new[] { "삼도류 - 까마귀 마물 사냥", "사자의 노래 Lv.2",
                    "삼도류 - 극 호랑이 사냥" }, new[] { "1080 번뇌봉" }),
            new SourceModelUnconnectedSkillRow("TR-22", "H09A",
                new[] { "물리 추가 타격", "해류 공격", "진각" }, new[] { "어인가라데" }),
            new SourceModelUnconnectedSkillRow("TR-24", "H0CO",
                new[] { "육식 Type 코비", "기본 공격 특수 피해", "콤비네이션 어택" },
                new[] { "어니스티 임팩트" }),
            new SourceModelUnconnectedSkillRow("TR-26", "H0B5",
                new[] { "빛의 기둥", "팔지경", "레이저", "마나 연타 광역기", "단일 치명타" }, Array.Empty<string>()),
            new SourceModelUnconnectedSkillRow("E-07", "h059",
                new[] { "불기둥", "불주먹", "적 중심 마법" }, new[] { "대염계 염제" }),
            new SourceModelUnconnectedSkillRow("E-09", "h067",
                new[] { "확률 광역 스턴", "음표 병사 연계", "3연타 마법", "라인·보스 추가 피해" },
                new[] { "광역 노래 마법" }),
            new SourceModelUnconnectedSkillRow("E-10", "h05B",
                new[] { "혼합 범위 타격", "치명타 단일 스턴" }, Array.Empty<string>()),
            new SourceModelUnconnectedSkillRow("I-02", "h04D",
                new[] { "범위 마법 스턴" }, new[] { "마나 낙뢰" }),
            new SourceModelUnconnectedSkillRow("I-07", "h04E",
                new[] { "광역 마법 스턴", "마법 타격" }, Array.Empty<string>()),
            new SourceModelUnconnectedSkillRow("I-08", "h04F",
                new[] { "단일 물리 타격", "타격·스턴", "기본 공격 추가 고정피해" },
                new[] { "전체체력 비례 고정피해" }),
            new SourceModelUnconnectedSkillRow("RN-01", "h06U", new[] { "미닛츠 스파이크" }, Array.Empty<string>()),
            new SourceModelUnconnectedSkillRow("RN-02", "h06V", new[] { "대옥 나선환" }, Array.Empty<string>()),
            new SourceModelUnconnectedSkillRow("RN-07", "h09K", new[] { "광역 마법" }, Array.Empty<string>()),
            new SourceModelUnconnectedSkillRow("RN-12", "h071",
                new[] { "검은 월아천충", "천쇄참월" }, Array.Empty<string>()),
            new SourceModelUnconnectedSkillRow("RN-13", "h06T",
                new[] { "비상식량" }, new[] { "슈퍼 페이몬 선풍" }),
            new SourceModelUnconnectedSkillRow("MY-03", "h0A2", new[] { "은폐 나이프" }, Array.Empty<string>()),
            new SourceModelUnconnectedSkillRow("MY-05", "h08I", new[] { "만해 천본앵경엄" }, new[] { "종경백제검" }),
            new SourceModelUnconnectedSkillRow("MY-09", "h0A6", new[] { "비광충", "생사의 경계" }, Array.Empty<string>()),
            new SourceModelUnconnectedSkillRow("MY-12", "h0BC",
                new[] { "짓누르기" }, new[] { "염동 회오리", "염동 운석낙하" }),
            new SourceModelUnconnectedSkillRow("MY-13", "h0AC", new[] { "권총 던지기" }, Array.Empty<string>()),
            // Fourth attack batch: two new owners with original map identities.
            new SourceModelUnconnectedSkillRow("L-09", "h02R",
                new[] { "육왕건", "육식 Lv4" }, Array.Empty<string>()),
            new SourceModelUnconnectedSkillRow("TR-07", "H08W",
                new[] { "격투 스턴", "람각", "지건", "육왕건" }, Array.Empty<string>()),
            new SourceModelUnconnectedSkillRow("R-42", "h01M",
                new[] { "단일 타격·스턴" }, Array.Empty<string>()),
            // Sixth batch: already-modeled native critical/stun entries are
            // excluded, including Shanks, Snakeman, Shirahoshi and Jinbe.
            new SourceModelUnconnectedSkillRow("TR-13", "H08Z",
                new[] { "원형 마법 스턴", "자신중심 마법 스턴" }, Array.Empty<string>()),
            new SourceModelUnconnectedSkillRow("E-12", "h05C",
                new[] { "혼합 공격", "혼합 스턴 타격" }, Array.Empty<string>()),
            new SourceModelUnconnectedSkillRow("I-03", "h049",
                new[] { "기본 공격 광역 추가", "범위 마법" }, Array.Empty<string>()),
            new SourceModelUnconnectedSkillRow("H-20", "h046",
                new[] { "눈보라" }, new[] { "잔설검" }),
            new SourceModelUnconnectedSkillRow("H-14", "h03J",
                new[] { "흉포한 검술" }, new[] { "혈우" }),
            // Fifteenth batch: verified source-tooltip identities only.
            // The existing full-packet gate rejects unimplemented formulas.
            new SourceModelUnconnectedSkillRow("L-26", "h03I",
                new[] { "무장색의 패기" }, Array.Empty<string>()),
            new SourceModelUnconnectedSkillRow("L-31", "h03E",
                new[] { "육식 - 체" }, Array.Empty<string>()),
            new SourceModelUnconnectedSkillRow("TR-17", "H095",
                new[] { "범위 스턴 타격" }, Array.Empty<string>()),
            new SourceModelUnconnectedSkillRow("T-05", "h07J",
                new[] { "고속전격" }, Array.Empty<string>()),
            new SourceModelUnconnectedSkillRow("MY-11", "h09R",
                new[] { "마왕", "스타 플라티나" }, Array.Empty<string>()),
            new SourceModelUnconnectedSkillRow("R-17", "h01S",
                new[] { "단일 물리 스턴" }, Array.Empty<string>()),
            new SourceModelUnconnectedSkillRow("X-04", "h05O",
                new[] { "조선공의 망치" }, Array.Empty<string>()),
            new SourceModelUnconnectedSkillRow("TR-03", "H09E",
                new[] { "범위 물리 타격" }, Array.Empty<string>()),
            new SourceModelUnconnectedSkillRow("TR-27", "H05N",
                new[] { "육식 체" }, Array.Empty<string>()),
            new SourceModelUnconnectedSkillRow("T-01", "h05S",
                new[] { "오버히트", "실실열매" }, Array.Empty<string>()),
            new SourceModelUnconnectedSkillRow("LI-05", "h040",
                new[] { "2연타 마법", "3연타 마법", "단일 스턴" }, Array.Empty<string>()),
            new SourceModelUnconnectedSkillRow("L-18", "h036",
                new[] { "사람사람열매" }, Array.Empty<string>()),
            new SourceModelUnconnectedSkillRow("H-21", "h045",
                new[] { "돌격 연타" }, Array.Empty<string>()),
            new SourceModelUnconnectedSkillRow("RN-14", "h072",
                new[] { "고양이 발톱" }, Array.Empty<string>()),
            new SourceModelUnconnectedSkillRow("I-12", "h04A",
                Array.Empty<string>(), new[] { "체력 소모 범위 공격" }),
            // 16th batch: new source-object owners; no fake E-02 Nika ID,
            // duplicate Vivi B59/B52 basic area hit or TR-28 BzH callback.
            new SourceModelUnconnectedSkillRow("E-05", "h05A",
                new[] { "버기 필살타", "전투 아군 공격력 버프" }, Array.Empty<string>()),
            new SourceModelUnconnectedSkillRow("D-02", "h03V",
                new[] { "기와 돌려차기", "단일 스턴" }, new[] { "어인공수도" }),
            new SourceModelUnconnectedSkillRow("H-15", "h03L",
                new[] { "일반 포격" }, new[] { "베가펑크 지식 탑재 포격" }),
        };
        public static readonly IReadOnlyList<SourceModelUnconnectedSkillRow> All =
            Array.AsReadOnly(Entries);
        private static readonly Dictionary<string, SourceModelUnconnectedSkillRow> ByRawcode =
            new Dictionary<string, SourceModelUnconnectedSkillRow>(StringComparer.Ordinal);

        static SourceModelUnconnectedSkillCatalog()
        {
            foreach (var row in Entries)
            {
                if (row.Rawcode.Length != 4 || ByRawcode.ContainsKey(row.Rawcode) ||
                    row.PracticeAttackSkills.Count + row.PracticeActiveSkills.Count == 0)
                    throw new InvalidOperationException("유닛 스킬 원본 코드 중복: " + row.Rawcode);
                ByRawcode.Add(row.Rawcode, row);
            }
        }

        public static bool TryGet(string originalRawcode,
            out SourceModelUnconnectedSkillRow row)
        {
            row = null;
            return originalRawcode != null &&
                ByRawcode.TryGetValue(originalRawcode, out row);
        }

        public static bool AllowsActive(SourceModelUnconnectedSkillRow row, string name)
        {
            if (row == null || string.IsNullOrEmpty(name)) return false;
            foreach (var allowed in row.PracticeActiveSkills)
                if (string.Equals(allowed, name, StringComparison.Ordinal)) return true;
            return false;
        }

        public static int ApprovedAttackPacketCount
        {
            get
            {
                int count = 0;
                foreach (var row in Entries) count += row.PracticeAttackSkills.Count;
                return count;
            }
        }
    }
}
