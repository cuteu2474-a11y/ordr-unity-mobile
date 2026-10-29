#if UNITY_EDITOR
using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Ordr.Mobile.Editor
{
    // Used by the existing APK job and the local menu. Never enables a
    // TEST_ONLY world or changes the game's source-model approval boundary.
    public static class OrdrAndroidDevelopmentBuild
    {
        public const string ApkPath = "build/Android/ORDR-Mobile-Development.apk";
        public const string ApplicationId = "com.ordr.mobile.development";

        [MenuItem("ORDR/Android 개발용 APK 제작")]
        public static void Build()
        {
            if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.Android)
                throw new BuildFailedException(
                    "Android 플랫폼으로 전환한 뒤 APK를 제작하세요. CLI에는 -buildTarget Android가 필요합니다.");

            const string scene = "Assets/Scenes/MainGame.unity";
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(scene) == null)
                throw new BuildFailedException("ORDR 기본 게임 씬이 없습니다: " + scene);
            OrdrCharacterDataSync.EnsureRequiredRuntimeResources();

            string version = Argument("-ordrBuildVersion") ?? "0.1.0";
            string codeText = Argument("-ordrVersionCode") ?? "1";
            int versionCode;
            if (!int.TryParse(codeText, NumberStyles.None, CultureInfo.InvariantCulture,
                    out versionCode) || versionCode < 1 || versionCode > 2100000000)
                throw new BuildFailedException("잘못된 Android 버전 코드: " + codeText);

            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android, ApplicationId);
            PlayerSettings.bundleVersion = version;
            PlayerSettings.Android.bundleVersionCode = versionCode;
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64 | AndroidArchitecture.ARMv7;
            PlayerSettings.Android.buildApkPerCpuArchitecture = false;
            PlayerSettings.Android.splitApplicationBinary = false;
            PlayerSettings.Android.useCustomKeystore = false;
            EditorUserBuildSettings.buildAppBundle = false;
            EditorUserBuildSettings.exportAsGoogleAndroidProject = false;
            PlayerSettings.defaultInterfaceOrientation = UIOrientation.AutoRotation;
            PlayerSettings.allowedAutorotateToPortrait = false;
            PlayerSettings.allowedAutorotateToPortraitUpsideDown = false;
            PlayerSettings.allowedAutorotateToLandscapeLeft = true;
            PlayerSettings.allowedAutorotateToLandscapeRight = true;

            Directory.CreateDirectory(Path.GetDirectoryName(ApkPath));
            // A failed local rebuild must not leave yesterday's APK looking new.
            if (File.Exists(ApkPath)) File.Delete(ApkPath);
            string manifestPath = Path.ChangeExtension(ApkPath, ".build.json");
            if (File.Exists(manifestPath)) File.Delete(manifestPath);
            BuildReport report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { scene },
                locationPathName = ApkPath,
                target = BuildTarget.Android,
                targetGroup = BuildTargetGroup.Android,
                options = BuildOptions.Development | BuildOptions.DetailedBuildReport
            });
            if (report == null || report.summary.result != BuildResult.Succeeded ||
                !File.Exists(ApkPath) || new FileInfo(ApkPath).Length == 0)
                throw new BuildFailedException("ORDR Android APK 제작 실패. Unity 빌드 로그를 확인하세요.");
            WriteRuntimeDataBuildManifest(report, manifestPath);
            Debug.Log("ORDR 개발용 APK 생성: " + ApkPath + " / " + version + " (" + versionCode + ")");
        }

        // Output filenames can change between Unity versions. Ask the actual
        // BuildReport which source assets were packed, then bind that evidence
        // to this exact APK instead of assuming a resources.assets ZIP entry.
        private static void WriteRuntimeDataBuildManifest(BuildReport report, string path)
        {
            var packed = report.packedAssets.SelectMany(file => file.contents).ToArray();
            string[] required = OrdrCharacterDataSync.RequiredRuntimeAssetPaths();
            string[] missing = required.Where(source => !packed.Any(asset =>
                asset.sourceAssetPath == source && asset.packedSize > 0)).ToArray();
            if (missing.Length != 0)
                throw new BuildFailedException("APK 필수 데이터 패킹 누락: " + string.Join(", ", missing));

            string hash;
            using (var sha = SHA256.Create())
            using (var stream = File.OpenRead(ApkPath))
                hash = BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
            File.WriteAllText(path, JsonUtility.ToJson(new RuntimeDataBuildManifest
            {
                formatVersion = 1,
                apkSha256 = hash,
                runtimeAssets = required
            }, true));
        }

        [Serializable]
        private sealed class RuntimeDataBuildManifest
        {
            public int formatVersion;
            public string apkSha256;
            public string[] runtimeAssets;
        }

        private static string Argument(string key)
        {
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length; i++)
                if (args[i] == key)
                {
                    if (i + 1 >= args.Length || args[i + 1].StartsWith("-", StringComparison.Ordinal))
                        throw new BuildFailedException("빌드 인수 값 누락: " + key);
                    return args[i + 1];
                }
            return null;
        }
    }
}
#endif
