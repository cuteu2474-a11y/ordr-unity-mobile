using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using Ordr.Mobile.Combat;
using Ordr.Mobile.Core;
using Ordr.Mobile.Mobile;

namespace Ordr.Mobile.Unity
{
    // One shared temporary sprite for all current unit definitions. No Warcraft
    // model, copyrighted texture or invented gameplay combat values are used.
    // This component only mirrors units that the game session actually owns.
    public sealed class OrdrUnitWorldPresenter : MonoBehaviour
    {
        [SerializeField] private OrdrCharacterCombatController controller;
        [SerializeField] private Camera worldCamera;
        [SerializeField] private float markerDiameterWorldUnits = 44f;
        [SerializeField] private float selectionTapRadiusPixels = 48f;
        [SerializeField] private float refreshIntervalSeconds = 0.12f;
        [SerializeField] private float attackFlashDurationSeconds = 0.18f;
        [SerializeField] private float attackCueDurationSeconds = 0.22f;
        [SerializeField] private int maximumSimultaneousAttackCues = 48;

        private sealed class Visual
        {
            public GameObject Root;
            public SpriteRenderer Icon;
            public SpriteRenderer AttackFlash;
            public float AttackFlashStartedAt = -1f;
            public bool IsPreviewFlash;
            public bool IsSkillFlash;
            public string Grade;
            public string OriginalWeaponType;
        }

        private sealed class EnemyVisual
        {
            public GameObject Root;
            public SpriteRenderer Icon;
            public SpriteRenderer HealthBarBack;
            public SpriteRenderer HealthBarFill;
            public Color BaseColor;
            public float HitAt = -1f;
        }

        private sealed class AttackCueVisual
        {
            public GameObject Root;
            public SpriteRenderer Body;
            public SpriteRenderer Impact;
            public BasicAttackCueKind Kind;
            public Vector3 From;
            public Vector3 To;
            public Color Tint;
            public float StartedAt;
        }

        private sealed class LastEnemyPosition
        {
            public Vector3 Position;
            public float LastSeenAt;
        }

        private readonly Dictionary<long, Visual> visuals = new Dictionary<long, Visual>();
        private readonly Dictionary<string, EnemyVisual> enemyVisuals =
            new Dictionary<string, EnemyVisual>(StringComparer.Ordinal);
        private readonly Dictionary<string, LastEnemyPosition> recentEnemyPositions =
            new Dictionary<string, LastEnemyPosition>(StringComparer.Ordinal);
        private readonly List<AttackCueVisual> attackCues = new List<AttackCueVisual>();
        private readonly List<Vector2> framePoints = new List<Vector2>();
        private IReadOnlyList<MobileOwnedUnitGroup> formationGroups = new List<MobileOwnedUnitGroup>();
        private MobileOwnedFormationLayout formationLayout;
        private int formationPage;
        private readonly HashSet<long> formationVisibleIds = new HashSet<long>();
        private long? lastFormationSelection;
        public int MainOwnedUnitCount { get; private set; }
        private GameObject evolutionMarker;
        private SpriteRenderer evolutionIcon;
        private bool cameraFramed;
        private float initialCameraHalfHeight;
        private bool pointerBeganOverMap;
        private bool pointerDragged;
        private bool multitouchGestureUsed;
        private bool suppressSelectionThisFrame;
        private Vector2 pointerStart;
        private Vector2 previousMouse;
        private Sprite markerSprite;
        private Texture2D markerTexture;
        private Sprite healthBarSprite;
        private Texture2D healthBarTexture;
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
                SyncEnemies(combat);
                SyncEvolutionActor();
                CollectMainBattlefieldPositions();
            }
            UpdateAttackFlashes();
            UpdateEnemyFlashes();
            UpdateAttackCues();
            var shell = GetComponent<OrdrMainSceneShell>();
            if (shell != null && shell.IsStartScreenVisible) return;
            HandleCameraNavigation();
            ReadWorldSelection(combat);
        }

        private void LateUpdate()
        {
            // The actual main camera must occupy the main battlefield only,
            // not the ribbon or the separate (currently placeholder) story area.
            if (worldCamera == null) worldCamera = Camera.main;
            if (worldCamera == null) return;
            Rect safe = Screen.safeArea;
            if (safe.width <= safe.height) return;
            var layout = MobileMapSkillLayout.Calculate(safe, Screen.height,
                MobileMapSkillLayout.UiScale(safe, Screen.dpi));
            Rect target = layout.MainCameraViewport(Screen.width, Screen.height);
            if (worldCamera.rect != target) worldCamera.rect = target;
            Vector2 center;
            float halfHeight;
            // Set the opening camera ONCE. A defense player's view must not
            // chase moving monsters or zoom as units are summoned or sold.
            if (!cameraFramed && MobileBattlefieldCameraFraming.TryFrame(framePoints,
                layout.Battle.width / layout.Battle.height, out center, out halfHeight))
            {
                var desired = new Vector3(center.x, center.y,
                    worldCamera.transform.position.z);
                worldCamera.transform.position = desired;
                worldCamera.orthographicSize = halfHeight;
                initialCameraHalfHeight = halfHeight;
                cameraFramed = true;
            }
            ApplyCenteredFormation(layout);
        }

        private bool TryBattleLayout(out MobileMapSkillLayout layout)
        {
            layout = null;
            Rect safe = Screen.safeArea;
            if (safe.width <= safe.height || worldCamera == null ||
                !worldCamera.orthographic || !cameraFramed) return false;
            layout = MobileMapSkillLayout.Calculate(safe, Screen.height,
                MobileMapSkillLayout.UiScale(safe, Screen.dpi));
            return true;
        }

        private static bool ContainsScreenPixel(MobileMapSkillLayout layout,
            Vector2 pixel)
        {
            return layout.Battle.Contains(new Vector2(pixel.x, Screen.height - pixel.y));
        }

        private void ApplyPan(Vector2 deltaPixels, MobileMapSkillLayout layout)
        {
            Vector2 result;
            Vector3 position = worldCamera.transform.position;
            if (MobileBattlefieldCameraNavigation.TryPan(position, deltaPixels,
                worldCamera.orthographicSize, layout.Battle.height, out result))
                worldCamera.transform.position = new Vector3(result.x, result.y, position.z);
        }

        private void ApplyZoom(float factor, Vector2 screenAnchor,
            MobileMapSkillLayout layout)
        {
            Vector2 center;
            float size;
            Vector3 position = worldCamera.transform.position;
            Vector2 viewportCenter = new Vector2(layout.Battle.center.x,
                Screen.height - layout.Battle.center.y);
            float minimum = Mathf.Max(40f, initialCameraHalfHeight * 0.4f);
            float maximum = Mathf.Max(minimum, initialCameraHalfHeight * 3f);
            if (!MobileBattlefieldCameraNavigation.TryZoom(position,
                worldCamera.orthographicSize, screenAnchor - viewportCenter,
                layout.Battle.height, factor, minimum, maximum, out center, out size))
                return;
            worldCamera.transform.position = new Vector3(center.x, center.y, position.z);
            worldCamera.orthographicSize = size;
        }

        private void HandleCameraNavigation()
        {
            suppressSelectionThisFrame = false;
            MobileMapSkillLayout layout;
            if (!TryBattleLayout(out layout)) return;
            if (Input.touchCount >= 2)
            {
                Touch first = Input.GetTouch(0);
                Touch second = Input.GetTouch(1);
                Vector2 centroid = (first.position + second.position) * 0.5f;
                Vector2 delta = (first.deltaPosition + second.deltaPosition) * 0.5f;
                Vector2 previousFirst = first.position - first.deltaPosition;
                Vector2 previousSecond = second.position - second.deltaPosition;
                float oldDistance = Vector2.Distance(previousFirst, previousSecond);
                float newDistance = Vector2.Distance(first.position, second.position);
                if (ContainsScreenPixel(layout, centroid))
                {
                    ApplyPan(delta, layout);
                    if (oldDistance > 2f && newDistance > 2f)
                        ApplyZoom(oldDistance / newDistance, centroid, layout);
                }
                multitouchGestureUsed = true;
                pointerDragged = true;
                suppressSelectionThisFrame = true;
                return;
            }

            if (Input.touchCount == 1)
            {
                Touch touch = Input.GetTouch(0);
                if (touch.phase == TouchPhase.Began)
                {
                    pointerStart = touch.position;
                    pointerBeganOverMap = ContainsScreenPixel(layout, touch.position) &&
                        !FormationContains(touch.position);
                    pointerDragged = false;
                }
                else if (touch.phase == TouchPhase.Moved && !multitouchGestureUsed)
                {
                    if ((touch.position - pointerStart).sqrMagnitude > 100f)
                        pointerDragged = true;
                    if (pointerDragged && pointerBeganOverMap) ApplyPan(touch.deltaPosition, layout);
                }
                if (touch.phase == TouchPhase.Ended || touch.phase == TouchPhase.Canceled)
                {
                    suppressSelectionThisFrame = pointerDragged || multitouchGestureUsed;
                    pointerBeganOverMap = false;
                }
                else if (pointerDragged || multitouchGestureUsed)
                    suppressSelectionThisFrame = true;
                return;
            }

            multitouchGestureUsed = false;
            Vector2 mouse = Input.mousePosition;
            if (Input.GetMouseButtonDown(0))
            {
                pointerStart = mouse;
                previousMouse = mouse;
                pointerBeganOverMap = ContainsScreenPixel(layout, mouse) && !FormationContains(mouse);
                pointerDragged = false;
            }
            if (Input.GetMouseButton(0))
            {
                if ((mouse - pointerStart).sqrMagnitude > 100f) pointerDragged = true;
                if (pointerDragged && pointerBeganOverMap) ApplyPan(mouse - previousMouse, layout);
            }
            previousMouse = mouse;
            if (Input.GetMouseButtonUp(0))
            {
                suppressSelectionThisFrame = pointerDragged;
                pointerBeganOverMap = false;
            }
            float scroll = Input.mouseScrollDelta.y;
            if (scroll != 0f && ContainsScreenPixel(layout, mouse))
                ApplyZoom(Mathf.Pow(0.85f, scroll), mouse, layout);
        }

        private void SyncEvolutionActor()
        {
            if (controller == null || controller.SharedEconomy == null ||
                !controller.StartingEvolutionActorId.HasValue)
            {
                if (evolutionMarker != null) evolutionMarker.SetActive(false);
                return;
            }
            long id = controller.StartingEvolutionActorId.Value;
            UnitState actor;
            try { actor = controller.SharedEconomy.Units.Get(id); }
            catch (KeyNotFoundException)
            {
                if (evolutionMarker != null) evolutionMarker.SetActive(false);
                return;
            }
            if (actor.UnitId != Original323SoloStartProfile.EvolutionRawcode)
            {
                if (evolutionMarker != null) evolutionMarker.SetActive(false);
                return;
            }
            if (evolutionMarker == null)
            {
                evolutionMarker = new GameObject("ORDR Starting Evolution Actor");
                evolutionMarker.transform.SetParent(transform, false);
                evolutionIcon = evolutionMarker.AddComponent<SpriteRenderer>();
                evolutionIcon.sprite = markerSprite;
                evolutionIcon.sortingOrder = 10;
            }
            evolutionMarker.SetActive(true);
            evolutionMarker.transform.position = new Vector3((float)actor.X,
                (float)actor.Y, 0f);
            bool selected = controller.SharedEconomy.Units.SelectedId == id;
            evolutionMarker.transform.localScale = Vector3.one *
                Mathf.Max(1f, markerDiameterWorldUnits) * (selected ? 1.05f : 0.82f);
            evolutionIcon.color = selected ? new Color(1f, 0.95f, 0.35f, 1f)
                : new Color(0.38f, 0.87f, 0.91f, 1f);
        }

        // Explicitly reveal an owned character's formation page. Actual actor
        // coordinates are never moved to match these presentation slots.
        public bool ShowOwnedUnit(long instanceId)
        {
            if (controller == null || controller.Combat == null ||
                controller.Combat.IsStoryDeployed(instanceId)) return false;
            SyncOwnedUnits(controller.Combat);
            for (int i = 0; i < formationGroups.Count; i++)
                if (formationGroups[i].Contains(instanceId))
                {
                    formationPage = formationLayout == null ? 0 : i / formationLayout.Capacity;
                    // Selecting a roster entry also reveals its nameplate page.
                    controller.Combat.Roster.Select(instanceId);
                    lastFormationSelection = null;
                    return true;
                }
            return false;
        }

        public bool ShowAllOwnedUnits()
        {
            if (controller == null || controller.Combat == null) return false;
            if (worldCamera == null) worldCamera = Camera.main;
            if (worldCamera == null) return false;
            var positions = new List<Vector2>();
            if (controller.PracticeBattle != null)
            {
                foreach (var point in controller.PracticeBattle.RouteCorners)
                    positions.Add(new Vector2((float)point.X, (float)point.Y));
                // Include every source Common spawn slot before the player
                // summons. This is a ONE-TIME/manual view, not camera chasing.
                for (int i = 0; i < 9; i++)
                    positions.Add(new Vector2((float)Original323SourceCommonWispWorld.OriginalCommonSpawnX(i),
                        (float)Original323SourceCommonWispWorld.OriginalCommonSpawnY));
            }
            foreach (var unit in controller.Combat.MobileUnitSnapshot())
                positions.Add(new Vector2((float)unit.Position.X, (float)unit.Position.Y));
            Rect safe = Screen.safeArea;
            if (safe.width <= safe.height) return false;
            var layout = MobileMapSkillLayout.Calculate(safe, Screen.height,
                MobileMapSkillLayout.UiScale(safe, Screen.dpi));
            Vector2 center;
            float halfHeight;
            if (!MobileBattlefieldCameraFraming.TryFrame(positions,
                layout.Battle.width / layout.Battle.height, out center, out halfHeight)) return false;
            Vector3 old = worldCamera.transform.position;
            worldCamera.transform.position = new Vector3(center.x, center.y, old.z);
            worldCamera.orthographicSize = halfHeight;
            initialCameraHalfHeight = halfHeight;
            cameraFramed = true;
            return true;
        }

        private void CollectMainBattlefieldPositions()
        {
            framePoints.Clear();
            if (controller != null && controller.PracticeBattle != null)
                foreach (var point in controller.PracticeBattle.RouteCorners)
                    framePoints.Add(new Vector2((float)point.X, (float)point.Y));
            if (controller != null && controller.Combat != null)
                foreach (var entry in controller.Combat.MobileUnitSnapshot())
                    if (!controller.Combat.IsStoryDeployed(entry.InstanceId))
                        framePoints.Add(new Vector2((float)entry.Position.X, (float)entry.Position.Y));
            foreach (var enemy in enemyVisuals.Values)
                if (enemy.Root.activeSelf) framePoints.Add(enemy.Root.transform.position);
            if (framePoints.Count == 0) framePoints.Add(Vector2.zero);
        }

        // A short presentation-only pulse replaces character swing/movement
        // animations. Verified hits and explicitly non-damaging map-timing
        // previews emit it; the flash never affects any combat calculation.
        private void OnCombatEvent(CharacterCombatEvent combatEvent)
        {
            if (combatEvent == null) return;
            if (combatEvent.Type == CharacterCombatEventType.EnemySpawned)
            {
                // The same enemy ID may be used in a later wave at a new position.
                if (!string.IsNullOrEmpty(combatEvent.EnemyId))
                    recentEnemyPositions.Remove(combatEvent.EnemyId);
                return;
            }
            if (combatEvent.Type == CharacterCombatEventType.SkillDamage ||
                combatEvent.Type == CharacterCombatEventType.StatusApplied ||
                combatEvent.Type == CharacterCombatEventType.ArmorBreakApplied)
            {
                // Only ACTUAL resolved impact/status events flash a victim.
                EnemyVisual hit;
                if (!string.IsNullOrEmpty(combatEvent.EnemyId) &&
                    enemyVisuals.TryGetValue(combatEvent.EnemyId, out hit))
                    hit.HitAt = Time.unscaledTime;
                return;
            }
            if (combatEvent.Type != CharacterCombatEventType.Attack &&
                combatEvent.Type != CharacterCombatEventType.AttackPreview &&
                combatEvent.Type != CharacterCombatEventType.SkillActivated)
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
            visual.IsSkillFlash = combatEvent.Type == CharacterCombatEventType.SkillActivated;
            visual.AttackFlash.enabled = true;
            visual.AttackFlash.color = visual.IsSkillFlash
                ? new Color(0.70f, 0.51f, 1f, 0.8f)
                : visual.IsPreviewFlash
                    ? new Color(0.62f, 0.85f, 1f, 0.8f)
                    : new Color(1f, 0.88f, 0.3f, 0.8f);
            // A confirmed cast is not proof of HP damage. The actual impact
            // arrives as SkillDamage/StatusApplied/ArmorBreakApplied.
            if (visual.IsSkillFlash) return;

            Vector3 target;
            if (!string.IsNullOrEmpty(combatEvent.EnemyId) &&
                TryFindEnemyPosition(combatEvent.EnemyId, out target))
            {
                bool preview = visual.IsPreviewFlash;
                DamageSchool verifiedSchool;
                DamageSchool? school = !preview && controller.Combat != null &&
                    controller.Combat.TryGetVerifiedAttackSchool(instanceId, out verifiedSchool)
                    ? (DamageSchool?)verifiedSchool : null;
                SpawnAttackCue(BasicAttackCueStyle.Choose(visual.OriginalWeaponType, school),
                    preview, visual.Root.transform.position, target);
                EnemyVisual enemy;
                if (enemyVisuals.TryGetValue(combatEvent.EnemyId, out enemy))
                    enemy.HitAt = Time.unscaledTime;
            }
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
                visual.AttackFlash.color = visual.IsSkillFlash
                    ? new Color(0.70f, 0.51f, 1f, 0.8f * remaining)
                    : visual.IsPreviewFlash
                        ? new Color(0.62f, 0.85f, 1f, 0.8f * remaining)
                        : new Color(1f, 0.88f, 0.3f, 0.8f * remaining);
            }
        }

        private void SyncEnemies(CharacterCombatRuntime combat)
        {
            var active = new HashSet<string>(StringComparer.Ordinal);
            foreach (var entry in combat.MobileEnemySnapshot())
            {
                active.Add(entry.Id);
                Vector3 position = new Vector3((float)entry.X, (float)entry.Y, 0f);
                LastEnemyPosition last;
                if (!recentEnemyPositions.TryGetValue(entry.Id, out last))
                {
                    last = new LastEnemyPosition();
                    recentEnemyPositions.Add(entry.Id, last);
                }
                last.Position = position;
                last.LastSeenAt = Time.unscaledTime;

                EnemyVisual enemy;
                if (!enemyVisuals.TryGetValue(entry.Id, out enemy))
                {
                    enemy = new EnemyVisual();
                    enemy.Root = new GameObject("ORDR Enemy " + entry.Id);
                    enemy.Root.transform.SetParent(transform, false);
                    enemy.Icon = enemy.Root.AddComponent<SpriteRenderer>();
                    enemy.Icon.sprite = markerSprite;
                    enemy.Icon.sortingOrder = 7;
                    var barBack = new GameObject("HP background");
                    barBack.transform.SetParent(enemy.Root.transform, false);
                    enemy.HealthBarBack = barBack.AddComponent<SpriteRenderer>();
                    enemy.HealthBarBack.sprite = healthBarSprite;
                    enemy.HealthBarBack.color = new Color(0.12f, 0.15f, 0.2f, 0.95f);
                    enemy.HealthBarBack.sortingOrder = 11;
                    barBack.transform.localPosition = new Vector3(0f, 0.70f, 0f);
                    barBack.transform.localScale = new Vector3(1.18f, 0.16f, 1f);
                    var barFill = new GameObject("HP actual fill");
                    barFill.transform.SetParent(enemy.Root.transform, false);
                    enemy.HealthBarFill = barFill.AddComponent<SpriteRenderer>();
                    enemy.HealthBarFill.sprite = healthBarSprite;
                    enemy.HealthBarFill.sortingOrder = 12;
                    enemy.BaseColor = entry.Kind == CharacterEnemyKind.Boss
                        ? new Color(1f, 0.34f, 0.23f, 1f)
                        : new Color(0.90f, 0.36f, 0.37f, 1f);
                    enemy.Icon.color = enemy.BaseColor;
                    enemyVisuals.Add(entry.Id, enemy);
                }
                enemy.Root.transform.position = position;
                enemy.Root.transform.localScale = Vector3.one *
                    Mathf.Max(1f, markerDiameterWorldUnits) * 0.57f;
                // MaxHealth is from the live CombatEnemy, never from a
                // display-only estimated damage or a preview attack event.
                float ratio = Mathf.Clamp01((float)((double)entry.Health / entry.MaxHealth));
                enemy.HealthBarFill.transform.localPosition = new Vector3(
                    -0.56f * (1f - ratio), 0.70f, 0f);
                enemy.HealthBarFill.transform.localScale = new Vector3(
                    1.12f * ratio, 0.11f, 1f);
                enemy.HealthBarFill.color = Color.Lerp(
                    new Color(0.94f, 0.30f, 0.28f),
                    new Color(0.32f, 0.90f, 0.47f), ratio);
            }

            var gone = new List<string>();
            foreach (var pair in enemyVisuals)
                if (!active.Contains(pair.Key))
                {
                    Destroy(pair.Value.Root);
                    gone.Add(pair.Key);
                }
            foreach (string id in gone) enemyVisuals.Remove(id);

            gone.Clear();
            foreach (var pair in recentEnemyPositions)
                if (!active.Contains(pair.Key) &&
                    Time.unscaledTime - pair.Value.LastSeenAt > 0.7f)
                    gone.Add(pair.Key);
            foreach (string id in gone) recentEnemyPositions.Remove(id);
        }

        private bool TryFindEnemyPosition(string enemyId, out Vector3 position)
        {
            LastEnemyPosition recent;
            if (recentEnemyPositions.TryGetValue(enemyId, out recent))
            {
                position = recent.Position;
                return true;
            }
            // A new enemy may be attacked before the next visual refresh.
            if (controller != null && controller.Combat != null)
                SyncEnemies(controller.Combat);
            if (recentEnemyPositions.TryGetValue(enemyId, out recent))
            {
                position = recent.Position;
                return true;
            }
            position = Vector3.zero;
            return false;
        }

        private void UpdateEnemyFlashes()
        {
            foreach (var enemy in enemyVisuals.Values)
            {
                float progress = (Time.unscaledTime - enemy.HitAt) / 0.16f;
                if (enemy.HitAt < 0f || progress >= 1f)
                {
                    enemy.Icon.color = enemy.BaseColor;
                    continue;
                }
                enemy.Icon.color = Color.Lerp(Color.white, enemy.BaseColor,
                    Mathf.Clamp01(progress));
            }
        }

        private AttackCueVisual GetAvailableCue()
        {
            foreach (var existing in attackCues)
                if (!existing.Root.activeSelf) return existing;

            if (attackCues.Count >= Mathf.Clamp(maximumSimultaneousAttackCues, 1, 128))
            {
                // The oldest cue can be recycled during a very dense attack burst.
                // This only drops visual frames, never an actual combat event.
                AttackCueVisual oldest = attackCues[0];
                foreach (var cue in attackCues)
                    if (cue.StartedAt < oldest.StartedAt) oldest = cue;
                return oldest;
            }

            var created = new AttackCueVisual();
            created.Root = new GameObject("ORDR Basic Attack Cue");
            created.Root.transform.SetParent(transform, false);
            created.Body = created.Root.AddComponent<SpriteRenderer>();
            created.Body.sprite = markerSprite;
            created.Body.sortingOrder = 18;
            var impact = new GameObject("Impact");
            impact.transform.SetParent(created.Root.transform, false);
            created.Impact = impact.AddComponent<SpriteRenderer>();
            created.Impact.sprite = markerSprite;
            created.Impact.sortingOrder = 17;
            attackCues.Add(created);
            return created;
        }

        private void SpawnAttackCue(BasicAttackCueKind kind, bool preview,
            Vector3 attacker, Vector3 target)
        {
            var cue = GetAvailableCue();
            cue.Root.SetActive(true);
            cue.Kind = kind;
            cue.From = attacker;
            cue.To = target;
            cue.StartedAt = Time.unscaledTime;
            cue.Tint = preview ? new Color(0.58f, 0.80f, 1f, 0.60f)
                : kind == BasicAttackCueKind.Magic
                    ? new Color(0.73f, 0.47f, 1f, 0.90f)
                    : new Color(1f, 0.87f, 0.29f, 0.92f);
            UpdateAttackCue(cue, 0f);
        }

        private void UpdateAttackCues()
        {
            float duration = Mathf.Max(0.04f, attackCueDurationSeconds);
            foreach (var cue in attackCues)
            {
                if (!cue.Root.activeSelf) continue;
                float progress = (Time.unscaledTime - cue.StartedAt) / duration;
                if (progress >= 1f)
                {
                    cue.Root.SetActive(false);
                    continue;
                }
                UpdateAttackCue(cue, Mathf.Clamp01(progress));
            }
        }

        private void UpdateAttackCue(AttackCueVisual cue, float progress)
        {
            float size = Mathf.Max(1f, markerDiameterWorldUnits);
            float fade = 1f - progress;
            var tint = cue.Tint;
            tint.a *= fade;
            cue.Body.color = tint;
            cue.Body.transform.rotation = Quaternion.identity;

            if (cue.Kind == BasicAttackCueKind.Slash)
            {
                cue.Body.transform.position = cue.To;
                cue.Body.transform.rotation = Quaternion.Euler(0f, 0f, 40f);
                cue.Body.transform.localScale = new Vector3(
                    size * (0.52f + 0.25f * progress),
                    size * 0.075f * fade, 1f);
                cue.Body.enabled = true;
                cue.Impact.enabled = false;
                return;
            }

            float flight = Mathf.Clamp01(progress / 0.72f);
            cue.Body.transform.position = Vector3.Lerp(cue.From, cue.To, flight);
            float dot = cue.Kind == BasicAttackCueKind.Magic ? 0.21f : 0.13f;
            cue.Body.transform.localScale = Vector3.one * (size * dot);
            cue.Body.enabled = flight < 1f;
            cue.Impact.enabled = progress >= 0.72f;
            if (cue.Impact.enabled)
            {
                float hit = (progress - 0.72f) / 0.28f;
                cue.Impact.transform.position = cue.To;
                cue.Impact.transform.localScale = Vector3.one * size *
                    Mathf.Lerp(0.16f, cue.Kind == BasicAttackCueKind.Magic ? 0.80f : 0.44f,
                        hit);
                var color = tint;
                color.a *= 1f - hit;
                cue.Impact.color = color;
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
            healthBarTexture = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            healthBarTexture.SetPixel(0, 0, Color.white);
            healthBarTexture.Apply();
            healthBarSprite = Sprite.Create(healthBarTexture,
                new Rect(0, 0, 1, 1), new Vector2(0.5f, 0.5f), 1f);
        }

        private void SyncOwnedUnits(CharacterCombatRuntime combat)
        {
            var entries = combat.MobileUnitSnapshot();
            formationGroups = MobileOwnedUnitFormation.Group(entries,
                id => !combat.IsStoryDeployed(id));
            MainOwnedUnitCount = 0;
            foreach (var group in formationGroups) MainOwnedUnitCount += group.InstanceIds.Count;
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
                    var basic = controller != null && controller.MapBaseStats != null
                        ? controller.MapBaseStats.Find(entry.Unit.Id) : null;
                    visual.OriginalWeaponType = basic != null && basic.Attack1 != null
                        ? basic.Attack1.OriginalWeaponType : null;
                    visuals.Add(entry.InstanceId, visual);
                }

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
                if (combat.IsStoryDeployed(entry.InstanceId) && visual.Root.activeSelf)
                    visual.Root.SetActive(false);
            }

            var expired = new List<long>();
            foreach (var item in visuals)
                if (!alive.Contains(item.Key))
                {
                    Destroy(item.Value.Root);
                    expired.Add(item.Key);
                }
            foreach (long id in expired) visuals.Remove(id);
            Rect safe = Screen.safeArea;
            if (safe.width > safe.height && worldCamera != null)
                ApplyCenteredFormation(MobileMapSkillLayout.Calculate(safe, Screen.height,
                    MobileMapSkillLayout.UiScale(safe, Screen.dpi)));
        }

        private void ApplyCenteredFormation(MobileMapSkillLayout layout)
        {
            if (worldCamera == null || !worldCamera.orthographic) return;
            float scale = MobileMapSkillLayout.UiScale(Screen.safeArea, Screen.dpi);
            formationLayout = MobileOwnedFormationLayout.Calculate(layout.Battle, scale,
                formationGroups.Count, formationPage);
            long? selected = controller == null || controller.Combat == null
                ? null : controller.Combat.Roster.SelectedId;
            if (selected != lastFormationSelection)
            {
                for (int i = 0; i < formationGroups.Count; i++)
                    if (formationGroups[i].Contains(selected))
                    { formationPage = i / formationLayout.Capacity; break; }
                lastFormationSelection = selected;
                formationLayout = MobileOwnedFormationLayout.Calculate(layout.Battle, scale,
                    formationGroups.Count, formationPage);
            }
            formationPage = formationLayout.Page;
            formationVisibleIds.Clear();
            for (int i = 0; i < formationLayout.Cells.Length; i++)
                foreach (long id in formationGroups[formationLayout.Start + i].InstanceIds)
                    formationVisibleIds.Add(id);
            foreach (var pair in visuals)
            {
                bool visible = formationVisibleIds.Contains(pair.Key);
                if (pair.Value.Root.activeSelf != visible) pair.Value.Root.SetActive(visible);
            }
            for (int i = 0; i < formationLayout.Cells.Length; i++)
            {
                var group = formationGroups[formationLayout.Start + i];
                Rect cell = formationLayout.Cells[i];
                Vector2 icon = new Vector2(cell.x + 16f * scale, cell.center.y);
                Vector3 position = worldCamera.ScreenToWorldPoint(new Vector3(icon.x,
                    Screen.height - icon.y, worldCamera.WorldToScreenPoint(Vector3.zero).z));
                position.z = 0f;
                float size = 23f * scale * worldCamera.orthographicSize * 2f /
                    Mathf.Max(1f, worldCamera.pixelHeight);
                long representative = group.Contains(selected) ? selected.Value : group.InstanceIds[0];
                foreach (long id in group.InstanceIds)
                {
                    Visual visual;
                    if (!visuals.TryGetValue(id, out visual)) continue;
                    visual.Root.transform.position = position;
                    visual.Root.transform.localScale = new Vector3(size, size, 1f);
                    visual.Icon.enabled = id == representative;
                }
            }
        }

        private bool FormationContains(Vector2 screen)
        {
            if (formationLayout == null) return false;
            Vector2 point = new Vector2(screen.x, Screen.height - screen.y);
            if (formationLayout.Footer.Contains(point)) return true;
            foreach (Rect cell in formationLayout.Cells) if (cell.Contains(point)) return true;
            return false;
        }

        private void OnGUI()
        {
            var shell = GetComponent<OrdrMainSceneShell>();
            if (shell != null && shell.IsStartScreenVisible) return;
            if (controller == null || controller.Combat == null || formationLayout == null ||
                Screen.safeArea.width <= Screen.safeArea.height) return;
            GUISkin old = OrdrMobileUiTheme.Activate();
            try
            {
                float scale = MobileMapSkillLayout.UiScale(Screen.safeArea, Screen.dpi);
                var nameStyle = new GUIStyle(GUI.skin.label) { fontSize = Mathf.RoundToInt(13f * scale),
                    fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleLeft,
                    wordWrap = true, richText = false, clipping = TextClipping.Clip,
                    padding = new RectOffset(0, 0, 0, 0) };
                var metaStyle = new GUIStyle(nameStyle) { fontSize = Mathf.RoundToInt(10f * scale),
                    wordWrap = false, fontStyle = FontStyle.Normal };
                long? selected = controller.Combat.Roster.SelectedId;
                for (int i = 0; i < formationLayout.Cells.Length; i++)
                {
                    var group = formationGroups[formationLayout.Start + i];
                    Rect cell = formationLayout.Cells[i];
                    bool chosen = group.Contains(selected);
                    // Leave the world marker/attack pulse visible to the left.
                    var label = new Rect(cell.x + 30f * scale, cell.y,
                        cell.width - 30f * scale, cell.height);
                    GUI.Box(label, GUIContent.none, chosen ? OrdrMobileUiTheme.SelectedTab : GUI.skin.box);
                    var text = new Rect(label.x + 4f * scale, label.y + 2f * scale,
                        label.width - 8f * scale, label.height - 15f * scale);
                    nameStyle.fontSize = Mathf.RoundToInt(13f * scale);
                    int smallest = Mathf.RoundToInt(10f * scale);
                    while (nameStyle.fontSize > smallest &&
                        nameStyle.CalcHeight(new GUIContent(group.Unit.Name), text.width) > text.height)
                        nameStyle.fontSize--;
                    GUI.Label(text, group.Unit.Name, nameStyle);
                    metaStyle.normal.textColor = ColorForGrade(group.Unit.Grade);
                    GUI.Label(new Rect(text.x, label.yMax - 14f * scale, text.width, 12f * scale),
                        group.Unit.Grade + " ×" + group.InstanceIds.Count, metaStyle);
                }
                Rect footer = formationLayout.Footer;
                metaStyle.alignment = TextAnchor.MiddleCenter;
                metaStyle.normal.textColor = Color.white;
                if (formationLayout.Pages == 1 && selected.HasValue)
                    foreach (var group in formationGroups)
                        if (group.Contains(selected))
                            GUI.Label(footer, group.Unit.Name + " · #" + selected.Value + " · 선택됨", metaStyle);
                if (formationLayout.Pages > 1)
                {
                    float buttonWidth = Mathf.Min(68f * scale, footer.width * .27f);
                    var button = new GUIStyle(GUI.skin.button) { fontSize = Mathf.RoundToInt(12f * scale),
                        padding = new RectOffset(2, 2, 2, 2) };
                    bool enabled = GUI.enabled;
                    GUI.enabled = enabled && formationPage > 0;
                    if (GUI.Button(new Rect(footer.x, footer.y, buttonWidth, footer.height), "이전", button))
                        formationPage--;
                    GUI.enabled = enabled && formationPage + 1 < formationLayout.Pages;
                    if (GUI.Button(new Rect(footer.xMax - buttonWidth, footer.y, buttonWidth, footer.height), "다음", button))
                        formationPage++;
                    GUI.enabled = enabled;
                    metaStyle.alignment = TextAnchor.MiddleCenter;
                    metaStyle.normal.textColor = Color.white;
                    GUI.Label(new Rect(footer.x + buttonWidth, footer.y,
                        footer.width - buttonWidth * 2, footer.height),
                        (formationPage + 1) + "/" + formationLayout.Pages + " · " + formationGroups.Count + "종", metaStyle);
                }
            }
            finally { GUI.skin = old; }
        }

        private void ReadWorldSelection(CharacterCombatRuntime combat)
        {
            if (suppressSelectionThisFrame || multitouchGestureUsed) return;
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

            // Only the main map accepts direct unit selection. The skill
            // ribbon, story field and bottom controls must never click through.
            Rect safe = Screen.safeArea;
            if (safe.width <= safe.height) return;
            float dpi = Screen.dpi > 0f ? Screen.dpi : 160f;
            var layout = MobileMapSkillLayout.Calculate(safe, Screen.height,
                MobileMapSkillLayout.UiScale(safe, dpi));
            Vector2 onGui = new Vector2(screen.x, Screen.height - screen.y);
            if (!layout.Battle.Contains(onGui)) return;
            // The right-side tab content overlays the main battlefield;
            // never select a unit through its buttons.
            var touchPanel = GetComponent<OrdrMobileTouchPanel>();
            if (touchPanel != null && touchPanel.BlocksWorldTap(onGui)) return;

            if (formationLayout != null)
            {
                if (formationLayout.Footer.Contains(onGui)) return;
                for (int i = 0; i < formationLayout.Cells.Length; i++)
                    if (formationLayout.Cells[i].Contains(onGui))
                    {
                        var group = formationGroups[formationLayout.Start + i];
                        // A sale/fusion may have committed since the 0.12s
                        // display snapshot. Select from the live group only.
                        foreach (var live in MobileOwnedUnitFormation.Group(combat.MobileUnitSnapshot(),
                            id => !combat.IsStoryDeployed(id)))
                            if (live.Unit.Id == group.Unit.Id)
                            { combat.Roster.Select(live.NextSelection(combat.Roster.SelectedId)); break; }
                        nextRefresh = 0f;
                        return;
                    }
            }
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
            if (evolutionMarker != null && evolutionMarker.activeSelf &&
                controller != null && controller.StartingEvolutionActorId.HasValue)
            {
                Vector3 visible = worldCamera.WorldToScreenPoint(
                    evolutionMarker.transform.position);
                float dx = visible.x - screen.x;
                float dy = visible.y - screen.y;
                float d2 = dx * dx + dy * dy;
                if (visible.z > 0f && d2 <= nearestPixels2)
                {
                    nearestId = controller.StartingEvolutionActorId.Value;
                    nearestPixels2 = d2;
                }
            }
            if (nearestId != 0)
            {
                try { combat.Roster.Select(nearestId); }
                catch (KeyNotFoundException) { nextRefresh = 0f; }
            }
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
            foreach (var item in enemyVisuals.Values)
                if (item.Root != null) Destroy(item.Root);
            enemyVisuals.Clear();
            recentEnemyPositions.Clear();
            foreach (var cue in attackCues)
                if (cue.Root != null) Destroy(cue.Root);
            attackCues.Clear();
            if (evolutionMarker != null) Destroy(evolutionMarker);
            if (markerSprite != null) Destroy(markerSprite);
            if (markerTexture != null) Destroy(markerTexture);
            if (healthBarSprite != null) Destroy(healthBarSprite);
            if (healthBarTexture != null) Destroy(healthBarTexture);
        }
    }
}
