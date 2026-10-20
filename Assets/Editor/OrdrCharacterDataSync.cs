#if UNITY_EDITOR
using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Ordr.Mobile.Editor
{
    // Unity 프로젝트 루트의 /data/*.json을 Resources TextAsset으로 복사한다.
    // 원본 JSON의 중복 편집을 피하려면 반드시 data/ 파일을 수정하고 이 메뉴로 동기화한다.
    public static class OrdrCharacterDataSync
    {
        [MenuItem("ORDR/캐릭터 데이터 271종 · 스킬 200종 동기화")]
        public static void Sync()
        {
            string projectRoot = Directory.GetParent(Application.dataPath).FullName;
            string source = Path.Combine(projectRoot, "data");
            string target = Path.Combine(Application.dataPath, "Resources", "OrdrData");
            if (!File.Exists(Path.Combine(source, "units.json")) ||
                !File.Exists(Path.Combine(source, "character-skill-profiles.json")))
            {
                throw new FileNotFoundException(
                    "프로젝트 루트 /data 폴더에서 카탈로그 JSON을 찾지 못했습니다.");
            }
            Directory.CreateDirectory(target);
            File.Copy(Path.Combine(source, "units.json"),
                Path.Combine(target, "units.json"), true);
            File.Copy(Path.Combine(source, "character-skill-profiles.json"),
                Path.Combine(target, "character-skill-profiles.json"), true);
            // 패치 근거 수치는 없어도 월드 소환을 중단시키지 않는다.
            string evidence = Path.Combine(source, "original-stat-evidence.json");
            if (File.Exists(evidence))
                File.Copy(evidence, Path.Combine(target, "original-stat-evidence.json"), true);
            string fusion = Path.Combine(source, "verified-mobile-fusion-recipes.json");
            if (File.Exists(fusion))
                File.Copy(fusion, Path.Combine(target, "verified-mobile-fusion-recipes.json"), true);
            AssetDatabase.Refresh();
            Debug.Log("ORDR 캐릭터 데이터가 Assets/Resources/OrdrData 로 동기화되었습니다.");
        }
    }
}
#endif
