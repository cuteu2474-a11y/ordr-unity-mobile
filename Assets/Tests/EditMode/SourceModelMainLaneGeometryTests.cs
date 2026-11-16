using System;
using System.Collections.Generic;
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
        public void FirstFiveRecruitsConvergeOnOneMiddleLeftRally()
        {
            var lane = MakeLane();
            var rally = lane.RallyPoint;
            Assert.AreEqual(-5112d, rally.X);
            Assert.AreEqual(4544d, rally.Y);
            // First owned combat characters follow the non-combat start actor.
            for (long id = 2; id <= 6; id++)
            {
                var position = lane.UnitPosition(id);
                Assert.LessOrEqual(Math.Abs(position.Y - rally.Y), 64d);
                double dx = position.X - lane.LeftX;
                double dy = position.Y - rally.Y;
                Assert.Less(dx * dx + dy * dy, 500d * 500d,
                    "All five early attackers must reach the middle-left lane at original 500 range.");
            }
        }

        [Test]
        public void FullCompactFormationAndOverflowRemainDistinctAndInFiniteReach()
        {
            var lane = MakeLane();
            var rally = lane.RallyPoint;
            var unique = new HashSet<Tuple<double, double>>();
            for (long id = 1; id <= 288; id++)
            {
                var position = lane.UnitPosition(id);
                Assert.IsTrue(unique.Add(Tuple.Create(position.X, position.Y)),
                    "Compact rally slots must not repeat for the supported stacked roster.");
                Assert.Less(position.X, lane.LeftX);
                Assert.Greater(position.Y, lane.BottomY);
                Assert.Less(position.Y, lane.TopY);
                double dx = position.X - lane.LeftX;
                double dy = position.Y - rally.Y;
                Assert.Less(dx * dx + dy * dy, 500d * 500d);
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
