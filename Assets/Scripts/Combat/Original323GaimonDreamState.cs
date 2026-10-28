using System;

namespace Ordr.Mobile.Combat
{
    // CCP reads YL[JG] exactly when the original CH8 -> CCP ability is
    // applied. The UI must not infer YL from the presence of any Nami unit.
    public interface IOriginal323GaimonNamiBonusWitness
    {
        bool TryReadOriginalYlFlag(out bool namiBonus, out string sourceEvidence);
    }

    // Player-level CCP state: iK means "already applied", YN enables two
    // rewards per treasure, LM becomes 978 or 1125. Multiple copies of
    // Gaimon can never double the flag or advance a timer.
    public sealed class Original323GaimonDreamState
    {
        public bool Applied { get; private set; }
        public bool NamiBonusWhenApplied { get; private set; }
        public double SearchHalfExtent =>
            !Applied ? Original323TreasureRules.DefaultHalfExtent :
            NamiBonusWhenApplied ?
                Original323TreasureRules.GaimonAndNamiHalfExtent :
                Original323TreasureRules.GaimonHalfExtent;
        public int TreasureWispQuantity => Applied ? 2 : 1;

        // Call only after a Gaimon h0BG has been COMMITTED into the live
        // roster, never from a spawn preview or before a failed transaction
        // rolls it back. A missing YL witness leaves CCP un-applied.
        public bool TryApplyAfterOwnedGaimon(
            bool hasCommittedGaimon,
            IOriginal323GaimonNamiBonusWitness sourceYl)
        {
            if (Applied || !hasCommittedGaimon || sourceYl == null)
                return false;
            bool nami; string evidence;
            if (!sourceYl.TryReadOriginalYlFlag(out nami, out evidence) ||
                !Original323BossRoundRules.TrustedRuntimeEvidence(evidence))
                return false;
            NamiBonusWhenApplied = nami;
            Applied = true; // original iK / YN are set once per player
            return true;
        }

        // Original CCP has no automatic "unapply on sell" step.
        // Any save/load must restore the whole snapshot atomically.
        public Original323GaimonDreamSnapshot Capture() =>
            new Original323GaimonDreamSnapshot(Applied,NamiBonusWhenApplied);

        public void Restore(Original323GaimonDreamSnapshot snapshot)
        {
            if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));
            if (!snapshot.Applied && snapshot.NamiBonusWhenApplied)
                throw new ArgumentException("원본 가이몬 꿈 상태가 모순됩니다.");
            Applied=snapshot.Applied;
            NamiBonusWhenApplied=snapshot.NamiBonusWhenApplied;
        }
    }

    public sealed class Original323GaimonDreamSnapshot
    {
        public bool Applied { get; private set; }
        public bool NamiBonusWhenApplied { get; private set; }
        public Original323GaimonDreamSnapshot(bool applied,bool nami)
        {
            if (!applied && nami) throw new ArgumentException("CCP Nami bonus without applied dream");
            Applied=applied;NamiBonusWhenApplied=nami;
        }
    }
}
