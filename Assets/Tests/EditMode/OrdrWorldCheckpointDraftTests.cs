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
                Assert.IsFalse(owner.IsGameplayPaused);
                var unpaused = owner.CaptureCoreCheckpointDraft();
                Assert.IsFalse(owner.IsGameplayPaused,
                    "정상 캡처 후에도 사용자의 일시정지 상태를 바꾸지 않습니다.");
                owner.SetManualGameplayPause(true);
                var captured = owner.CaptureWorldCheckpointDraft();
                Assert.IsTrue(owner.IsGameplayPaused);
                Assert.AreEqual(unpaused.LogicalTime, captured.LogicalTime);
                Assert.AreEqual(owner.Combat.Time, captured.LogicalTime);
                Assert.AreEqual(owner.Combat.CharacterCount, captured.Combat.Actors.Count);
                Assert.AreEqual(owner.Combat.EnemyCount, captured.Combat.Enemies.Count);
                Assert.AreEqual(0, captured.Waves.ActiveWave);
                Assert.IsNull(captured.Practice);
                var store = new OrdrCoreCheckpointStore(folder);
                store.WriteCore(captured);
                // Explicitly version the binary payload, not only its
                // checksummed A/B slot envelope.
                var slot = Path.Combine(folder, "ordr-core-draft-v1-0.json");
                var envelope = File.ReadAllText(slot);
                Assert.IsTrue(envelope.StartsWith("ORDR-CORE-BIN-v3\n",
                    StringComparison.Ordinal));
                var encoded = envelope.Substring(envelope.LastIndexOf('\n') + 1);
                Assert.AreEqual(12,
                    BitConverter.ToInt32(Convert.FromBase64String(encoded), 0));
                var loaded = store.LoadCore();
                Assert.AreEqual(captured.SessionId, loaded.SessionId);
                Assert.AreEqual(captured.Combat.Actors.Count,
                    loaded.Combat.Actors.Count);
                // Combat v4 must reach the unpublished reconstruction
                // pipeline rather than fail the stale v2 version gate.
                var prepared = OrdrCoreCheckpoint.PrepareWorldRestore(loaded);
                Assert.AreEqual(loaded.SessionId,
                    prepared.Missions.SessionId);
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
