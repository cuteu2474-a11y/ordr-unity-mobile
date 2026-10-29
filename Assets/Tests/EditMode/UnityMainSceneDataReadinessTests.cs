using System;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Ordr.Mobile.Unity;
using UnityEngine;

namespace Ordr.Mobile.Tests
{
    // MainGame and the /data importer previously had NO scene-level smoke
    // assertion despite 946 passing pure EditMode tests. This makes a blank
    // first-launch screen fail in CI before an APK can ever be considered.
    public sealed class UnityMainSceneDataReadinessTests
    {
        private static readonly string[] RequiredNames =
        {
            "units",
            "character-skill-profiles",
            "original-2323C-map-base-stats",
            "original-2323C-monster-wave-script-evidence",
            "original-2323C-monster-object-abilities",
            "original-2323C-wisp-pools"
        };

        [Test]
        public void PinnedRuntimeJsonIsActuallyLoadableFromUnityResources()
        {
            foreach (var name in RequiredNames)
            {
                var source = Path.Combine(Application.dataPath, "..",
                    "data", name + ".json");
                Assert.IsTrue(File.Exists(source), "Missing original data: " + name);
                var runtime = Resources.Load<TextAsset>("OrdrData/" + name);
                Assert.IsNotNull(runtime,
                    "Editor importer did not package a TextAsset: " + name);
                Assert.IsFalse(string.IsNullOrWhiteSpace(runtime.text),
                    "Imported runtime data is empty: " + name);
                Assert.AreEqual(File.ReadAllText(source).Length,
                    runtime.text.Length, "Runtime JSON differs from /data: " + name);
            }
        }

        [Test]
        public void DefaultSceneRetainsGameControllerAndStartUIComponents()
        {
            var scene = File.ReadAllText(Path.Combine(
                Application.dataPath, "Scenes", "MainGame.unity"));
            string ControllerGuid(string metaPath)
            {
                var meta = File.ReadAllText(Path.Combine(
                    Application.dataPath, metaPath));
                return meta.Split('\n').First(line =>
                    line.StartsWith("guid: ", StringComparison.Ordinal)).Substring(6).Trim();
            }
            var controller = ControllerGuid(
                "Scripts/Unity/OrdrCharacterCombatController.cs.meta");
            var shell = ControllerGuid(
                "Scripts/Unity/OrdrMainSceneShell.cs.meta");
            StringAssert.Contains("guid: " + controller, scene);
            StringAssert.Contains("guid: " + shell, scene);
            StringAssert.Contains("m_Name: ORDR Game Root", scene);
        }

        [Test]
        public void UnityStartUsesPinnedEconomyAndReportsMissingCombatTruthfully()
        {
            var root = new GameObject("ORDR First-Play Smoke");
            root.SetActive(false);
            try
            {
                var c = root.AddComponent<OrdrCharacterCombatController>();
                var awake = typeof(OrdrCharacterCombatController)
                    .GetMethod("Awake", BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.IsNotNull(awake);
                awake.Invoke(c, null); // EditMode inactive GameObject, no gameplay tick.
                Assert.IsNotNull(c.Units);
                Assert.AreEqual(275, c.Units.Count);
                Assert.IsNotNull(c.Skills);
                Assert.IsNotNull(c.OriginalMonsterSpawnIdentities);
                Assert.IsNotNull(c.OriginalMonsterObjectAbilities);
                Assert.IsNull(c.Combat, "Do not invent an unverified live damage world.");
                string firstBlocker;
                Assert.IsFalse(c.CanBeginVerifiedFirstWave(out firstBlocker));
                StringAssert.Contains("피해 계산기", firstBlocker);
                var blockers = c.GetFirstWaveBlockers();
                CollectionAssert.Contains(blockers,
                    "원본 검증된 피해 계산기 및 전투 월드 연결");
                StringAssert.Contains("첫 라운드 차단 요인",
                    c.ExportFirstWaveReadiness());
                Assert.AreEqual(blockers.Count, blockers.Distinct().Count());
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }
    }
}
