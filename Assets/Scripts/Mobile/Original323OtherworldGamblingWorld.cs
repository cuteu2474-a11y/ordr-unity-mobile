using System;
using System.Collections.Generic;
using Ordr.Mobile.Combat;
using Ordr.Mobile.Core;
using Ordr.Mobile.Data;

namespace Ordr.Mobile.Mobile
{
    public sealed class Original323OtherworldGamblingWorld : IOriginal323OtherworldGamblingWorld
    {
        private readonly CharacterCombatRuntime combat;
        private readonly IOriginalWispDeferredBonusPort abilities;
        private readonly Dictionary<string, string> ids = new Dictionary<string, string>(StringComparer.Ordinal);
        private readonly Dictionary<string, string> names = new Dictionary<string, string>(StringComparer.Ordinal);
        private bool inFlight;

        public Original323OtherworldGamblingWorld(CharacterCombatRuntime runtime,
            CharacterUnitCatalog units, OriginalMapBaseStatCatalog objects,
            Original323CwrAcquisitionRoutes randomRoutes,
            Original323WispAssignedRosterMap rareAssignments,
            IOriginalWispDeferredBonusPort deferredAbilities)
        {
            combat = runtime ?? throw new ArgumentNullException(nameof(runtime));
            abilities = deferredAbilities ?? throw new ArgumentNullException(nameof(deferredAbilities));
            if (units == null || objects == null || randomRoutes == null || rareAssignments == null ||
                objects.SourceW3xSha256 != OriginalCommonSaleRules.OriginalMapSha256)
                throw new ArgumentException("CJM 원본 로스터/객체/보상 매핑이 필요합니다.");
            foreach (string rawcode in Original323OtherworldGamblingRules.RandomRawcodes)
            {
                string id;
                if (!randomRoutes.TryResolveRandomOnlyAward(rawcode, out id))
                    throw new ArgumentException("CJM 랜덤전용 원본 매핑 누락: " + rawcode);
                Add(rawcode, id, "랜덤", units, objects);
            }
            foreach (string rawcode in Original323OtherworldGamblingRules.CasinoRareRawcodes)
            {
                string id;
                if (!rareAssignments.TryResolveUnit(rawcode, out id))
                    throw new ArgumentException("CJM 카지노 원본 매핑 누락: " + rawcode);
                Add(rawcode, id, "희귀함", units, objects);
            }
            Add(Original323SourceMaterialRewardWorld.TokenRawcode,
                Original323SourceMaterialRewardWorld.TokenCatalogId, "재료", units, objects);
        }

        private void Add(string rawcode, string id, string grade,
            CharacterUnitCatalog units, OriginalMapBaseStatCatalog objects)
        {
            var entry = units.Find(id);
            var source = objects.Find(id);
            if (entry == null || entry.Grade != grade || source == null ||
                source.OriginalRawcode != rawcode || ids.ContainsKey(rawcode))
                throw new ArgumentException("CJM 원본 결과 객체 불일치: " + rawcode);
            ids.Add(rawcode, id); names.Add(rawcode, entry.Name);
        }

        public IOriginal323OtherworldGamblingTransaction Begin(Original323OtherworldGamblingOutcome outcome)
        {
            if (inFlight || outcome == null || !ids.ContainsKey(outcome.Rawcode))
                throw new InvalidOperationException("CJM 결과 생성 중복/객체 오류");
            inFlight = true;
            return new Spawn(this, outcome);
        }

        private sealed class Spawn : IOriginal323OtherworldGamblingTransaction
        {
            private readonly Original323OtherworldGamblingWorld owner;
            private readonly Original323OtherworldGamblingOutcome outcome;
            private long actor;
            private bool committed, disposed;
            public Spawn(Original323OtherworldGamblingWorld source, Original323OtherworldGamblingOutcome draw)
            { owner = source; outcome = draw; }
            public bool TrySpawn(out long instanceId, out string displayName)
            {
                instanceId = 0; displayName = null;
                if (disposed || actor != 0) throw new InvalidOperationException("CJM 소환 재진입");
                actor = owner.combat.SpawnVerifiedOriginalPositionCharacter(owner.ids[outcome.Rawcode],
                    Original323SourceMaterialRewardWorld.SourceStartX,
                    Original323SourceMaterialRewardWorld.SourceStartY);
                if (outcome.BonusAbility != null &&
                    !owner.abilities.TryDeferOriginalAbility(actor, outcome.BonusAbility))
                    throw new InvalidOperationException("CJM 원본 리롤 능력 보류 기록 실패");
                instanceId = actor; displayName = owner.names[outcome.Rawcode];
                return true;
            }
            public void Commit()
            {
                if (disposed || actor <= 0) throw new InvalidOperationException("CJM 생성 확정 대상 오류");
                committed = true;
            }
            public void Dispose()
            {
                if (disposed) return;
                disposed = true;
                try
                {
                    if (!committed && actor > 0)
                    {
                        owner.abilities.Forget(actor);
                        owner.combat.RollbackUncommittedSpawn(actor);
                    }
                }
                finally { owner.inFlight = false; }
            }
        }
    }
}
