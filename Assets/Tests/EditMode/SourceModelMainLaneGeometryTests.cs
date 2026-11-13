using System;
using NUnit.Framework;
using Ordr.Mobile.Combat;

namespace Ordr.Mobile.Tests
{
    public sealed class SourceModelMainLaneGeometryTests
    {
        private static SourceModelMainLaneGeometry MakeLane()
        {
            return new SourceModelMainLaneGeometry(new[] {
                new CombatPathPoint(-4960, 5632),
                new CombatPathPoint(-4992, 3456),
                new CombatPathPoint(-2560, 3456),
                new CombatPathPoint(-2560, 5632)
            });
        }

        [Test]
        public void BossReversesOnlyOnLeftSideWithoutAStop()
        {
            var lane = MakeLane();
            double length = lane.TopY - lane.BottomY;
            Assert.AreEqual(-4992, lane.BossPosition(0).X);
            Assert.AreEqual(5632, lane.BossPosition(0).Y);
            Assert.AreEqual(3456, lane.BossPosition(length).Y);
            Assert.AreEqual(5632, lane.BossPosition(2 * length).Y);
            Assert.Less(lane.BossPosition(length + 10).Y,
                lane.BossPosition(length + 20).Y);
            Assert.AreEqual(0, lane.BossProgress(2 * length));
        }

        [Test]
        public void StableSlotsNeverOverlapMonsterLaneX()
        {
            var lane = MakeLane();
            for (long id = 1; id <= 90; id++)
            {
                var slot = lane.UnitPosition(id);
                Assert.Less(slot.X, lane.LeftX);
                Assert.Greater(slot.Y, lane.BottomY);
                Assert.Less(slot.Y, lane.TopY);
                Assert.AreEqual(slot.X, lane.UnitPosition(id).X);
                Assert.AreEqual(slot.Y, lane.UnitPosition(id).Y);
            }
        }

        [Test]
        public void BadGeometryAndTravelDistanceAreRejected()
        {
            var lane = MakeLane();
            Assert.Throws<ArgumentOutOfRangeException>(() => lane.BossPosition(-1));
            Assert.Throws<ArgumentOutOfRangeException>(() => lane.BossProgress(double.NaN));
            Assert.Throws<ArgumentOutOfRangeException>(() => lane.UnitPosition(0));
            Assert.Throws<ArgumentException>(() =>
                new SourceModelMainLaneGeometry(new[] {
                    new CombatPathPoint(0, 0), new CombatPathPoint(0, 1)
                }));
        }
    }
}
