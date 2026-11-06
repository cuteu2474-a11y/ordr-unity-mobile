using System;
using System.Collections.Generic;
using Ordr.Mobile.Combat;
using Ordr.Mobile.Core;
using Ordr.Mobile.Data;

namespace Ordr.Mobile.Mobile
{
    // CKR exchange-house physical CKG/CKJ rewards use the SAME roster
    // as original fusion materials and otherworld unit gambling.
    public sealed class Original323CompuUnitWorld : IOriginal323CompuUnitWorld
    {
        private readonly CharacterCombatRuntime combat;
        private readonly Dictionary<string, string> byRawcode =
            new Dictionary<string, string>(StringComparer.Ordinal);
        private bool inFlight;

        public Original323CompuUnitWorld(CharacterCombatRuntime runtime,
            CharacterUnitCatalog units, OriginalMapBaseStatCatalog objects,
            Original323CwrAcquisitionRoutes acquisitionRoutes)
        {
            combat = runtime ?? throw new ArgumentNullException(nameof(runtime));
            if (units == null || objects == null || acquisitionRoutes == null ||
                objects.SourceW3xSha256 != OriginalCommonSaleRules.OriginalMapSha256)
                throw new ArgumentException("원본 콤푸 환전 유닛 객체 검증 실패");
            Add(Original323SourceMaterialRewardWorld.PirateShipRawcode,
                Original323SourceMaterialRewardWorld.PirateShipCatalogId, units, objects,
                "히든", "해적선");
            Add(Original323SourceMaterialRewardWorld.TokenRawcode,
                Original323SourceMaterialRewardWorld.TokenCatalogId, units, objects, "재료");
            foreach (string rawcode in Original323OtherworldGamblingRules.RandomRawcodes)
            {
                string catalogId;
                if (!acquisitionRoutes.TryResolveRandomOnlyAward(rawcode, out catalogId))
                    throw new ArgumentException("환전 CKJ 랜덤전용 매핑 누락: " + rawcode);
                Add(rawcode, catalogId, units, objects, "랜덤");
            }
        }

        private void Add(string rawcode, string catalogId,
            CharacterUnitCatalog units, OriginalMapBaseStatCatalog objects,
            params string[] grades)
        {
            var unit = units.Find(catalogId);
            var source = objects.Find(catalogId);
            bool gradeValid = false;
            if (unit != null)
                foreach (string grade in grades)
                    if (unit.Grade == grade) gradeValid = true;
            if (!gradeValid || source == null ||
                source.OriginalRawcode != rawcode ||
                byRawcode.ContainsKey(rawcode))
                throw new ArgumentException("원본 콤푸 교환 대상 불일치: " + rawcode);
            byRawcode.Add(rawcode, catalogId);
        }

        public IOriginal323CompuUnitTransaction Begin(string recipe, string randomRawcode)
        {
            if (inFlight || (recipe != "CKG" && recipe != "CKJ") ||
                (recipe == "CKJ" &&
                 (string.IsNullOrEmpty(randomRawcode) ||
                  !byRawcode.ContainsKey(randomRawcode) ||
                  randomRawcode == Original323SourceMaterialRewardWorld.TokenRawcode ||
                  randomRawcode == Original323SourceMaterialRewardWorld.PirateShipRawcode)))
                throw new InvalidOperationException("원본 콤푸 교환 보상 지정 오류");
            inFlight = true;
            return new Staged(this, recipe, randomRawcode);
        }

        private sealed class Staged : IOriginal323CompuUnitTransaction
        {
            private readonly Original323CompuUnitWorld owner;
            private readonly string recipe, randomRawcode;
            private readonly List<long> actors = new List<long>();
            private bool committed, disposed, spawned;
            public Staged(Original323CompuUnitWorld source, string id, string raw)
            { owner = source; recipe = id; randomRawcode = raw; }
            public bool TrySpawn(out string description)
            {
                if (disposed || spawned)
                    throw new InvalidOperationException("콤푸 교환 소환 중복 실행");
                spawned = true;
                var raws = recipe == "CKG"
                    ? new[] { Original323SourceMaterialRewardWorld.PirateShipRawcode }
                    : new[] { randomRawcode, Original323SourceMaterialRewardWorld.TokenRawcode };
                foreach (string rawcode in raws)
                {
                    long id = owner.combat.SpawnVerifiedOriginalPositionCharacter(
                        owner.byRawcode[rawcode],
                        Original323SourceMaterialRewardWorld.SourceStartX,
                        Original323SourceMaterialRewardWorld.SourceStartY);
                    if (id <= 0)
                        throw new InvalidOperationException("콤푸 교환 실물 생성 실패");
                    actors.Add(id);
                }
                description = recipe == "CKG" ? "해적선" : "랜덤전용 유닛 + 행운의 토큰";
                return true;
            }
            public void Commit()
            {
                if (disposed || !spawned || actors.Count != (recipe == "CKG" ? 1 : 2))
                    throw new InvalidOperationException("미완료 콤푸 환전 소환 확정 불가");
                committed = true;
            }
            public void Dispose()
            {
                if (disposed) return;
                disposed = true;
                try
                {
                    if (!committed)
                        for (int i = actors.Count - 1; i >= 0; i--)
                            owner.combat.RollbackUncommittedSpawn(actors[i]);
                }
                finally { owner.inFlight = false; }
            }
        }
    }
}
