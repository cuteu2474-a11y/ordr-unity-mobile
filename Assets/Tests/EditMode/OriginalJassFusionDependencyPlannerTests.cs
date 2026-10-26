using System;
using System.Collections.Generic;
using NUnit.Framework;
using Ordr.Mobile.Data;
using Ordr.Mobile.Mobile;
using UnityEngine;

namespace Ordr.Mobile.Tests
{
    public sealed class OriginalJassFusionDependencyPlannerTests
    {
        private static OriginalJassFusionDependencyPlanner Planner()
        {
            var r = Resources.Load<TextAsset>("OrdrData/original-jass-fusion-registry");
            var o = Resources.Load<TextAsset>("OrdrData/original-fusion-object-reconciliation");
            var u = Resources.Load<TextAsset>("OrdrData/units");
            Assert.IsNotNull(r); Assert.IsNotNull(o); Assert.IsNotNull(u);
            var catalog = OriginalJassFusionCatalog.FromJson(r.text);
            return new OriginalJassFusionDependencyPlanner(catalog,
                OriginalFusionObjectPreviewCatalog.FromJson(o.text, catalog,
                    CharacterUnitCatalog.FromJson(u.text)));
        }

        [Test]
        public void RealAceFusionListsOriginalLuffyAndMarineGunner()
        {
            var x = Planner().Build("A00D", 4);
            Assert.AreEqual(2, x.Count);
            Assert.AreEqual("h003", x[0].Rawcode);
            Assert.AreEqual("h009", x[1].Rawcode);
            Assert.IsTrue(x[0].Name.Contains("루피"));
            Assert.IsTrue(x[1].Name.Contains("총병"));
            Assert.AreEqual(1, x[0].Count);
        }

        [Test]
        public void OriginalA024ExpandsTwoUncommonCraftingBranches()
        {
            bool a = false, b = false, leaf = false;
            foreach (var x in Planner().Build("A024", 4))
            {
                if (x.Source == "A0N5") a = true;
                if (x.Source == "A00D")
                { b = true; Assert.Greater(x.SpecialCount, 0); }
                if (x.Depth == 1 && x.Rawcode == "h003") leaf = true;
            }
            Assert.IsTrue(a); Assert.IsTrue(b); Assert.IsTrue(leaf);
        }

        [Test]
        public void ShallowExpansionMarksOmittedDescendants()
        {
            var x = Planner().Build("A024", 1);
            Assert.AreEqual(3, x.Count);
            Assert.IsTrue(x[0].DepthLimited);
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                Planner().Build("A024", 0));
            Assert.Throws<KeyNotFoundException>(() =>
                Planner().Build("XXXX", 3));
        }
    }
}
