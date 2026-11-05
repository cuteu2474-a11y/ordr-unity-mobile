using System;

namespace Ordr.Mobile.Core
{
    // CKW/CKX share kt[player]: one selection after round 20.
    public sealed class Original323NavigationSelection
    {
        private readonly GameSession game;
        private bool choosing;
        public string ChosenNavigation { get; private set; }
        public bool CanChoose => !choosing && ChosenNavigation == null &&
            game.Phase == SessionPhase.Running && game.CurrentWave > 20;
        public Original323NavigationSelection(GameSession session)
        { game = session ?? throw new ArgumentNullException(nameof(session)); }
        public bool TryChoose(string name, Action apply)
        {
            if (string.IsNullOrWhiteSpace(name) || apply == null)
                throw new ArgumentException("항법 이름과 적용 처리가 필요합니다.");
            if (!CanChoose) return false;
            choosing = true;
            try { apply(); ChosenNavigation = name; return true; }
            finally { choosing = false; }
        }
    }
}
