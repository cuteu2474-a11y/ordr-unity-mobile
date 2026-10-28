using System;
using NUnit.Framework;
using Ordr.Mobile.Mobile;
using Ordr.Mobile.Combat;

namespace Ordr.Mobile.Tests
{
    public sealed class MobileTreasureProbabilityCalibrationTests
    {
        [Test]
        public void SourceGeometryUsesTheExactOriginalSquareAndBFjBounds()
        {
            var center=MobileTreasureProbabilityCalibration.FixedCenter(
                (Original323TreasureRules.MinX+Original323TreasureRules.MaxX)/2,
                (Original323TreasureRules.MinY+Original323TreasureRules.MaxY)/2,
                850d,4);
            double expected=1700d*1700d /
                ((Original323TreasureRules.MaxX-Original323TreasureRules.MinX)*
                 (Original323TreasureRules.MaxY-Original323TreasureRules.MinY));
            Assert.AreEqual(expected,center.PerChestHit,1e-13);
            Assert.AreEqual(Math.Pow(1d-expected,4),center.Miss,1e-13);
            Assert.AreEqual(1d,center.Miss+center.Single+center.Multiple,1e-13);
        }

        [Test]
        public void GaimonAndNamiIncreaseCoverageWithoutGuessingAJackpotRate()
        {
            var basic=MobileTreasureProbabilityCalibration.FixedCenter(
                0,0,Original323TreasureRules.DefaultHalfExtent,4);
            var gaimon=MobileTreasureProbabilityCalibration.FixedCenter(
                0,0,Original323TreasureRules.GaimonHalfExtent,4);
            var nami=MobileTreasureProbabilityCalibration.FixedCenter(
                0,0,Original323TreasureRules.GaimonAndNamiHalfExtent,4);
            Assert.Greater(gaimon.PerChestHit,basic.PerChestHit);
            Assert.Greater(nami.PerChestHit,gaimon.PerChestHit);
            Assert.Less(nami.Miss,gaimon.Miss);
            Assert.Greater(gaimon.Multiple,basic.Multiple);
            Assert.AreEqual("SOURCE_GEOMETRY_ONLY_NOT_PLAYER_PARITY",
                MobileTreasureProbabilityCalibration.Mode);
        }

        [Test]
        public void EdgeClipAndNoTreasureAreHandledWithoutPhantomRewards()
        {
            var edge=MobileTreasureProbabilityCalibration.FixedCenter(
                Original323TreasureRules.MinX,Original323TreasureRules.MinY,850,1);
            var far=MobileTreasureProbabilityCalibration.FixedCenter(
                999999,999999,850,10);
            var empty=MobileTreasureProbabilityCalibration.FixedCenter(
                0,0,850,0);
            Assert.AreEqual(850d*850d/
                ((Original323TreasureRules.MaxX-Original323TreasureRules.MinX)*
                 (Original323TreasureRules.MaxY-Original323TreasureRules.MinY)),
                 edge.PerChestHit,1e-13);
            Assert.AreEqual(0d,far.PerChestHit);
            Assert.AreEqual(1d,far.Miss);
            Assert.AreEqual(1d,empty.Miss);
            Assert.AreEqual(0d,empty.Single+empty.Multiple);
        }

        [Test]
        public void ActualDistributionDifferenceIsDetectedAcrossMatchingCohorts()
        {
            var source=new TreasureDistributionSample(20,true,true,7,720,210,70);
            var match=new TreasureDistributionSample(20,true,true,7,725,207,68);
            var mismatch=new TreasureDistributionSample(20,true,true,7,400,550,50);
            Assert.IsTrue(MobileTreasureProbabilityCalibration.
                CompareObserved(source,match).DistributionsConsistent);
            Assert.IsFalse(MobileTreasureProbabilityCalibration.
                CompareObserved(source,mismatch).DistributionsConsistent);
            Assert.Throws<ArgumentException>(()=>
                MobileTreasureProbabilityCalibration.CompareObserved(
                    source,new TreasureDistributionSample(21,true,true,7,720,210,70)));
        }

        [Test]
        public void ComparisonNeedsRealSampleVolumeAndNeverApprovesGameplay()
        {
            var source=new TreasureDistributionSample(10,true,false,4,70,20,10);
            var mobile=new TreasureDistributionSample(10,true,false,4,70,20,10);
            var report=MobileTreasureProbabilityCalibration.CompareObserved(
                source,mobile);
            Assert.IsFalse(report.DistributionsConsistent);
            StringAssert.Contains("1000회 미만",report.Description);
            Assert.Throws<ArgumentException>(()=>
                new TreasureDistributionSample(10,false,false,0,50,1,0));
        }
    }
}
