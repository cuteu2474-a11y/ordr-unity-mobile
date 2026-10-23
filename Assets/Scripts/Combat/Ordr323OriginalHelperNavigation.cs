using System;
using Ordr.Mobile.Core;

namespace Ordr.Mobile.Combat
{
    // war3map.j CKX -> CKS: navigation is available after wave 20 and
    // a player may choose only one navigation. Other routes must share this
    // same selection lock when their source-verified effects are implemented.
    public sealed class Ordr323OriginalHelperNavigation
    {
        private readonly GameSession game;
        private readonly Ordr323OriginalHelperCowActor helper;
        public string ChosenNavigation { get; private set; }

        public Ordr323OriginalHelperNavigation(GameSession session,
            Ordr323OriginalHelperCowActor actualHelper)
        {
            game = session ?? throw new ArgumentNullException(nameof(session));
            helper = actualHelper ?? throw new ArgumentNullException(nameof(actualHelper));
        }

        public bool CanChooseMaximumOutput =>
            game.Phase == SessionPhase.Running && game.CurrentWave > 20 &&
            ChosenNavigation == null && helper.IsAlive &&
            !helper.MaximumOutputNavigationChosen && !helper.AlchemyNavigationChosen;

        public bool CanChooseAlchemy =>
            game.Phase == SessionPhase.Running && game.CurrentWave > 20 &&
            ChosenNavigation == null && helper.IsAlive &&
            !helper.MaximumOutputNavigationChosen && !helper.AlchemyNavigationChosen;

        public bool TryChooseAlchemy()
        {
            if (!CanChooseAlchemy) return false;
            helper.ApplyVerifiedAlchemyNavigation();
            ChosenNavigation = "최고의도움 :: 연금술";
            return true;
        }

        public bool TryChooseMaximumOutput()
        {
            if (!CanChooseMaximumOutput) return false;
            helper.ApplyVerifiedMaximumOutputNavigation();
            ChosenNavigation = "최고의도움 :: 최대출력";
            return true;
        }
    }

    // A0UY inherits unknown cast-cost fields from base AOsf. The JASS
    // handler CJk proves only the AFTER-CAST +300 / three charge behavior.
    // A real original-cost/cooldown validator must commit the cast first.
    public interface IOrdr323VerifiedManaPotionCastPort
    {
        bool TryCommitOriginalA0UYCast(
            Ordr323OriginalHelperCowActor actor, int abilityLevel);
    }

    public sealed class Ordr323OriginalHelperManaPotion
    {
        private readonly GameSession game;
        private readonly Ordr323OriginalHelperCowActor helper;
        private readonly IOrdr323VerifiedManaPotionCastPort casting;

        public Ordr323OriginalHelperManaPotion(GameSession session,
            Ordr323OriginalHelperCowActor actualHelper,
            IOrdr323VerifiedManaPotionCastPort verifiedCasting)
        {
            game = session ?? throw new ArgumentNullException(nameof(session));
            helper = actualHelper ?? throw new ArgumentNullException(nameof(actualHelper));
            casting = verifiedCasting ?? throw new ArgumentNullException(nameof(verifiedCasting));
        }

        public bool CanAttempt => game.Phase == SessionPhase.Running &&
            helper.IsAlive && helper.MaximumOutputNavigationChosen &&
            helper.ManaPotionChargesRemaining > 0;

        public bool TryUse()
        {
            if (!CanAttempt) return false;
            int level = helper.GetAbilityLevel("A0UY");
            if (!casting.TryCommitOriginalA0UYCast(helper, level)) return false;
            helper.ApplyManaPotionEffectAfterCommittedCast();
            return true;
        }
    }
}
