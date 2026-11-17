using System;
using System.Collections.Generic;
using Ordr.Mobile.Combat;
using Ordr.Mobile.Core;
using Ordr.Mobile.Data;

namespace Ordr.Mobile.Mobile
{
    public interface IOriginal323UnitGamblingWorld
    {
        IOriginal323UnitGamblingTransaction Begin(IReadOnlyList<string> rawcodes, string bonusAbility);
    }
    public interface IOriginal323UnitGamblingTransaction : IDisposable
    {
        string Stage();
        void Commit();
    }

    public sealed class Original323UnitGamblingWorld :
        IOriginal323UnitGamblingWorld, IOriginal323RestoreCommerceOwner
    {
        private readonly CharacterCombatRuntime combat;
        private readonly Original323WispDeferredAbilityRegistry abilities;
        private readonly Dictionary<string, string> ids = new Dictionary<string, string>();
        private readonly Dictionary<string, string> names = new Dictionary<string, string>();
        private bool inFlight;
        public Original323UnitGamblingWorld(CharacterCombatRuntime runtime,
            CharacterUnitCatalog units, OriginalMapBaseStatCatalog objects,
            Original323WispAssignedRosterMap assignments,
            Original323CwrAcquisitionRoutes randomRoutes,
            Original323WispDeferredAbilityRegistry registry)
        {
            combat = runtime ?? throw new ArgumentNullException(nameof(runtime));
            abilities = registry ?? throw new ArgumentNullException(nameof(registry));
            if (units == null || objects == null || assignments == null || randomRoutes == null ||
                objects.SourceW3xSha256 != OriginalCommonSaleRules.OriginalMapSha256)
                throw new ArgumentException("유닛 도박의 원본 신원 매핑이 필요합니다.");
            var all = new List<string>();
            all.AddRange(Original323UnitGamblingRules.Common);
            all.AddRange(Original323UnitGamblingRules.Uncommon);
            all.AddRange(Original323UnitGamblingRules.Special);
            all.AddRange(Original323UnitGamblingRules.HighSpecial);
            all.AddRange(Original323UnitGamblingRules.HighRare);
            all.AddRange(Original323OtherworldGamblingRules.RandomRawcodes);
            all.AddRange(new[] { "h060", "h05X", "h06G" });
            foreach (string raw in all)
            {
                if (ids.ContainsKey(raw)) continue;
                string id;
                if (raw == "h060") id = Original323SourceMaterialRewardWorld.PirateShipCatalogId;
                else if (raw == "h05X") id = Original323SourceMaterialRewardWorld.RayleighCatalogId;
                else if (raw == "h06G") id = Original323SourceMaterialRewardWorld.TokenCatalogId;
                else if (CommonCatalogId(raw) != null) id = CommonCatalogId(raw);
                else if (!assignments.TryResolveUnit(raw, out id) &&
                         !randomRoutes.TryResolveRandomOnlyAward(raw, out id))
                    throw new ArgumentException("유닛 도박 원본 매핑 누락: " + raw);
                var unit = units.Find(id); var source = objects.Find(id);
                if (unit == null || source == null || source.OriginalRawcode != raw)
                    throw new ArgumentException("유닛 도박 객체 불일치: " + raw);
                ids.Add(raw, id); names.Add(raw, unit.Name);
            }
        }
        private static string CommonCatalogId(string rawcode)
        {
            for (int i = 0; i < Original323UnitGamblingRules.Common.Count; i++)
                if (Original323UnitGamblingRules.Common[i] == rawcode) return "C-" + (i + 1).ToString("00");
            return null;
        }
        public bool IsAttachedTo(CharacterCombatRuntime runtime,
            Original323WispDeferredAbilityRegistry registry) =>
            ReferenceEquals(combat, runtime) &&
            ReferenceEquals(abilities, registry);

        public string CatalogId(string rawcode) => ids[rawcode];
        public string DisplayName(string rawcode) => names[rawcode];
        public IOriginal323UnitGamblingTransaction Begin(IReadOnlyList<string> rawcodes, string bonusAbility)
        {
            if (inFlight || rawcodes == null || (bonusAbility != null && bonusAbility != "A0YE"))
                throw new InvalidOperationException("유닛 도박 중복 거래/능력 오류");
            var copy = new List<string>(rawcodes);
            foreach (string raw in copy) if (!ids.ContainsKey(raw)) throw new ArgumentException(raw);
            inFlight = true;
            return new Purchase(this, copy, bonusAbility);
        }
        private sealed class Purchase : IOriginal323UnitGamblingTransaction
        {
            private readonly Original323UnitGamblingWorld owner;
            private readonly List<string> rawcodes;
            private readonly List<long> spawned = new List<long>();
            private readonly string bonus;
            private bool staged, committed, disposed;
            public Purchase(Original323UnitGamblingWorld source, List<string> codes, string ability)
            { owner = source; rawcodes = codes; bonus = ability; }
            public string Stage()
            {
                if (disposed || staged) throw new InvalidOperationException("유닛 도박 생성 재진입");
                staged = true;
                var labels = new List<string>();
                foreach (string raw in rawcodes)
                {
                    bool uncommon = Contains(Original323UnitGamblingRules.Uncommon, raw);
                    long id = owner.combat.SpawnVerifiedOriginalPositionCharacter(owner.ids[raw],
                        uncommon ? -3760 : Original323SourceMaterialRewardWorld.SourceStartX,
                        uncommon ? 3904 : Original323SourceMaterialRewardWorld.SourceStartY);
                    spawned.Add(id);
                    if (bonus != null && !owner.abilities.TryDeferOriginalAbility(id, bonus))
                        throw new InvalidOperationException("유닛 도박 A0YE 기록 실패");
                    labels.Add(owner.names[raw]);
                }
                return labels.Count == 0 ? "유닛 보상 없음" : string.Join(" + ", labels);
            }
            private static bool Contains(IReadOnlyList<string> values, string raw)
            { foreach (string value in values) if (value == raw) return true; return false; }
            public void Commit()
            {
                if (!staged || disposed || committed) throw new InvalidOperationException("생성 확정 오류");
                committed = true;
            }
            public void Dispose()
            {
                if (disposed) return;
                disposed = true;
                try
                {
                    if (!committed) for (int i = spawned.Count - 1; i >= 0; i--)
                    { owner.abilities.Forget(spawned[i]); owner.combat.RollbackUncommittedSpawn(spawned[i]); }
                }
                finally { owner.inFlight = false; }
            }
        }
    }
}
