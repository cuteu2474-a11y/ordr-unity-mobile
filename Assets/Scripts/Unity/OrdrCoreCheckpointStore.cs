using System;
using System.IO;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Ordr.Mobile.Core;
using UnityEngine;

namespace Ordr.Mobile.Unity
{
    // S02 durable storage for the CORE DRAFT, NOT the resumable battle.
    // A/B slots are independently checksummed. Writing the older/missing
    // slot never removes the last valid checkpoint even on a power loss.
    public sealed class OrdrCoreCheckpointStore
    {
        [Serializable]
        private sealed class SlotEnvelope
        {
            public int Version = 1;
            public long Generation;
            public string Payload;
            public string Sha256;
        }

        private sealed class ValidSlot
        {
            public int Index;
            public long Generation;
            public OrdrCoreCheckpointState State;
        }

        // v2 stores the signed payload as raw JSON, not JSON embedded inside
        // another JSON string. ReadSlot still accepts legacy v1 envelopes.
        private const string RawSlotHeader = "ORDR-CORE-RAW-v2\n";
        private readonly string folder;
        public OrdrCoreCheckpointStore(string directory)
        {
            if (string.IsNullOrWhiteSpace(directory))
                throw new ArgumentException("저장 디렉터리가 필요합니다.", nameof(directory));
            folder = Path.GetFullPath(directory);
        }

        public static OrdrCoreCheckpointStore ForApplication()
        {
            return new OrdrCoreCheckpointStore(Application.persistentDataPath);
        }

        public bool HasCoreDraft()
        {
            return ReadSlot(0) != null || ReadSlot(1) != null;
        }

        public OrdrCoreCheckpointState LoadCore()
        {
            var a = ReadSlot(0);
            var b = ReadSlot(1);
            var newest = Newest(a, b);
            if (newest != null) return newest.State;
            if (File.Exists(SlotPath(0)) || File.Exists(SlotPath(1)))
                throw new FormatException(
                    "저장본 두 개 모두 손상되었거나 지원되지 않는 버전입니다.");
            throw new FileNotFoundException("이전에 저장된 핵심 상태가 없습니다.");
        }

        public void WriteCore(OrdrCoreCheckpointState state)
        {
            // Validate the whole payload before touching any previous file.
            OrdrCoreCheckpoint.Validate(state);
            var a = ReadSlot(0);
            var b = ReadSlot(1);
            var latest = Newest(a, b);
            if (latest == null &&
                (File.Exists(SlotPath(0)) || File.Exists(SlotPath(1))))
                throw new FormatException(
                    "정상 저장본이 없어 손상 파일을 덮어쓰지 않습니다.");
            if (latest != null && latest.Generation == long.MaxValue)
                throw new OverflowException("저장 세대 번호가 초과됐습니다.");

            int target = latest == null ? 0 : 1 - latest.Index;
            long generation = latest == null ? 1 : latest.Generation + 1;
            string payload = JsonUtility.ToJson(state);
            // Escaping a complete checkpoint JSON as the string field of
            // another JsonUtility object is lossy on some Unity runtimes.
            // An unambiguous 3-line prefix preserves exact payload bytes.
            string content = RawSlotHeader +
                generation.ToString(CultureInfo.InvariantCulture) + "\n" +
                Digest(generation, payload) + "\n" + payload;
            Directory.CreateDirectory(folder);
            string temp = Path.Combine(folder,
                "ordr-core-draft-" + Guid.NewGuid().ToString("N") + ".tmp");
            try
            {
                using (var file = new FileStream(temp, FileMode.CreateNew,
                    FileAccess.Write, FileShare.None))
                {
                    byte[] bytes = new UTF8Encoding(false).GetBytes(content);
                    file.Write(bytes, 0, bytes.Length);
                    file.Flush(true);
                }
                // The target is the older/missing slot. The latest known-good
                // slot is never touched until a complete newer slot exists.
                string targetPath = SlotPath(target);
                if (File.Exists(targetPath)) File.Delete(targetPath);
                File.Move(temp, targetPath);
            }
            finally
            {
                if (File.Exists(temp)) File.Delete(temp);
            }
        }

        private string SlotPath(int i)
        {
            return Path.Combine(folder, "ordr-core-draft-v1-" + i + ".json");
        }

        private ValidSlot ReadSlot(int i)
        {
            string path = SlotPath(i);
            if (!File.Exists(path)) return null;
            try
            {
                string content = File.ReadAllText(path, Encoding.UTF8);
                long generation;
                string payload, checksum;
                if (content.StartsWith(RawSlotHeader, StringComparison.Ordinal))
                {
                    int second = content.IndexOf('\n', RawSlotHeader.Length);
                    int third = second < 0 ? -1 : content.IndexOf('\n', second + 1);
                    if (second < 0 || third < 0 ||
                        !long.TryParse(content.Substring(RawSlotHeader.Length,
                            second - RawSlotHeader.Length),
                            NumberStyles.None, CultureInfo.InvariantCulture,
                            out generation)) return null;
                    checksum = content.Substring(second + 1, third - second - 1);
                    payload = content.Substring(third + 1);
                }
                else
                {
                    // Backward compatible with the original JSON envelope.
                    var legacy = JsonUtility.FromJson<SlotEnvelope>(content);
                    if (legacy == null || legacy.Version != 1) return null;
                    generation = legacy.Generation;
                    payload = legacy.Payload;
                    checksum = legacy.Sha256;
                }
                if (generation < 1 || string.IsNullOrWhiteSpace(payload) ||
                    !string.Equals(checksum, Digest(generation, payload),
                        StringComparison.OrdinalIgnoreCase))
                    return null;
                var state = JsonUtility.FromJson<OrdrCoreCheckpointState>(payload);
                OrdrCoreCheckpoint.Validate(state);
                return new ValidSlot {
                    Index = i, Generation = generation, State = state
                };
            }
            catch (Exception e) when (
                e is ArgumentException || e is FormatException ||
                e is IOException || e is UnauthorizedAccessException ||
                e is OverflowException)
            {
                return null;
            }
        }

        private static ValidSlot Newest(ValidSlot a, ValidSlot b)
        {
            if (a == null) return b;
            if (b == null) return a;
            // An equal generation is a torn/duplicated transaction, not a
            // reason to pick an arbitrary version.
            if (a.Generation == b.Generation)
                throw new FormatException("중복된 저장 세대 번호가 발견됐습니다.");
            return a.Generation > b.Generation ? a : b;
        }

        private static string Digest(long generation, string payload)
        {
            byte[] input = Encoding.UTF8.GetBytes(
                "ORDR-CORE-v1\n" + generation + "\n" + payload);
            using (var hash = SHA256.Create())
            {
                var bytes = hash.ComputeHash(input);
                var result = new StringBuilder(bytes.Length * 2);
                foreach (var b in bytes) result.Append(b.ToString("x2"));
                return result.ToString();
            }
        }
    }
}
