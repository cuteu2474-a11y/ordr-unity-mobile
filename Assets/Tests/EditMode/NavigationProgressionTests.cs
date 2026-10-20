using System;
using NUnit.Framework;
using Ordr.Mobile.Core;

namespace Ordr.Mobile.Tests
{
    public class NavigationProgressionTests
    {
        // 테스트 전용 가상 노드이며 원작의 실제 항법 노드를 뜻하지 않는다.
        [Test]
        public void CannotAcquireBeforePrerequisiteOrAcrossRoutes()
        {
            var navigation = new NavigationProgression(new[] {
                new NavigationNode("test-a", "test-route-1", null),
                new NavigationNode("test-b", "test-route-1", new[] { "test-a" }),
                new NavigationNode("test-c", "test-route-2", null)
            });
            navigation.ChooseRoute("test-route-1");
            Assert.IsFalse(navigation.CanAcquire("test-b"));
            Assert.IsFalse(navigation.CanAcquire("test-c"));
            navigation.Acquire("test-a");
            Assert.IsTrue(navigation.CanAcquire("test-b"));
            navigation.Acquire("test-b");
            Assert.AreEqual(2, navigation.AcquiredCount);
            Assert.Throws<InvalidOperationException>(() => navigation.Acquire("test-b"));
            Assert.Throws<InvalidOperationException>(() => navigation.ChooseRoute("test-route-2"));
        }

        [Test]
        public void RejectsMissingPrerequisitesAndUnknownRoutes()
        {
            Assert.Throws<ArgumentException>(() => new NavigationProgression(new[] {
                new NavigationNode("test-b", "test-route", new[] { "missing" })
            }));
            var empty = new NavigationProgression(new NavigationNode[0]);
            Assert.Throws<System.Collections.Generic.KeyNotFoundException>(() => empty.ChooseRoute("unknown"));
        }
    }
}
