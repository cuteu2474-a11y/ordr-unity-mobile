#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using Ordr.Mobile.Combat;
using Ordr.Mobile.Core;
using Ordr.Mobile.Data;
using Ordr.Mobile.Unity;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Ordr.Mobile.Editor
{
    // Explicit, user-launched, Editor-only practice using the ALREADY EXISTING
    // controller, wave scheduler, Wisp transactions, damage rules and wallet.
    // This assembly is NEVER part of Android/iOS builds, and no resource,
    // ScriptableObject, scene or static verified-wave JSON is modified.
    [InitializeOnLoad]
    public static class OrdrFirstWavePreview
    {
        private const string Armed = "ordr323.editor.firstwave.preview.armed";
        private const string ScenePath = "Assets/Scenes/MainGame.unity";

        static OrdrFirstWavePreview()
        {
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
        }

        [MenuItem("ORDR/첫 라운드 직접 체험 (Unity Editor 전용)")]
        public static void Launch()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                EditorUtility.DisplayDialog("ORDR", "먼저 Play 모드를 종료해 주세요.", "확인");
                return;
            }
            if (!EditorUtility.DisplayDialog(
                "원랜디 첫 라운드 개발용 체험",
                "정식 악몽·이세계 원본 플레이가 아닙니다.\\n\\n" +
                "몬스터 HP/방어/좌표, 첫 타격 시점, 최종 HP 정수화, " +
                "랜덤위습 초기 추첨과 골드 난수는 이 체험에서만 사용하는 가상 입력입니다.\\n\\n" +
                "원본 35회 출현 일정과 기존 Unity 전투/위습/경제 UI를 사용하며 " +
                "다른 라운드·상위 유닛은 아직 체험할 수 없습니다.\\n\\n" +
                "Android APK를 만들거나 게임 데이터를 수정하지 않습니다.",
                "가상 첫 라운드 체험", "취소"))
                return;
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                return;
            try
            {
                OrdrCharacterDataSync.EnsureRequiredRuntimeResources();
                EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
                SessionState.SetBool(Armed, true);
                EditorApplication.isPlaying = true;
            }
            catch (Exception ex)
            {
                SessionState.SetBool(Armed, false);
                Debug.LogException(ex);
                EditorUtility.DisplayDialog("ORDR 체험 시작 실패",
                    ex.Message, "확인");
            }
        }

        private static void OnPlayModeChanged(PlayModeStateChange change)
        {
            if (change == PlayModeStateChange.ExitingPlayMode)
                SessionState.SetBool(Armed, false);
            if (change != PlayModeStateChange.EnteredPlayMode ||
                !SessionState.GetBool(Armed, false)) return;
            SessionState.SetBool(Armed, false);
            EditorApplication.delayCall += AttachPreviewToActualScene;
        }

        private static void AttachPreviewToActualScene()
        {
            if (!EditorApplication.isPlaying) return;
            try
            {
                var controller = UnityEngine.Object
                    .FindFirstObjectByType<OrdrCharacterCombatController>();
                if (controller == null || controller.Units == null ||
                    controller.MapBaseStats == null ||
                    controller.OriginalMonsterSpawnIdentities == null ||
                    controller.OriginalMonsterObjectAbilities == null)
                    throw new InvalidOperationException(
                        "MainGame의 실제 컨트롤러와 원본 도감 로딩이 필요합니다.");
                if (controller.Combat != null)
                    throw new InvalidOperationException(
                        "이미 전투가 시작된 기본 씬에는 체험 입력을 덮어쓸 수 없습니다.");

                controller.ConfigureRandomWispResourceRng(new EditorWispRandom());
                controller.ConfigureVerifiedLiveMonsterWaves(
                    new EditorPreviewWaveApproval());
                controller.ConfigureOriginalMonsterGoldEvents(
                    new EditorPreviewGoldDeath());
                controller.ConfigureOriginalCommonNativeDamageWhenVerified(
                    new EditorPreviewAttack(), null, new EditorPreviewQuantizer(),
                    new EditorPreviewDice());
                controller.RegisterVerifiedLiveMonsterWavePlan(
                    BuildEditorOnlyFirstWave(controller.OriginalMonsterSpawnIdentities));
                controller.MarkEditorPreview();

                string blocked;
                if (!controller.CanBeginVerifiedFirstWave(out blocked))
                    throw new InvalidOperationException(
                        "체험 시작 조건이 아직 미완료입니다: " + blocked);

                Debug.LogWarning(
                    "ORDR UNITY EDITOR 체험 연결 완료: 가상 HP/방어/난수. " +
                    "게임 화면의 '체험 첫 라운드 시작'을 누른 후 왼쪽 위습 메뉴에서 " +
                    "'흔함 소환'과 '소환 확정'을 누르세요. " +
                    "원본 모드의 실전 검증으로 간주하지 않습니다.");
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
                EditorUtility.DisplayDialog(
                    "ORDR 개발용 첫 라운드 구성 실패",
                    ex.Message + "\\n\\n기본 씬의 정식 시작 차단은 유지됩니다.",
                    "확인");
            }
        }

        private static VerifiedWavePlan BuildEditorOnlyFirstWave(
            Original323MonsterWaveIdentityCatalog source)
        {
            var schedule = source.BuildSinglePlayerSpawnTemplates(1);
            if (schedule.Count != 35)
                throw new InvalidOperationException(
                    "원본 첫 라운드의 몬스터 출현표가 35마리가 아닙니다.");
            var spawns = new List<VerifiedWaveEnemy>();
            for (int i = 0; i < schedule.Count; i++)
            {
                var sourceSpawn = schedule[i];
                // Not source-confirmed live stats or spawn coordinates.
                // Keep the dots near the first common Wisp's native map spot
                // so the existing, non-following camera can show the battle.
                double x = -4710.0 + (i % 7) * 64.0;
                double y = 3232.0 + (i / 7) * 76.0;
                spawns.Add(new VerifiedWaveEnemy(
                    sourceSpawn.AtSeconds, sourceSpawn.InstanceId,
                    55L, sourceSpawn.Kind, x, y, 0,
                    new EnemyDefenseProfile(sourceSpawn.Kind,
                        0, null, "normal",
                        "EDITOR_PREVIEW_NON_RETAIL: invented armor/position"),
                    sourceSpawn.Rawcode));
            }
            return new VerifiedWavePlan(1, spawns,
                "EDITOR_PREVIEW_NON_RETAIL: actual JASS identities/timing; fake final stats");
        }

        private sealed class EditorPreviewWaveApproval :
            IOriginal323LiveMonsterWaveEvidence
        {
            public bool TryApprove(VerifiedWavePlan plan, out string evidence)
            {
                evidence = "EDITOR_PREVIEW_NON_RETAIL: explicitly launched sandbox";
                return EditorApplication.isPlaying &&
                    plan != null && plan.WaveNumber == 1;
            }
        }

        private sealed class EditorPreviewDice : ICombatProcRandom
        {
            private readonly System.Random random = new System.Random(2323);
            public int Next(int upperExclusive)
            {
                return random.Next(upperExclusive);
            }
        }

        private sealed class EditorPreviewQuantizer :
            IOrdr323VerifiedDamageQuantization
        {
            public long Quantize(double damage, Ordr323VerifiedDamageHit hit)
            {
                // Temporary Editor preview ONLY; retail Warcraft rounding
                // has not been verified, and must not leak into players.
                return (long)Math.Truncate(damage);
            }
        }

        private sealed class EditorPreviewAttack :
            IOriginalCommonNativeAttackVerification
        {
            public bool TryVerify(string id, string rawcode, out double delay,
                out DamageSchool school, out string evidence)
            {
                delay = 0.35; // Editor-only illustrative first-swing offset.
                school = DamageSchool.Physical;
                evidence = "EDITOR_PREVIEW_NON_RETAIL: common unit native dice preview";
                // Only source-mapped Common units. All other grades retain
                // their existing unapproved-attack protections.
                int number;
                return id != null && id.StartsWith("C-", StringComparison.Ordinal) &&
                    int.TryParse(id.Substring(2), out number) &&
                    number >= 1 && number <= 9 &&
                    !string.IsNullOrWhiteSpace(rawcode) && rawcode.Length == 4;
            }
        }

        private sealed class EditorWispRandom : IOriginalWispRandom
        {
            private readonly System.Random random = new System.Random(2323);
            private int draws;
            public int NextInclusive(int minimum, int maximum)
            {
                // The FIRST draw is deliberately C-01 so a new player can
                // exercise actual CombatWaveDirector and basic attack. This
                // is a TUTORIAL shortcut, not original BFh seed equivalence.
                int current = draws++;
                if (current == 0 && minimum == 1 && maximum == 10000)
                    return 10000;
                if (current == 1 && minimum == 0 && maximum == 8)
                    return 0;
                if (maximum < minimum || maximum == int.MaxValue)
                    throw new ArgumentOutOfRangeException(nameof(maximum));
                return random.Next(minimum, maximum + 1);
            }
        }

        private sealed class EditorPreviewGoldDeath :
            IOriginal323MonsterGoldDeathWitness
        {
            public bool TryConfirmOriginalGoldDeath(int spawnWave, string id,
                string rawcode, out int player, out int deathRound, out int roll,
                out float bonus, out int observedGold, out string evidence)
            {
                player = 0;
                deathRound = 1;
                roll = 3;
                bonus = 0;
                observedGold = 2;
                evidence = "EDITOR_PREVIEW_NON_RETAIL: every kill rewarded in sandbox";
                return EditorApplication.isPlaying && spawnWave == 1 &&
                    rawcode == "o00I" && !string.IsNullOrWhiteSpace(id);
            }
        }
    }
}
#endif
