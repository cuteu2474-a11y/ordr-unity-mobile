using System;
using NUnit.Framework;
using Ordr.Mobile.Core;

namespace Ordr.Mobile.Tests
{
    public class BasicLoopTests
    {
        [Test]
        public void SessionTracksWaveAndEnemies()
        {
            var session = new GameSession();
            Assert.AreEqual("nightmare-mutation", GameSession.ModeId);
            session.Start();
            session.BeginWave(1);
            session.SpawnEnemy();
            session.SpawnEnemy();
            session.DefeatEnemy();
            session.LeakEnemy();
            Assert.AreEqual(0, session.AliveEnemies);
            Assert.AreEqual(1, session.DefeatedEnemies);
            Assert.AreEqual(1, session.LeakedEnemies);
            Assert.AreEqual(SessionPhase.Running, session.Phase);
        }

        [Test]
        public void WaveCannotSkipNumbers()
        {
            var session = new GameSession();
            session.Start();
            Assert.Throws<ArgumentOutOfRangeException>(() => session.BeginWave(2));
        }

        [Test]
        public void PathInterpolatesAndClamps()
        {
            var path = new PathProgress(new[] { new PathPoint(0, 0), new PathPoint(10, 0), new PathPoint(10, 10) });
            Assert.AreEqual(20d, path.TotalLength);
            Assert.AreEqual(5d, path.PositionAt(5).X, 1e-9);
            Assert.AreEqual(5d, path.PositionAt(15).Y, 1e-9);
            Assert.AreEqual(10d, path.PositionAt(999).Y, 1e-9);
        }

        [Test]
        public void UnitCanBeSelectedAndMoved()
        {
            var roster = new UnitRoster();
            long id = roster.Add("verified-unit-id", 1, 2);
            roster.Select(id);
            roster.MoveSelected(3, 4);
            Assert.AreEqual(3d, roster.Get(id).X);
            Assert.AreEqual(4d, roster.Get(id).Y);
            Assert.IsTrue(roster.Remove(id));
            Assert.IsNull(roster.SelectedId);
        }
    }
}
