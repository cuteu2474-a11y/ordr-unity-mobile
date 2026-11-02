using System;
using System.Collections.Generic;
using UnityEngine;
using Ordr.Mobile.Combat;

namespace Ordr.Mobile.Unity
{
    // World-space presentation only. Never feeds monster simulation or the
    // original native movement rules. Caller must supply its ACTIVE route.
    public sealed class OrdrMonsterPathSegment
    {
        public Vector2 Start { get; private set; }
        public Vector2 End { get; private set; }
        public float Length { get; private set; }
        public float AngleDegrees { get; private set; }
        public Vector2 Middle { get { return (Start + End) * .5f; } }

        internal OrdrMonsterPathSegment(Vector2 a, Vector2 b)
        {
            Start = a; End = b;
            var delta = b - a;
            Length = delta.magnitude;
            if (Length <= 0 || float.IsNaN(Length) || float.IsInfinity(Length))
                throw new ArgumentException("이동로에 길이가 없는 구간이 있습니다.");
            AngleDegrees = Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg;
        }

        public Vector2 At(float fraction) => Vector2.Lerp(Start, End, Mathf.Clamp01(fraction));
    }

    public sealed class OrdrMonsterPathGeometry
    {
        private readonly List<OrdrMonsterPathSegment> segments;
        public IReadOnlyList<OrdrMonsterPathSegment> Segments
        { get { return segments.AsReadOnly(); } }
        public bool IsLoop { get; private set; }
        public Vector2 Start => segments[0].Start;
        public Vector2 End => segments[segments.Count - 1].End;

        private OrdrMonsterPathGeometry(List<OrdrMonsterPathSegment> rows, bool loop)
        {
            segments = rows;
            IsLoop = loop;
        }

        // The practice model repeats its four routeCenters indefinitely.
        // A different future, source-verified route may be non-looping, but
        // no observed live positions are converted into imaginary corners.
        public static OrdrMonsterPathGeometry Build(
            IReadOnlyList<CombatPathPoint> corners, bool loop)
        {
            if (corners == null) throw new ArgumentNullException(nameof(corners));
            if (corners.Count < 2 || corners.Count > 128)
                throw new ArgumentException("경로 지점이 2~128개여야 합니다.", nameof(corners));
            var points = new List<Vector2>(corners.Count);
            foreach (var point in corners)
            {
                if (point == null || double.IsNaN(point.X) || double.IsInfinity(point.X) ||
                    double.IsNaN(point.Y) || double.IsInfinity(point.Y) ||
                    Math.Abs(point.X) > 10000000 || Math.Abs(point.Y) > 10000000)
                    throw new ArgumentException("원본 이동 경로 좌표가 올바르지 않습니다.");
                points.Add(new Vector2((float)point.X, (float)point.Y));
            }
            var result = new List<OrdrMonsterPathSegment>();
            for (int i = 1; i < points.Count; i++)
                result.Add(new OrdrMonsterPathSegment(points[i - 1], points[i]));
            if (loop) result.Add(new OrdrMonsterPathSegment(points[points.Count - 1], points[0]));
            return new OrdrMonsterPathGeometry(result, loop);
        }
    }
}
