using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using TingLu.Config;
using TingLu.Contracts;

namespace TingLu.Perception
{
    /// <summary>
    /// Builds geometry only from a frozen probe/contact and sound observations only from
    /// already-permitted acoustic candidates. It never queries the live world.
    /// </summary>
    public sealed class StreetPerception
    {
        private const double ArcStepDegrees = 1.0;
        private const double VertexRayOffsetDegrees = 0.0001;
        private const double AngleMergeEpsilonDegrees = 0.000001;
        private const float GeometryEpsilon = 0.00001f;
        private const float MinimumCellArea = 0.00000001f;
        private const double SampleTimeTolerance = 0.000001;
        private const int MaxAdaptiveDepth = 18;

        private readonly StreetConfig config;
        private readonly IGameClock clock;
        private long frameId;
        private readonly List<StreetSurface> preparedInputs = new List<StreetSurface>();
        private List<PreparedSurface> preparedSurfaces;

        public PerceptionFrame Current { get; private set; }

        public StreetPerception(StreetConfig streetConfig, IGameClock gameClock)
        {
            if (streetConfig == null) throw new ArgumentNullException("streetConfig");
            if (gameClock == null) throw new ArgumentNullException("gameClock");
            streetConfig.Validate();
            config = streetConfig;
            clock = gameClock;
            Current = new PerceptionFrame(frameId, ContractGuard.Time(clock.Now), new Observation[0]);
        }

        public StreetProbeResult Probe(StreetProbeRequest request, StreetWorldFrame worldFrame)
        {
            if (request == null) throw new ArgumentNullException("request");
            if (worldFrame == null) throw new ArgumentNullException("worldFrame");
            if (clock.Paused) return EmptyProbe(request.Id, request.Time);
            EnsureSameSample(request.Time, worldFrame.Time);

            var blockers = new List<StreetObject>();
            foreach (StreetObject item in worldFrame.Objects)
                if (item != null && item.BlocksProbe && item.Polygon.Count >= 3)
                    blockers.Add(item);

            List<ProbeCell> cells = BuildSectorCells(request, blockers);
            List<Segment2> boundary = BuildOcclusionBoundary(cells, blockers);
            var coverage = new ProbeCoverage(request.Id, request.Time, cells, boundary);
            List<StreetKnownSurface> knownSurfaces = BuildKnownSurfaces(request.Time, cells, worldFrame.Surfaces);
            List<StreetKnownObject> knownObjects = BuildKnownObjects(request.Time, cells, worldFrame.Objects);
            List<StreetSignalObservation> signals = BuildSignalObservations(request.Time, worldFrame, cells, knownObjects);
            return new StreetProbeResult(coverage, knownSurfaces, knownObjects, signals);
        }

        public StreetProbeResult Contact(StreetContact contact, StreetWorldFrame worldFrame)
        {
            if (contact == null) throw new ArgumentNullException("contact");
            if (worldFrame == null) throw new ArgumentNullException("worldFrame");
            if (clock.Paused || contact.Vehicle)
                return EmptyProbe("contact:" + contact.ObjectId, worldFrame.Time);

            StreetObject target = null;
            foreach (StreetObject item in worldFrame.Objects)
            {
                if (item != null && StringComparer.Ordinal.Equals(item.Id, contact.ObjectId))
                {
                    target = item;
                    break;
                }
            }

            // Contact is a local geometry observation, not an explored ground area.
            // In particular it cannot prove that surrounding temporary-memory space is empty.
            var coverage = new ProbeCoverage("contact:" + contact.ObjectId, worldFrame.Time,
                new ProbeCell[0], new Segment2[0]);
            if (target == null || target.Kind == StreetKind.Active || target.Polygon.Count < 3)
                return new StreetProbeResult(coverage, new StreetKnownSurface[0], new StreetKnownObject[0]);

            List<Segment2> localContour = ClipContourToCircle(target.Polygon, contact.Point, config.ContactRadius);
            if (localContour.Count == 0)
                return new StreetProbeResult(coverage, new StreetKnownSurface[0], new StreetKnownObject[0]);

            var observed = new StreetKnownObject(target.Id, target.MaterialId, target.Kind, localContour,
                target.Height, worldFrame.Time, worldFrame.Time + config.Gameplay.painSeconds);
            return new StreetProbeResult(coverage, new StreetKnownSurface[0], new[] { observed });
        }

        public PerceptionFrame ProjectAcoustics(IEnumerable<AcousticQueryResult> candidates, double now)
        {
            ContractGuard.Time(now);
            if (candidates == null) throw new ArgumentNullException("candidates");

            var observations = new List<Observation>();
            if (!clock.Paused)
            {
                foreach (AcousticQueryResult candidate in candidates)
                {
                    if (!IsPermittedSound(candidate, now)) continue;
                    string objectId = String.IsNullOrWhiteSpace(candidate.ObjectId)
                        ? candidate.SourceId
                        : candidate.ObjectId;
                    observations.Add(new Observation(objectId, candidate.Category, MemoryPolicy.Never,
                        ObservationSource.PassiveSound, InformationKind.SoundLocation,
                        candidate.ObservedSourcePosition, new Segment2[0], candidate.MaterialId,
                        candidate.SampledAt, candidate.SampledAt + config.SoundSeconds, candidate.Semantic));
                }
            }

            Current = new PerceptionFrame(++frameId, now, observations);
            return Current;
        }

        public void Clear()
        {
            Current = new PerceptionFrame(++frameId, ContractGuard.Time(clock.Now), new Observation[0]);
        }

        private bool IsPermittedSound(AcousticQueryResult candidate, double now)
        {
            if (candidate == null || !candidate.Active || candidate.Discernibility <= 0f)
                return false;

            // 06 owns audibility (including distance, obstruction and discernibility).
            // 04 only checks that its frozen candidate is still current for projection.
            return candidate.SampledAt <= now + SampleTimeTolerance &&
                candidate.SampledAt + config.SoundSeconds >= now - SampleTimeTolerance;
        }

        private static StreetProbeResult EmptyProbe(string id, double time)
        {
            var coverage = new ProbeCoverage(id, time, new ProbeCell[0], new Segment2[0]);
            return new StreetProbeResult(coverage, new StreetKnownSurface[0], new StreetKnownObject[0]);
        }

        private static void EnsureSameSample(double requestTime, double frameTime)
        {
            if (Math.Abs(requestTime - frameTime) > SampleTimeTolerance)
                throw new ArgumentException("Probe request and world frame must use the same sample time.", "worldFrame");
        }

        private List<ProbeCell> BuildSectorCells(StreetProbeRequest request, IReadOnlyList<StreetObject> blockers)
        {
            foreach (StreetObject blocker in blockers)
                if (StrictlyContains(blocker.Polygon, request.Origin))
                    return new List<ProbeCell>();

            double start = -request.Angle * 0.5;
            double end = request.Angle * 0.5;
            var angles = new List<double>();
            int arcSegments = Math.Max(1, (int)Math.Ceiling(request.Angle / ArcStepDegrees));
            for (int i = 0; i <= arcSegments; i++)
                angles.Add(start + (end - start) * i / arcSegments);

            AddVertexEvents(request, blockers, start, end, angles);
            AddRadiusIntersectionEvents(request, blockers, start, end, angles);
            angles.Sort();
            var uniqueAngles = new List<double>(angles.Count);
            foreach (double angle in angles)
            {
                if (uniqueAngles.Count == 0 || angle - uniqueAngles[uniqueAngles.Count - 1] > AngleMergeEpsilonDegrees)
                    uniqueAngles.Add(angle);
            }

            var cells = new List<ProbeCell>(uniqueAngles.Count * 2);
            for (int i = 0; i + 1 < uniqueAngles.Count; i++)
                AddAdaptiveCell(request, blockers, uniqueAngles[i], uniqueAngles[i + 1], 0, cells);
            return cells;
        }

        private static void AddVertexEvents(StreetProbeRequest request, IReadOnlyList<StreetObject> blockers,
            double start, double end, List<double> angles)
        {
            foreach (StreetObject blocker in blockers)
            {
                for (int i = 0; i < blocker.Polygon.Count; i++)
                {
                    Point2 vertex = blocker.Polygon[i];
                    double dx = vertex.X - request.Origin.X;
                    double dy = vertex.Y - request.Origin.Y;
                    if (dx * dx + dy * dy <= GeometryEpsilon * GeometryEpsilon) continue;

                    double dot = dx * request.Facing.X + dy * request.Facing.Y;
                    double cross = dx * request.Facing.Y - dy * request.Facing.X;
                    double angle = Math.Atan2(cross, dot) * 180.0 / Math.PI;
                    if (angle < start - VertexRayOffsetDegrees || angle > end + VertexRayOffsetDegrees) continue;

                    angles.Add(Clamp(angle, start, end));
                    if (angle - VertexRayOffsetDegrees > start)
                        angles.Add(angle - VertexRayOffsetDegrees);
                    if (angle + VertexRayOffsetDegrees < end)
                        angles.Add(angle + VertexRayOffsetDegrees);
                }
            }
        }

        private static void AddRadiusIntersectionEvents(StreetProbeRequest request, IReadOnlyList<StreetObject> blockers,
            double start, double end, List<double> angles)
        {
            float radiusSquared = request.Radius * request.Radius;
            foreach (StreetObject blocker in blockers)
            {
                for (int i = 0; i < blocker.Polygon.Count; i++)
                {
                    Point2 a = blocker.Polygon[i];
                    Point2 b = blocker.Polygon[(i + 1) % blocker.Polygon.Count];
                    float dx = b.X - a.X;
                    float dy = b.Y - a.Y;
                    float lengthSquared = dx * dx + dy * dy;
                    if (lengthSquared <= GeometryEpsilon * GeometryEpsilon) continue;

                    float ox = a.X - request.Origin.X;
                    float oy = a.Y - request.Origin.Y;
                    float projection = -(ox * dx + oy * dy) / lengthSquared;
                    float closestT = (float)Clamp((double)projection, 0.0, 1.0);
                    float closestX = ox + dx * closestT;
                    float closestY = oy + dy * closestT;
                    float closestSquared = closestX * closestX + closestY * closestY;
                    if (closestSquared > radiusSquared + GeometryEpsilon) continue;

                    double along = Math.Sqrt(Math.Max(0.0, radiusSquared - closestSquared) / lengthSquared);
                    double firstT = projection - along;
                    double secondT = projection + along;
                    AddEdgeCircleEvent(request, a, dx, dy, firstT, start, end, angles);
                    if (Math.Abs(secondT - firstT) > 0.000001)
                        AddEdgeCircleEvent(request, a, dx, dy, secondT, start, end, angles);
                }
            }
        }

        private static void AddEdgeCircleEvent(StreetProbeRequest request, Point2 edgeStart, float edgeDx, float edgeDy,
            double t, double start, double end, List<double> angles)
        {
            if (t < -0.000001 || t > 1.000001) return;
            Point2 point = new Point2(edgeStart.X + edgeDx * (float)t, edgeStart.Y + edgeDy * (float)t);
            double dx = point.X - request.Origin.X;
            double dy = point.Y - request.Origin.Y;
            if (dx * dx + dy * dy <= GeometryEpsilon * GeometryEpsilon) return;

            double dot = dx * request.Facing.X + dy * request.Facing.Y;
            double cross = dx * request.Facing.Y - dy * request.Facing.X;
            double angle = Math.Atan2(cross, dot) * 180.0 / Math.PI;
            if (angle < start - VertexRayOffsetDegrees || angle > end + VertexRayOffsetDegrees) return;

            angles.Add(Clamp(angle, start, end));
            if (angle - VertexRayOffsetDegrees > start) angles.Add(angle - VertexRayOffsetDegrees);
            if (angle + VertexRayOffsetDegrees < end) angles.Add(angle + VertexRayOffsetDegrees);
        }

        private void AddAdaptiveCell(StreetProbeRequest request, IReadOnlyList<StreetObject> blockers,
            double start, double end, int depth, List<ProbeCell> cells)
        {
            if (end - start <= AngleMergeEpsilonDegrees) return;
            Point2 a = BoundaryPoint(request, blockers, start);
            Point2 b = BoundaryPoint(request, blockers, end);
            double middleAngle = (start + end) * 0.5;
            Point2 middleDirection = DirectionAt(request.Facing, middleAngle);
            float middleDistance = NearestBlockerDistance(request.Origin, middleDirection, request.Radius, blockers);
            float chordDistance;
            bool chordHit = StreetGeometry.RaySegment(request.Origin, middleDirection,
                new Segment2(a, b), out chordDistance);

            // A nearer midpoint means a wall was hidden behind the chord; a farther
            // midpoint means the chord cut across a gap or under-approximated the arc.
            // Split in either case. Vertex and radius-intersection events make these
            // spans stable around narrow walls, gaps, and the probe-range boundary.
            float deviation = chordHit ? Math.Abs(middleDistance - chordDistance) : 0f;
            float tolerance = Math.Max(0.0001f, request.Radius * 0.00001f);
            if (deviation > tolerance)
            {
                if (depth >= MaxAdaptiveDepth) return;
                AddAdaptiveCell(request, blockers, start, middleAngle, depth + 1, cells);
                AddAdaptiveCell(request, blockers, middleAngle, end, depth + 1, cells);
                return;
            }

            float area = Math.Abs(StreetGeometry.Cross(request.Origin, b, a)) * 0.5f;
            if (area <= MinimumCellArea) return;
            // Positive yaw sweeps clockwise in (X,Y), so reverse the endpoints for CCW cells.
            cells.Add(new ProbeCell(new[] { request.Origin, b, a }));
        }

        private static Point2 BoundaryPoint(StreetProbeRequest request, IReadOnlyList<StreetObject> blockers, double angle)
        {
            Point2 direction = DirectionAt(request.Facing, angle);
            float distance = NearestBlockerDistance(request.Origin, direction, request.Radius, blockers);
            return new Point2(request.Origin.X + direction.X * distance,
                request.Origin.Y + direction.Y * distance);
        }

        private static float NearestBlockerDistance(Point2 origin, Point2 direction, float radius,
            IReadOnlyList<StreetObject> blockers)
        {
            float nearest = radius;
            foreach (StreetObject blocker in blockers)
            {
                for (int i = 0; i < blocker.Polygon.Count; i++)
                {
                    Segment2 edge = new Segment2(blocker.Polygon[i], blocker.Polygon[(i + 1) % blocker.Polygon.Count]);
                    float distance;
                    if (StreetGeometry.RaySegment(origin, direction, edge, out distance) &&
                        distance > GeometryEpsilon && distance < nearest)
                        nearest = distance;
                }
            }
            return nearest;
        }

        private static Point2 DirectionAt(Point2 facing, double yawDegrees)
        {
            return StreetCoordinates.Turn(facing, (float)yawDegrees);
        }

        private static bool StrictlyContains(IReadOnlyList<Point2> polygon, Point2 point)
        {
            if (polygon == null || polygon.Count < 3) return false;
            for (int i = 0; i < polygon.Count; i++)
            {
                Point2 a = polygon[i];
                Point2 b = polygon[(i + 1) % polygon.Count];
                if (DistanceToSegment(point, new Segment2(a, b)) <= GeometryEpsilon) return false;
            }
            return StreetGeometry.Contains(polygon, point, GeometryEpsilon);
        }

        private static List<Segment2> BuildOcclusionBoundary(IReadOnlyList<ProbeCell> cells,
            IReadOnlyList<StreetObject> blockers)
        {
            var boundary = new List<Segment2>();
            foreach (StreetObject blocker in blockers)
            {
                if (blocker.Kind == StreetKind.Active) continue;
                for (int i = 0; i < blocker.Polygon.Count; i++)
                {
                    Segment2 edge = new Segment2(blocker.Polygon[i], blocker.Polygon[(i + 1) % blocker.Polygon.Count]);
                    boundary.AddRange(ClipSegmentToCells(edge, cells));
                }
            }
            return boundary;
        }

        private List<StreetKnownObject> BuildKnownObjects(double time, IReadOnlyList<ProbeCell> cells,
            IReadOnlyList<StreetObject> objects)
        {
            var known = new List<StreetKnownObject>();
            double currentUntil = time + config.Gameplay.echoSeconds;
            foreach (StreetObject item in objects)
            {
                if (item == null || item.Kind == StreetKind.Active || item.Polygon.Count < 3) continue;
                var contour = new List<Segment2>();
                for (int i = 0; i < item.Polygon.Count; i++)
                {
                    Segment2 edge = new Segment2(item.Polygon[i], item.Polygon[(i + 1) % item.Polygon.Count]);
                    contour.AddRange(ClipSegmentToCells(edge, cells));
                }
                if (contour.Count == 0) continue;
                known.Add(new StreetKnownObject(item.Id, item.MaterialId, item.Kind, contour,
                    item.Height, time, currentUntil));
            }
            return known;
        }

        private static List<StreetSignalObservation> BuildSignalObservations(double time, StreetWorldFrame frame,
            IReadOnlyList<ProbeCell> cells, IReadOnlyList<StreetKnownObject> knownObjects)
        {
            var result = new List<StreetSignalObservation>();
            if (frame.NextSignalChangeAt <= time + SampleTimeTolerance) return result;
            foreach (StreetObject item in frame.Objects)
            {
                if (item == null || item.Kind != StreetKind.Permanent || item.Polygon.Count < 3 ||
                    (item.Id != "street.signal.ns" && item.Id != "street.signal.ew")) continue;
                bool confirmed = false;
                for (int i = 0; i < knownObjects.Count; i++)
                    if (StringComparer.Ordinal.Equals(knownObjects[i].ObjectId, item.Id)) { confirmed = true; break; }
                if (!confirmed) continue;
                double expires = Math.Min(frame.NextSignalChangeAt, time + 0.8);
                if (expires <= time + SampleTimeTolerance) continue;
                result.Add(new StreetSignalObservation(item.Id, item.Position, item.SoundActive, time, expires));
            }
            return result;
        }

        private List<StreetKnownSurface> BuildKnownSurfaces(double time, IReadOnlyList<ProbeCell> cells,
            IReadOnlyList<StreetSurface> surfaces)
        {
            PrepareSurfaces(surfaces);
            // Coverage keeps every original visibility triangle. Surface clipping can use
            // the exact same union with fewer convex pieces, without radial seam fragments.
            List<List<Point2>> clippingCells = CoalesceSurfaceCells(cells);
            var known = new List<StreetKnownSurface>();
            var claimedByCell = new List<PreparedSurface>[clippingCells.Count];
            var cellBounds = new SurfaceBounds[clippingCells.Count];
            for(int i=0;i<clippingCells.Count;i++)
            { claimedByCell[i]=new List<PreparedSurface>();cellBounds[i]=SurfaceBounds.From(clippingCells[i]); }
            double currentUntil = time + config.Gameplay.echoSeconds;
            foreach (PreparedSurface surface in preparedSurfaces)
                for(int cellIndex=0;cellIndex<clippingCells.Count;cellIndex++)
                {
                    if (!surface.Bounds.Overlaps(cellBounds[cellIndex])) continue;
                    List<Point2> intersection = StreetGeometry.Intersect(clippingCells[cellIndex], surface.Polygon);
                    if (!StreetGeometry.Valid(intersection)) continue;
                    var fragments=new List<List<Point2>> { intersection };
                    foreach(PreparedSurface prior in claimedByCell[cellIndex])
                    {
                        var remaining=new List<List<Point2>>();
                        foreach(List<Point2> fragment in fragments)
                        {
                            if(!prior.Bounds.Overlaps(SurfaceBounds.From(fragment))||!StreetGeometry.Valid(StreetGeometry.Intersect(fragment,prior.Polygon)))
                            { remaining.Add(fragment);continue; }
                            foreach(List<Point2> piece in StreetGeometry.Subtract(fragment,prior.Polygon))
                                if(StreetGeometry.Valid(piece))remaining.Add(piece);
                        }
                        fragments=remaining;
                        if(fragments.Count==0)break;
                    }
                    foreach(List<Point2> fragment in fragments)
                        known.Add(new StreetKnownSurface(surface.MaterialId,fragment,time,currentUntil,surface.TextureAxis,
                            surface.HeightBase,surface.HeightSlopeX,surface.HeightSlopeY));
                    claimedByCell[cellIndex].Add(surface);
                }
            return known;
        }

        private static List<List<Point2>> CoalesceSurfaceCells(IReadOnlyList<ProbeCell> cells)
        {
            var result = new List<List<Point2>>();
            List<Point2> current = null;
            foreach (ProbeCell cell in cells)
            {
                IReadOnlyList<Point2> triangle = cell.Polygon;
                // Only successive triangles made by AddAdaptiveCell have this fan layout.
                // Exact endpoint identity is required: a missing cell must remain a gap.
                if (current != null && triangle.Count == 3 &&
                    SamePoint(current[0], triangle[0]) && SamePoint(current[1], triangle[2]))
                {
                    var combined = new List<Point2>(current.Count + 1);
                    combined.Add(current[0]);
                    combined.Add(triangle[1]);
                    for (int i = 1; i < current.Count; i++) combined.Add(current[i]);
                    if (IsStrictConvexFan(combined))
                    {
                        current = combined;
                        result[result.Count - 1] = current;
                        continue;
                    }
                }
                current = new List<Point2>(triangle);
                result.Add(current);
            }
            return result;
        }

        private static bool SamePoint(Point2 a, Point2 b) { return a.X == b.X && a.Y == b.Y; }

        private static bool IsStrictConvexFan(IReadOnlyList<Point2> polygon)
        {
            var vertices = new HashSet<Point2>();
            for (int i = 0; i < polygon.Count; i++)
            {
                if (!vertices.Add(polygon[i])) return false;
                Point2 a = polygon[i], b = polygon[(i + 1) % polygon.Count], c = polygon[(i + 2) % polygon.Count];
                double cross = ((double)b.X - a.X) * ((double)c.Y - b.Y) -
                    ((double)b.Y - a.Y) * ((double)c.X - b.X);
                // Conservative: round-off can prevent a merge, never authorize an outward hull.
                if (cross < 0) return false;
                if (cross == 0 && ((double)b.X - a.X) * ((double)c.X - b.X) +
                    ((double)b.Y - a.Y) * ((double)c.Y - b.Y) <= 0) return false;
            }
            return true;
        }

        private void PrepareSurfaces(IReadOnlyList<StreetSurface> surfaces)
        {
            bool unchanged = preparedSurfaces != null && preparedInputs.Count == surfaces.Count;
            for (int i = 0; unchanged && i < surfaces.Count; i++)
                unchanged = ReferenceEquals(preparedInputs[i], surfaces[i]);
            if (unchanged) return;

            // StreetSurface is immutable. Cache sorting, convex decomposition and bounds;
            // material masking remains local to each actual probe cell.
            var ordered = new List<StreetSurface>(surfaces.Count);
            foreach (StreetSurface surface in surfaces)
                if (surface != null) ordered.Add(surface);
            ordered.Sort(delegate(StreetSurface a, StreetSurface b)
            {
                int priority = b.Priority.CompareTo(a.Priority);
                return priority != 0 ? priority : StringComparer.Ordinal.Compare(a.Id, b.Id);
            });

            var prepared = new List<PreparedSurface>();
            foreach (StreetSurface surface in ordered)
            {
                List<List<Point2>> convexPieces = ConvexPieces(surface.Polygon);
                foreach (List<Point2> surfacePiece in convexPieces)
                    prepared.Add(new PreparedSurface(surface.MaterialId, surfacePiece, surface.TextureAxis,
                        surface.HeightBase,surface.HeightSlopeX,surface.HeightSlopeY));
            }
            preparedInputs.Clear();
            foreach (StreetSurface surface in surfaces) preparedInputs.Add(surface);
            preparedSurfaces = prepared;
        }

        private sealed class PreparedSurface
        {
            public readonly string MaterialId;
            public readonly List<Point2> Polygon;
            public readonly SurfaceBounds Bounds;
            public readonly Point2 TextureAxis;
            public readonly float HeightBase,HeightSlopeX,HeightSlopeY;
            public PreparedSurface(string material, List<Point2> polygon, Point2 textureAxis,float heightBase,float heightSlopeX,float heightSlopeY)
            { MaterialId=material;Polygon=polygon;Bounds=SurfaceBounds.From(polygon);TextureAxis=textureAxis;HeightBase=heightBase;HeightSlopeX=heightSlopeX;HeightSlopeY=heightSlopeY; }
        }

        private struct SurfaceBounds
        {
            private float minX,minY,maxX,maxY;
            public bool Overlaps(SurfaceBounds b)
            { return minX<=b.maxX&&maxX>=b.minX&&minY<=b.maxY&&maxY>=b.minY; }
            public static SurfaceBounds From(IReadOnlyList<Point2> polygon)
            {
                var bounds=new SurfaceBounds { minX=polygon[0].X,maxX=polygon[0].X,minY=polygon[0].Y,maxY=polygon[0].Y };
                foreach(Point2 p in polygon)
                { bounds.minX=Math.Min(bounds.minX,p.X);bounds.maxX=Math.Max(bounds.maxX,p.X);bounds.minY=Math.Min(bounds.minY,p.Y);bounds.maxY=Math.Max(bounds.maxY,p.Y); }
                return bounds;
            }
        }

        private static List<List<Point2>> ConvexPieces(IReadOnlyList<Point2> polygon)
        {
            var points = new List<Point2>(polygon);
            RemoveCollinearVertices(points);
            if (points.Count < 3 || !StreetGeometry.Valid(points)) return new List<List<Point2>>();
            if (IsConvex(points)) return new List<List<Point2>> { points };

            var triangles = new List<List<Point2>>();
            int sign = StreetGeometry.Area(points) >= 0 ? 1 : -1;
            var indices = new List<int>();
            for (int i = 0; i < points.Count; i++) indices.Add(i);
            int guard = points.Count * points.Count;
            while (indices.Count > 3 && guard-- > 0)
            {
                bool clipped = false;
                for (int i = 0; i < indices.Count; i++)
                {
                    int previous = indices[(i + indices.Count - 1) % indices.Count];
                    int current = indices[i];
                    int next = indices[(i + 1) % indices.Count];
                    if (sign * StreetGeometry.Cross(points[previous], points[current], points[next]) <= GeometryEpsilon)
                        continue;

                    bool containsVertex = false;
                    foreach (int candidate in indices)
                    {
                        if (candidate == previous || candidate == current || candidate == next) continue;
                        if (PointInTriangle(points[candidate], points[previous], points[current], points[next], sign))
                        {
                            containsVertex = true;
                            break;
                        }
                    }
                    if (containsVertex) continue;

                    triangles.Add(new List<Point2> { points[previous], points[current], points[next] });
                    indices.RemoveAt(i);
                    clipped = true;
                    break;
                }
                if (!clipped) throw new ArgumentException("Surface polygon is not a simple triangulable polygon.", "polygon");
            }
            if (indices.Count == 3)
                triangles.Add(new List<Point2> { points[indices[0]], points[indices[1]], points[indices[2]] });
            return triangles;
        }

        private static void RemoveCollinearVertices(List<Point2> points)
        {
            bool removed = true;
            while (removed && points.Count > 3)
            {
                removed = false;
                for (int i = 0; i < points.Count; i++)
                {
                    Point2 previous = points[(i + points.Count - 1) % points.Count];
                    Point2 current = points[i];
                    Point2 next = points[(i + 1) % points.Count];
                    if (Math.Abs(StreetGeometry.Cross(previous, current, next)) <= GeometryEpsilon)
                    {
                        points.RemoveAt(i);
                        removed = true;
                        break;
                    }
                }
            }
        }

        private static bool IsConvex(IReadOnlyList<Point2> points)
        {
            int sign = StreetGeometry.Area(points) >= 0 ? 1 : -1;
            for (int i = 0; i < points.Count; i++)
                if (sign * StreetGeometry.Cross(points[i], points[(i + 1) % points.Count], points[(i + 2) % points.Count]) < -GeometryEpsilon)
                    return false;
            return true;
        }

        private static bool PointInTriangle(Point2 point, Point2 a, Point2 b, Point2 c, int sign)
        {
            return sign * StreetGeometry.Cross(a, b, point) >= -GeometryEpsilon &&
                sign * StreetGeometry.Cross(b, c, point) >= -GeometryEpsilon &&
                sign * StreetGeometry.Cross(c, a, point) >= -GeometryEpsilon;
        }

        private static List<Segment2> ClipSegmentToCells(Segment2 segment, IReadOnlyList<ProbeCell> cells)
        {
            var intervals = new List<ParameterInterval>();
            Point2 delta = new Point2(segment.B.X - segment.A.X, segment.B.Y - segment.A.Y);
            float lengthSquared = delta.X * delta.X + delta.Y * delta.Y;
            if (lengthSquared <= GeometryEpsilon * GeometryEpsilon) return new List<Segment2>();

            foreach (ProbeCell cell in cells)
            {
                Segment2 clipped;
                if (!ClipSegmentToCell(segment, cell.Polygon, out clipped)) continue;
                float t0 = Parameter(segment.A, delta, lengthSquared, clipped.A);
                float t1 = Parameter(segment.A, delta, lengthSquared, clipped.B);
                intervals.Add(new ParameterInterval(Math.Min(t0, t1), Math.Max(t0, t1)));
            }

            intervals.Sort(delegate(ParameterInterval a, ParameterInterval b) { return a.Start.CompareTo(b.Start); });
            var merged = new List<ParameterInterval>();
            foreach (ParameterInterval interval in intervals)
            {
                if (merged.Count == 0 || interval.Start > merged[merged.Count - 1].End + GeometryEpsilon)
                    merged.Add(interval);
                else
                    merged[merged.Count - 1].End = Math.Max(merged[merged.Count - 1].End, interval.End);
            }

            var result = new List<Segment2>(merged.Count);
            foreach (ParameterInterval interval in merged)
            {
                Point2 a = StreetGeometry.Lerp(segment.A, segment.B, Clamp01(interval.Start));
                Point2 b = StreetGeometry.Lerp(segment.A, segment.B, Clamp01(interval.End));
                if (StreetGeometry.Distance(a, b) > GeometryEpsilon) result.Add(new Segment2(a, b));
            }
            return result;
        }

        private static float Parameter(Point2 origin, Point2 delta, float lengthSquared, Point2 point)
        {
            return ((point.X - origin.X) * delta.X + (point.Y - origin.Y) * delta.Y) / lengthSquared;
        }

        private static List<Segment2> ClipContourToCircle(IReadOnlyList<Point2> polygon, Point2 center, float radius)
        {
            var result = new List<Segment2>();
            if (radius <= 0f) return result;
            for (int i = 0; i < polygon.Count; i++)
            {
                Segment2 segment = new Segment2(polygon[i], polygon[(i + 1) % polygon.Count]);
                Segment2 clipped;
                if (ClipSegmentToCircle(segment, center, radius, out clipped)) result.Add(clipped);
            }
            return result;
        }

        private static bool ClipSegmentToCell(Segment2 segment, IReadOnlyList<Point2> polygon,
            out Segment2 clipped)
        {
            float low = 0f;
            float high = 1f;
            float sign = StreetGeometry.Area(polygon) >= 0f ? 1f : -1f;
            for (int i = 0; i < polygon.Count; i++)
            {
                Point2 a = polygon[i];
                Point2 b = polygon[(i + 1) % polygon.Count];
                // Cross products scale with edge length (m²), while GeometryEpsilon
                // is a linear world-space tolerance (m). Scale it by the edge length
                // so short visibility-cell edges do not gain a disproportionately
                // large outward allowance.
                float crossTolerance = GeometryEpsilon * StreetGeometry.Distance(a, b);
                float d0 = sign * StreetGeometry.Cross(a, b, segment.A) + crossTolerance;
                float d1 = sign * StreetGeometry.Cross(a, b, segment.B) + crossTolerance;
                if (d0 < 0f && d1 < 0f)
                {
                    clipped = default(Segment2);
                    return false;
                }

                float denominator = d1 - d0;
                if (Math.Abs(denominator) < 1e-12f) continue;
                float t = -d0 / denominator;
                if (d0 < 0f) low = Math.Max(low, t);
                if (d1 < 0f) high = Math.Min(high, t);
                if (high - low <= 1e-7f)
                {
                    clipped = default(Segment2);
                    return false;
                }
            }

            clipped = new Segment2(StreetGeometry.Lerp(segment.A, segment.B, low),
                StreetGeometry.Lerp(segment.A, segment.B, high));
            return true;
        }

        private static bool ClipSegmentToCircle(Segment2 segment, Point2 center, float radius, out Segment2 clipped)
        {
            Point2 delta = new Point2(segment.B.X - segment.A.X, segment.B.Y - segment.A.Y);
            Point2 fromCenter = new Point2(segment.A.X - center.X, segment.A.Y - center.Y);
            float a = delta.X * delta.X + delta.Y * delta.Y;
            if (a <= GeometryEpsilon * GeometryEpsilon)
            {
                clipped = default(Segment2);
                return false;
            }

            float b = 2f * (fromCenter.X * delta.X + fromCenter.Y * delta.Y);
            float c = fromCenter.X * fromCenter.X + fromCenter.Y * fromCenter.Y - radius * radius;
            float discriminant = b * b - 4f * a * c;
            if (discriminant < 0f)
            {
                clipped = default(Segment2);
                return false;
            }

            float root = (float)Math.Sqrt(discriminant);
            float low = Math.Max(0f, (-b - root) / (2f * a));
            float high = Math.Min(1f, (-b + root) / (2f * a));
            if (high - low <= GeometryEpsilon)
            {
                clipped = default(Segment2);
                return false;
            }

            clipped = new Segment2(StreetGeometry.Lerp(segment.A, segment.B, low),
                StreetGeometry.Lerp(segment.A, segment.B, high));
            return true;
        }

        private static float DistanceToSegment(Point2 point, Segment2 segment)
        {
            return StreetGeometry.Distance(point, StreetGeometry.Closest(point, segment));
        }

        private static float Clamp01(float value) { return Math.Max(0f, Math.Min(1f, value)); }
        private static double Clamp(double value, double min, double max) { return Math.Max(min, Math.Min(max, value)); }

        private sealed class ParameterInterval
        {
            public float Start;
            public float End;
            public ParameterInterval(float start, float end) { Start = start; End = end; }
        }
    }
}
