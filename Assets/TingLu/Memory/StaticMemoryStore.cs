using System;
using System.Collections.Generic;
using TingLu.Contracts;

namespace TingLu.Memory
{
    /// <summary>Owns frozen, world-space contour fragments. It never retains world objects.</summary>
    public sealed class StaticMemoryStore : IMemoryCheckpoint
    {
        private readonly List<StaticMemorySnapshot> fragments = new List<StaticMemorySnapshot>();
        public IReadOnlyList<StaticMemorySnapshot> Fragments { get { return fragments.AsReadOnly(); } }

        public IReadOnlyList<StaticMemorySnapshot> Export() { return Clone(fragments); }
        public void Restore(IReadOnlyList<StaticMemorySnapshot> values)
        {
            if (values == null) throw new ArgumentNullException("values");
            fragments.Clear();
            foreach (StaticMemorySnapshot item in values)
                if (item != null) fragments.Add(Clone(item));
        }

        public void Observe(Observation observation)
        {
            if (observation == null || observation.Category != ObjectCategory.Static ||
                observation.MemoryPolicy != MemoryPolicy.ConfirmedStaticGeometry ||
                observation.Kind == InformationKind.SoundLocation || observation.ConfirmedContour.Count == 0)
                return;
            fragments.Add(new StaticMemorySnapshot(Guid.NewGuid().ToString("N"), observation));
        }

        public void Clear(ProbeCoverage coverage)
        {
            if (coverage == null || coverage.Cells.Count == 0 || fragments.Count == 0) return;
            var kept = new List<StaticMemorySnapshot>();
            foreach (StaticMemorySnapshot old in fragments)
            {
                var outside = new List<Segment2>();
                foreach (Segment2 segment in old.WorldContour)
                    outside.AddRange(OutsideCoverage(segment, coverage));
                if (outside.Count == 0) continue;
                foreach (List<Segment2> part in ConnectedRuns(outside))
                {
                    if (part.Count == 0) continue;
                    Observation copy = new Observation(old.ObjectId, ObjectCategory.Static,
                        MemoryPolicy.ConfirmedStaticGeometry, old.Provenance, InformationKind.LocalGeometry,
                        part[0].A, part, null, old.ObservedAt, old.ObservedAt, old.Semantic);
                    kept.Add(new StaticMemorySnapshot(Guid.NewGuid().ToString("N"), copy));
                }
            }
            fragments.Clear(); fragments.AddRange(kept);
        }

        private static IReadOnlyList<StaticMemorySnapshot> Clone(IEnumerable<StaticMemorySnapshot> source)
        {
            var result = new List<StaticMemorySnapshot>();
            foreach (StaticMemorySnapshot item in source)
            {
                Observation copy = new Observation(item.ObjectId, ObjectCategory.Static,
                    MemoryPolicy.ConfirmedStaticGeometry, item.Provenance, InformationKind.LocalGeometry,
                    item.WorldContour[0].A, item.WorldContour, null, item.ObservedAt, item.ObservedAt, item.Semantic);
                result.Add(new StaticMemorySnapshot(item.FragmentId, copy));
            }
            return result.AsReadOnly();
        }
        private static StaticMemorySnapshot Clone(StaticMemorySnapshot item) { return Clone(new[] { item })[0]; }

        private static List<Segment2> OutsideCoverage(Segment2 line, ProbeCoverage coverage)
        {
            var cuts = new List<float> { 0f, 1f };
            foreach (ProbeCell cell in coverage.Cells)
                for (int i = 0; i < cell.Polygon.Count; i++)
                {
                    float t;
                    if (IntersectionParameter(line.A, line.B, cell.Polygon[i], cell.Polygon[(i + 1) % cell.Polygon.Count], out t))
                        cuts.Add(t);
                }
            cuts.Sort();
            var unique = new List<float>();
            foreach (float cut in cuts)
                if (unique.Count == 0 || Math.Abs(cut - unique[unique.Count - 1]) > 0.00001f)
                    unique.Add(Math.Max(0, Math.Min(1, cut)));
            var result = new List<Segment2>();
            for (int i = 0; i < unique.Count - 1; i++)
            {
                float lo = unique[i], hi = unique[i + 1];
                if (hi - lo < 0.00001f) continue;
                Point2 mid = Lerp(line.A, line.B, (lo + hi) * 0.5f);
                bool covered = false;
                foreach (ProbeCell cell in coverage.Cells)
                    if (PointInPolygon(mid, cell.Polygon)) { covered = true; break; }
                if (!covered) result.Add(new Segment2(Lerp(line.A, line.B, lo), Lerp(line.A, line.B, hi)));
            }
            return result;
        }

        private static List<List<Segment2>> ConnectedRuns(List<Segment2> segments)
        {
            var runs = new List<List<Segment2>>();
            foreach (Segment2 segment in segments)
            {
                List<Segment2> found = null;
                foreach (List<Segment2> run in runs)
                    if (Distance(run[run.Count - 1].B, segment.A) < 0.001f) { found = run; break; }
                if (found == null) { found = new List<Segment2>(); runs.Add(found); }
                found.Add(segment);
            }
            return runs;
        }

        private static bool IntersectionParameter(Point2 p, Point2 p2, Point2 q, Point2 q2, out float t)
        {
            float rx = p2.X - p.X, ry = p2.Y - p.Y, sx = q2.X - q.X, sy = q2.Y - q.Y;
            float den = rx * sy - ry * sx, qpx = q.X - p.X, qpy = q.Y - p.Y;
            if (Math.Abs(den) < 0.000001f) { t = 0; return false; }
            float u = (qpx * ry - qpy * rx) / den;
            t = (qpx * sy - qpy * sx) / den;
            return t >= 0 && t <= 1 && u >= 0 && u <= 1;
        }

        private static bool PointInPolygon(Point2 point, IReadOnlyList<Point2> polygon)
        {
            bool inside = false;
            for (int i = 0, j = polygon.Count - 1; i < polygon.Count; j = i++)
            {
                Point2 a = polygon[i], b = polygon[j];
                if (((a.Y > point.Y) != (b.Y > point.Y)) &&
                    point.X < (b.X - a.X) * (point.Y - a.Y) / (b.Y - a.Y) + a.X) inside = !inside;
            }
            return inside;
        }

        private static Point2 Lerp(Point2 a, Point2 b, float t)
        { return new Point2(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t); }
        private static float Distance(Point2 a, Point2 b)
        { float x = a.X - b.X, y = a.Y - b.Y; return (float)Math.Sqrt(x * x + y * y); }
    }
}
