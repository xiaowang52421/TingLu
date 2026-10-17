using System;
using System.Collections.Generic;
using TingLu.Contracts;

namespace TingLu.Memory
{
    public sealed class StreetMemory : IStreetMemory
    {
        private const float MergeEpsilon = StreetGeometry.Epsilon * 2f;
        private const float MinimumArea = 1e-8f;

        private readonly List<StreetKnownSurface> surfaces = new List<StreetKnownSurface>();
        private readonly List<StreetKnownObject> objects = new List<StreetKnownObject>();
        private StreetMemoryFrame current = StreetMemoryFrame.Empty;

        public StreetMemoryFrame Current { get { return current; } }

        public void Apply(StreetProbeResult result)
        {
            if (result == null) throw new ArgumentNullException("result");

            var coverage = new CoverageIndex(result.Coverage.Cells);
            var observedSurfaces = new CoverageIndex(result.Surfaces);
            var touchedSurfaces = new HashSet<SurfaceKey>();
            var updatedSurfaces = new List<StreetKnownSurface>(surfaces.Count + result.Surfaces.Count);

            foreach (StreetKnownSurface old in surfaces)
            {
                if (!observedSurfaces.MayOverlap(Bounds2.FromPolygon(old.Polygon)))
                {
                    updatedSurfaces.Add(old);
                    continue;
                }

                bool changed;
                List<List<Point2>> remaining = SubtractCoverage(old.Polygon, observedSurfaces, out changed);
                if (!changed)
                {
                    updatedSurfaces.Add(old);
                    continue;
                }

                touchedSurfaces.Add(new SurfaceKey(old));
                foreach (List<Point2> polygon in remaining)
                    updatedSurfaces.Add(CopySurface(old, polygon, old.ObservedAt, old.CurrentUntil));
            }

            foreach (StreetKnownSurface observed in result.Surfaces)
            {
                if (observed == null) continue;
                updatedSurfaces.Add(CopySurface(observed, observed.Polygon, observed.ObservedAt, observed.CurrentUntil));
                touchedSurfaces.Add(new SurfaceKey(observed));
            }

            List<StreetKnownSurface> nextSurfaces = MergeTouchedSurfaces(updatedSurfaces, touchedSurfaces);

            var touchedObjects = new HashSet<ObjectKey>();
            var updatedObjects = new List<StreetKnownObject>(objects.Count + result.Objects.Count);
            var observedContours = new Dictionary<string, List<Segment2>>(StringComparer.Ordinal);
            foreach (StreetKnownObject observed in result.Objects)
            {
                if (observed == null) continue;
                List<Segment2> contour;
                if (!observedContours.TryGetValue(observed.ObjectId, out contour))
                {
                    contour = new List<Segment2>();
                    observedContours.Add(observed.ObjectId, contour);
                }
                contour.AddRange(observed.Contour);
            }

            foreach (StreetKnownObject old in objects)
            {
                bool changed = false;
                List<Segment2> remaining = new List<Segment2>(old.Contour);
                if (old.Kind == StreetKind.Temporary && ContourMayOverlap(old.Contour, coverage))
                {
                    bool coverageChanged;
                    remaining = SubtractCoverage(remaining, coverage, out coverageChanged);
                    changed |= coverageChanged;
                }

                List<Segment2> observedForId;
                if (remaining.Count != 0 && observedContours.TryGetValue(old.ObjectId, out observedForId))
                {
                    bool observedChanged;
                    remaining = SubtractSegments(remaining, observedForId, out observedChanged);
                    changed |= observedChanged;
                }

                if (!changed)
                {
                    updatedObjects.Add(old);
                }
                else
                {
                    touchedObjects.Add(new ObjectKey(old));
                    if (remaining.Count != 0)
                        updatedObjects.Add(CopyObject(old, remaining, old.ObservedAt, old.CurrentUntil));
                }
            }

            foreach (StreetKnownObject observed in result.Objects)
            {
                if (observed == null) continue;
                var copy = CopyObject(observed, observed.Contour, observed.ObservedAt, observed.CurrentUntil);
                updatedObjects.Add(copy);
                touchedObjects.Add(new ObjectKey(copy));
            }

            List<StreetKnownObject> nextObjects = MergeTouchedObjects(updatedObjects, touchedObjects);
            var nextCurrent = new StreetMemoryFrame(nextSurfaces, nextObjects);

            surfaces.Clear();
            surfaces.AddRange(nextSurfaces);
            objects.Clear();
            objects.AddRange(nextObjects);
            current = nextCurrent;
        }

        public StreetMemoryFrame Export()
        {
            return CopyFrame(current, false);
        }

        public void Import(StreetMemoryFrame frame, bool stale)
        {
            if (frame == null) throw new ArgumentNullException("frame");

            surfaces.Clear();
            foreach (StreetKnownSurface surface in frame.Surfaces)
                if (surface != null) surfaces.Add(CopySurface(surface, surface.Polygon, surface.ObservedAt, stale ? 0 : surface.CurrentUntil));

            objects.Clear();
            foreach (StreetKnownObject item in frame.Objects)
                if (item != null) objects.Add(CopyObject(item, item.Contour, item.ObservedAt, stale ? 0 : item.CurrentUntil));

            RebuildCurrent();
        }

        public void MarkStale()
        {
            for (int i = 0; i < surfaces.Count; i++)
            {
                StreetKnownSurface old = surfaces[i];
                surfaces[i] = CopySurface(old, old.Polygon, old.ObservedAt, 0);
            }
            for (int i = 0; i < objects.Count; i++)
            {
                StreetKnownObject old = objects[i];
                objects[i] = CopyObject(old, old.Contour, old.ObservedAt, 0);
            }
            RebuildCurrent();
        }

        private void RebuildCurrent()
        {
            current = new StreetMemoryFrame(surfaces, objects);
        }

        private static StreetMemoryFrame CopyFrame(StreetMemoryFrame frame, bool stale)
        {
            var copiedSurfaces = new List<StreetKnownSurface>(frame.Surfaces.Count);
            foreach (StreetKnownSurface surface in frame.Surfaces)
                copiedSurfaces.Add(CopySurface(surface, surface.Polygon, surface.ObservedAt, stale ? 0 : surface.CurrentUntil));

            var copiedObjects = new List<StreetKnownObject>(frame.Objects.Count);
            foreach (StreetKnownObject item in frame.Objects)
                copiedObjects.Add(CopyObject(item, item.Contour, item.ObservedAt, stale ? 0 : item.CurrentUntil));

            return new StreetMemoryFrame(copiedSurfaces, copiedObjects);
        }

        private static StreetKnownSurface CopySurface(StreetKnownSurface source, IEnumerable<Point2> polygon,
            double observedAt, double currentUntil)
        {
            return new StreetKnownSurface(source.MaterialId, polygon, observedAt, currentUntil, source.TextureAxis,
                source.HeightBase, source.HeightSlopeX, source.HeightSlopeY);
        }

        private static StreetKnownObject CopyObject(StreetKnownObject source, IEnumerable<Segment2> contour,
            double observedAt, double currentUntil)
        {
            return new StreetKnownObject(source.ObjectId, source.MaterialId, source.Kind, contour,
                source.Height, observedAt, currentUntil);
        }

        private static List<List<Point2>> SubtractCoverage(IReadOnlyList<Point2> polygon,
            CoverageIndex coverage, out bool changed)
        {
            changed = false;
            var pieces = new List<List<Point2>> { new List<Point2>(polygon) };
            Bounds2 polygonBounds = Bounds2.FromPolygon(polygon);
            if (!coverage.MayOverlap(polygonBounds)) return pieces;

            foreach (BoundedPolygon cell in coverage.Cells)
            {
                if (cell.Bounds.MinX > polygonBounds.MaxX + MergeEpsilon) break;
                if (!polygonBounds.Overlaps(cell.Bounds)) continue;
                var remaining = new List<List<Point2>>();
                foreach (List<Point2> piece in pieces)
                {
                    Bounds2 pieceBounds = Bounds2.FromPolygon(piece);
                    if (!pieceBounds.Overlaps(cell.Bounds))
                    {
                        remaining.Add(piece);
                        continue;
                    }

                    List<Point2> intersection = StreetGeometry.Intersect(piece, cell.Polygon);
                    if (!StreetGeometry.Valid(intersection) || Math.Abs(StreetGeometry.Area(intersection)) <= MinimumArea)
                    {
                        remaining.Add(piece);
                        continue;
                    }

                    changed = true;
                    foreach (List<Point2> outside in StreetGeometry.Subtract(piece, cell.Polygon))
                        if (StreetGeometry.Valid(outside) && Math.Abs(StreetGeometry.Area(outside)) > MinimumArea)
                            remaining.Add(outside);
                }
                pieces = remaining;
                if (pieces.Count == 0) break;
            }

            return pieces;
        }

        private static List<Segment2> SubtractCoverage(IReadOnlyList<Segment2> contour,
            CoverageIndex coverage, out bool changed)
        {
            changed = false;
            var remaining = new List<Segment2>(contour.Count);
            foreach (Segment2 original in contour)
            {
                var pieces = new List<Segment2> { original };
                Bounds2 segmentBounds = Bounds2.FromSegment(original);
                if (coverage.MayOverlap(segmentBounds))
                {
                    foreach (BoundedPolygon cell in coverage.Cells)
                    {
                        if (cell.Bounds.MinX > segmentBounds.MaxX + MergeEpsilon) break;
                        if (!segmentBounds.Overlaps(cell.Bounds)) continue;
                        var outside = new List<Segment2>();
                        foreach (Segment2 piece in pieces)
                        {
                            List<Segment2> difference = StreetGeometry.SubtractSegment(piece, cell.Polygon);
                            if (difference.Count != 1 || !SameSegment(difference[0], piece)) changed = true;
                            outside.AddRange(difference);
                        }
                        pieces = outside;
                        if (pieces.Count == 0) break;
                    }
                }
                remaining.AddRange(pieces);
            }
            return remaining;
        }

        private static List<Segment2> SubtractSegments(IReadOnlyList<Segment2> source,
            IReadOnlyList<Segment2> confirmed, out bool changed)
        {
            changed = false;
            var remaining = new List<Segment2>(source.Count);
            var confirmedBounds = new List<Bounds2>(confirmed.Count);
            foreach (Segment2 segment in confirmed) confirmedBounds.Add(Bounds2.FromSegment(segment));

            foreach (Segment2 original in source)
            {
                var pieces = new List<Segment2> { original };
                Bounds2 originalBounds = Bounds2.FromSegment(original);
                for (int i = 0; i < confirmed.Count; i++)
                {
                    if (!originalBounds.Overlaps(confirmedBounds[i])) continue;
                    var next = new List<Segment2>();
                    foreach (Segment2 piece in pieces)
                    {
                        bool cut;
                        next.AddRange(SubtractSegment(piece, confirmed[i], out cut));
                        changed |= cut;
                    }
                    pieces = next;
                    if (pieces.Count == 0) break;
                }
                remaining.AddRange(pieces);
            }
            return remaining;
        }

        private static List<Segment2> SubtractSegment(Segment2 subject, Segment2 clip, out bool changed)
        {
            changed = false;
            float dx = subject.B.X - subject.A.X, dy = subject.B.Y - subject.A.Y;
            float lengthSquared = dx * dx + dy * dy;
            float clipDx = clip.B.X - clip.A.X, clipDy = clip.B.Y - clip.A.Y;
            if (lengthSquared <= MergeEpsilon * MergeEpsilon)
                return new List<Segment2> { subject };

            float length = (float)Math.Sqrt(lengthSquared);
            float clipLength = (float)Math.Sqrt(clipDx * clipDx + clipDy * clipDy);
            float parallel = Math.Abs(dx * clipDy - dy * clipDx);
            float offsetA = Math.Abs(dx * (clip.A.Y - subject.A.Y) - dy * (clip.A.X - subject.A.X));
            float offsetB = Math.Abs(dx * (clip.B.Y - subject.A.Y) - dy * (clip.B.X - subject.A.X));
            if (clipLength <= MergeEpsilon || parallel > MergeEpsilon * length * clipLength ||
                offsetA > MergeEpsilon * length || offsetB > MergeEpsilon * length)
                return new List<Segment2> { subject };

            float start = ((clip.A.X - subject.A.X) * dx + (clip.A.Y - subject.A.Y) * dy) / lengthSquared;
            float end = ((clip.B.X - subject.A.X) * dx + (clip.B.Y - subject.A.Y) * dy) / lengthSquared;
            float low = Math.Max(0, Math.Min(start, end));
            float high = Math.Min(1, Math.Max(start, end));
            if (high - low <= MergeEpsilon / length) return new List<Segment2> { subject };

            changed = true;
            var result = new List<Segment2>(2);
            if (low > MergeEpsilon / length)
                result.Add(new Segment2(subject.A, StreetGeometry.Lerp(subject.A, subject.B, low)));
            if (high < 1 - MergeEpsilon / length)
                result.Add(new Segment2(StreetGeometry.Lerp(subject.A, subject.B, high), subject.B));
            return result;
        }

        private static bool SameSegment(Segment2 a, Segment2 b)
        {
            return (StreetGeometry.Distance(a.A, b.A) <= MergeEpsilon && StreetGeometry.Distance(a.B, b.B) <= MergeEpsilon) ||
                (StreetGeometry.Distance(a.A, b.B) <= MergeEpsilon && StreetGeometry.Distance(a.B, b.A) <= MergeEpsilon);
        }

        private static bool ContourMayOverlap(IReadOnlyList<Segment2> contour, CoverageIndex coverage)
        {
            foreach (Segment2 segment in contour)
                if (coverage.MayOverlap(Bounds2.FromSegment(segment))) return true;
            return false;
        }

        private static List<StreetKnownSurface> MergeTouchedSurfaces(List<StreetKnownSurface> values,
            HashSet<SurfaceKey> touched)
        {
            if (touched.Count == 0) return values;
            var groups = new Dictionary<SurfaceKey, List<List<Point2>>>();
            var order = new List<SurfaceKey>();
            var result = new List<StreetKnownSurface>(values.Count);
            foreach (StreetKnownSurface surface in values)
            {
                var key = new SurfaceKey(surface);
                if (!touched.Contains(key))
                {
                    result.Add(surface);
                    continue;
                }
                List<List<Point2>> group;
                if (!groups.TryGetValue(key, out group))
                {
                    group = new List<List<Point2>>();
                    groups.Add(key, group);
                    order.Add(key);
                }
                group.Add(new List<Point2>(surface.Polygon));
            }

            foreach (SurfaceKey key in order)
            {
                List<List<Point2>> group = groups[key];
                MergeAdjacentConvex(group);
                foreach (List<Point2> polygon in group)
                    result.Add(new StreetKnownSurface(key.MaterialId, polygon, key.ObservedAt, key.CurrentUntil,
                        key.TextureAxis,key.HeightBase,key.HeightSlopeX,key.HeightSlopeY));
            }
            return result;
        }

        private static List<StreetKnownObject> MergeTouchedObjects(List<StreetKnownObject> values,
            HashSet<ObjectKey> touched)
        {
            if (touched.Count == 0) return values;
            var groups = new Dictionary<ObjectKey, List<Segment2>>();
            var order = new List<ObjectKey>();
            var result = new List<StreetKnownObject>(values.Count);
            foreach (StreetKnownObject item in values)
            {
                var key = new ObjectKey(item);
                if (!touched.Contains(key))
                {
                    result.Add(item);
                    continue;
                }
                List<Segment2> group;
                if (!groups.TryGetValue(key, out group))
                {
                    group = new List<Segment2>();
                    groups.Add(key, group);
                    order.Add(key);
                }
                group.AddRange(item.Contour);
            }

            foreach (ObjectKey key in order)
            {
                List<Segment2> contour = groups[key];
                MergeCollinearSegments(contour);
                if (contour.Count != 0)
                    result.Add(new StreetKnownObject(key.ObjectId, key.MaterialId, key.Kind, contour,
                        key.Height, key.ObservedAt, key.CurrentUntil));
            }
            return result;
        }

        private static void MergeAdjacentConvex(List<List<Point2>> polygons)
        {
            if (polygons.Count < 2) return;

            var activePolygons = new List<MergePolygon>(polygons.Count);
            var edgeGroups = new Dictionary<EdgeKey, MergeEdgeGroup>();
            var mergeableGroups = new SortedSet<MergeEdgeGroup>(new MergeEdgeGroupComparer());
            int nextGroupId = 0;
            for (int i = 0; i < polygons.Count; i++)
            {
                var polygon = new MergePolygon(i, polygons[i]);
                activePolygons.Add(polygon);
                AddPolygonEdges(polygon, edgeGroups, mergeableGroups, ref nextGroupId);
            }

            while (mergeableGroups.Count != 0)
            {
                MergeEdgeGroup group = mergeableGroups.Min;
                EdgeReference left = group.MergeLeft;
                EdgeReference right = group.MergeRight;
                List<Point2> combined = group.MergedPolygon;

                RemovePolygonEdges(left.Polygon, edgeGroups, mergeableGroups);
                RemovePolygonEdges(right.Polygon, edgeGroups, mergeableGroups);
                left.Polygon.Active = false;
                right.Polygon.Active = false;

                var merged = new MergePolygon(activePolygons.Count, combined);
                activePolygons.Add(merged);
                AddPolygonEdges(merged, edgeGroups, mergeableGroups, ref nextGroupId);
            }

            polygons.Clear();
            foreach (MergePolygon polygon in activePolygons)
                if (polygon.Active) polygons.Add(polygon.Points);
        }

        private static void AddPolygonEdges(MergePolygon polygon,
            Dictionary<EdgeKey, MergeEdgeGroup> edgeGroups,
            SortedSet<MergeEdgeGroup> mergeableGroups, ref int nextGroupId)
        {
            for (int edgeIndex = 0; edgeIndex < polygon.Points.Count; edgeIndex++)
            {
                Point2 a = polygon.Points[edgeIndex];
                Point2 b = polygon.Points[(edgeIndex + 1) % polygon.Points.Count];
                var key = new EdgeKey(a, b);
                MergeEdgeGroup group;
                if (!edgeGroups.TryGetValue(key, out group))
                {
                    group = new MergeEdgeGroup(key, nextGroupId++);
                    edgeGroups.Add(key, group);
                }
                mergeableGroups.Remove(group);
                group.References.Add(new EdgeReference(polygon, edgeIndex, a, b));
                RefreshMergeCandidate(group, mergeableGroups);
            }
        }

        private static void RemovePolygonEdges(MergePolygon polygon,
            Dictionary<EdgeKey, MergeEdgeGroup> edgeGroups,
            SortedSet<MergeEdgeGroup> mergeableGroups)
        {
            for (int edgeIndex = 0; edgeIndex < polygon.Points.Count; edgeIndex++)
            {
                Point2 a = polygon.Points[edgeIndex];
                Point2 b = polygon.Points[(edgeIndex + 1) % polygon.Points.Count];
                var key = new EdgeKey(a, b);
                MergeEdgeGroup group;
                if (!edgeGroups.TryGetValue(key, out group)) continue;

                mergeableGroups.Remove(group);
                for (int i = group.References.Count - 1; i >= 0; i--)
                {
                    if (ReferenceEquals(group.References[i].Polygon, polygon))
                        group.References.RemoveAt(i);
                }
                if (group.References.Count == 0)
                {
                    edgeGroups.Remove(key);
                    continue;
                }
                RefreshMergeCandidate(group, mergeableGroups);
            }
        }

        private static void RefreshMergeCandidate(MergeEdgeGroup group,
            SortedSet<MergeEdgeGroup> mergeableGroups)
        {
            group.HasMerge = false;
            group.MergedPolygon = null;
            if (group.References.Count >= 2)
            {
                for (int i = 0; i < group.References.Count && !group.HasMerge; i++)
                {
                    for (int j = i + 1; j < group.References.Count; j++)
                    {
                        EdgeReference left = group.References[i], right = group.References[j];
                        if (ReferenceEquals(left.Polygon, right.Polygon) ||
                            !SamePoint(left.A, right.B) || !SamePoint(left.B, right.A)) continue;

                        var pair = new MergeEdgePairKey(left, right);
                        if (group.RejectedPairs != null && group.RejectedPairs.Contains(pair)) continue;

                        if (HasOtherSharedBoundary(left.Polygon.Points, left.EdgeIndex,
                            right.Polygon.Points, right.EdgeIndex))
                        {
                            RejectMergePair(group, pair);
                            continue;
                        }

                        List<Point2> combined;
                        if (!TryMerge(left.Polygon.Points, left.EdgeIndex,
                            right.Polygon.Points, right.EdgeIndex, out combined))
                        {
                            RejectMergePair(group, pair);
                            continue;
                        }

                        group.MergeLeft = left;
                        group.MergeRight = right;
                        group.MergedPolygon = combined;
                        group.HasMerge = true;
                        break;
                    }
                }
            }

            if (group.HasMerge)
            {
                EdgeReference first = group.References[0];
                group.FirstPolygonOrder = first.Polygon.Order;
                group.FirstEdgeIndex = first.EdgeIndex;
                mergeableGroups.Add(group);
            }
        }

        private static void RejectMergePair(MergeEdgeGroup group, MergeEdgePairKey pair)
        {
            if (group.RejectedPairs == null) group.RejectedPairs = new HashSet<MergeEdgePairKey>();
            group.RejectedPairs.Add(pair);
        }

        private static bool HasOtherSharedBoundary(IReadOnlyList<Point2> a, int selectedA,
            IReadOnlyList<Point2> b, int selectedB)
        {
            for (int i = 0; i < a.Count; i++)
            {
                if (i == selectedA) continue;
                Point2 a0 = a[i], a1 = a[(i + 1) % a.Count];
                float adx = a1.X - a0.X, ady = a1.Y - a0.Y;
                float aLengthSquared = adx * adx + ady * ady;
                if (aLengthSquared <= MergeEpsilon * MergeEpsilon) continue;
                float aLength = (float)Math.Sqrt(aLengthSquared);

                for (int j = 0; j < b.Count; j++)
                {
                    if (j == selectedB) continue;
                    Point2 b0 = b[j], b1 = b[(j + 1) % b.Count];
                    float bdx = b1.X - b0.X, bdy = b1.Y - b0.Y;
                    float bLength = (float)Math.Sqrt(bdx * bdx + bdy * bdy);
                    if (bLength <= MergeEpsilon ||
                        Math.Abs(adx * bdy - ady * bdx) > MergeEpsilon * aLength * bLength ||
                        Math.Abs(adx * (b0.Y - a0.Y) - ady * (b0.X - a0.X)) > MergeEpsilon * aLength ||
                        Math.Abs(adx * (b1.Y - a0.Y) - ady * (b1.X - a0.X)) > MergeEpsilon * aLength) continue;

                    float t0 = ((b0.X - a0.X) * adx + (b0.Y - a0.Y) * ady) / aLengthSquared;
                    float t1 = ((b1.X - a0.X) * adx + (b1.Y - a0.Y) * ady) / aLengthSquared;
                    float overlapLow = Math.Max(0f, Math.Min(t0, t1));
                    float overlapHigh = Math.Min(1f, Math.Max(t0, t1));
                    if ((overlapHigh - overlapLow) * aLength > MergeEpsilon) return true;
                }
            }
            return false;
        }

        private static bool HasRepeatedVertex(IReadOnlyList<Point2> points)
        {
            for (int i = 0; i < points.Count; i++)
            {
                for (int j = i + 1; j < points.Count; j++)
                {
                    if (j == i + 1 || (i == 0 && j == points.Count - 1)) continue;
                    if (StreetGeometry.Distance(points[i], points[j]) <= MergeEpsilon) return true;
                }
            }
            return false;
        }

        private sealed class MergePolygon
        {
            public readonly int Order;
            public readonly List<Point2> Points;
            public bool Active = true;
            public MergePolygon(int order, List<Point2> points) { Order = order; Points = points; }
        }

        private sealed class MergeEdgeGroup
        {
            public readonly EdgeKey Key;
            public readonly int Id;
            public readonly List<EdgeReference> References = new List<EdgeReference>(2);
            public HashSet<MergeEdgePairKey> RejectedPairs;
            public int FirstPolygonOrder;
            public int FirstEdgeIndex;
            public bool HasMerge;
            public EdgeReference MergeLeft, MergeRight;
            public List<Point2> MergedPolygon;
            public MergeEdgeGroup(EdgeKey key, int id) { Key = key; Id = id; }
        }

        private sealed class MergeEdgeGroupComparer : IComparer<MergeEdgeGroup>
        {
            public int Compare(MergeEdgeGroup left, MergeEdgeGroup right)
            {
                if (ReferenceEquals(left, right)) return 0;
                int order = left.FirstPolygonOrder.CompareTo(right.FirstPolygonOrder);
                if (order != 0) return order;
                int edge = left.FirstEdgeIndex.CompareTo(right.FirstEdgeIndex);
                return edge != 0 ? edge : left.Id.CompareTo(right.Id);
            }
        }

        private static bool TryMerge(IReadOnlyList<Point2> a, int edgeA,
            IReadOnlyList<Point2> b, int edgeB, out List<Point2> combined)
        {
            combined = null;
            var points = new List<Point2>(a.Count + b.Count - 2);
            for (int step = 0; step < a.Count; step++)
                AddDistinct(points, a[(edgeA + 1 + step) % a.Count]);
            for (int step = 2; step < b.Count; step++)
                AddDistinct(points, b[(edgeB + step) % b.Count]);
            if (points.Count > 1 && SamePoint(points[0], points[points.Count - 1]))
                points.RemoveAt(points.Count - 1);
            if (!StreetGeometry.Valid(points)) return false;
            if (HasRepeatedVertex(points)) return false;

            if (StreetGeometry.Area(points) < 0) points.Reverse();
            float area = StreetGeometry.Area(points);
            if (area <= MinimumArea) return false;
            for (int i = 0; i < points.Count; i++)
                if (StreetGeometry.Cross(points[i], points[(i + 1) % points.Count], points[(i + 2) % points.Count]) < -MergeEpsilon)
                    return false;

            combined = points;
            return true;
        }

        private static void AddDistinct(List<Point2> points, Point2 point)
        {
            if (points.Count == 0 || !SamePoint(points[points.Count - 1], point))
                points.Add(point);
        }

        private static bool SamePoint(Point2 a, Point2 b)
        {
            return a.X == b.X && a.Y == b.Y;
        }

        private static void MergeCollinearSegments(List<Segment2> segments)
        {
            bool merged;
            do
            {
                merged = false;
                for (int i = 0; i < segments.Count && !merged; i++)
                {
                    for (int j = i + 1; j < segments.Count; j++)
                    {
                        Segment2 combined;
                        if (!TryMerge(segments[i], segments[j], out combined)) continue;
                        segments[i] = combined;
                        segments.RemoveAt(j);
                        merged = true;
                        break;
                    }
                }
            } while (merged);

            segments.Sort(CompareSegments);
        }

        private static bool TryMerge(Segment2 a, Segment2 b, out Segment2 combined)
        {
            combined = default(Segment2);
            float dx = a.B.X - a.A.X, dy = a.B.Y - a.A.Y;
            float length = (float)Math.Sqrt(dx * dx + dy * dy);
            if (length <= MergeEpsilon) return false;
            float bdx = b.B.X - b.A.X, bdy = b.B.Y - b.A.Y;
            float bLength = (float)Math.Sqrt(bdx * bdx + bdy * bdy);
            if (bLength <= MergeEpsilon) return false;

            float parallel = Math.Abs(dx * bdy - dy * bdx);
            float offsetA = Math.Abs(dx * (b.A.Y - a.A.Y) - dy * (b.A.X - a.A.X));
            float offsetB = Math.Abs(dx * (b.B.Y - a.A.Y) - dy * (b.B.X - a.A.X));
            if (parallel > MergeEpsilon * length * bLength ||
                offsetA > MergeEpsilon * length || offsetB > MergeEpsilon * length) return false;

            float denominator = dx * dx + dy * dy;
            float ta = ((b.A.X - a.A.X) * dx + (b.A.Y - a.A.Y) * dy) / denominator;
            float tb = ((b.B.X - a.A.X) * dx + (b.B.Y - a.A.Y) * dy) / denominator;
            float low = Math.Min(0f, Math.Min(ta, tb));
            float high = Math.Max(1f, Math.Max(ta, tb));
            if (low > 1f + MergeEpsilon / length || high < -MergeEpsilon / length ||
                Math.Max(0f, Math.Min(ta, tb)) > Math.Min(1f, Math.Max(ta, tb)) + MergeEpsilon / length)
                return false;

            combined = new Segment2(
                new Point2(a.A.X + dx * low, a.A.Y + dy * low),
                new Point2(a.A.X + dx * high, a.A.Y + dy * high));
            return true;
        }

        private static int CompareSegments(Segment2 a, Segment2 b)
        {
            int x = a.A.X.CompareTo(b.A.X);
            if (x != 0) return x;
            int y = a.A.Y.CompareTo(b.A.Y);
            if (y != 0) return y;
            x = a.B.X.CompareTo(b.B.X);
            return x != 0 ? x : a.B.Y.CompareTo(b.B.Y);
        }

        private struct Bounds2
        {
            public readonly float MinX, MinY, MaxX, MaxY;
            private Bounds2(float minX, float minY, float maxX, float maxY)
            { MinX = minX; MinY = minY; MaxX = maxX; MaxY = maxY; }

            public bool Overlaps(Bounds2 other)
            {
                return MinX <= other.MaxX + MergeEpsilon && MaxX + MergeEpsilon >= other.MinX &&
                    MinY <= other.MaxY + MergeEpsilon && MaxY + MergeEpsilon >= other.MinY;
            }

            public static Bounds2 FromPolygon(IReadOnlyList<Point2> polygon)
            {
                float minX = polygon[0].X, maxX = minX, minY = polygon[0].Y, maxY = minY;
                for (int i = 1; i < polygon.Count; i++)
                {
                    Point2 p = polygon[i];
                    minX = Math.Min(minX, p.X); maxX = Math.Max(maxX, p.X);
                    minY = Math.Min(minY, p.Y); maxY = Math.Max(maxY, p.Y);
                }
                return new Bounds2(minX, minY, maxX, maxY);
            }

            public static Bounds2 FromSegment(Segment2 segment)
            {
                return new Bounds2(Math.Min(segment.A.X, segment.B.X), Math.Min(segment.A.Y, segment.B.Y),
                    Math.Max(segment.A.X, segment.B.X), Math.Max(segment.A.Y, segment.B.Y));
            }

            public static Bounds2 Union(Bounds2 a, Bounds2 b)
            {
                return new Bounds2(Math.Min(a.MinX, b.MinX), Math.Min(a.MinY, b.MinY),
                    Math.Max(a.MaxX, b.MaxX), Math.Max(a.MaxY, b.MaxY));
            }
        }

        private sealed class CoverageIndex
        {
            public readonly List<BoundedPolygon> Cells = new List<BoundedPolygon>();
            private bool hasBounds;
            private Bounds2 bounds;

            public CoverageIndex(IReadOnlyList<ProbeCell> cells)
            {
                foreach (ProbeCell cell in cells)
                {
                    if (cell == null || cell.Polygon.Count < 3) continue;
                    Add(cell.Polygon);
                }
                SortCells();
            }

            public CoverageIndex(IReadOnlyList<StreetKnownSurface> surfaces)
            {
                foreach (StreetKnownSurface surface in surfaces)
                    if (surface != null && surface.Polygon.Count >= 3) Add(surface.Polygon);
                SortCells();
            }

            public bool MayOverlap(Bounds2 value) { return hasBounds && bounds.Overlaps(value); }

            private void Add(IReadOnlyList<Point2> polygon)
            {
                Bounds2 polygonBounds = Bounds2.FromPolygon(polygon);
                Cells.Add(new BoundedPolygon(polygon, polygonBounds));
                bounds = hasBounds ? Bounds2.Union(bounds, polygonBounds) : polygonBounds;
                hasBounds = true;
            }

            private void SortCells()
            {
                Cells.Sort(delegate(BoundedPolygon a, BoundedPolygon b)
                {
                    int result = a.Bounds.MinX.CompareTo(b.Bounds.MinX);
                    if (result != 0) return result;
                    result = a.Bounds.MinY.CompareTo(b.Bounds.MinY);
                    if (result != 0) return result;
                    result = a.Bounds.MaxX.CompareTo(b.Bounds.MaxX);
                    return result != 0 ? result : a.Bounds.MaxY.CompareTo(b.Bounds.MaxY);
                });
            }
        }

        private sealed class BoundedPolygon
        {
            public readonly IReadOnlyList<Point2> Polygon;
            public readonly Bounds2 Bounds;
            public BoundedPolygon(IReadOnlyList<Point2> polygon, Bounds2 bounds) { Polygon = polygon; Bounds = bounds; }
        }

        private struct SurfaceKey : IEquatable<SurfaceKey>
        {
            public readonly string MaterialId;
            public readonly double ObservedAt, CurrentUntil;
            public readonly Point2 TextureAxis;
            public readonly float HeightBase,HeightSlopeX,HeightSlopeY;
            public SurfaceKey(StreetKnownSurface surface)
            { MaterialId = surface.MaterialId; ObservedAt = surface.ObservedAt; CurrentUntil = surface.CurrentUntil; TextureAxis = surface.TextureAxis;
              HeightBase=surface.HeightBase;HeightSlopeX=surface.HeightSlopeX;HeightSlopeY=surface.HeightSlopeY; }
            public bool Equals(SurfaceKey other)
            {
                return StringComparer.Ordinal.Equals(MaterialId, other.MaterialId) &&
                    ObservedAt.Equals(other.ObservedAt) && CurrentUntil.Equals(other.CurrentUntil) &&
                    TextureAxis.X.Equals(other.TextureAxis.X) && TextureAxis.Y.Equals(other.TextureAxis.Y) &&
                    HeightBase.Equals(other.HeightBase) && HeightSlopeX.Equals(other.HeightSlopeX) && HeightSlopeY.Equals(other.HeightSlopeY);
            }
            public override bool Equals(object obj) { return obj is SurfaceKey && Equals((SurfaceKey)obj); }
            public override int GetHashCode()
            {
                unchecked
                {
                    int hash = (StringComparer.Ordinal.GetHashCode(MaterialId) * 397) ^ ObservedAt.GetHashCode();
                    hash = hash * 397 ^ CurrentUntil.GetHashCode();
                    hash = hash * 397 ^ TextureAxis.X.GetHashCode();
                    hash = hash * 397 ^ TextureAxis.Y.GetHashCode();
                    hash = hash * 397 ^ HeightBase.GetHashCode();
                    hash = hash * 397 ^ HeightSlopeX.GetHashCode();
                    return hash * 397 ^ HeightSlopeY.GetHashCode();
                }
            }
        }

        private struct ObjectKey : IEquatable<ObjectKey>
        {
            public readonly string ObjectId, MaterialId;
            public readonly StreetKind Kind;
            public readonly float Height;
            public readonly double ObservedAt, CurrentUntil;
            public ObjectKey(StreetKnownObject item)
            { ObjectId = item.ObjectId; MaterialId = item.MaterialId; Kind = item.Kind; Height = item.Height; ObservedAt = item.ObservedAt; CurrentUntil = item.CurrentUntil; }
            public bool Equals(ObjectKey other)
            {
                return StringComparer.Ordinal.Equals(ObjectId, other.ObjectId) && StringComparer.Ordinal.Equals(MaterialId, other.MaterialId) &&
                    Kind == other.Kind && Height.Equals(other.Height) && ObservedAt.Equals(other.ObservedAt) && CurrentUntil.Equals(other.CurrentUntil);
            }
            public override bool Equals(object obj) { return obj is ObjectKey && Equals((ObjectKey)obj); }
            public override int GetHashCode()
            {
                unchecked
                {
                    int hash = StringComparer.Ordinal.GetHashCode(ObjectId);
                    hash = (hash * 397) ^ StringComparer.Ordinal.GetHashCode(MaterialId);
                    hash = (hash * 397) ^ (int)Kind;
                    hash = (hash * 397) ^ Height.GetHashCode();
                    hash = (hash * 397) ^ ObservedAt.GetHashCode();
                    return (hash * 397) ^ CurrentUntil.GetHashCode();
                }
            }
        }

        private struct QuantizedPoint : IEquatable<QuantizedPoint>, IComparable<QuantizedPoint>
        {
            private readonly long x, y;
            public QuantizedPoint(Point2 point)
            { x = (long)Math.Round(point.X / MergeEpsilon); y = (long)Math.Round(point.Y / MergeEpsilon); }
            public int CompareTo(QuantizedPoint other)
            { int result = x.CompareTo(other.x); return result != 0 ? result : y.CompareTo(other.y); }
            public bool Equals(QuantizedPoint other) { return x == other.x && y == other.y; }
            public override bool Equals(object obj) { return obj is QuantizedPoint && Equals((QuantizedPoint)obj); }
            public override int GetHashCode() { unchecked { return (x.GetHashCode() * 397) ^ y.GetHashCode(); } }
        }

        private struct EdgeKey : IEquatable<EdgeKey>
        {
            private readonly QuantizedPoint first, second;
            public EdgeKey(Point2 a, Point2 b)
            {
                var qa = new QuantizedPoint(a); var qb = new QuantizedPoint(b);
                if (qa.CompareTo(qb) <= 0) { first = qa; second = qb; }
                else { first = qb; second = qa; }
            }
            public bool Equals(EdgeKey other) { return first.Equals(other.first) && second.Equals(other.second); }
            public override bool Equals(object obj) { return obj is EdgeKey && Equals((EdgeKey)obj); }
            public override int GetHashCode() { unchecked { return (first.GetHashCode() * 397) ^ second.GetHashCode(); } }
        }

        private struct EdgeReference
        {
            public readonly MergePolygon Polygon;
            public readonly int EdgeIndex;
            public readonly Point2 A, B;
            public EdgeReference(MergePolygon polygon, int edgeIndex, Point2 a, Point2 b)
            { Polygon = polygon; EdgeIndex = edgeIndex; A = a; B = b; }
        }

        private struct MergeEdgePairKey : IEquatable<MergeEdgePairKey>
        {
            private readonly int firstPolygon, firstEdge, secondPolygon, secondEdge;

            public MergeEdgePairKey(EdgeReference a, EdgeReference b)
            {
                bool ordered = a.Polygon.Order < b.Polygon.Order ||
                    (a.Polygon.Order == b.Polygon.Order && a.EdgeIndex <= b.EdgeIndex);
                EdgeReference first = ordered ? a : b;
                EdgeReference second = ordered ? b : a;
                firstPolygon = first.Polygon.Order;
                firstEdge = first.EdgeIndex;
                secondPolygon = second.Polygon.Order;
                secondEdge = second.EdgeIndex;
            }

            public bool Equals(MergeEdgePairKey other)
            {
                return firstPolygon == other.firstPolygon && firstEdge == other.firstEdge &&
                    secondPolygon == other.secondPolygon && secondEdge == other.secondEdge;
            }

            public override bool Equals(object obj)
            { return obj is MergeEdgePairKey && Equals((MergeEdgePairKey)obj); }

            public override int GetHashCode()
            {
                unchecked
                {
                    int hash = firstPolygon;
                    hash = hash * 397 ^ firstEdge;
                    hash = hash * 397 ^ secondPolygon;
                    return hash * 397 ^ secondEdge;
                }
            }
        }
    }
}
