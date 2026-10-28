using System;
using System.Collections.Generic;
using Ordr.Mobile.Combat;
using Ordr.Mobile.Core;
using Ordr.Mobile.Data;

namespace Ordr.Mobile.Mobile
{
    // war3map.j Bn2/BoK, BC5 spell-effect registrations; A0EB/A0EJ Cast1.
    // Consumes the exact owned actor after the full cast and stages ALL outputs
    // under the existing exclusive rollback-capable unit transaction.
    public sealed class Original323UnitDecompositionService : IManualUnitDecompositionPort, IDisposable
    {
        private sealed class Pending
        {
            public long Id;
            public string UnitId;
            public double Left;
            public CharacterCombatRuntime.OriginalUnitCast Cast;
        }
        private readonly CharacterCombatRuntime combat;
        private readonly SharedEconomySession economy;
        private readonly IOriginalWispRandom random;
        private readonly Dictionary<long, Pending> pending = new Dictionary<long, Pending>();
        // Non-rewindable original streams retain a failed result for the same actor.
        private readonly Dictionary<long, int> rolledCounts = new Dictionary<long, int>();
        private bool busy;
        private bool disposed;
        public string LastMessage { get; private set; }
        public int PendingCount => pending.Count;
        public static bool Supports(string id) => id == "S-02" || id == "S-11";
        public static string Description(string id) => id == "S-02"
            ? "5초 시전 · 후쿠로 1기 + 버기 1기 + 총병 1기"
            : id == "S-11" ? "2초 시전 · 버기 2~3기 (각 50%)" : null;
        public Original323UnitDecompositionService(CharacterCombatRuntime world, SharedEconomySession shared,
            OriginalMapBaseStatCatalog map, IOriginalWispRandom sharedScriptRandom)
        {
            combat = world ?? throw new ArgumentNullException(nameof(world));
            economy = shared ?? throw new ArgumentNullException(nameof(shared));
            random = sharedScriptRandom ?? throw new ArgumentNullException(nameof(sharedScriptRandom));
            if (map == null || map.SourceW3xSha256 != OriginalCommonSaleRules.OriginalMapSha256 ||
                !Object.ReferenceEquals(combat.Roster, economy.Units))
                throw new ArgumentException("분해 원본과 보유 세션이 일치하지 않습니다.");
            Validate(map, "S-02", "h01C", "A0EJ");
            Validate(map, "S-11", "h015", "A0EB");
            Validate(map, "U-13", "h00A", null);
            Validate(map, "C-07", "h005", null);
            Validate(map, "C-08", "h009", null);
        }
        private static void Validate(OriginalMapBaseStatCatalog map, string id, string raw, string ability)
        {
            var row = map.Find(id);
            if (row == null || row.OriginalRawcode != raw ||
                (ability != null && !Contains(row.OriginalAbilityRawcodes, ability)))
                throw new FormatException("분해 유닛 원본 신원 불일치: " + id);
        }
        private static bool Contains(IReadOnlyList<string> values, string code)
        { foreach (string value in values) if (value == code) return true; return false; }
        public double Remaining(long id) => pending.ContainsKey(id) ? pending[id].Left : 0;
        public bool IsCasting(long id) => pending.ContainsKey(id);
        public ManualActionResult Request(long id)
        {
            if (disposed || busy || pending.ContainsKey(id) || !economy.HasStarted || combat.Game.Phase != SessionPhase.Running)
                return Reject("이미 시전 중이거나 지금 분해할 수 없습니다.");
            UnitState unit;
            try { unit = combat.Roster.Get(id); }
            catch (KeyNotFoundException) { return Reject("보유 유닛이 없습니다."); }
            if (!Supports(unit.UnitId) || combat.IsStoryDeployed(id)) return Reject("분해 가능한 보유 유닛을 선택해 주세요.");
            // Preserve an unrewindable failed result: another actor may not bypass it.
            foreach (long old in new List<long>(rolledCounts.Keys))
                if (!Exists(old)) rolledCounts.Remove(old);
            if (rolledCounts.Count > 0 && !rolledCounts.ContainsKey(id))
                return Reject("대기 중인 같은 유닛의 분해를 먼저 완료해 주세요.");
            try
            {
                var cast = combat.BeginOriginalUnitCast(id);
                pending.Add(id, new Pending { Id = id, UnitId = unit.UnitId,
                    Left = unit.UnitId == "S-02" ? 5 : 2, Cast = cast });
                LastMessage = Description(unit.UnitId) + " · 시전 시작";
                return new ManualActionResult(ManualActionState.Succeeded, LastMessage);
            }
            catch (InvalidOperationException error) { return Reject(error.Message); }
        }
        public ManualActionResult Cancel(long id)
        {
            Pending cast;
            if (busy || !pending.TryGetValue(id, out cast)) return Reject("취소할 시전이 없습니다.");
            cast.Cast.Dispose(); pending.Remove(id);
            LastMessage = "분해 시전 취소 · 원래 유닛 유지";
            return new ManualActionResult(ManualActionState.Succeeded, LastMessage);
        }
        // Caller passes ONLY advancing foreground logical time. Preparation has
        // its own foreground clock and does not start monsters/rounds.
        public void Advance(double seconds)
        {
            if (double.IsNaN(seconds) || double.IsInfinity(seconds) || seconds < 0)
                throw new ArgumentOutOfRangeException(nameof(seconds));
            if (disposed) return;
            if (busy) throw new InvalidOperationException("분해 처리 재진입 불가");
            busy = true;
            try
            {
                var ordered = new List<Pending>(pending.Values);
                ordered.Sort((a,b) => { int c = a.Left.CompareTo(b.Left); return c != 0 ? c : a.Id.CompareTo(b.Id); });
                foreach (var item in ordered)
                {
                    if (!item.Cast.IsCurrent)
                    { Finish(item, "분해 시전 중단 · 유닛 이동/제거/파견 또는 게임 종료"); continue; }
                    item.Left = Math.Max(0, item.Left - seconds);
                    if (item.Left > 1e-9 || combat.PendingSimulationSeconds != 0) continue;
                    Complete(item);
                }
            }
            finally { busy = false; }
        }
        private void Complete(Pending item)
        {
            var rewindable = random as IOriginal323RollbackWispRandom;
            int checkpoint = rewindable == null ? -1 : rewindable.CaptureCheckpoint();
            try
            {
                using (var swap = combat.BeginOriginalUnitRemoval(item.Id))
                {
                    if (!item.Cast.IsCurrent) throw new InvalidOperationException("시전 유닛 상태가 바뀌었습니다.");
                    int buggyCount = 1;
                    if (item.UnitId == "S-11" && !rolledCounts.TryGetValue(item.Id, out buggyCount))
                    {
                        buggyCount = random.NextInclusive(2,3);
                        if (buggyCount < 2 || buggyCount > 3) throw new InvalidOperationException("원본 버기 분해 난수 범위 오류");
                        rolledCounts.Add(item.Id, buggyCount);
                    }
                    if (item.UnitId == "S-02") swap.StageResult("U-13", -3760, 3904);
                    for (int i=0; i<buggyCount; i++) swap.StageResult("C-07", -3232, 3232);
                    if (item.UnitId == "S-02") swap.StageResult("C-08", -2976, 3232);
                    swap.Commit();
                    rolledCounts.Remove(item.Id);
                    Finish(item, item.UnitId == "S-02" ? "갱 뱃지 분해 완료 · 후쿠로 + 버기 + 총병"
                        : "버기 마기탄 분해 완료 · 버기 " + buggyCount + "기");
                }
            }
            catch (Exception error)
            {
                if (rewindable != null) { rewindable.RestoreCheckpoint(checkpoint); rolledCounts.Remove(item.Id); }
                Finish(item, "분해 취소 · 원래 유닛 유지: " + error.Message);
            }
        }
        private void Finish(Pending item, string message)
        { item.Cast.Dispose(); pending.Remove(item.Id); LastMessage = message; }
        private bool Exists(long id)
        { try { combat.Roster.Get(id); return true; } catch (KeyNotFoundException) { return false; } }
        private static ManualActionResult Reject(string message) => new ManualActionResult(ManualActionState.Rejected, message);
        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            foreach (var item in pending.Values) item.Cast.Dispose();
            pending.Clear();
        }
    }
}
