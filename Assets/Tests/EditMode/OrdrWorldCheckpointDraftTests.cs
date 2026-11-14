using System;
using System.IO;
using System.Reflection;
using NUnit.Framework;
using Ordr.Mobile.Core;
using Ordr.Mobile.Unity;
using UnityEngine;

namespace Ordr.Mobile.Tests
{
    public sealed class OrdrWorldCheckpointDraftTests
    {
        [Test]
        public void UnitPreparationCapturesSameFrameWorldAndRejectsUnsafeResume()
        {
            var root = new GameObject("TEST_ONLY_WORLD_SAVE");
            root.SetActive(false);
            string folder = Path.Combine(Path.GetTempPath(),
                "ordr-world-draft-" + Guid.NewGuid().ToString("N"));
            try
            {
                var owner = root.AddComponent<OrdrCharacterCombatController>();
                typeof(OrdrCharacterCombatController).GetMethod("Awake",
                    BindingFlags.NonPublic | BindingFlags.Instance)
                    .Invoke(owner, null);
                owner.BeginUnitPreparation();
                owner.SetManualGameplayPause(true);
                var captured = owner.CaptureWorldCheckpointDraft();
                Assert.AreEqual(owner.Combat.Time, captured.LogicalTime);
                Assert.AreEqual(owner.Combat.CharacterCount, captured.Combat.Actors.Count);
                Assert.AreEqual(owner.Combat.EnemyCount, captured.Combat.Enemies.Count);
                Assert.AreEqual(0, captured.Waves.ActiveWave);
                Assert.IsNull(captured.Practice);
                var store = new OrdrCoreCheckpointStore(folder);
                store.WriteCore(captured);
                var loaded = store.LoadCore();
                Assert.AreEqual(captured.SessionId, loaded.SessionId);
                Assert.AreEqual(captured.Combat.Actors.Count,
                    loaded.Combat.Actors.Count);
                Assert.Throws<NotSupportedException>(() =>
                    OrdrCoreCheckpoint.Restore(loaded),
                    "The complete live battle is NOT yet safe to resume.");
                Assert.IsTrue(owner.IsGameplayPaused);
                Assert.AreEqual(captured.LogicalTime, owner.Combat.Time);

                if (loaded.Combat.Actors.Count > 0)
                {
                    loaded.Combat.Actors[0].CatalogId = "tampered";
                    Assert.Throws<FormatException>(() =>
                        OrdrCoreCheckpoint.Validate(loaded));
                    Assert.Throws<FormatException>(() => store.WriteCore(loaded));
                    Assert.AreNotEqual("tampered",
                        store.LoadCore().Combat.Actors[0].CatalogId);
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
                if (Directory.Exists(folder)) Directory.Delete(folder, true);
            }
        }
    }
}
