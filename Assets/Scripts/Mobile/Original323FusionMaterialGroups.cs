using System;
using System.Collections.Generic;
using Ordr.Mobile.Data;

namespace Ordr.Mobile.Mobile
{
    // CWR material buckets are distinct from the unit's real object identity.
    // CHy registers H099 and H0B2 under H08T through BH0. H08T is NOT
    // another free actor, a third Luffy, or a result-object alias.
    public interface IOriginalJassFusionMaterialGroups
    {
        bool TryGetMaterialGroup(string groupRawcode, out IReadOnlyList<string> catalogIds);
    }

    public sealed class Original323FusionMaterialGroups : IOriginalJassFusionMaterialGroups
    {
        private readonly IReadOnlyList<string> luffy;
        public Original323FusionMaterialGroups(OriginalMapBaseStatCatalog original)
        {
            if (original == null || original.SourceW3xSha256 !=
                Ordr.Mobile.Core.OriginalCommonSaleRules.OriginalMapSha256 ||
                original.Find("TR-08")?.OriginalRawcode != "H099" ||
                original.Find("TR-14")?.OriginalRawcode != "H0B2")
                throw new ArgumentException("원본 루피 조합 재료 분류가 일치하지 않습니다.");
            luffy = Array.AsReadOnly(new[] { "TR-08", "TR-14" });
        }
        public bool TryGetMaterialGroup(string rawcode, out IReadOnlyList<string> catalogIds)
        {
            catalogIds = rawcode == "H08T" ? luffy : null;
            return catalogIds != null;
        }
    }
    // Source-backed PICK candidate preview, deliberately NOT a spending approval.
    // Every original random-only object RN-01..RN-14 owns A800 in the W3X object
    // ability list; the actual BGI(PICK,A800) removal callback is still unaudited.
    public interface IOriginalJassPickMaterialPreview
    {
        bool TryPreview(OriginalJassFusionInstruction instruction,
            IReadOnlyList<MobileUnitSnapshot> owned,
            Func<long, bool> storyDeployed, ISet<long> reserved,
            out IReadOnlyList<long> candidates, out string label);
    }

    public sealed class Original323MysticPickCandidatePreview : IOriginalJassPickMaterialPreview
    {
        public const string NativeRandomAbility = "A800";
        private readonly OriginalMapBaseStatCatalog source;

        public Original323MysticPickCandidatePreview(
            OriginalMapBaseStatCatalog original, CharacterUnitCatalog catalog)
        {
            if (original == null || catalog == null ||
                original.SourceW3xSha256 !=
                    Ordr.Mobile.Core.OriginalCommonSaleRules.OriginalMapSha256)
                throw new ArgumentException("PICK 검토용 원본 객체 출처가 다릅니다.");
            source = original;
            int randomOwners = 0;
            foreach (var objectRow in original.All)
            {
                bool ownsPick = false;
                foreach (string ability in objectRow.OriginalAbilityRawcodes)
                    if (ability == NativeRandomAbility) { ownsPick = true; break; }
                if (!ownsPick) continue;
                var unit = catalog.Require(objectRow.UnitId);
                if (unit.Grade != "랜덤" ||
                    !unit.Id.StartsWith("RN-", StringComparison.Ordinal))
                    throw new FormatException("A800 소유 원본 객체가 랜덤전용 외에 있습니다.");
                randomOwners++;
            }
            if (randomOwners != 14)
                throw new FormatException("원본 A800 랜덤전용 14종 목록이 변경됐습니다.");
        }

        public bool TryPreview(OriginalJassFusionInstruction instruction,
            IReadOnlyList<MobileUnitSnapshot> owned,
            Func<long, bool> storyDeployed, ISet<long> reserved,
            out IReadOnlyList<long> candidates, out string label)
        {
            candidates = null;
            label = null;
            if (instruction == null || instruction.Kind != "PICK" ||
                instruction.Rawcode != NativeRandomAbility ||
                instruction.Count != 1)
                return false;
            if (owned == null || storyDeployed == null || reserved == null)
                throw new ArgumentNullException("PICK 후보의 보유/파견/중복 검사 누락");
            var available = new List<long>();
            foreach (var unit in owned)
            {
                if (unit == null || reserved.Contains(unit.InstanceId) ||
                    storyDeployed(unit.InstanceId) || unit.Unit.Grade != "랜덤")
                    continue;
                var original = source.Find(unit.Unit.Id);
                if (original == null) continue;
                foreach (string ability in original.OriginalAbilityRawcodes)
                    if (ability == NativeRandomAbility)
                    {
                        available.Add(unit.InstanceId);
                        break;
                    }
            }
            available.Sort();
            var selected = new List<long>();
            for (int i = 0; i < available.Count && i < instruction.Count; i++)
                selected.Add(available[i]);
            candidates = selected.AsReadOnly();
            label = "랜덤 전용 유닛 (" + NativeRandomAbility + " 후보)";
            return true;
        }
    }

}
