using System;
using NUnit.Framework;
using Ordr.Mobile.Core;

namespace Ordr.Mobile.Tests
{
    public class OtherworldModeConfigurationTests
    {
        [Test]
        public void OnlyNightmareOtherworldIsConfigured()
        {
            var config = new OtherworldModeConfiguration();
            Assert.IsTrue(config.NightmareOnly);
            Assert.IsTrue(config.OtherworldEnabled);
            Assert.AreEqual("nightmare-otherworld", OtherworldModeConfiguration.ModeId);
        }

        [Test]
        public void CatalogFlagIsExplicitAndNotInferred()
        {
            var config = new OtherworldModeConfiguration();
            var ordinary = new UnitCatalogEntry("test-1", "테스트 일반", false, "https://ordr.kr/");
            var special = new UnitCatalogEntry("test-2", "테스트 이세계", true, "https://ordr.kr/");
            Assert.IsFalse(config.IsOtherworldUnit(ordinary));
            Assert.IsTrue(config.IsOtherworldUnit(special));
        }

        [Test]
        public void MissingSourceIsRejected()
        {
            Assert.Throws<ArgumentException>(() =>
                new UnitCatalogEntry("test", "테스트", true, ""));
        }
    }
}
