using System;
using System.Collections.Generic;
using System.Globalization;

namespace Ordr.Mobile.Combat
{
    // Only progress/ownership is persisted here. The real HP/MP, enemy
    // statuses and wallet receipts are restored by their existing owners.
    [Serializable] public sealed class SourceBulkIntState
    {
        public long OwnerId;
        public int Value;
    }
    [Serializable] public sealed class SourceBulkLongState
    {
        public long OwnerId;
        public long Value;
    }
    [Serializable] public sealed class SourceBulkDoubleState
    {
        public long OwnerId;
        public double Value;
    }
    [Serializable] public sealed class SourceBulkCooldownState
    {
        public long OwnerId;
        public string SkillKey;
        public double DueAt;
    }
    [Serializable] public sealed class SourceBulkTargetState
    {
        public long OwnerId;
        public string EnemyId;
    }
    [Serializable] public sealed class SourceBulkRecipientState
    {
        public long OwnerId;
        public long RecipientId;
    }
    [Serializable] public sealed class SourceBulkOwnerCheckpoint
    {
        public int Version = 1;
        public List<SourceBulkCooldownState> Cooldowns = new List<SourceBulkCooldownState>();
        public List<SourceBulkIntState> AttackCounts = new List<SourceBulkIntState>();
        public List<SourceBulkIntState> SansBones = new List<SourceBulkIntState>();
        public List<SourceBulkDoubleState> GabanPower = new List<SourceBulkDoubleState>();
        public List<SourceBulkDoubleState> AllyRegen = new List<SourceBulkDoubleState>();
        public List<SourceBulkLongState> GiftSerials = new List<SourceBulkLongState>();
        public List<SourceBulkTargetState> GabanTargets = new List<SourceBulkTargetState>();
        public List<SourceBulkRecipientState> AnyaRecipients = new List<SourceBulkRecipientState>();
    }

    public sealed partial class CharacterCombatRuntime
    {
        private static bool BulkFinite(double v) =>
            !double.IsNaN(v) && !double.IsInfinity(v);
        private static string AnyaAuraKey(long owner, long target) =>
            "practice-tooltip:bulk:" + owner.ToString(CultureInfo.InvariantCulture) +
            ":MY-08:passive-ally-speed:" + target.ToString(CultureInfo.InvariantCulture);

        private static bool TryBulkCooldown(string key, out long owner, out string skill)
        {
            owner = 0; skill = null;
            int at = key == null ? -1 : key.IndexOf(':');
            if (at < 1 || at >= key.Length - 1 ||
                !long.TryParse(key.Substring(0, at), NumberStyles.None,
                    CultureInfo.InvariantCulture, out owner) || owner <= 0) return false;
            skill = key.Substring(at + 1);
            return key == owner.ToString(CultureInfo.InvariantCulture) + ":" + skill;
        }

        private static bool BulkCooldownAllowed(string catalog, string skill)
        {
            switch (skill)
            {
                case "D-04:skill:2": return catalog == "D-04";
                case "MY-01:skill:2": return catalog == "MY-01";
                case "I-02:skill:2": return catalog == "I-02";
                case "L-12:skill:0:delete": return catalog == "L-12";
                case "TR-02:skill:0": return catalog == "TR-02";
                case "D-06:skill:1": return catalog == "D-06";
                case "MY-05:skill:1": return catalog == "MY-05";
                case "L-22:skill:0": return catalog == "L-22";
                case "MY-08:skill:6": return catalog == "MY-08";
                default: return false;
            }
        }

        private static List<SourceBulkIntState> CaptureBulkInts(Dictionary<long, int> map)
        {
            var saved = new List<SourceBulkIntState>();
            var ids = new List<long>(map.Keys); ids.Sort();
            foreach (long id in ids)
                saved.Add(new SourceBulkIntState { OwnerId = id, Value = map[id] });
            return saved;
        }

        private SourceBulkOwnerCheckpoint CaptureSourceBulkOwners()
        {
            var saved = new SourceBulkOwnerCheckpoint {
                AttackCounts = CaptureBulkInts(bulkAutoAttackCounts),
                SansBones = CaptureBulkInts(bulkSansBones)
            };
            var keys = new List<string>(additionalAutoReadyAt.Keys);
            keys.Sort(StringComparer.Ordinal);
            foreach (string key in keys)
            {
                long owner; string skill;
                if (!TryBulkCooldown(key, out owner, out skill))
                    throw new NotSupportedException("추가 기술의 재사용 시계 ID가 잘못됐습니다.");
                saved.Cooldowns.Add(new SourceBulkCooldownState {
                    OwnerId = owner, SkillKey = skill, DueAt = additionalAutoReadyAt[key]
                });
            }
            var ids = new List<long>(bulkAutoFlatPower.Keys); ids.Sort();
            foreach (long id in ids)
                saved.GabanPower.Add(new SourceBulkDoubleState {
                    OwnerId = id, Value = bulkAutoFlatPower[id]
                });
            ids = new List<long>(bulkAllyRegenerationEnds.Keys); ids.Sort();
            foreach (long id in ids)
                saved.AllyRegen.Add(new SourceBulkDoubleState {
                    OwnerId = id, Value = bulkAllyRegenerationEnds[id]
                });
            ids = new List<long>(bulkGiftSerials.Keys); ids.Sort();
            foreach (long id in ids)
                saved.GiftSerials.Add(new SourceBulkLongState {
                    OwnerId = id, Value = bulkGiftSerials[id]
                });
            ids = new List<long>(bulkArmorAuraTargets.Keys); ids.Sort();
            foreach (long id in ids)
                saved.GabanTargets.Add(new SourceBulkTargetState {
                    OwnerId = id, EnemyId = bulkArmorAuraTargets[id]
                });
            const string prefix = "practice-tooltip:bulk:";
            const string separator = ":MY-08:passive-ally-speed:";
            foreach (var lease in bulkAnyaAttackSpeedRecipients)
            {
                string key = lease.Key;
                int pos = key == null ? -1 :
                    key.IndexOf(separator, StringComparison.Ordinal);
                long owner, recipient;
                if (pos <= prefix.Length ||
                    !key.StartsWith(prefix, StringComparison.Ordinal) ||
                    !long.TryParse(key.Substring(prefix.Length,
                        pos - prefix.Length), NumberStyles.None,
                        CultureInfo.InvariantCulture, out owner) ||
                    !long.TryParse(key.Substring(pos + separator.Length),
                        NumberStyles.None, CultureInfo.InvariantCulture, out recipient) ||
                    owner <= 0 || recipient <= 0 || recipient != lease.Value ||
                    AnyaAuraKey(owner, recipient) != key)
                    throw new NotSupportedException(
                        "아냐 공격속도 오라의 수신자 정보가 잘못됐습니다.");
                saved.AnyaRecipients.Add(new SourceBulkRecipientState {
                    OwnerId = owner, RecipientId = recipient
                });
            }
            saved.AnyaRecipients.Sort((a, b) => {
                int x = a.OwnerId.CompareTo(b.OwnerId);
                return x != 0 ? x : a.RecipientId.CompareTo(b.RecipientId);
            });
            return saved;
        }

        private static void ValidateBulkInts(
            List<SourceBulkIntState> rows,
            Dictionary<long, SourceCombatActorCheckpoint> actors,
            string catalog)
        {
            if (rows == null || rows.Count > actors.Count)
                throw new FormatException("누적 기술 장부 항목 수가 잘못됐습니다.");
            long last = 0;
            foreach (var row in rows)
            {
                SourceCombatActorCheckpoint actor;
                if (row == null || row.OwnerId <= last || row.Value <= 0 ||
                    !actors.TryGetValue(row.OwnerId, out actor) ||
                    actor.CatalogId != catalog)
                    throw new FormatException("누적 기술의 소유자/횟수가 잘못됐습니다.");
                last = row.OwnerId;
            }
        }

        public static void ValidateSourceBulkOwners(SourceCombatRuntimeCheckpoint combat)
        {
            if (combat == null || combat.Version != 4 ||
                combat.BulkOwners == null || combat.BulkOwners.Version != 1 ||
                combat.Actors == null || combat.Enemies == null ||
                !BulkFinite(combat.Time) || combat.Time < 0)
                throw new FormatException("자동 기술 저장 장부가 없습니다.");
            var state = combat.BulkOwners;
            var actors = new Dictionary<long, SourceCombatActorCheckpoint>();
            foreach (var actor in combat.Actors)
            {
                if (actor == null || actor.InstanceId <= 0 ||
                    actors.ContainsKey(actor.InstanceId))
                    throw new FormatException("자동 기술 소유자가 중복됐습니다.");
                actors.Add(actor.InstanceId, actor);
            }
            ValidateBulkInts(state.AttackCounts, actors, "MY-07");
            ValidateBulkInts(state.SansBones, actors, "MY-07");
            if (state.Cooldowns == null ||
                state.Cooldowns.Count > actors.Count * 10)
                throw new FormatException("자동 기술 시계 목록이 잘못됐습니다.");
            string lastKey = null;
            foreach (var row in state.Cooldowns)
            {
                SourceCombatActorCheckpoint owner;
                string key = row == null ? null :
                    row.OwnerId.ToString(CultureInfo.InvariantCulture) +
                    ":" + row.SkillKey;
                if (row == null ||
                    !actors.TryGetValue(row.OwnerId, out owner) ||
                    !BulkCooldownAllowed(owner.CatalogId, row.SkillKey) ||
                    !BulkFinite(row.DueAt) || row.DueAt < 0 ||
                    row.DueAt > combat.Time + 600 ||
                    (lastKey != null &&
                     StringComparer.Ordinal.Compare(lastKey, key) >= 0))
                    throw new FormatException("자동 기술 재사용 시계가 잘못됐습니다.");
                lastKey = key;
            }
            if (state.GabanPower == null ||
                state.GabanPower.Count > actors.Count)
                throw new FormatException("가반 추가 피해 목록이 없습니다.");
            long last = 0;
            foreach (var row in state.GabanPower)
            {
                SourceCombatActorCheckpoint owner;
                if (row == null || row.OwnerId <= last ||
                    !actors.TryGetValue(row.OwnerId, out owner) ||
                    owner.CatalogId != "I-08" ||
                    !BulkFinite(row.Value) || row.Value <= 0 ||
                    row.Value > 1000000000000.0)
                    throw new FormatException("가반 추가 피해 수치가 잘못됐습니다.");
                last = row.OwnerId;
            }
            if (state.AllyRegen == null || state.AllyRegen.Count > actors.Count)
                throw new FormatException("아군 재생 시계 목록이 없습니다.");
            last = 0;
            foreach (var row in state.AllyRegen)
            {
                if (row == null || row.OwnerId <= last ||
                    !actors.ContainsKey(row.OwnerId) ||
                    !BulkFinite(row.Value) ||
                    row.Value < combat.Time - .00000001 ||
                    row.Value > combat.Time + 6.00000001)
                    throw new FormatException("아군 재생 시간/대상이 잘못됐습니다.");
                last = row.OwnerId;
            }
            if (state.GiftSerials == null ||
                state.GiftSerials.Count > actors.Count)
                throw new FormatException("선물 지급 횟수 목록이 없습니다.");
            last = 0;
            foreach (var row in state.GiftSerials)
            {
                SourceCombatActorCheckpoint owner;
                if (row == null || row.OwnerId <= last ||
                    !actors.TryGetValue(row.OwnerId, out owner) ||
                    (owner.CatalogId != "MY-08" && owner.CatalogId != "MY-13") ||
                    row.Value <= 0)
                    throw new FormatException("선물/강탈 영수증 번호가 잘못됐습니다.");
                last = row.OwnerId;
            }
            if (state.GabanTargets == null ||
                state.GabanTargets.Count > actors.Count)
                throw new FormatException("가반 방어력 감소 대상이 없습니다.");
            last = 0;
            foreach (var row in state.GabanTargets)
            {
                SourceCombatActorCheckpoint owner;
                if (row == null || row.OwnerId <= last ||
                    !actors.TryGetValue(row.OwnerId, out owner) ||
                    owner.CatalogId != "I-08" ||
                    string.IsNullOrWhiteSpace(row.EnemyId) ||
                    row.EnemyId.Length > 256)
                    throw new FormatException("가반 방어력 감소 신원이 잘못됐습니다.");
                last = row.OwnerId;
            }
            if (state.AnyaRecipients == null ||
                (long)state.AnyaRecipients.Count >
                    (long)actors.Count * actors.Count)
                throw new FormatException("아냐 오라 수신자 목록이 없습니다.");
            long lastOwner = 0, lastRecipient = 0;
            foreach (var row in state.AnyaRecipients)
            {
                SourceCombatActorCheckpoint owner, target;
                if (row == null ||
                    !actors.TryGetValue(row.OwnerId, out owner) ||
                    !actors.TryGetValue(row.RecipientId, out target) ||
                    owner.CatalogId != "MY-08" ||
                    (row.OwnerId < lastOwner ||
                     (row.OwnerId == lastOwner &&
                      row.RecipientId <= lastRecipient)) ||
                    target.Status == null || target.Status.Effects == null)
                    throw new FormatException("아냐 오라 수신자가 잘못됐습니다.");
                bool witnessed = false;
                string source = AnyaAuraKey(row.OwnerId, row.RecipientId);
                foreach (var effect in target.Status.Effects)
                    if (effect != null && effect.SourceId == source &&
                        effect.Type == CombatEffectType.AttackSpeedBuff &&
                        effect.Origin == CombatEffectOrigin.Aura &&
                        effect.NeverExpires && effect.Amount == 30 &&
                        effect.DurationSeconds == 0) witnessed = true;
                if (!witnessed)
                    throw new FormatException(
                        "아냐 오라 수신자와 실제 버프가 일치하지 않습니다.");
                lastOwner = row.OwnerId;
                lastRecipient = row.RecipientId;
            }
            if (!combat.HasSourceModelBasicAttacks &&
                (state.Cooldowns.Count != 0 || state.AttackCounts.Count != 0 ||
                 state.SansBones.Count != 0 || state.GabanPower.Count != 0 ||
                 state.AllyRegen.Count != 0 || state.GiftSerials.Count != 0 ||
                 state.GabanTargets.Count != 0 || state.AnyaRecipients.Count != 0))
                throw new FormatException("원본 모드가 아닌 자동 기술 장부입니다.");
        }

        private void RestoreSourceBulkOwners(SourceBulkOwnerCheckpoint state)
        {
            if (state == null || additionalAutoReadyAt.Count != 0 ||
                bulkAutoAttackCounts.Count != 0 || bulkSansBones.Count != 0 ||
                bulkAutoFlatPower.Count != 0 || bulkAllyRegenerationEnds.Count != 0 ||
                bulkGiftSerials.Count != 0 || bulkArmorAuraTargets.Count != 0 ||
                bulkAnyaAttackSpeedRecipients.Count != 0)
                throw new InvalidOperationException(
                    "자동 기술 상태는 빈 전투에만 복원합니다.");
            foreach (var row in state.Cooldowns)
                additionalAutoReadyAt.Add(
                    row.OwnerId.ToString(CultureInfo.InvariantCulture) +
                    ":" + row.SkillKey, row.DueAt);
            foreach (var row in state.AttackCounts)
                bulkAutoAttackCounts.Add(row.OwnerId, row.Value);
            foreach (var row in state.SansBones)
                bulkSansBones.Add(row.OwnerId, row.Value);
            foreach (var row in state.GabanPower)
                bulkAutoFlatPower.Add(row.OwnerId, row.Value);
            foreach (var row in state.AllyRegen)
                bulkAllyRegenerationEnds.Add(row.OwnerId, row.Value);
            foreach (var row in state.GiftSerials)
                bulkGiftSerials.Add(row.OwnerId, row.Value);
            foreach (var row in state.GabanTargets)
                bulkArmorAuraTargets.Add(row.OwnerId, row.EnemyId);
            foreach (var row in state.AnyaRecipients)
                bulkAnyaAttackSpeedRecipients.Add(
                    AnyaAuraKey(row.OwnerId, row.RecipientId), row.RecipientId);
        }
    }
}
