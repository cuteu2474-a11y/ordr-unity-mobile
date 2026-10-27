using System;
using Ordr.Mobile.Core;
using Ordr.Mobile.Data;

namespace Ordr.Mobile.Combat
{
    // Direct transcription of pinned war3map.j BIQ/BIR/BIS/BIU/BIY/CRy.
    // e0IX costs ONE Wisp. The original BFi stream chooses a 24/10000
    // pirate-ship outcome before drawing one of nine Common rawcodes.
    // Non-ship draws alternate: one uniform candidate, then two DIFFERENT
    // candidates with the lesser previous-count chosen (ties choose first).
    // This does not claim to reproduce an unknown Warcraft native seed.
    public sealed class Original323SourceCommonWispWorld :
        IOriginal323CommonWispRecruitmentWorld,
        IOriginal323CommonWispRecruitmentAvailability
    {
        private sealed class PendingDraw
        {
            public int Index;
            public bool PirateShip;
        }

        private sealed class Transaction : IOriginal323CommonWispRecruitmentTransaction
        {
            private readonly Original323SourceCommonWispWorld owner;
            private long createdId;
            private bool committed;
            private bool disposed;

            public Transaction(Original323SourceCommonWispWorld world) { owner = world; }

            public bool TryCreate(out long instanceId, out string originalUnitRawcode)
            {
                if (disposed || createdId != 0)
                    throw new InvalidOperationException("랜덤위습 거래가 이미 사용되었습니다.");
                PendingDraw draw = owner.PreparePending();
                if (draw.PirateShip)
                {
                    // h060 is an actual attacking pirate ship, NOT one of
                    // nine commons. Until its full combat world is built,
                    // preserve the result and player's Wisp. Never reroll.
                    instanceId = 0;
                    originalUnitRawcode = null;
                    return false;
                }

                string catalogId = owner.catalogIds[draw.Index];
                createdId = owner.combat.SpawnVerifiedOriginalPositionCharacter(catalogId,
                    OriginalCommonSpawnX(draw.Index), OriginalCommonSpawnY);
                instanceId = createdId;
                originalUnitRawcode = Rawcodes[draw.Index];
                return true;
            }

            public void Commit()
            {
                if (disposed || committed || createdId <= 0 ||
                    owner.pending == null || owner.pending.PirateShip)
                    throw new InvalidOperationException("원본 위습 소환의 확정 대상이 없습니다.");
                int index = owner.pending.Index;
                if (owner.counts[index] == int.MaxValue ||
                    owner.normalDrawCount == int.MaxValue)
                    throw new OverflowException("원본 위습 추첨 누적 횟수 초과");
                owner.counts[index]++;
                owner.normalDrawCount++;
                owner.pending = null;
                committed = true;
            }

            public void Dispose()
            {
                if (disposed) return;
                disposed = true;
                if (!committed && createdId > 0)
                    owner.combat.RollbackUncommittedSpawn(createdId);
                // An unfinished draw is retained, not rerolled, across a
                // failed wallet commit or external transaction exception.
            }
        }

        public const string PinnedWar3mapJSha256 =
            Original323MonsterWaveIdentityCatalog.OriginalWar3mapJSha256;
        public const string PirateShipRawcode = "h060";
        public const double OriginalCommonSpawnBaseX = -4768.0;
        public const double OriginalCommonSpawnY = 3232.0;
        public const int OriginalCommonSpawnSpacingX = 256;
        private static readonly string[] Rawcodes = {
            "h003", "h002", "h001", "h007", "h004",
            "h008", "h005", "h009", "h006"
        };

        private readonly CharacterCombatRuntime combat;
        private readonly IOriginalWispRandom originalDrawRandom;
        private readonly string[] catalogIds = new string[9];
        private readonly int[] counts = new int[9];
        private PendingDraw pending;
        private int normalDrawCount;

        public int OriginalRandomWispCost { get { return 1; } }
        public string OriginalSourceEvidence
        {
            get { return "war3map.j " + PinnedWar3mapJSha256 +
                "; BIQ/BIR/BIS/BIU/BIY/CRy; singleplayer Vw=false"; }
        }
        public bool HasPendingPirateShip { get { return pending != null && pending.PirateShip; } }
        public bool CanDraw { get { return !HasPendingPirateShip; } }
        public string UnavailableReason
        {
            get { return HasPendingPirateShip
                ? "원본 해적선(h060) 추첨 결과를 보존 중입니다. 해적선 전투 구현 전까지 추가 소환할 수 없습니다."
                : null; }
        }
        public int ConfirmedCommonDraws { get { return normalDrawCount; } }

        public int ConfirmedCountForOriginal(string rawcode)
        {
            for (int i = 0; i < Rawcodes.Length; i++)
                if (Rawcodes[i] == rawcode) return counts[i];
            throw new ArgumentException("원본 흔함 9종 rawcode가 아닙니다.", nameof(rawcode));
        }

        public static double OriginalCommonSpawnX(int originalPoolIndex)
        {
            if (originalPoolIndex < 0 || originalPoolIndex >= Rawcodes.Length)
                throw new ArgumentOutOfRangeException(nameof(originalPoolIndex));
            return OriginalCommonSpawnBaseX +
                OriginalCommonSpawnSpacingX * originalPoolIndex;
        }

        public Original323SourceCommonWispWorld(CharacterCombatRuntime liveCombat,
            CharacterUnitCatalog units, OriginalMapBaseStatCatalog originalObjects,
            IOriginalWispRandom sourceRandom)
        {
            combat = liveCombat ?? throw new ArgumentNullException(nameof(liveCombat));
            if (units == null || originalObjects == null)
                throw new ArgumentNullException(nameof(originalObjects));
            originalDrawRandom = sourceRandom ?? throw new ArgumentNullException(nameof(sourceRandom));
            for (int i = 0; i < Rawcodes.Length; i++)
            {
                string id = "C-" + (i + 1).ToString("D2");
                CatalogUnit unit = units.Find(id);
                OriginalMapCharacterBasics original = originalObjects.Find(id);
                if (unit == null || unit.Grade != "흔함" || original == null ||
                    original.MappingStatus != "verified-common-map-identity" ||
                    original.OriginalRawcode != Rawcodes[i])
                    throw new InvalidOperationException(
                        "원본 랜덤위습 흔함 추첨 목록과 실제 캐릭터 데이터가 다릅니다: " + id);
                catalogIds[i] = id;
            }
        }

        public IOriginal323CommonWispRecruitmentTransaction Begin()
        {
            return new Transaction(this);
        }

        private PendingDraw PreparePending()
        {
            if (pending != null) return pending;
            int rare = Draw(1, 10000);
            if (rare <= 24)
            {
                pending = new PendingDraw { Index = -1, PirateShip = true };
                return pending;
            }

            int first = Draw(0, Rawcodes.Length - 1);
            int selected = first;
            if ((normalDrawCount + 1) % 2 == 0)
            {
                int second = first;
                int attempts = 0;
                while (second == first)
                {
                    if (++attempts > 1024)
                        throw new InvalidOperationException(
                            "원본 흔함 후보 추첨이 서로 다른 두 유닛을 만들지 못했습니다.");
                    second = Draw(0, Rawcodes.Length - 1);
                }
                if (counts[first] > counts[second]) selected = second;
            }
            pending = new PendingDraw { Index = selected };
            return pending;
        }

        private int Draw(int min, int max)
        {
            int value = originalDrawRandom.NextInclusive(min, max);
            if (value < min || value > max)
                throw new InvalidOperationException("원본 BFi 추첨 범위를 벗어났습니다.");
            return value;
        }
    }
}
