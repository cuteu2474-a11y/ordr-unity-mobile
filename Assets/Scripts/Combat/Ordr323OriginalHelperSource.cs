using System;
using System.Collections.Generic;

namespace Ordr.Mobile.Combat
{
    // Verified directly from the map's imported SLKs, NOT just war3map.w3a.
    // Units\AbilityData.slk SHA-256:
    // 23e9eb93d805a63cc76bfb617189331aef1e40c0c0ec69860bd7399a3bef2eda
    // Units\UnitBalance.slk SHA-256:
    // f7f26217d8097b9791723feb02d9181277d9afb1947dc0a3bf553ce590ab3af5
    // Units\UnitAbilities.slk SHA-256:
    // fbd47be22d1482d63df15d70d7a8fa38f7f26b288443a5d74aa056104aef139d
    public static class Ordr323OriginalHelperSource
    {
        public const string Rawcode = "h08A";
        public const double StartingMana = 15.0;
        public const double MaximumMana = 10000.0;
        public const double BaseManaRegenPerSecond = 0.3;
        // CJk: each successful A0UY cast restores 300, after the engine has
        // already resolved the ability's inherited mana cost and readiness.
        public const double ManaPotionGain = 300.0;
        public const int ManaPotionCharges = 3;
        public const string HighestHelpResearchRawcode = "R037";
        public const string CastSourceSha256 =
            "23e9eb93d805a63cc76bfb617189331aef1e40c0c0ec69860bd7399a3bef2eda";

        // Original JASS CUo sets kk[player] at RV[player] - 1200 Y.
        // RV[player] is spawned at RectMin(Ll[player]), where Ll = B2..B5.
        public static bool TryGetPlayerHelperSpawn(int playerId, out double x, out double y)
        {
            x = 0; y = 0;
            switch (playerId)
            {
                case 0: x = -5888; y = 4464; return true;
                case 1: x = 128; y = 4464; return true;
                case 2: x = -5888; y = -656; return true;
                case 3: x = 128; y = -656; return true;
                default: return false;
            }
        }
    }

    // The original imported SLK contains separate cast RANGE (Rng1/2) and
    // hit radius (Area1/2). 99999 is the actual sourced cast range, not a
    // guessed "infinite range" converted from the circle radius.
    public sealed class Ordr323OriginalHelperCastDataSource :
        IOriginalHelperCowVerifiedCastDataSource
    {
        public bool TryRead(string rawcode, int level,
            out OriginalHelperCowVerifiedCastData source)
        {
            source = null;
            if (level != 1 && level != 2) return false;
            double range = 99999;
            double mana = 0, cooldown = 0;
            switch (rawcode)
            {
                case "A0BZ":
                    mana = level == 1 ? 500 : 333;
                    cooldown = level == 1 ? 100 : 66;
                    break;
                case "A07T":
                    mana = 320;
                    cooldown = level == 1 ? 150 : 100;
                    break;
                default:
                    return false;
            }
            source = new OriginalHelperCowVerifiedCastData(
                rawcode, level, range, mana, cooldown,
                "W3X:Units/AbilityData.slk:" +
                Ordr323OriginalHelperSource.CastSourceSha256);
            return true;
        }
    }

    // A source-backed runtime instance of the noncombat helper unit. This is
    // the ONE real mana store for manual Wisp refill and automatic skills.
    // CharacterCombatRuntime does NOT fabricate h08A as an attacking unit.
    public sealed class Ordr323OriginalHelperCowActor : IOriginalHelperCowCombatActor
    {
        private readonly Dictionary<string, int> levels =
            new Dictionary<string, int>(StringComparer.Ordinal);
        public string OriginalRawcode => Ordr323OriginalHelperSource.Rawcode;
        public int OwnerPlayerId { get; private set; }
        public double X { get; private set; }
        public double Y { get; private set; }
        public bool IsAlive { get; private set; } = true;
        public double CurrentMana { get; private set; }
        public double MaximumMana => Ordr323OriginalHelperSource.MaximumMana;
        public bool MaximumOutputNavigationChosen { get; private set; }
        public bool AlchemyNavigationChosen { get; private set; }
        // CKS(1) researches R037 for the owning PLAYER, in addition to
        // leveling helper skills. Keep this visible to later tech checks.
        public int HighestHelpResearchLevel { get; private set; }
        public int ManaPotionChargesRemaining =>
            GetAbilityLevel("A0UY") == 0 ? 0 :
            Ordr323OriginalHelperSource.ManaPotionCharges - GetAbilityLevel("A0UY") + 1;

        public Ordr323OriginalHelperCowActor(int ownerPlayerId)
        {
            double x, y;
            if (!Ordr323OriginalHelperSource.TryGetPlayerHelperSpawn(
                ownerPlayerId, out x, out y))
                throw new ArgumentOutOfRangeException(nameof(ownerPlayerId));
            OwnerPlayerId = ownerPlayerId;
            X = x; Y = y;
            CurrentMana = Ordr323OriginalHelperSource.StartingMana;
            // Imported Units\UnitAbilities.slk explicitly includes A0BZ,
            // A07T, among the default h08A abilities. Initial level is 1.
            levels["A0BZ"] = 1;
            levels["A07T"] = 1;
            levels["A0BY"] = 1;
            levels["A07X"] = 1;
            levels["A07W"] = 1;
        }

        // CKS(1, player): one of the original mutually exclusive navigation
        // choices. These five upgrades AND A0UY are one indivisible effect.
        // Round/unselected checks belong to the session navigation service.
        internal void ApplyVerifiedMaximumOutputNavigation()
        {
            if (!IsAlive || MaximumOutputNavigationChosen || AlchemyNavigationChosen)
                throw new InvalidOperationException("도움소 최대출력 항법 중복 또는 대상 부재");
            foreach (string rawcode in new[] { "A0BZ", "A0BY", "A07T", "A07X", "A07W" })
            {
                if (GetAbilityLevel(rawcode) != 1)
                    throw new InvalidOperationException("원본 도움소 초기 능력 상태가 다릅니다: " + rawcode);
            }
            if (GetAbilityLevel("A0UY") != 0)
                throw new InvalidOperationException("마나 포션이 이미 존재합니다.");
            foreach (string rawcode in new[] { "A0BZ", "A0BY", "A07T", "A07X", "A07W" })
                levels[rawcode] = 2;
            levels["A0UY"] = 1;
            HighestHelpResearchLevel = 1;
            MaximumOutputNavigationChosen = true;
        }

        // CKS(2, player) grants A0J2 and researches R037. Unlike CKS(1),
        // none of the five attack/area skills are upgraded and A0UY is absent.
        internal void ApplyVerifiedAlchemyNavigation()
        {
            if (!IsAlive || MaximumOutputNavigationChosen || AlchemyNavigationChosen ||
                GetAbilityLevel(Ordr323OriginalHelperAlchemy.AbilityRawcode) != 0)
                throw new InvalidOperationException("연금술 항법을 중복 또는 다른 항법과 함께 선택할 수 없습니다.");
            levels[Ordr323OriginalHelperAlchemy.AbilityRawcode] = 1;
            HighestHelpResearchLevel = 1;
            AlchemyNavigationChosen = true;
        }

        // Called ONLY after the host has committed an authentic A0UY cast,
        // including any inherited Warcraft base ability cost. CJk advances
        // level 1->2->3->removed, then applies +300 even at maximum mana.
        internal void ApplyManaPotionEffectAfterCommittedCast()
        {
            int level = GetAbilityLevel("A0UY");
            if (!IsAlive || !MaximumOutputNavigationChosen || level < 1 || level > 3)
                throw new InvalidOperationException("유효하지 않은 마나 포션 시전");
            if (level == 3) levels.Remove("A0UY");
            else levels["A0UY"] = level + 1;
            SetMana(Math.Min(MaximumMana, CurrentMana +
                Ordr323OriginalHelperSource.ManaPotionGain));
        }

        public int GetAbilityLevel(string originalAbilityRawcode)
        {
            int value;
            return originalAbilityRawcode != null &&
                levels.TryGetValue(originalAbilityRawcode, out value) ? value : 0;
        }

        // Upgrade levels must originate from an independently verified
        // navigation/research transaction, never a UI decoration.
        public void ConfigureVerifiedAbilityUpgrade(
            string originalAbilityRawcode, int level, string originalEvidence)
        {
            OriginalHelperCowAreaEvidence.VerifiedRadius(originalAbilityRawcode);
            if (string.IsNullOrWhiteSpace(originalEvidence) || level < 1 || level > 2)
                throw new ArgumentException("원본 도움소 스킬 강화의 출처가 필요합니다.");
            if (!IsAlive)
                throw new InvalidOperationException("사망한 도움소는 강화할 수 없습니다.");
            levels[originalAbilityRawcode] = level;
        }

        public bool IsOriginalCastReady(string originalAbilityRawcode)
        {
            // Ability availability only: mana and cooldown are validated by
            // OriginalHelperCowLiveCombatWorld, not duplicated here.
            return IsAlive && GetAbilityLevel(originalAbilityRawcode) > 0;
        }

        public void SetMana(double amount)
        {
            if (!IsAlive || double.IsNaN(amount) || double.IsInfinity(amount) ||
                amount < 0 || amount > MaximumMana)
                throw new ArgumentOutOfRangeException(nameof(amount));
            CurrentMana = amount;
        }

        public void AdvanceBaseManaRegeneration(double elapsedSeconds)
        {
            if (double.IsNaN(elapsedSeconds) || double.IsInfinity(elapsedSeconds) ||
                elapsedSeconds < 0) throw new ArgumentOutOfRangeException(nameof(elapsedSeconds));
            if (!IsAlive || elapsedSeconds == 0) return;
            CurrentMana = Math.Min(MaximumMana,
                CurrentMana + Ordr323OriginalHelperSource.BaseManaRegenPerSecond *
                elapsedSeconds);
        }
    }
}
