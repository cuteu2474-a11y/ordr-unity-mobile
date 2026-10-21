using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using Ordr.Mobile.Combat;
using Ordr.Mobile.Core;
using Ordr.Mobile.Mobile;

namespace Ordr.Mobile.Unity
{
    // One shared temporary sprite for all 271 unit definitions. No Warcraft
    // model, copyrighted texture or invented gameplay combat values are used.
    // This component only mirrors units that the game session actually owns.
    public sealed class OrdrUnitWorldPresenter : MonoBehaviour
    {
        [SerializeField] private OrdrCharacterCombatController controller;
        [SerializeField] private Camera worldCamera;
        [SerializeField] private float markerDiameterWorldUnits = 44f;
        [SerializeField] private float selectionTapRadiusPixels = 48f;
        [SerializeField] private float worldTapMinimumScreenHeightRatio = 0.46f;
        [SerializeField] private float refreshIntervalSeconds = 0.12f;
        [SerializeField] private float attackFlashDurationSeconds = 0.18f;

        private sealed class Visual
        {
            public GameObject Root;
            public SpriteRenderer Icon;
            public SpriteRenderer AttackFlash;
            public float AttackFlashStartedAt = -1f;
            public bool IsPreviewFlash;
            public string Grade;
        }

        private readonly Dictionary<long, Visual> visuals = new Dictionary<long, Visual>();
        private Sprite markerSprite;
        private Texture2D markerTexture;
        private float nextRefresh;

        public int VisibleUnitCount { get { return visuals.Count; } }

        private void Awake()
        {
            if (controller == null)
                controller = GetComponent<OrdrCharacterCombatController>();
            if (worldCamera == null)
                worldCamera = Camera.main;
            CreateSharedMarker();
        }

        private void OnEnable()
        {
            if (controller == null)
                controller = GetComponent<OrdrCharacterCombatController>();
            if (controller != null)
                controller.OnCombatEvent += OnCombatEvent;
        }

        private void OnDisable()
        {
            if (controller != null)
                controller.OnCombatEvent -= OnCombatEvent;
        }

        private void Update()
        {
            var combat = controller == null ? null : controller.Combat;
            if (combat == null) return;
            if (Time.unscaledTime >= nextRefresh)
            {
                nextRefresh = Time.unscaledTime + Mathf.Max(0.02f, refreshIntervalSeconds);
                SyncOwnedUnits(combat);
            }
            UpdateAttackFlashes();
            ReadWorldSelection(combat);
        }

        // A short presentation-only pulse replaces character swing/movement
        // animations. Verified hits and explicitly non-damaging map-timing
        // previews emit it; the flash never affects any combat calculation.
        private void OnCombatEvent(CharacterCombatEvent combatEvent)
        {
            if (combatEvent == null ||
                (combatEvent.Type != CharacterCombatEventType.Attack &&
                 combatEvent.Type != CharacterCombatEventType.AttackPreview))
                return;
            long instanceId;
            if (!long.TryParse(combatEvent.ActorId, NumberStyles.None,
                CultureInfo.InvariantCulture, out instanceId)) return;

            Visual visual;
            if (!visuals.TryGetValue(instanceId, out visual))
            {
                // A freshly recruited unit can attack before the next 0.12s
                // presentation snapshot. Create its marker on demand.
                if (controller != null && controller.Combat != null)
                    SyncOwnedUnits(controller.Combat);
                if (!visuals.TryGetValue(instanceId, out visual)) return;
            }
            if (!visual.Root.activeSelf) return;
            visual.AttackFlashStartedAt = Time.unscaledTime;
            visual.IsPreviewFlash = combatEvent.Type == CharacterCombatEventType.AttackPreview;
            visual.AttackFlash.enabled = true;
            visual.AttackFlash.color = visual.IsPreviewFlash
                ? new Color(0.62f, 0.85f, 1f, 0.8f)
                : new Color(1f, 0.88f, 0.3f, 0.8f);
        }

        private void UpdateAttackFlashes()
        {
            float duration = Mathf.Max(0.02f, attackFlashDurationSeconds);
            foreach (var visual in visuals.Values)
            {
                if (visual.AttackFlashStartedAt < 0f) continue;
                float progress = (Time.unscaledTime - visual.AttackFlashStartedAt) / duration;
                if (progress >= 1f)
                {
                    visual.AttackFlash.enabled = false;
                    visual.AttackFlashStartedAt = -1f;
                    continue;
                }
                float remaining = 1f - Mathf.Clamp01(progress);
                visual.AttackFlash.transform.localScale = Vector3.one *
                    Mathf.Lerp(1.10f, 1.70f, progress);
                visual.AttackFlash.color = visual.IsPreviewFlash
                    ? new Color(0.62f, 0.85f, 1f, 0.8f * remaining)
                    : new Color(1f, 0.88f, 0.3f, 0.8f * remaining);
            }
        }

        private void CreateSharedMarker()
        {
            const int size = 32;
            markerTexture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            markerTexture.filterMode = FilterMode.Bilinear;
            markerTexture.wrapMode = TextureWrapMode.Clamp;
            var pixels = new Color[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float dx = (x + 0.5f - size * 0.5f) / (size * 0.5f);
                    float dy = (y + 0.5f - size * 0.5f) / (size * 0.5f);
                    float radius = Mathf.Sqrt(dx * dx + dy * dy);
                    float alpha = Mathf.Clamp01((1f - radius) * 16f);
                    pixels[y * size + x] = new Color(1f, 1f, 1f, alpha);
                }
            markerTexture.SetPixels(pixels);
            markerTexture.Apply();
            markerSprite = Sprite.Create(markerTexture,
                new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size);
        }

        private void SyncOwnedUnits(CharacterCombatRuntime combat)
        {
            var entries = combat.MobileUnitSnapshot();
            var alive = new HashSet<long>();
            foreach (var entry in entries)
            {
                alive.Add(entry.InstanceId);
                Visual visual;
                if (!visuals.TryGetValue(entry.InstanceId, out visual))
                {
                    visual = new Visual();
                    visual.Root = new GameObject("ORDR Unit " + entry.InstanceId +
                        " [" + entry.Unit.Id + "]");
                    visual.Root.transform.SetParent(transform, false);
                    visual.Icon = visual.Root.AddComponent<SpriteRenderer>();
                    visual.Icon.sprite = markerSprite;
                    visual.Icon.sortingOrder = 10;
                    var flash = new GameObject("Attack flash");
                    flash.transform.SetParent(visual.Root.transform, false);
                    visual.AttackFlash = flash.AddComponent<SpriteRenderer>();
                    visual.AttackFlash.sprite = markerSprite;
                    visual.AttackFlash.sortingOrder = 9;
                    visual.AttackFlash.enabled = false;
                    visual.Grade = entry.Unit.Grade;
                    visuals.Add(entry.InstanceId, visual);
                }

                visual.Root.transform.position = new Vector3(
                    (float)entry.Position.X, (float)entry.Position.Y, 0f);
                bool isSelected = combat.Roster.SelectedId.HasValue &&
                    combat.Roster.SelectedId.Value == entry.InstanceId;
                float size = Mathf.Max(1f, markerDiameterWorldUnits) *
                    (isSelected ? 1.25f : 1f);
                visual.Root.transform.localScale = new Vector3(size, size, 1f);
                visual.Icon.color = isSelected
                    ? new Color(1f, 0.95f, 0.35f, 1f)
                    : ColorForGrade(visual.Grade);
                // Deployed story units remain in the roster, but are no longer
                // represented as attackers in the main battle lane.
                visual.Root.SetActive(!combat.IsStoryDeployed(entry.InstanceId));
            }

            var expired = new List<long>();
            foreach (var item in visuals)
                if (!alive.Contains(item.Key))
                {
                    Destroy(item.Value.Root);
                    expired.Add(item.Key);
                }
            foreach (long id in expired) visuals.Remove(id);
        }

        private void ReadWorldSelection(CharacterCombatRuntime combat)
        {
            if (worldCamera == null) worldCamera = Camera.main;
            if (worldCamera == null || !worldCamera.orthographic) return;

            Vector2 screen;
            if (Input.touchCount > 0)
            {
                Touch touch = Input.GetTouch(0);
                if (touch.phase != TouchPhase.Ended) return;
                screen = touch.position;
            }
            else if (Input.GetMouseButtonUp(0))
                screen = Input.mousePosition;
            else return;

            // Lower screen is reserved for the touch command panel. Never
            // redirect economy/dispatch/fusion taps into the world.
            if (screen.y < Screen.height *
                Mathf.Clamp01(worldTapMinimumScreenHeightRatio)) return;

            long nearestId = 0;
            float nearestPixels2 = Mathf.Max(16f, selectionTapRadiusPixels);
            nearestPixels2 *= nearestPixels2;
            foreach (var entry in visuals)
            {
                if (!entry.Value.Root.activeSelf) continue;
                Vector3 visible = worldCamera.WorldToScreenPoint(entry.Value.Root.transform.position);
                if (visible.z <= 0f) continue;
                float dx = visible.x - screen.x;
                float dy = visible.y - screen.y;
                float d2 = dx * dx + dy * dy;
                if (d2 > nearestPixels2) continue;
                if (nearestId == 0 || d2 < nearestPixels2)
                {
                    nearestId = entry.Key;
                    nearestPixels2 = d2;
                }
            }
            if (nearestId != 0) combat.Roster.Select(nearestId);
        }

        private static Color ColorForGrade(string grade)
        {
            // Presentation-only colors. They never affect combat, targeting,
            // buffs, fusion, score or original grade rules.
            switch (grade)
            {
                case "흔함": return new Color(0.80f, 0.82f, 0.85f);
                case "안흔함": return new Color(0.38f, 0.78f, 0.52f);
                case "특별함": return new Color(0.31f, 0.58f, 0.94f);
                case "특수함": return new Color(0.28f, 0.86f, 0.84f);
                case "희귀함": return new Color(0.64f, 0.46f, 0.96f);
                case "전설": return new Color(0.98f, 0.67f, 0.19f);
                case "히든": return new Color(0.91f, 0.45f, 0.77f);
                case "변화": return new Color(0.75f, 0.51f, 0.99f);
                case "제한됨": return new Color(0.97f, 0.40f, 0.43f);
                case "왜곡됨": return new Color(0.70f, 0.33f, 0.47f);
                case "초월함": return new Color(0.96f, 0.86f, 0.36f);
                case "영원한": return new Color(0.96f, 0.89f, 0.68f);
                case "불멸의": return new Color(0.95f, 0.58f, 0.55f);
                case "세라핌": return new Color(0.92f, 0.93f, 0.98f);
                case "랜덤": return new Color(0.57f, 0.75f, 0.88f);
                case "신비함": return new Color(0.54f, 0.87f, 0.79f);
                default: return Color.white;
            }
        }

        private void OnDestroy()
        {
            foreach (var item in visuals.Values)
                if (item.Root != null) Destroy(item.Root);
            visuals.Clear();
            if (markerSprite != null) Destroy(markerSprite);
            if (markerTexture != null) Destroy(markerTexture);
        }
    }
}
