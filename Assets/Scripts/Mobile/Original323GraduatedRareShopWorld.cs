using System;
using System.Collections.Generic;
using Ordr.Mobile.Combat;
using Ordr.Mobile.Core;
using Ordr.Mobile.Data;

namespace Ordr.Mobile.Mobile
{
    // CJE/CJF: the graduated H0B0 purchase rolls a 99% rare pool
    // or a 1% special pool. The pool differs from the e019 rare Wisp:
    // h05X (Silvers Rayleigh) replaces that Wisp's h0BG (Gaimon).
    public interface IOriginal323GraduatedRareShopWorld
    {
        IOriginal323GraduatedRareShopTransaction Begin();
    }

    public interface IOriginal323GraduatedRareShopTransaction : IDisposable
    {
        bool TrySpawn(out long instanceId, out string sourceRawcode, out string displayName);
        void Commit();
    }

    public sealed class Original323GraduatedRareShopWorld :
        IOriginal323GraduatedRareShopWorld
    {
        private static readonly string[] RareRawcodes = {
            "h09X", "h01Q", "h025", "h01L", "h022", "h021", "h020", "h01Z", "h01Y", "h01X", "h01W", "h01V", "h01U", "h01T", "h02M", "h01S", "h01R", "h01M", "h01N", "h01O", "h05L", "h01P", "h02I", "h029", "h028", "h027", "h026", "h02A", "h024", "h02C", "h02D", "h02G", "h02F", "h02E", "h04H", "h023", "h02B", "h02H", "h02J", "h02K", "h02L", "h05K", "h05X"
        };
        private static readonly string[] SpecialRawcodes = {
            "h05H", "h08N", "h05Q", "h05O", "h05P", "h05R", "h09T"
        };
        public const int RareBranchPercent = 99;
        public const string RareRayleighRawcode = "h05X";
        public const string RareRayleighCatalogId = "R-43";
        public const string NativeBonusAbility = "A0YE";

        private readonly CharacterCombatRuntime combat;
        private readonly IOriginalWispRandom branchRandom;
        private readonly IOriginalWispRandom nativePoolRandom;
        private readonly IOriginalWispDeferredBonusPort deferredBonus;
        private readonly Dictionary<string, string> assigned =
            new Dictionary<string, string>(StringComparer.Ordinal);
        private readonly Dictionary<string, string> labels =
            new Dictionary<string, string>(StringComparer.Ordinal);
        private string pendingRawcode;
        private bool inFlight;

        public Original323GraduatedRareShopWorld(CharacterCombatRuntime liveCombat,
            CharacterUnitCatalog units, OriginalMapBaseStatCatalog objects,
            Original323WispAssignedRosterMap sourceWispAssignments,
            IOriginalWispRandom scriptBranchRandom,
            IOriginalWispRandom nativeRandom,
            IOriginalWispDeferredBonusPort abilityReceipt = null)
        {
            combat = liveCombat ?? throw new ArgumentNullException(nameof(liveCombat));
            if (units == null || objects == null || sourceWispAssignments == null)
                throw new ArgumentNullException(nameof(sourceWispAssignments));
            if (objects.SourceW3xSha256 != OriginalCommonSaleRules.OriginalMapSha256)
                throw new ArgumentException("원본 졸업 상점 캐릭터 W3X 해시 불일치");
            branchRandom = scriptBranchRandom ??
                throw new ArgumentNullException(nameof(scriptBranchRandom));
            nativePoolRandom = nativeRandom ??
                throw new ArgumentNullException(nameof(nativeRandom));
            deferredBonus = abilityReceipt;
            Validate(RareRawcodes, "희귀함", units, objects, sourceWispAssignments);
            Validate(SpecialRawcodes, "특수함", units, objects, sourceWispAssignments);
            if (assigned.Count != 50 || RareRawcodes.Length != 43 ||
                SpecialRawcodes.Length != 7)
                throw new InvalidOperationException("CJE 43/7 원본 졸업 상점 추첨 목록 누락");
        }

        private void Validate(IEnumerable<string> codes, string grade,
            CharacterUnitCatalog units, OriginalMapBaseStatCatalog objects,
            Original323WispAssignedRosterMap sourceWispAssignments)
        {
            foreach (string rawcode in codes)
            {
                string id;
                if (rawcode == RareRayleighRawcode)
                    id = RareRayleighCatalogId;
                else if (!sourceWispAssignments.TryResolveUnit(rawcode, out id))
                    throw new InvalidOperationException("CJE 졸업 상점 매핑 누락: " + rawcode);
                CatalogUnit entry = units.Find(id);
                OriginalMapCharacterBasics original = objects.Find(id);
                if (entry == null || entry.Grade != grade || original == null ||
                    original.OriginalRawcode != rawcode || assigned.ContainsKey(rawcode))
                    throw new InvalidOperationException("CJE 졸업 상점 실제 원본 객체 불일치: " + rawcode);
                assigned.Add(rawcode, id);
                labels.Add(rawcode, entry.Name);
            }
        }

        public IOriginal323GraduatedRareShopTransaction Begin()
        {
            if (inFlight)
                throw new InvalidOperationException("졸업 상점 희귀 추첨 중복 거래");
            inFlight = true;
            return new Purchase(this);
        }

        private sealed class Purchase : IOriginal323GraduatedRareShopTransaction
        {
            private readonly Original323GraduatedRareShopWorld owner;
            private long spawned;
            private bool committed;
            private bool disposed;

            public Purchase(Original323GraduatedRareShopWorld source) { owner = source; }

            public bool TrySpawn(out long instanceId, out string sourceRawcode,
                out string displayName)
            {
                instanceId = 0;
                sourceRawcode = null;
                displayName = null;
                if (disposed || spawned != 0)
                    throw new InvalidOperationException("졸업 상점 생성 거래 재진입");
                if (owner.pendingRawcode == null)
                {
                    int branch = owner.branchRandom.NextInclusive(1, 100);
                    if (branch < 1 || branch > 100)
                        throw new InvalidOperationException("CJF 희귀/특수 분기 난수 범위 오류");
                    string[] pool = branch <= RareBranchPercent ? RareRawcodes : SpecialRawcodes;
                    int index = owner.nativePoolRandom.NextInclusive(0, pool.Length - 1);
                    if (index < 0 || index >= pool.Length)
                        throw new InvalidOperationException("CJF 원본 UnitPool 인덱스 범위 오류");
                    owner.pendingRawcode = pool[index];
                }
                sourceRawcode = owner.pendingRawcode;
                displayName = owner.labels[sourceRawcode];
                string id = owner.assigned[sourceRawcode];
                spawned = owner.combat.SpawnVerifiedOriginalPositionCharacter(id,
                    Original323SourceMaterialRewardWorld.SourceStartX,
                    Original323SourceMaterialRewardWorld.SourceStartY);
                // CJF adds A0YE to rare results except h05X. The existing
                // shell receipt is honest about its unimplemented native effect.
                if (owner.deferredBonus != null && sourceRawcode != RareRayleighRawcode &&
                    Array.IndexOf(RareRawcodes, sourceRawcode) >= 0 &&
                    !owner.deferredBonus.TryDeferOriginalAbility(spawned, NativeBonusAbility))
                    throw new InvalidOperationException("CJF 원본 A0YE 획득 기록 실패");
                instanceId = spawned;
                return true;
            }

            public void Commit()
            {
                if (disposed || committed || spawned <= 0 ||
                    owner.pendingRawcode == null)
                    throw new InvalidOperationException("CJF 구매 확정 대상 불일치");
                owner.pendingRawcode = null;
                committed = true;
            }

            public void Dispose()
            {
                if (disposed) return;
                disposed = true;
                try
                {
                    if (!committed && spawned > 0)
                    {
                        owner.deferredBonus?.Forget(spawned);
                        owner.combat.RollbackUncommittedSpawn(spawned);
                    }
                }
                finally { owner.inFlight = false; }
                // A rejected debit leaves the exact pending draw for a retry.
            }
        }
    }
}
