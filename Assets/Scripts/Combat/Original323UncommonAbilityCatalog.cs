using System;
using System.Collections.Generic;
using System.Linq;
using Ordr.Mobile.Data;

namespace Ordr.Mobile.Combat
{
    // Extracted from S2 2.323[C] Units/AbilityData.slk +
    // Units/CampaignAbilityStrings.txt (W3X SHA-256 below).
    // Warcraft native passives, CWR fusion commands, shop/storage and JASS
    // callbacks must NOT be treated as interchangeable combat skills.
    public enum OriginalUncommonProcKind
    {
        MultiTarget, BashStun, Critical, LineMagic, BonusPhysical, BonusPhysicalStun
    }

    public sealed class OriginalUncommonProc
    {
        public string UnitId { get; private set; }
        public string OriginalUnitRawcode { get; private set; }
        public string AbilityRawcode { get; private set; }
        public string ProcId { get; private set; }
        public string Name { get; private set; }
        public OriginalUncommonProcKind Kind { get; private set; }
        public int ChancePerMillion { get; private set; }
        public double BonusDamage { get; private set; }
        public double CriticalMultiplier { get; private set; }
        public double NormalStunSeconds { get; private set; }
        public double HeroStunSeconds { get; private set; }
        public int NativeTargetCount { get; private set; }
        public string Evidence { get { return "ORDR 2.323[C] W3X Units/AbilityData.slk + CampaignAbilityStrings.txt"; } }

        internal OriginalUncommonProc(string owner, string raw, string ability,
            string suffix, string name, OriginalUncommonProcKind kind, int chance,
            double damage, double multiplier, double normalStun, double heroStun,
            int targetCount = 1)
        {
            if (string.IsNullOrWhiteSpace(owner) || raw == null || raw.Length != 4 ||
                ability == null || ability.Length != 4 || string.IsNullOrWhiteSpace(suffix) ||
                string.IsNullOrWhiteSpace(name) || chance < 0 || chance > 1000000 ||
                targetCount < 1 || damage < 0 || multiplier < 0 ||
                normalStun < 0 || heroStun < 0)
                throw new ArgumentException("원본 안흔함 능력 근거/발동 데이터 오류");
            UnitId = owner; OriginalUnitRawcode = raw;
            AbilityRawcode = ability; ProcId = ability + ":" + suffix;
            Name = name; Kind = kind; ChancePerMillion = chance;
            BonusDamage = damage; CriticalMultiplier = multiplier;
            NormalStunSeconds = normalStun; HeroStunSeconds = heroStun;
            NativeTargetCount = targetCount;
        }
    }

    // Provides the COMPLETE source-verified effect (including hero status,
    // damage school/armor, targeting, additional hit and/or line geometry).
    // A tooltip value alone never approves execution. No runtime port means
    // no proc RNG roll, damage, stun, or unintended half-skill.
    public interface IOriginalUncommonVerifiedProcWorld
    {
        bool SupportsCompleteEffect(OriginalUncommonProc proc);
        bool CanAffectTarget(OriginalUncommonProc proc, string attackerId, CombatEnemy target);
        bool TryApplyCompleteEffect(OriginalUncommonProc proc,
            CharacterCombatRuntime world, string attackerId,
            string targetId, out string originalProof);
    }

    public sealed class Original323UncommonAbilityCatalog
    {
        public const string SourceW3xSha256 =
            "0794996c9bc73eaa3020f20ebc45fb37cfa64a6e98ffe8aa0dfa3b356e77bc5c";
        public const string AbilityDataSlkSha256 =
            "23e9eb93d805a63cc76bfb617189331aef1e40c0c0ec69860bd7399a3bef2eda";
        public const string AbilityStringsSha256 =
            "1e6d9f5ccd22f67ad0edf5264bce77aa6b944032881b81ab7051f6bebf97b979";

        private sealed class OriginalUncommonUnit
        {
            public string Id;
            public string Rawcode;
            public string Abilities;
            public OriginalUncommonUnit(string id, string raw, string codes)
            { Id = id; Rawcode = raw; Abilities = codes; }
        }

        private static readonly OriginalUncommonUnit[] PinnedUnits = {
            new OriginalUncommonUnit("U-01", "h00O", "A07Y,A0JE,A0B8,A07M,A03E,Avul"),
            new OriginalUncommonUnit("U-02", "h00C", "A07Y,A0JE,A0B8,A01X,A017,Avul"),
            new OriginalUncommonUnit("U-03", "h00D", "A07Y,A0JE,A0B8,A07P,A00K,A019,A1A0,Avul"),
            new OriginalUncommonUnit("U-04", "h00M", "A07Y,A0JE,A0B8,A07I,A024,A01A,Avul"),
            new OriginalUncommonUnit("U-05", "h00K", "A07Y,A0JE,A0B8,A07R,A00S,A01E,Avul"),
            new OriginalUncommonUnit("U-06", "h00I", "A07Y,A0JE,A0B8,A00Z,A01I,Avul"),
            new OriginalUncommonUnit("U-07", "h00N", "A07Y,A0JE,A0B8,A01D,A01K,Avul"),
            new OriginalUncommonUnit("U-08", "h00G", "A07Y,A0B8,A00B,A014,A1A1,Avul,A0JE"),
            new OriginalUncommonUnit("U-09", "h00E", "A07Y,A0JE,A07Q,A0B8,A02B,A01C,Avul"),
            new OriginalUncommonUnit("U-10", "h00J", "A07Y,A0JE,A0B8,A030,A01N,Avul"),
            new OriginalUncommonUnit("U-11", "h00L", "A07Y,A0JE,A0B8,A02Y,A01Q,Avul"),
            new OriginalUncommonUnit("U-12", "h00F", "A07Y,A0JE,A07S,A0B8,A0N8,A01P,Avul"),
            new OriginalUncommonUnit("U-13", "h00A", "A07Y,A0JE,A0B8,A07I,A0NA,A01O,Avul"),
            new OriginalUncommonUnit("U-14", "h00H", "A029,Avul")
        };

        private readonly Dictionary<string, IReadOnlyList<OriginalUncommonProc>> byUnit =
            new Dictionary<string, IReadOnlyList<OriginalUncommonProc>>(StringComparer.Ordinal);
        private readonly Dictionary<string, OriginalUncommonProc> byUnitAndProc =
            new Dictionary<string, OriginalUncommonProc>(StringComparer.Ordinal);
        private readonly Dictionary<string, IReadOnlyList<string>> objectAbilities =
            new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);

        public int UnitCount { get { return byUnit.Count; } }
        public int NativeProcCount { get { return byUnitAndProc.Count; } }

        // Original WC3 tooltips and *native* SLK rows, not guessed web-DEX skills.
        private static IEnumerable<OriginalUncommonProc> Evidence()
        {
            yield return new OriginalUncommonProc("U-01", "h00O", "A07M", "multi3",
                "꽃꽃 열매: 3명 동시 공격", OriginalUncommonProcKind.MultiTarget,
                1000000, 0, 0, 0, 0, 3);
            yield return new OriginalUncommonProc("U-03", "h00D", "A07P", "bash",
                "잠의 노래 프람: 10% 기절", OriginalUncommonProcKind.BashStun,
                100000, 0, 0, .3, .15);
            yield return new OriginalUncommonProc("U-04", "h00M", "A07I", "critical",
                "육식: 15% 1.5배 크리티컬", OriginalUncommonProcKind.Critical,
                150000, 0, 1.5, 0, 0);
            yield return new OriginalUncommonProc("U-04", "h00M", "A07I", "rankyaku",
                "람각: 10% 직선 마법 150", OriginalUncommonProcKind.LineMagic,
                100000, 150, 0, 0, 0);
            yield return new OriginalUncommonProc("U-05", "h00K", "A07R", "flame",
                "이글이글: 10% 추가 물리 200", OriginalUncommonProcKind.BonusPhysical,
                100000, 200, 0, 0, 0);
            yield return new OriginalUncommonProc("U-09", "h00E", "A07Q", "shigure",
                "시구레: 20% 물리 200 + 기절", OriginalUncommonProcKind.BonusPhysicalStun,
                200000, 200, 0, .7, .35);
            yield return new OriginalUncommonProc("U-12", "h00F", "A07S", "fishman",
                "어인: 20% 물리 150 + 기절", OriginalUncommonProcKind.BonusPhysicalStun,
                200000, 150, 0, .1, .05);
            yield return new OriginalUncommonProc("U-13", "h00A", "A07I", "critical",
                "육식: 15% 1.5배 크리티컬", OriginalUncommonProcKind.Critical,
                150000, 0, 1.5, 0, 0);
            yield return new OriginalUncommonProc("U-13", "h00A", "A07I", "rankyaku",
                "람각: 10% 직선 마법 150", OriginalUncommonProcKind.LineMagic,
                100000, 150, 0, 0, 0);
        }

        public Original323UncommonAbilityCatalog(OriginalMapBaseStatCatalog map)
        {
            if (map == null) throw new ArgumentNullException(nameof(map));
            if (map.SourceW3xSha256 != SourceW3xSha256)
                throw new FormatException("원본 안흔함 스킬 W3X 버전이 다릅니다.");
            var mutable = new Dictionary<string, List<OriginalUncommonProc>>(StringComparer.Ordinal);
            foreach (var pinned in PinnedUnits)
            {
                var row = map.Find(pinned.Id);
                if (row == null || row.OriginalRawcode != pinned.Rawcode ||
                    row.Attack1 == null || row.CombatParityVerified ||
                    string.Join(",", row.OriginalAbilityRawcodes) != pinned.Abilities)
                    throw new FormatException("안흔함 W3X 오브젝트/능력 순서가 다릅니다: " + pinned.Id);
                mutable.Add(pinned.Id, new List<OriginalUncommonProc>());
                objectAbilities.Add(pinned.Id,
                    new List<string>(row.OriginalAbilityRawcodes).AsReadOnly());
            }
            foreach (var proc in Evidence())
            {
                var source = map.Find(proc.UnitId);
                if (source == null || source.OriginalRawcode != proc.OriginalUnitRawcode ||
                    !objectAbilities[proc.UnitId].Contains(proc.AbilityRawcode) ||
                    byUnitAndProc.ContainsKey(proc.UnitId + "/" + proc.ProcId))
                    throw new FormatException("안흔함 W3X 네이티브 스킬 원본 링크 오류: " +
                        proc.UnitId + "/" + proc.ProcId);
                mutable[proc.UnitId].Add(proc);
                byUnitAndProc.Add(proc.UnitId + "/" + proc.ProcId, proc);
            }
            foreach (var entry in mutable)
                byUnit.Add(entry.Key, entry.Value.AsReadOnly());
        }

        public IReadOnlyList<OriginalUncommonProc> ForUnit(string unitId)
        {
            IReadOnlyList<OriginalUncommonProc> found;
            return unitId != null && byUnit.TryGetValue(unitId, out found)
                ? found : Array.Empty<OriginalUncommonProc>();
        }

        public IReadOnlyList<string> MapAbilitiesFor(string unitId)
        {
            IReadOnlyList<string> found;
            return unitId != null && objectAbilities.TryGetValue(unitId, out found)
                ? found : Array.Empty<string>();
        }

        public OriginalUncommonProc FindProc(string unitId, string procId)
        {
            OriginalUncommonProc found;
            return unitId != null && procId != null &&
                byUnitAndProc.TryGetValue(unitId + "/" + procId, out found)
                ? found : null;
        }
    }
}
