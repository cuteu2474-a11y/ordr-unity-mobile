using System;

namespace Ordr.Mobile.Core
{
    // CUo + CUn in the pinned 2.323[C] JASS: 50G at CUo, +10G
    // for the only active player at CUn, five e0IX, one e0HP at CJ center.
    // e0HP is an evolution-system actor, NOT an ordinary combat character.
    public static class Original323SoloStartProfile
    {
        public const string EvolutionRawcode = "e0HP";
        public const double EvolutionX = -3456.0;
        public const double EvolutionY = -9696.0;

        public static VerifiedEconomyStartProfile Create()
        {
            return new VerifiedEconomyStartProfile("ORDR_S2_2.323[C]",
                GameSession.ModeId,
                "war3map.j SHA256 96201e80edec1b2764f85818760306c6b2f0becd390ba4d63f41c86f3dfaff6d; CUo/CUn/CJ",
                true,
                new ResourceBundle().Add("gold", 60).Add(WispResourceIds.Random, 5),
                new[] { new VerifiedStartingUnit(EvolutionRawcode, EvolutionX, EvolutionY) });
        }

        public static bool MatchesPinnedSource(VerifiedEconomyStartProfile profile)
        {
            if (profile == null || !profile.IsSourceVerified ||
                profile.Evidence != Create().Evidence ||
                profile.Resources.Get("gold") != 60 ||
                profile.Resources.Get(WispResourceIds.Random) != 5 ||
                profile.StartingUnits.Count != 1) return false;
            foreach (var row in profile.Resources.Entries)
                if (row.Key != "gold" && row.Key != WispResourceIds.Random) return false;
            var unit = profile.StartingUnits[0];
            return unit.CatalogId == EvolutionRawcode &&
                unit.X == EvolutionX && unit.Y == EvolutionY;
        }
    }

    // The external evolution-world port must register one non-combat e0HP
    // in the same UnitRoster and undo the real spawn on rollback.
    public interface IOriginalStartEvolutionWorld
    {
        long SpawnAndTrack(SharedEconomySession shared, string rawcode, double x, double y);
        void RemoveAndUntrack(SharedEconomySession shared, long instanceId);
    }
}
