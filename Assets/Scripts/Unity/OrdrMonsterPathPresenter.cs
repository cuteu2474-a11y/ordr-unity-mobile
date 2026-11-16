using System;
using System.Collections.Generic;
using UnityEngine;
using Ordr.Mobile.Combat;

namespace Ordr.Mobile.Unity
{
    // The current SourceModelPracticeBattle actually uses RouteCorners as
    // its route, including the closing corner. No route is shown in strict
    // native/observed mode until its own authoritative geometry is available.
    [DisallowMultipleComponent]
    public sealed class OrdrMonsterPathPresenter : MonoBehaviour
    {
        private static readonly Color Shoulder = new Color(.05f, .10f, .16f, .97f);
        private static readonly Color Brass = new Color(.53f, .42f, .23f, .93f);
        private static readonly Color Lane = new Color(.19f, .35f, .39f, .95f);
        private static readonly Color Dashed = new Color(.70f, .76f, .58f, .74f);
        private const float ArrowSpacing = 320f;
        private readonly List<GameObject> generated = new List<GameObject>();
        private IReadOnlyList<CombatPathPoint> source;
        public bool HasPath { get { return generated.Count > 0; } }
        public int SegmentCount { get; private set; }

        public void ShowPracticeRoute(IReadOnlyList<CombatPathPoint> corners)
        {
            if (ReferenceEquals(source, corners) && HasPath) return;
            // Validate fully BEFORE clearing the former route; never allow
            // partial invalid geometry to masquerade as a real battlefield.
            var geometry = OrdrMonsterPathGeometry.Build(corners, true);
            Clear();
            source = corners;
            try
            {
                foreach (var segment in geometry.Segments)
                {
                    AddRoad(segment, OrdrBattlefieldVisualSizing.ShoulderWidth,
                        Shoulder, -94, "path shoulder");
                    AddRoad(segment, OrdrBattlefieldVisualSizing.BorderWidth,
                        Brass, -93, "route border");
                    AddRoad(segment, OrdrBattlefieldVisualSizing.LaneWidth,
                        Lane, -92, "monster lane");
                    int marks = Mathf.Min(24,
                        Mathf.Max(1, Mathf.FloorToInt(segment.Length / ArrowSpacing)));
                    for (int i = 0; i < marks; i++)
                    {
                        float position = (i + .5f) / marks;
                        AddDirectionalArrow(segment.At(position), segment.AngleDegrees);
                    }
                    SegmentCount++;
                }
                // Rounded elbows cover cracks without changing path geometry.
                for (int i = 0; i < geometry.Segments.Count; i++)
                    AddCircle(geometry.Segments[i].Start,
                        OrdrBattlefieldVisualSizing.LaneWidth, -91,
                        Lane, "path turn");
                // Practice routes loop back to their spawn. There is NO fake
                // destination/escape marker on a looping monster lane.
                AddMarker(geometry.Start, OrdrBattlefieldArt.PathSpawnSprite,
                    OrdrBattlefieldVisualSizing.SpawnMarkerDiameter,
                    -88, "monster spawn loop origin");
            }
            catch
            {
                Clear();
                throw;
            }
        }

        public void Clear()
        {
            source = null;
            SegmentCount = 0;
            foreach (var go in generated)
                if (go != null)
                {
                    if (Application.isPlaying) Destroy(go);
                    else DestroyImmediate(go);
                }
            generated.Clear();
        }

        private void AddRoad(OrdrMonsterPathSegment part, float height,
            Color color, int sorting, string name)
        {
            var go = Create(name);
            go.transform.position = new Vector3(part.Middle.x, part.Middle.y, 0f);
            go.transform.rotation = Quaternion.Euler(0f, 0f, part.AngleDegrees);
            go.transform.localScale = new Vector3(part.Length, height, 1f);
            var sprite = go.AddComponent<SpriteRenderer>();
            sprite.sprite = OrdrBattlefieldArt.PathFillSprite;
            sprite.color = color;
            sprite.sortingOrder = sorting;
        }

        private void AddCircle(Vector2 point, float diameter, int sorting,
            Color tint, string name)
        {
            var go = Create(name);
            go.transform.position = new Vector3(point.x, point.y, 0f);
            go.transform.localScale = Vector3.one * diameter;
            var sprite = go.AddComponent<SpriteRenderer>();
            sprite.sprite = OrdrBattlefieldArt.PathCircleSprite;
            sprite.sortingOrder = sorting;
            sprite.color = tint;
        }

        private void AddDirectionalArrow(Vector2 position, float angle)
        {
            var go = Create("monster route arrow");
            go.transform.position = new Vector3(position.x, position.y, 0f);
            go.transform.rotation = Quaternion.Euler(0f, 0f, angle);
            go.transform.localScale = Vector3.one *
                OrdrBattlefieldVisualSizing.ArrowDiameter;
            var sprite = go.AddComponent<SpriteRenderer>();
            sprite.sprite = OrdrBattlefieldArt.PathArrowSprite;
            sprite.color = Dashed;
            sprite.sortingOrder = -90;
        }

        private void AddMarker(Vector2 pos, Sprite icon, float diameter,
            int sorting, string name)
        {
            var go = Create(name);
            go.transform.position = new Vector3(pos.x, pos.y, 0f);
            go.transform.localScale = Vector3.one * diameter;
            var sprite = go.AddComponent<SpriteRenderer>();
            sprite.sprite = icon;
            sprite.sortingOrder = sorting;
        }

        private GameObject Create(string name)
        {
            var go = new GameObject("ORDR " + name);
            go.transform.SetParent(transform, false);
            generated.Add(go);
            return go;
        }

        private void OnDestroy()
        {
            Clear();
        }
    }
}
