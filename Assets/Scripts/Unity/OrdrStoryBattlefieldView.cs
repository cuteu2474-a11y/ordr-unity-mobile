using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using Ordr.Mobile.Combat;
using Ordr.Mobile.Mobile;

namespace Ordr.Mobile.Unity
{
    // Presentation only. Screen slots never move runtime actors or cause hits.
    internal sealed class OrdrStoryBattlefieldView : IDisposable
    {
        private sealed class Cue
        {
            public string Target;
            public double At;
            public bool Skill;
        }

        private readonly OrdrCharacterCombatController controller;
        private CharacterCombatRuntime combat;
        private readonly List<MobileUnitSnapshot> deployed = new List<MobileUnitSnapshot>();
        private readonly Dictionary<long, Cue> cues = new Dictionary<long, Cue>();
        private readonly List<long> stale = new List<long>();
        private readonly HashSet<long> ownedIds = new HashSet<long>();
        private float nextRefresh;
        private int page;
        private string hitTarget;
        private double hitAt = -1;
        private GUIStyle caption, tiny;

        public OrdrStoryBattlefieldView(OrdrCharacterCombatController source)
        {
            controller = source;
            controller.OnCombatEvent += OnEvent;
        }

        public void Tick()
        {
            if (!ReferenceEquals(combat, controller.Combat))
            {
                if (combat != null) combat.ActualEnemyDamage -= OnDamage;
                combat = controller.Combat;
                if (combat != null) combat.ActualEnemyDamage += OnDamage;
                deployed.Clear();
                cues.Clear();
                hitTarget = null;
                hitAt = -1;
                nextRefresh = 0;
                page = 0;
            }
            if (combat == null || Time.unscaledTime < nextRefresh) return;
            nextRefresh = Time.unscaledTime + .12f;
            deployed.Clear();
            ownedIds.Clear();
            foreach (var row in combat.MobileUnitSnapshot())
                if (combat.IsStoryDeployed(row.InstanceId))
                {
                    deployed.Add(row);
                    ownedIds.Add(row.InstanceId);
                }
            deployed.Sort((a, b) => a.InstanceId.CompareTo(b.InstanceId));
            stale.Clear();
            foreach (var item in cues)
                if (!ownedIds.Contains(item.Key) || combat.Time - item.Value.At > .6)
                    stale.Add(item.Key);
            foreach (long id in stale) cues.Remove(id);
        }

        private bool IsBuilding(string enemyId)
        {
            return enemyId != null && controller.PracticeStoryBuildings != null &&
                controller.PracticeStoryBuildings.FindEncounter(enemyId) != null;
        }

        private void MarkActor(string actor, string target, bool skill)
        {
            long id;
            if (!long.TryParse(actor, NumberStyles.None, CultureInfo.InvariantCulture, out id) &&
                !combat.TryGetSourceIndependentAttackOwner(actor, out id)) return;
            if (!combat.IsStoryDeployed(id)) return;
            Cue cue;
            if (!cues.TryGetValue(id, out cue))
                cues.Add(id, cue = new Cue());
            cue.Target = target;
            cue.At = combat.Time;
            cue.Skill = skill;
        }

        private void OnEvent(CharacterCombatEvent value)
        {
            if (combat == null || value == null || !IsBuilding(value.EnemyId)) return;
            if (value.Type == CharacterCombatEventType.Attack ||
                value.Type == CharacterCombatEventType.SkillActivated ||
                (value.Type == CharacterCombatEventType.SkillDamage && value.Amount > 0))
                MarkActor(value.ActorId, value.EnemyId,
                    value.Type != CharacterCombatEventType.Attack);
        }

        private void OnDamage(string actor, string target, long amount, bool killed)
        {
            if (amount <= 0 || !IsBuilding(target)) return;
            hitTarget = target;
            hitAt = combat.Time;
            // Skill and independent-summon damage share the real HP ledger.
            MarkActor(actor, target, false);
        }

        public void Draw(Rect area, float scale)
        {
            Tick();
            if (caption == null)
            {
                caption = new GUIStyle(GUI.skin.label) {
                    alignment = TextAnchor.MiddleLeft, clipping = TextClipping.Clip };
                tiny = new GUIStyle(caption) { alignment = TextAnchor.MiddleCenter };
            }
            float header = Mathf.Min(26f * scale, area.height * .25f);
            float footer = Mathf.Min(17f * scale, area.height * .23f);
            float ground = area.height - footer - 2f;
            float size = Mathf.Min(52f * scale, Mathf.Max(12f, ground - header - 12f * scale));
            float buildingWidth = Mathf.Min(150f * scale, area.width * .30f);
            float buildingX = area.width - buildingWidth - 8f;
            float rowWidth = Mathf.Max(1f, buildingX - 24f);
            int capacity = Mathf.Max(1, Mathf.FloorToInt(rowWidth / (size + 9f)));
            int pages = Mathf.Max(1, (deployed.Count + capacity - 1) / capacity);
            page = Mathf.Clamp(page, 0, pages - 1);
            caption.fontSize = Mathf.Max(9, Mathf.RoundToInt(header * .72f));
            tiny.fontSize = Mathf.Max(8, Mathf.RoundToInt(footer * .68f));
            Color old = GUI.color;
            GUI.BeginGroup(area);
            try
            {
                Fill(new Rect(0, ground, area.width, 2), new Color(.35f, .55f, .55f));
                var world = controller.PracticeStoryBuildings;
                Original323StoryBuildingEncounter alive = null, last = null;
                if (world != null)
                    foreach (var entry in world.Encounters)
                    {
                        last = entry;
                        if (entry.State == Original323StoryBuildingState.Alive) alive = entry;
                    }
                var enemy = combat == null || alive == null ? null : combat.EnemyOf(alive.EnemyId);
                bool hasTarget = enemy != null && enemy.Health > 0;
                string title = hasTarget ? alive.Building.Stage + ". " + alive.Building.Name +
                    "   HP " + enemy.Health + " / " + enemy.MaxHealth : "스토리 전장";
                if (hasTarget)
                {
                    Color text = caption.normal.textColor;
                    caption.normal.textColor = OrdrMobileUiTheme.Gold;
                    var titleContent = new GUIContent(title);
                    while (caption.fontSize > 11 && caption.CalcSize(titleContent).x > area.width - 12f)
                        caption.fontSize--;
                    GUI.Label(new Rect(6, 0, area.width - 12, header), titleContent, caption);
                    caption.normal.textColor = text;
                }
                var displayed = alive ?? last;
                float flash = combat != null && displayed != null && hitTarget == displayed.EnemyId
                    ? Fade(combat.Time - hitAt, .24f) : 0;
                DrawBuilding(new Rect(buildingX, header + 10f * scale, buildingWidth,
                    Mathf.Max(10, ground - header - 10f * scale)), hasTarget, flash);
                if (hasTarget)
                {
                    float ratio = Mathf.Clamp01((float)((double)enemy.Health / enemy.MaxHealth));
                    Rect bar = new Rect(8f, header + 1f, area.width - 16f, 5f * scale);
                    Fill(bar, new Color(.10f, .13f, .18f));
                    bar.width *= ratio;
                    Fill(bar, Color.Lerp(new Color(.95f, .3f, .2f),
                        new Color(.35f, .85f, .55f), ratio));
                }
                else
                {
                    var scenario = controller.SoloStory;
                    string wait = scenario == null ? "전투 준비" :
                        scenario.CompletedAllBuildings ? "스토리 완료" :
                        scenario.LostByStory ? "스토리 종료" :
                        scenario.EggheadWaitRemaining > 0 ?
                            "등장 " + Math.Ceiling(scenario.EggheadWaitRemaining) + "초" :
                        last != null ? "격파 · 다음 건물 대기" : "건물 등장 대기";
                    GUI.Label(new Rect(6, 0, area.width - 12, header),
                        "스토리 전장 · " + wait, caption);
                }
                if (hasTarget && deployed.Count == 0)
                    GUI.Label(new Rect(8, header, rowWidth, ground - header),
                        "스토리 메뉴에서 유닛을 파견하세요", tiny);
                for (int slot = 0; slot < capacity && page * capacity + slot < deployed.Count; slot++)
                {
                    var unit = deployed[page * capacity + slot];
                    // Recheck before input: a transaction may have consumed this instance.
                    if (!combat.IsStoryDeployed(unit.InstanceId)) continue;
                    float x = 8 + slot * (size + 9);
                    Cue cue;
                    float age = cues.TryGetValue(unit.InstanceId, out cue) && hasTarget &&
                        cue.Target == alive.EnemyId ? (float)(combat.Time - cue.At) : 100;
                    float action = Fade(age, .34f);
                    float thrust = Mathf.Sin(Mathf.Clamp01(age / .34f) * Mathf.PI) * 5f * scale;
                    Rect card = new Rect(x + (action > 0 ? thrust : 0), ground - size, size, size);
                    bool selected = combat.Roster.SelectedId == unit.InstanceId;
                    Fill(new Rect(card.x - 2, card.y - 2, size + 4, size + 4),
                        selected ? OrdrMobileUiTheme.Gold : new Color(.25f, .55f, .65f));
                    Fill(card, new Color(.12f, .22f, .29f));
                    var portrait = OrdrCharacterPortraits.Find(unit.Unit.Id);
                    if (portrait != null)
                    {
                        Rect uv = portrait.rect;
                        GUI.DrawTextureWithTexCoords(card, portrait.texture,
                            new Rect(uv.x / portrait.texture.width, uv.y / portrait.texture.height,
                                uv.width / portrait.texture.width, uv.height / portrait.texture.height));
                    }
                    else GUI.Label(card, unit.Unit.Name, tiny);
                    if (GUI.Button(card, new GUIContent("", unit.Unit.Name), GUIStyle.none) &&
                        combat.IsStoryDeployed(unit.InstanceId))
                        combat.Roster.Select(unit.InstanceId);
                    if (action > 0)
                    {
                        Color tint = cue.Skill ? new Color(.8f, .5f, 1f, action) :
                            new Color(1f, .8f, .35f, action);
                        float travel = Mathf.Clamp01(age / .18f);
                        Vector2 end = new Vector2(buildingX + buildingWidth * .35f, ground - size * .45f);
                        Vector2 dot = Vector2.Lerp(card.center, end, travel);
                        Line(card.center, dot, tint, 2f * scale);
                        Fill(new Rect(dot.x - 2, dot.y - 2, 5, 5), tint);
                    }
                }
                string status = "파견 유닛 " + deployed.Count + "기";
                if (controller.SoloStory != null)
                {
                    status += " · 스토리 " + controller.SoloStory.HighestDestroyedStage + "/14";
                    if (controller.SoloStory.EggheadDeadlineRemaining > 0)
                        status += " · 제한 " + Math.Ceiling(controller.SoloStory.EggheadDeadlineRemaining) + "초";
                }
                caption.fontSize = Mathf.Max(9, Mathf.RoundToInt(footer * .76f));
                GUI.Label(new Rect(6, area.height - footer, area.width - 90f * scale, footer),
                    status, caption);
                if (pages > 1)
                {
                    float w = 27f * scale;
                    float right = area.width - 4;
                    if (GUI.Button(new Rect(right - w * 3, area.height - footer, w, footer),
                        "◀", tiny)) page = (page + pages - 1) % pages;
                    GUI.Label(new Rect(right - w * 2, area.height - footer, w, footer),
                        (page + 1) + "/" + pages, tiny);
                    if (GUI.Button(new Rect(right - w, area.height - footer, w, footer),
                        "▶", tiny)) page = (page + 1) % pages;
                }
            }
            finally { GUI.EndGroup(); GUI.color = old; }
        }

        private static float Fade(double age, float duration) =>
            age < 0 || age >= duration ? 0 : 1f - (float)age / duration;

        private static void DrawBuilding(Rect r, bool alive, float hit)
        {
            Color wall = Color.Lerp(new Color(.32f, .43f, .53f), Color.white, hit);
            if (!alive)
            {
                for (int i = 0; i < 4; i++)
                    Fill(new Rect(r.x + i * r.width / 4, r.yMax - 5,
                        r.width / 5, 4), Color.Lerp(new Color(.26f, .32f, .36f),
                            OrdrMobileUiTheme.Gold, hit));
                return;
            }
            float shake = Mathf.Sin(hit * 25f) * hit * 3f;
            r.x += shake;
            Fill(new Rect(r.x, r.yMax - 3, r.width, 3), new Color(.12f, .18f, .23f));
            Texture2D texture = OrdrBattlefieldArt.StoryFortressTexture;
            if (texture != null)
            {
                Rect image = OrdrBattlefieldArt.FitTexture(r, texture, OrdrBattlefieldArt.StoryFortressUv);
                Color before = GUI.color;
                try
                {
                    GUI.color = Color.Lerp(Color.white, new Color(1f,.65f,.42f,1f), hit * .65f);
                    GUI.DrawTextureWithTexCoords(image, texture, OrdrBattlefieldArt.StoryFortressUv);
                }
                finally { GUI.color = before; }
            }
            else
            {
                Fill(new Rect(r.x + r.width * .12f, r.y + r.height * .28f,
                    r.width * .76f, r.height * .72f), wall);
                for (int i = 0; i < 3; i++)
                {
                    float x = r.x + r.width * (.10f + i * .30f);
                    Fill(new Rect(x, r.y, r.width * .20f, r.height * .4f), wall);
                    Fill(new Rect(x - 1, r.y, r.width * .20f + 2, 3), OrdrMobileUiTheme.Gold);
                    Fill(new Rect(x + r.width * .07f, r.y + r.height * .4f,
                        r.width * .06f, r.height * .16f), new Color(.08f, .14f, .21f));
                }
                Fill(new Rect(r.center.x - r.width * .08f, r.yMax - r.height * .35f,
                    r.width * .16f, r.height * .35f), new Color(.08f, .14f, .21f));
            }
            if (hit > 0)
            {
                Color spark = new Color(1, .85f, .4f, hit);
                Line(r.center + new Vector2(-7, -7), r.center + new Vector2(7, 7), spark, 3);
                Line(r.center + new Vector2(-7, 7), r.center + new Vector2(7, -7), spark, 3);
            }
        }

        private static void Fill(Rect rect, Color tint)
        {
            Color old = GUI.color;
            GUI.color = tint;
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            GUI.color = old;
        }

        private static void Line(Vector2 from, Vector2 to, Color tint, float width)
        {
            Vector2 delta = to - from;
            if (delta.sqrMagnitude < .01f) return;
            Matrix4x4 old = GUI.matrix;
            try
            {
                GUIUtility.RotateAroundPivot(Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg, from);
                Fill(new Rect(from.x, from.y - width * .5f, delta.magnitude, width), tint);
            }
            finally { GUI.matrix = old; }
        }

        public void Dispose()
        {
            controller.OnCombatEvent -= OnEvent;
            if (combat != null) combat.ActualEnemyDamage -= OnDamage;
            cues.Clear();
            deployed.Clear();
        }
    }
}
