namespace Ordr.Mobile.Combat
{
    // The W3A field "isr2" is source-verified, but HOW AIsr damage
    // reduction affects an enemy's outgoing/incoming attack pipeline is
    // not yet proven in Unity. A07T cannot cast unless an original-tested
    // implementation explicitly handles this persistent ability change.
    public interface IOriginalA04REffectEngine
    {
        bool CanApplyOriginalA04RLevel(CombatEnemy enemy, int nextLevel,
            double originalIsr2Field);
        // This must synchronously update the real enemy's ability-dependent
        // combat effects. Its transaction must not throw after preflight.
        void ApplyOriginalA04RLevel(CombatEnemy enemy, int nextLevel,
            double originalIsr2Field);
    }
}
