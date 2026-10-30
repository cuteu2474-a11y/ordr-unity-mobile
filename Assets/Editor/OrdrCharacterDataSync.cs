#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Ordr.Mobile.Editor
{
    // Imports existing /data JSON into Unity Resources. No fake units,
    // combat values, random events or costs are created during import.
    [InitializeOnLoad]
    public static class OrdrCharacterDataSync
    {
        private static readonly string[] DataFiles =
        {
            "mobile-practice-battle-model.json",
            "mobile-standalone-enemy-model.json",
            "practice-native-inherited-defaults.json",
            "units.json",
            "character-skill-profiles.json",
            "original-stat-evidence.json",
            "original-2323C-map-base-stats.json",
            "original-2323C-native-pure-passives.json",
            "original-2323C-all-unit-aura-audit.json",
            "original-2323C-monster-wave-script-evidence.json",
            "original-2323C-monster-object-abilities.json",
            "original-2323C-nightmare-a04q-event-evidence.json",
            "original-2323C-missing-skill-profile-evidence.json",
            "verified-mobile-fusion-recipes.json",
            "otherworld-2323C-mystic-web-recipes.json",
            "original-jass-fusion-registry.json",
            // Source-object reconciliation for read-only JASS fusion browsing.
            // This is not an approved map for unit consumption.
            "original-fusion-object-reconciliation.json",
            "original-2323C-helper-alchemy-recipes.json",
            "original-unit-rawcode-label-links.json",
            "original-jass-mystic-candidate-reconciliation.json",
            "original-2323C-wisp-pools.json"
        };

        // The scene's Awake() requires these actual imported TextAssets.
        // Neither the first-play button nor a build may invent missing data.
        // Every listed file ships with this repository and participates in
        // gameplay/catalog setup. Requiring them prevents a successful APK with partially
        // disabled menus or unit effects when an input is missing.
        private static readonly string[] RequiredRuntimeData = DataFiles;

        internal static string[] RequiredRuntimeAssetPaths()
        {
            return RequiredRuntimeData.Select(file => "Assets/Resources/OrdrData/" + file).ToArray();
        }

        static OrdrCharacterDataSync()
        {
            EditorApplication.delayCall += SyncOnProjectOpen;
        }

        // Build-preflight uses the SAME importer as EditMode and the manual
        // menu; do not add a second copy of /data or bake guessed defaults.
        internal static void EnsureRequiredRuntimeResources()
        {
            CopySourceData(logIfUnchanged: false);
            var missing = RequiredRuntimeData.Where(file =>
                Resources.Load<TextAsset>(
                    "OrdrData/" + Path.GetFileNameWithoutExtension(file)) == null)
                .ToArray();
            if (missing.Length > 0)
                throw new BuildFailedException(
                    "ORDR 빌드 중단: Unity Resources에 필수 원본 JSON이 없습니다: " +
                    string.Join(", ", missing) +
                    " / 전체 GitHub 저장소의 data 폴더와 Editor 동기화를 확인하세요.");
        }

        private static void SyncOnProjectOpen()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                EditorApplication.delayCall += SyncOnProjectOpen;
                return;
            }

            try
            {
                CopySourceData(logIfUnchanged: false);
            }
            catch (Exception ex)
            {
                Debug.LogError("ORDR 첫 프로젝트 데이터 연결 실패: " + ex.Message +
                    " / 상단 ORDR 데이터 동기화 메뉴를 확인하세요.");
            }
        }

        [MenuItem("ORDR/캐릭터 데이터 273종 · 스킬 200종 동기화")]
        public static void Sync()
        {
            CopySourceData(logIfUnchanged: true);
        }

        private static void CopySourceData(bool logIfUnchanged)
        {
            string root = Directory.GetParent(Application.dataPath).FullName;
            string source = Path.Combine(root, "data");
            string target = Path.Combine(Application.dataPath, "Resources", "OrdrData");
            if (!File.Exists(Path.Combine(source, "units.json")) ||
                !File.Exists(Path.Combine(source, "character-skill-profiles.json")))
                throw new FileNotFoundException(
                    "GitHub 전체 저장소의 /data 폴더가 없습니다. Assets 폴더만 열지 마세요.");

            Directory.CreateDirectory(target);
            int changed = 0;
            foreach (string file in DataFiles)
            {
                string from = Path.Combine(source, file);
                if (!File.Exists(from)) continue;
                string to = Path.Combine(target, file);
                if (File.Exists(to) &&
                    new FileInfo(from).Length == new FileInfo(to).Length &&
                    File.ReadAllBytes(from).SequenceEqual(File.ReadAllBytes(to)))
                    continue;

                File.Copy(from, to, overwrite: true);
                changed++;
            }

            if (changed > 0)
                // A build may follow immediately. In that case the newly
                // copied JSON must be fully imported before scene packaging.
                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);

            if (changed > 0 || logIfUnchanged)
                Debug.Log("ORDR 원본 도감 데이터 " + changed +
                    "개 파일 동기화. 게임 기능·공식의 검증 또는 실행 결과는 아닙니다.");
        }
    }

    // Shared by APK production and other Unity builds. Protects each build
    // from silently shipping an empty menu due to untracked Resources caches.
    public sealed class OrdrRuntimeDataBuildPreflight : IPreprocessBuildWithReport
    {
        public int callbackOrder { get { return -10000; } }

        public void OnPreprocessBuild(BuildReport report)
        {
            OrdrCharacterDataSync.EnsureRequiredRuntimeResources();
        }
    }
}
#endif
