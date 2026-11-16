using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Networking;

namespace Ordr.Mobile.Unity
{
    // Local private portraits take precedence. Public APKs request only images
    // needed by the active UI and cache them on the device, not in public Git.
    public static class OrdrCharacterPortraits
    {
        private const string ResourcesPrefix = "CharacterPortraits/";
        private const string PortraitUrlPrefix = "https://ordr.kr/images/";
        private const string CacheFolder = "CharacterPortraitCache";
        private const int MaxParallelRequests = 3;
        private const int MaxPngBytes = 4 * 1024 * 1024;
        private static readonly Dictionary<string, Sprite> Cache =
            new Dictionary<string, Sprite>(StringComparer.Ordinal);
        private static readonly HashSet<string> LocalChecked =
            new HashSet<string>(StringComparer.Ordinal);
        private static readonly HashSet<string> Scheduled =
            new HashSet<string>(StringComparer.Ordinal);
        private static readonly HashSet<string> Unavailable =
            new HashSet<string>(StringComparer.Ordinal);
        private static readonly Queue<string> Pending = new Queue<string>();
        private static int activeRequests;

        public static Sprite Find(string catalogUnitId)
        {
            if (!IsSafeCatalogId(catalogUnitId)) return null;
            Sprite portrait;
            if (Cache.TryGetValue(catalogUnitId, out portrait)) return portrait;

            if (LocalChecked.Add(catalogUnitId))
            {
                portrait = Resources.Load<Sprite>(ResourcesPrefix + catalogUnitId);
                if (portrait == null) portrait = LoadCachedFile(catalogUnitId);
                // The user chose to roll back every imported shared-sheet
                // character asset. Only true per-unit local/cached photos
                // satisfy the portrait request; an unverified atlas cell must
                // never short-circuit the existing original-photo downloader.
                // Until a real portrait arrives, callers use the authored
                // neutral square marker rather than the shared-sheet art.
                if (portrait != null)
                {
                    Cache[catalogUnitId] = portrait;
                    return portrait;
                }
            }

            // Never fetch during imports or EditMode tests.
            if (Application.isPlaying && !Unavailable.Contains(catalogUnitId) &&
                Scheduled.Add(catalogUnitId))
            {
                Pending.Enqueue(catalogUnitId);
                PumpDownloads();
            }
            return null;
        }

        private static Sprite LoadCachedFile(string id)
        {
            try
            {
                string file = CachePath(id);
                if (File.Exists(file))
                    return DecodeSprite(File.ReadAllBytes(file), id);
            }
            catch (Exception exception)
            {
                Debug.LogWarning("ORDR portrait cache unavailable: " + exception.Message);
            }
            return null;
        }

        private static void PumpDownloads()
        {
            while (activeRequests < MaxParallelRequests && Pending.Count > 0)
            {
                string id = Pending.Dequeue();
                string filename = id.Replace("-", "");
                // Wiki filenames: C-01 -> C1.png, TR-24 -> TR24.png.
                int firstDigit = 0;
                while (firstDigit < filename.Length &&
                       !char.IsDigit(filename[firstDigit])) firstDigit++;
                string imageName = filename.Substring(0, firstDigit) +
                    int.Parse(filename.Substring(firstDigit)).ToString();

                UnityWebRequest request = UnityWebRequest.Get(
                    PortraitUrlPrefix + imageName + ".png");
                request.timeout = 12;
                activeRequests++;
                UnityWebRequestAsyncOperation operation = request.SendWebRequest();
                operation.completed += _ => FinishDownload(id, request);
            }
        }

        private static void FinishDownload(string id, UnityWebRequest request)
        {
            try
            {
                if (request.result == UnityWebRequest.Result.Success &&
                    request.downloadHandler != null)
                {
                    byte[] bytes = request.downloadHandler.data;
                    Sprite portrait = DecodeSprite(bytes, id);
                    if (portrait != null)
                    {
                        Cache[id] = portrait;
                        try
                        {
                            string file = CachePath(id);
                            Directory.CreateDirectory(Path.GetDirectoryName(file));
                            File.WriteAllBytes(file, bytes);
                        }
                        catch (Exception exception)
                        {
                            Debug.LogWarning("ORDR portrait cache write unavailable: " +
                                exception.Message);
                        }
                        return;
                    }
                }
                // Missing art or a connection error leaves the square marker.
                Unavailable.Add(id);
            }
            catch (Exception exception)
            {
                Unavailable.Add(id);
                Debug.LogWarning("ORDR portrait load failed for " + id + ": " +
                    exception.Message);
            }
            finally
            {
                request.Dispose();
                activeRequests--;
                PumpDownloads();
            }
        }

        private static Sprite DecodeSprite(byte[] png, string id)
        {
            if (!IsValidPngHeader(png)) return null;
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (!ImageConversion.LoadImage(texture, png, true))
            {
                UnityEngine.Object.Destroy(texture);
                return null;
            }
            texture.filterMode = FilterMode.Bilinear;
            texture.name = "ORDR Portrait " + id;
            return Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height),
                new Vector2(0.5f, 0.5f), 100f);
        }

        private static bool IsValidPngHeader(byte[] bytes)
        {
            if (bytes == null || bytes.Length < 33 || bytes.Length > MaxPngBytes ||
                bytes[0] != 137 || bytes[1] != 80 || bytes[2] != 78 || bytes[3] != 71 ||
                bytes[4] != 13 || bytes[5] != 10 || bytes[6] != 26 || bytes[7] != 10 ||
                bytes[8] != 0 || bytes[9] != 0 || bytes[10] != 0 || bytes[11] != 13 ||
                bytes[12] != 73 || bytes[13] != 72 || bytes[14] != 68 || bytes[15] != 82)
                return false;
            int width = (bytes[16] << 24) | (bytes[17] << 16) |
                (bytes[18] << 8) | bytes[19];
            int height = (bytes[20] << 24) | (bytes[21] << 16) |
                (bytes[22] << 8) | bytes[23];
            return width >= 1 && width <= 2048 && height >= 1 && height <= 2048;
        }

        private static string CachePath(string id)
        {
            return Path.Combine(Application.persistentDataPath, CacheFolder, id + ".png");
        }

        private static bool IsSafeCatalogId(string id)
        {
            if (string.IsNullOrEmpty(id) || id.Length < 4 || id.Length > 6) return false;
            int separator = id.IndexOf('-');
            if (separator < 1 || separator > 3 || separator != id.Length - 3) return false;
            for (int i = 0; i < separator; i++)
                if (id[i] < 'A' || id[i] > 'Z') return false;
            return id[separator + 1] >= '0' && id[separator + 1] <= '9' &&
                id[separator + 2] >= '0' && id[separator + 2] <= '9';
        }
    }
}
