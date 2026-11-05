using System;
using System.Collections.Generic;
using Ordr.Mobile.Combat;
using Ordr.Mobile.Data;

namespace Ordr.Mobile.Mobile
{
    // UI labels from the public ORDR 1.31 JASS ShowScoreBoard family.
    // The active 2.323[C] total-clear score ledger is NOT wired yet.
    // Only verified, successfully committed common-wisp draws are counted here.
    public sealed class MobileOriginalChatScoreboard
    {
        public static readonly string[] CommonCatalogIds =
        {
            "C-01", "C-02", "C-03", "C-04", "C-05", "C-06", "C-07", "C-08", "C-09"
        };

        public static readonly string[] CommonNames =
        {
            "루피", "조로", "나미", "우솝", "상디", "쵸파", "버기", "총병", "칼병"
        };

        public static readonly string[] GambleNames =
        {
            "하급", "중급", "고급", "다른세계", "압살롬"
        };

        private readonly Dictionary<string, int> confirmedCommonDraws =
            new Dictionary<string, int>(StringComparer.Ordinal);

        public int ConfirmedCommonDraws { get; private set; }

        public MobileOriginalChatScoreboard()
        {
            foreach (string catalogId in CommonCatalogIds)
                confirmedCommonDraws.Add(catalogId, 0);
        }

        // A successful source recruitment transaction is the ONLY caller.
        // Pirate ships, rejects and unrelated roster/fusion creations are excluded.
        public bool RecordCommittedCommonWispDraw(string catalogId)
        {
            int previous;
            if (catalogId == null || !confirmedCommonDraws.TryGetValue(catalogId, out previous))
                return false;
            int next = checked(previous + 1);
            int total = checked(ConfirmedCommonDraws + 1);
            confirmedCommonDraws[catalogId] = next;
            ConfirmedCommonDraws = total;
            return true;
        }

        public int CommonDrawCount(string catalogId)
        {
            int value;
            return catalogId != null && confirmedCommonDraws.TryGetValue(catalogId, out value)
                ? value : 0;
        }
    }

    // Read-only reference snapshot for the original utility-board spec view.
    // It intentionally never changes combat buffs, stacking, skill casts or the wallet.
    // Source profile data is incomplete: these values are descriptive potential
    // sums, NOT the debuffs currently applied to a particular enemy.
    public sealed class MobileUtilitySpecSnapshot
    {
        public int UnitCount { get; internal set; }
        public int AuraDefinitions { get; internal set; }
        public int TriggeredDefinitions { get; internal set; }
        public double AuraArmorReduction { get; internal set; }
        public double AuraMovementSlowPercent { get; internal set; }
        public double TriggeredArmorReduction { get; internal set; }
        public double TriggeredMovementSlowPercent { get; internal set; }
    }

    public static class MobileOriginalUtilityBoard
    {
        // Native aura definitions of the same ability do not contribute twice
        // just because a player owns repeated copies of the same unit.
        public static MobileUtilitySpecSnapshot Capture(
            IEnumerable<string> ownedUnitIds,
            CharacterSkillCatalog profiles,
            IOriginal323AuraCatalog originalAuras)
        {
            var result = new MobileUtilitySpecSnapshot();
            if (ownedUnitIds == null) return result;
            var uniqueAuras = new HashSet<string>(StringComparer.Ordinal);
            foreach (string unitId in ownedUnitIds)
            {
                if (string.IsNullOrWhiteSpace(unitId)) continue;
                result.UnitCount++;

                var nativeAuras = originalAuras?.FindAll(unitId);
                if (nativeAuras != null)
                    foreach (var aura in nativeAuras)
                    {
                        if (aura == null || aura.Level != 1 ||
                            (aura.Type != CombatEffectType.ArmorReduction &&
                             aura.Type != CombatEffectType.MovementSlowPercent))
                            continue;
                        string key = aura.Type + ":" + aura.AbilityRawcode +
                            ":" + aura.BuffRawcode + ":" + aura.Level;
                        if (!uniqueAuras.Add(key)) continue;
                        result.AuraDefinitions++;
                        if (aura.Type == CombatEffectType.ArmorReduction)
                            result.AuraArmorReduction += Math.Max(0, aura.Amount);
                        else
                            result.AuraMovementSlowPercent += Math.Max(0, aura.Amount);
                    }

                var profile = profiles?.Find(unitId);
                if (profile == null) continue;
                foreach (var skill in profile.Skills)
                {
                    double armor = 0;
                    double slow = 0;
                    foreach (var effect in skill.Components)
                    {
                        if (effect.Type == "armorReduction")
                        {
                            var value = effect.Number("amount");
                            if (value.HasValue && !double.IsNaN(value.Value) &&
                                !double.IsInfinity(value.Value) && value.Value > armor)
                                armor = value.Value;
                        }
                        else if (effect.Type == "movementSlow" ||
                                 effect.Type == "lingeringSlow" ||
                                 effect.Type == "areaSlow")
                        {
                            var value = effect.Number("percent");
                            if (value.HasValue && !double.IsNaN(value.Value) &&
                                !double.IsInfinity(value.Value) && value.Value > slow)
                                slow = value.Value;
                        }
                    }
                    // Different phases of one skill are not separate simultaneous
                    // casts. At most one value per type per skill is counted.
                    if (armor <= 0 && slow <= 0) continue;
                    result.TriggeredDefinitions++;
                    result.TriggeredArmorReduction += armor;
                    result.TriggeredMovementSlowPercent += slow;
                }
            }
            return result;
        }
    }

}
