using NUnit.Framework;
using UnityEngine;
using Ordr.Mobile.Unity;

namespace Ordr.Mobile.Tests
{
    public sealed class OrdrBattlefieldArtTests
    {
        [Test]
        public void AuthoredAssetsAreCachedAndDontNeedNetworkOrPrivatePhotos()
        {
            Assert.IsNotNull(OrdrBattlefieldArt.BoardSprite);
            Assert.AreSame(OrdrBattlefieldArt.BoardSprite, OrdrBattlefieldArt.BoardSprite);
            Assert.IsNotNull(OrdrBattlefieldArt.StoryTexture);
            Assert.IsNotNull(OrdrBattlefieldArt.HeaderTexture);
            Assert.AreEqual(256, OrdrBattlefieldArt.StoryTexture.width);
            Assert.AreSame(OrdrBattlefieldArt.UnitSprite("C-01"),
                OrdrBattlefieldArt.UnitSprite("C-01"));
            Assert.AreNotSame(OrdrBattlefieldArt.UnitSprite("C-01"),
                OrdrBattlefieldArt.UnitSprite("C-02"));
            Assert.AreEqual(1f, OrdrBattlefieldArt.UnitSprite("C-01").bounds.size.x, 0.0001f);
            Assert.IsNotNull(OrdrBattlefieldArt.GradeFrame);
            Assert.IsNotNull(OrdrBattlefieldArt.EnemySprite(true));
            Assert.AreNotSame(OrdrBattlefieldArt.EnemySprite(true),
                OrdrBattlefieldArt.EnemySprite(false));
            Assert.IsNotNull(OrdrBattlefieldArt.EnemyHaloSprite);
            Assert.IsNotNull(OrdrBattlefieldArt.SkillRingSprite);
            Assert.IsNotNull(OrdrBattlefieldArt.SkillSparkSprite);
            Assert.AreSame(OrdrBattlefieldArt.SkillRingSprite,
                OrdrBattlefieldArt.SkillRingSprite);
            Assert.IsNotNull(OrdrBattlefieldArt.EvolutionSprite);
        }
    }
}
