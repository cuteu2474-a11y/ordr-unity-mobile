using System;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using Ordr.Mobile.Combat;
using Ordr.Mobile.Unity;

namespace Ordr.Mobile.Tests
{
    public sealed class OrdrMonsterPathPresentationTests
    {
        private static string Data(string filename) =>
            File.ReadAllText(Path.Combine(Application.dataPath, "..", "data", filename));

        [Test]
        public void PracticeRoadUsesTheExactFourMovementCornersAndClosesTheLoop()
        {
            var model = new SourceModelPracticeCatalog(
                Data("mobile-standalone-enemy-model.json"), 65);
            var geometry = OrdrMonsterPathGeometry.Build(model.RouteCorners, true);
            Assert.IsTrue(geometry.IsLoop);
            Assert.AreEqual(4, geometry.Segments.Count);
            for (int i = 0; i < model.RouteCorners.Count; i++)
            {
                int next = (i + 1) % model.RouteCorners.Count;
                Assert.AreEqual(model.RouteCorners[i].X, geometry.Segments[i].Start.x, 0.0001);
                Assert.AreEqual(model.RouteCorners[i].Y, geometry.Segments[i].Start.y, 0.0001);
                Assert.AreEqual(model.RouteCorners[next].X, geometry.Segments[i].End.x, 0.0001);
                Assert.AreEqual(model.RouteCorners[next].Y, geometry.Segments[i].End.y, 0.0001);
            }
            Assert.AreEqual(geometry.Start, geometry.End);
            Assert.AreEqual(model.Position(0).X, geometry.Start.x, 0.0001);
        }

        [Test]
        public void DestinationIsNotInventedForLoopingMonsters()
        {
            var line = OrdrMonsterPathGeometry.Build(
                new[] { new CombatPathPoint(0, 0),
                    new CombatPathPoint(100, 0) }, false);
            Assert.IsFalse(line.IsLoop);
            Assert.AreEqual(1, line.Segments.Count);
            Assert.AreEqual(new Vector2(0, 0), line.Start);
            Assert.AreEqual(new Vector2(100, 0), line.End);
            Assert.AreEqual(0f, line.Segments[0].AngleDegrees, 0.001f);
        }

        [Test]
        public void RejectedBadGeometryCannotFormAPath()
        {
            Assert.Throws<ArgumentNullException>(() => OrdrMonsterPathGeometry.Build(null, true));
            Assert.Throws<ArgumentException>(() => OrdrMonsterPathGeometry.Build(
                new[] { new CombatPathPoint(1, 1) }, true));
            Assert.Throws<ArgumentException>(() => OrdrMonsterPathGeometry.Build(
                new[] { new CombatPathPoint(1, 1),
                    new CombatPathPoint(1, 1) }, false));
        }

        [Test]
        public void CreatedPathCanBeClearedWithoutTouchingBattleState()
        {
            var root = new GameObject("test path visuals");
            try
            {
                var visual = root.AddComponent<OrdrMonsterPathPresenter>();
                visual.ShowPracticeRoute(new[] {
                    new CombatPathPoint(0, 0), new CombatPathPoint(100, 0),
                    new CombatPathPoint(100, 100), new CombatPathPoint(0, 100)
                });
                Assert.IsTrue(visual.HasPath);
                Assert.AreEqual(4, visual.SegmentCount);
                visual.Clear();
                Assert.IsFalse(visual.HasPath);
                Assert.AreEqual(0, visual.SegmentCount);
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }

        [Test]
        public void PathAssetsReuseOneSharedSpritePerKind()
        {
            Assert.AreSame(OrdrBattlefieldArt.PathFillSprite, OrdrBattlefieldArt.PathFillSprite);
            Assert.AreSame(OrdrBattlefieldArt.PathCircleSprite, OrdrBattlefieldArt.PathCircleSprite);
            Assert.AreSame(OrdrBattlefieldArt.PathArrowSprite, OrdrBattlefieldArt.PathArrowSprite);
            Assert.AreSame(OrdrBattlefieldArt.PathSpawnSprite, OrdrBattlefieldArt.PathSpawnSprite);
        }
    }
}
