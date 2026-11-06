using System;
using System.Collections.Generic;

namespace Ordr.Mobile.Combat
{
    // war3map.j: CN* / CMy / CMx / CMv / CMw / CMu / BeL.
    // Personal missions and story stage clears must not silently grant these receipts.
    public enum Original323SharedMissionKind
    {
        RumbarSouls, JusticeGate, ShandoraBell, SeaKing,
        FastAlabasta, DestructionKing, MarineAdmiral, TreasureHunter
    }

    public enum Original323PoneglyphPhase { Locked, Ready, Decoding, Completed }

    // The witness must be backed by the corresponding committed original-world
    // event. A UI tap, a JASS function name, or a caller-provided string alone
    // does not certify a shared mission or an interpreter command.
    public interface IOriginal323PoneglyphEventWitness
    {
        bool TryConfirmSharedMission(Original323SharedMissionKind kind,
            string eventId, int currentRound, out string sourceEvidence);
        bool TryConfirmInterpreterCommand(string originalCommand,
            string eventId, out string sourceEvidence);
        bool TryConfirmUnitAbilityState(long instanceId, string abilityRawcode,
            bool present, string eventId, out string sourceEvidence);
    }

    public sealed class Original323PoneglyphSession
    {
        public static readonly string[] SharedMissionNames =
        {
            "룸바 해적단 영혼 해방", "정의의 문 파괴", "샨도라의 황금 종",
            "해왕류 퇴치", "신속한 철거", "파괴왕", "해군대장 저지",
            "트레져 헌터"
        };

        private static readonly HashSet<string> ForbiddenAbilities =
            new HashSet<string>(StringComparer.Ordinal)
            {
                "A080", "A0B9", "A0BB", "A0BA", "A09G",
                "A17T", "A0B8", "A800", "Aloc"
            };

        private readonly bool[] completedMissions = new bool[8];
        private readonly HashSet<string> acceptedSourceEvents =
            new HashSet<string>(StringComparer.Ordinal);
        private readonly Dictionary<long, Dictionary<string, bool>> abilityOverrides =
            new Dictionary<long, Dictionary<string, bool>>();
        private readonly HashSet<string> revealedChestIds =
            new HashSet<string>(StringComparer.Ordinal);

        public int SharedCompletedCount { get; private set; }
        public bool InterpreterActive { get; private set; }
        public bool EverCompleted { get; private set; }
        public long? ActiveUnitId { get; private set; }
        public int ActiveDurationSeconds { get; private set; }
        public double StartedAt { get; private set; }
        public double FinishesAt { get; private set; }
        public int AttemptCount { get; private set; }
        public string LastMessage { get; private set; } = "공유 도전과제 8개 완료 대기";
        public Original323PoneglyphPhase Phase =>
            EverCompleted ? Original323PoneglyphPhase.Completed :
            ActiveUnitId.HasValue ? Original323PoneglyphPhase.Decoding :
            SharedCompletedCount == completedMissions.Length ?
                Original323PoneglyphPhase.Ready : Original323PoneglyphPhase.Locked;
        public int RevealedChestCount => revealedChestIds.Count;

        public bool IsSharedMissionCompleted(Original323SharedMissionKind kind)
        {
            int index = (int)kind;
            return index >= 0 && index < completedMissions.Length &&
                completedMissions[index];
        }

        public bool CanRecordVerifiedSharedMission(Original323SharedMissionKind kind,
            string eventId, int currentRound)
        {
            int index = (int)kind;
            return index >= 0 && index < completedMissions.Length &&
                !string.IsNullOrWhiteSpace(eventId) &&
                !completedMissions[index] && !acceptedSourceEvents.Contains(eventId) &&
                (kind != Original323SharedMissionKind.FastAlabasta || currentRound < 9) &&
                (kind != Original323SharedMissionKind.DestructionKing || currentRound < 30);
        }

        public bool RecordVerifiedSharedMission(Original323SharedMissionKind kind,
            string eventId, int currentRound, string sourceEvidence)
        {
            if (!CanRecordVerifiedSharedMission(kind, eventId, currentRound) ||
                string.IsNullOrWhiteSpace(sourceEvidence))
                return false;
            int index = (int)kind;
            acceptedSourceEvents.Add(eventId);
            completedMissions[index] = true;
            SharedCompletedCount++;
            LastMessage = SharedCompletedCount == 8 ?
                "공유 도전과제 8개 완료 · 포네그리프 해독 가능" :
                SharedMissionNames[index] + " 완료 (" + SharedCompletedCount + "/8)";
            return true;
        }

        public bool EnableVerifiedInterpreter(string originalCommand,
            string eventId, string evidence)
        {
            if ((originalCommand != "Byh" && originalCommand != "B4Z" &&
                 originalCommand != "B6Y") ||
                string.IsNullOrWhiteSpace(eventId) ||
                string.IsNullOrWhiteSpace(evidence) ||
                acceptedSourceEvents.Contains(eventId))
                return false;
            acceptedSourceEvents.Add(eventId);
            InterpreterActive = true; // Once true, stays true within this run.
            return true;
        }

        // Native ability events override the static object list per instance.
        // In particular, the CJH Absalom-failure zombie receives A09G at runtime.
        public void RecordVerifiedAbilityState(long instanceId, string abilityRawcode,
            bool present)
        {
            if (instanceId <= 0 || string.IsNullOrEmpty(abilityRawcode) ||
                abilityRawcode.Length != 4)
                throw new ArgumentException("원본 인스턴스/기술 ID 필요");
            Dictionary<string, bool> abilities;
            if (!abilityOverrides.TryGetValue(instanceId, out abilities))
            {
                abilities = new Dictionary<string, bool>(StringComparer.Ordinal);
                abilityOverrides.Add(instanceId, abilities);
            }
            abilities[abilityRawcode] = present;
        }

        public void ForgetUnit(long instanceId)
        {
            abilityOverrides.Remove(instanceId);
            if (ActiveUnitId == instanceId)
                Interrupt("해독 유닛이 소멸하여 실패 · 재시도 가능");
        }

        public bool IsEligible(long instanceId, IEnumerable<string> baseAbilities,
            out string reason)
        {
            if (instanceId <= 0 || baseAbilities == null)
            {
                reason = "원본 유닛 능력 목록 미확인";
                return false;
            }
            var abilities = new HashSet<string>(baseAbilities, StringComparer.Ordinal);
            Dictionary<string, bool> changes;
            if (abilityOverrides.TryGetValue(instanceId, out changes))
                foreach (var pair in changes)
                {
                    if (pair.Value) abilities.Add(pair.Key);
                    else abilities.Remove(pair.Key);
                }
            foreach (string ability in ForbiddenAbilities)
                if (abilities.Contains(ability))
                {
                    reason = "원본 금지 기술 " + ability + " 보유";
                    return false;
                }
            reason = "해독 자격 충족";
            return true;
        }

        public bool TryStart(long instanceId, IEnumerable<string> baseAbilities,
            double combatTime, out string message)
        {
            if (Phase != Original323PoneglyphPhase.Ready)
            {
                message = Phase == Original323PoneglyphPhase.Locked ?
                    "공유 도전과제 8개 완료 후 해독할 수 있습니다." :
                    Phase == Original323PoneglyphPhase.Decoding ?
                    "다른 유닛이 해독 중입니다." : "이미 해독을 완료했습니다.";
                return false;
            }
            if (double.IsNaN(combatTime) || double.IsInfinity(combatTime) ||
                combatTime < 0)
            {
                message = "유효하지 않은 전투 시각";
                return false;
            }
            if (!IsEligible(instanceId, baseAbilities, out message))
                return false;
            ActiveUnitId = instanceId;
            StartedAt = combatTime;
            ActiveDurationSeconds = InterpreterActive ? 50 : 150;
            FinishesAt = combatTime + ActiveDurationSeconds;
            AttemptCount++;
            LastMessage = "유닛 #" + instanceId + " 해독 시작 · " +
                ActiveDurationSeconds + "초";
            message = LastMessage;
            return true;
        }

        public double RemainingSeconds(double combatTime)
        {
            return ActiveUnitId.HasValue ?
                Math.Max(0, FinishesAt - combatTime) : 0;
        }

        public bool Interrupt(string message)
        {
            if (!ActiveUnitId.HasValue) return false;
            ActiveUnitId = null;
            ActiveDurationSeconds = 0;
            LastMessage = message ?? "해독 중단 · 재시도 가능";
            return true;
        }

        // Returns true only when the active attempt has just ended.
        public bool Advance(double combatTime, bool unitStillExists,
            Original323TreasureHunt treasure, int currentRound)
        {
            if (!ActiveUnitId.HasValue) return false;
            if (!unitStillExists)
            {
                Interrupt("해독 유닛 사망·소멸 · 재시도 가능");
                return true;
            }
            if (combatTime < FinishesAt) return false;
            ActiveUnitId = null;
            ActiveDurationSeconds = 0;
            EverCompleted = true;
            revealedChestIds.Clear();
            // BeL exposes only source-observed, currently existing locations.
            // It neither creates chests nor calls the treasure reward settlement.
            if (treasure != null)
                foreach (var chest in treasure.Chests)
                    if (!chest.Collected && chest.SpawnRound <= currentRound)
                        revealedChestIds.Add(chest.OriginalChestId);
            LastMessage = treasure == null ?
                "해독 완료 · 원본 보물 좌표 공급자가 연결되지 않아 공개할 위치 없음" :
                "해독 완료 · 현재 보물 " + revealedChestIds.Count +
                "개 위치 공개 (획득/보상 없음)";
            return true;
        }

        public IReadOnlyList<Original323TreasureLocation> VisibleRevealedChests(
            Original323TreasureHunt treasure)
        {
            var visible = new List<Original323TreasureLocation>();
            if (treasure != null)
                foreach (var chest in treasure.Chests)
                    if (!chest.Collected && revealedChestIds.Contains(chest.OriginalChestId))
                        visible.Add(chest);
            return visible.AsReadOnly();
        }
    }
}
