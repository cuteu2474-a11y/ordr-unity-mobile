using System;
using System.Collections.Generic;
using System.Globalization;

namespace Ordr.Mobile.Combat
{
    // Bonney's age receipts are owned by a specific TR-21 instance and a
    // specific living enemy. They are not generic status buffs or inventory.
    [Serializable]
    public sealed class SourceBonneyAgeStackCheckpoint
    {
        public long OwnerId;
        public string EnemyId;
        public int Stacks;
    }

    [Serializable]
    public sealed class SourceBonneyAgeTriggerCheckpoint
    {
        public long OwnerId;
        public string EnemyId;
    }

    [Serializable]
    public sealed class SourceBonneyRootCheckpoint
    {
        public string EnemyId;
        public List<long> OwnerIds = new List<long>();
    }

    [Serializable]
    public sealed class SourceBonneyAgeOwnerCheckpoint
    {
        public int Version = 1;
        public List<SourceBonneyAgeStackCheckpoint> Stacks =
            new List<SourceBonneyAgeStackCheckpoint>();
        public List<SourceBonneyAgeTriggerCheckpoint> Triggered =
            new List<SourceBonneyAgeTriggerCheckpoint>();
        public List<SourceBonneyRootCheckpoint> Roots =
            new List<SourceBonneyRootCheckpoint>();
    }

    public sealed partial class CharacterCombatRuntime
    {
        private static string BonneyAgeOwnerKey(long owner, string enemyId) =>
            owner.ToString(CultureInfo.InvariantCulture) + ":" + enemyId;

        private static int CompareBonneyReceipt(
            long aOwner, string aEnemy, long bOwner, string bEnemy)
        {
            int compare = aOwner.CompareTo(bOwner);
            return compare != 0 ? compare :
                StringComparer.Ordinal.Compare(aEnemy, bEnemy);
        }

        private SourceBonneyAgeOwnerCheckpoint CaptureBonneyAgeOwners()
        {
            var result = new SourceBonneyAgeOwnerCheckpoint();
            foreach (var pair in sourceModelBonneyAge)
            {
                long owner;
                string enemy;
                if (!TrySplitCompositeOwnerEnemy(pair.Key, out owner, out enemy))
                    throw new NotSupportedException(
                        "보니 세월 스택의 원래 시전자와 대상을 해독할 수 없습니다.");
                result.Stacks.Add(new SourceBonneyAgeStackCheckpoint {
                    OwnerId = owner, EnemyId = enemy, Stacks = pair.Value
                });
            }
            result.Stacks.Sort((a, b) => CompareBonneyReceipt(
                a.OwnerId, a.EnemyId, b.OwnerId, b.EnemyId));
            foreach (string key in sourceModelBonneyAgeTriggered)
            {
                long owner;
                string enemy;
                if (!TrySplitCompositeOwnerEnemy(key, out owner, out enemy))
                    throw new NotSupportedException(
                        "보니 세월관통 1회 발동 영수증의 소유자가 없습니다.");
                result.Triggered.Add(new SourceBonneyAgeTriggerCheckpoint {
                    OwnerId = owner, EnemyId = enemy
                });
            }
            result.Triggered.Sort((a, b) => CompareBonneyReceipt(
                a.OwnerId, a.EnemyId, b.OwnerId, b.EnemyId));
            var enemies = new List<string>(sourceModelBonneyRootOwner.Keys);
            enemies.Sort(StringComparer.Ordinal);
            foreach (string enemy in enemies)
            {
                var row = new SourceBonneyRootCheckpoint { EnemyId = enemy };
                row.OwnerIds.AddRange(sourceModelBonneyRootOwner[enemy]);
                row.OwnerIds.Sort();
                result.Roots.Add(row);
            }
            if (result.Roots.Count != sourceModelBonneyShrunk.Count)
                throw new InvalidOperationException(
                    "보니 속박·크기 감소 대상 수가 일치하지 않습니다.");
            foreach (var row in result.Roots)
                if (!sourceModelBonneyShrunk.Contains(row.EnemyId))
                    throw new InvalidOperationException(
                        "보니 속박에 대응하는 크기 감소가 없습니다.");
            return result;
        }

        public static void ValidateBonneyAgeOwners(
            SourceCombatRuntimeCheckpoint combat)
        {
            if (combat == null || combat.BonneyAgeOwners == null ||
                combat.BonneyAgeOwners.Version != 1 ||
                combat.BonneyAgeOwners.Stacks == null ||
                combat.BonneyAgeOwners.Triggered == null ||
                combat.BonneyAgeOwners.Roots == null ||
                combat.Actors == null || combat.Enemies == null ||
                combat.BonneyAgeOwners.Stacks.Count > 1000000 ||
                combat.BonneyAgeOwners.Triggered.Count > 1000000 ||
                combat.BonneyAgeOwners.Roots.Count > 1000000)
                throw new FormatException("보니 세월·속박 체크포인트가 없습니다.");
            var state = combat.BonneyAgeOwners;
            var actors = new Dictionary<long, string>();
            foreach (var actor in combat.Actors)
            {
                if (actor == null || actor.InstanceId <= 0 ||
                    actors.ContainsKey(actor.InstanceId))
                    throw new FormatException("세월 스택 시전자 ID가 중복됐습니다.");
                actors.Add(actor.InstanceId, actor.CatalogId);
            }
            var enemyKinds = new Dictionary<string, CharacterEnemyKind>(
                StringComparer.Ordinal);
            foreach (var enemy in combat.Enemies)
            {
                if (enemy == null || string.IsNullOrWhiteSpace(enemy.EnemyId) ||
                    enemyKinds.ContainsKey(enemy.EnemyId))
                    throw new FormatException("보니 대상 적 ID가 중복됐습니다.");
                enemyKinds.Add(enemy.EnemyId, enemy.Kind);
            }
            var stacks = new Dictionary<string, int>(StringComparer.Ordinal);
            long previousOwner = 0;
            string previousEnemy = null;
            foreach (var row in state.Stacks)
            {
                string catalog;
                if (row == null ||
                    (previousOwner != 0 &&
                     CompareBonneyReceipt(previousOwner, previousEnemy,
                         row.OwnerId, row.EnemyId) >= 0) ||
                    !actors.TryGetValue(row.OwnerId, out catalog) ||
                    catalog != "TR-21" ||
                    string.IsNullOrWhiteSpace(row.EnemyId) ||
                    row.EnemyId.Length > 256 ||
                    !enemyKinds.ContainsKey(row.EnemyId) ||
                    row.Stacks <= 0)
                    throw new FormatException("보니 세월 스택의 원래 주인/적이 없습니다.");
                stacks.Add(BonneyAgeOwnerKey(row.OwnerId, row.EnemyId),
                    row.Stacks);
                previousOwner = row.OwnerId;
                previousEnemy = row.EnemyId;
            }
            var triggered = new HashSet<string>(StringComparer.Ordinal);
            previousOwner = 0;
            previousEnemy = null;
            foreach (var row in state.Triggered)
            {
                string key = row == null ? null :
                    BonneyAgeOwnerKey(row.OwnerId, row.EnemyId);
                int count;
                if (row == null ||
                    (previousOwner != 0 &&
                     CompareBonneyReceipt(previousOwner, previousEnemy,
                         row.OwnerId, row.EnemyId) >= 0) ||
                    !stacks.TryGetValue(key, out count) ||
                    count < 40 || !triggered.Add(key))
                    throw new FormatException(
                        "세월관통 1회 발동은 동일 적 40스택 이후여야 합니다.");
                previousOwner = row.OwnerId;
                previousEnemy = row.EnemyId;
            }
            string lastRoot = null;
            foreach (var root in state.Roots)
            {
                CharacterEnemyKind kind;
                if (root == null ||
                    string.IsNullOrWhiteSpace(root.EnemyId) ||
                    !enemyKinds.TryGetValue(root.EnemyId, out kind) ||
                    kind != CharacterEnemyKind.Line ||
                    lastRoot != null &&
                    StringComparer.Ordinal.Compare(lastRoot, root.EnemyId) >= 0 ||
                    root.OwnerIds == null || root.OwnerIds.Count == 0 ||
                    root.OwnerIds.Count > actors.Count)
                    throw new FormatException(
                        "보니 속박 대상은 살아 있는 일반 라인 적이어야 합니다.");
                long previous = 0;
                foreach (long owner in root.OwnerIds)
                {
                    if (owner <= previous ||
                        !triggered.Contains(
                            BonneyAgeOwnerKey(owner, root.EnemyId)))
                        throw new FormatException(
                            "보니 속박의 실물 소유자/40스택 발동이 없습니다.");
                    previous = owner;
                }
                lastRoot = root.EnemyId;
            }
            if (!combat.HasSourceModelBasicAttacks &&
                (state.Stacks.Count != 0 || state.Triggered.Count != 0 ||
                 state.Roots.Count != 0))
                throw new FormatException(
                    "원본 모델이 아닌 전장에 세월관통 권리가 저장됐습니다.");
        }

        private void RestoreBonneyAgeOwners(SourceBonneyAgeOwnerCheckpoint saved)
        {
            if (saved == null ||
                sourceModelBonneyAge.Count != 0 ||
                sourceModelBonneyAgeTriggered.Count != 0 ||
                sourceModelBonneyRootOwner.Count != 0 ||
                sourceModelBonneyShrunk.Count != 0)
                throw new InvalidOperationException(
                    "보니 세월/속박은 새 전투 월드에만 복원합니다.");
            foreach (var row in saved.Stacks)
                sourceModelBonneyAge.Add(
                    BonneyAgeOwnerKey(row.OwnerId, row.EnemyId), row.Stacks);
            foreach (var row in saved.Triggered)
                sourceModelBonneyAgeTriggered.Add(
                    BonneyAgeOwnerKey(row.OwnerId, row.EnemyId));
            foreach (var row in saved.Roots)
            {
                sourceModelBonneyRootOwner.Add(row.EnemyId,
                    new HashSet<long>(row.OwnerIds));
                sourceModelBonneyShrunk.Add(row.EnemyId);
            }
            // Restore changes no HP, stun, rewards, RNG or transform.
        }
    }
}
