using System;
using System.Collections.Generic;

namespace Ordr.Mobile.Core
{
    // 렌더링·Unity 물리와 무관한 경로 보간. 실제 경로 좌표와 속도는 원작 확인 후 주입한다.
    public readonly struct PathPoint
    {
        public readonly double X;
        public readonly double Y;
        public PathPoint(double x, double y) { X = x; Y = y; }
    }

    public sealed class PathProgress
    {
        private readonly PathPoint[] points;
        private readonly double[] cumulative;
        public double TotalLength { get; }

        public PathProgress(IReadOnlyList<PathPoint> path)
        {
            if (path == null || path.Count < 2) throw new ArgumentException("경로에는 점이 두 개 이상 필요합니다.");
            points = new PathPoint[path.Count];
            cumulative = new double[path.Count];
            for (int i = 0; i < path.Count; i++)
            {
                points[i] = path[i];
                if (i == 0) continue;
                double dx = points[i].X - points[i - 1].X;
                double dy = points[i].Y - points[i - 1].Y;
                double distance = Math.Sqrt(dx * dx + dy * dy);
                if (double.IsNaN(distance) || double.IsInfinity(distance) || distance <= 0)
                    throw new ArgumentException("경로 구간은 유효한 양수 길이여야 합니다.");
                cumulative[i] = cumulative[i - 1] + distance;
            }
            TotalLength = cumulative[cumulative.Length - 1];
        }

        public PathPoint PositionAt(double distance)
        {
            if (double.IsNaN(distance)) throw new ArgumentOutOfRangeException(nameof(distance));
            double d = Math.Max(0, Math.Min(distance, TotalLength));
            for (int i = 1; i < cumulative.Length; i++)
            {
                if (d > cumulative[i] && i < cumulative.Length - 1) continue;
                double fraction = (d - cumulative[i - 1]) / (cumulative[i] - cumulative[i - 1]);
                return new PathPoint(
                    points[i - 1].X + (points[i].X - points[i - 1].X) * fraction,
                    points[i - 1].Y + (points[i].Y - points[i - 1].Y) * fraction);
            }
            return points[points.Length - 1];
        }
    }
}
