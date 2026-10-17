using System;
using System.Collections.Generic;
using TingLu.Contracts;
using TingLu.Config;

namespace TingLu.Perception
{
    /// <summary>Builds player-facing observations only from acoustic results and explicit probes/contacts.</summary>
    public sealed class PerceptionService : IPerceptionService, IDisposable
    {
        private readonly IWorldQuery world;
        private readonly IAcousticQuery acoustics;
        private readonly IPlayerStateReader player;
        private readonly IGameClock clock;
        private readonly GameplayConfig config;
        private readonly IMemoryCheckpoint memory;
        private readonly Action<Observation> remember;
        private readonly Action<ProbeCoverage> clearCoveredMemory;
        private readonly string probeLevelId;
        private readonly Dictionary<string, Observation> transient = new Dictionary<string, Observation>();
        private PerceptionFrame current = new PerceptionFrame(0, 0, new Observation[0]);
        private long frameId;
        private ProbeCoverage lastCoverage;
        private bool disposed;

        public PerceptionFrame Current { get { EnsureLive(); return current; } }
        public IReadOnlyList<StaticMemorySnapshot> Fragments { get { EnsureLive(); return memory.Fragments; } }
        public ProbeCoverage LastProbeCoverage { get { return lastCoverage; } }

        public PerceptionService(IWorldQuery worldQuery, IAcousticQuery acousticQuery,
            IPlayerStateReader playerState, IGameClock gameClock, GameplayConfig tuning,
            IMemoryCheckpoint staticMemory, Action<Observation> rememberObservation,
            Action<ProbeCoverage> clearCoveredFragments, string levelId)
        {
            if (worldQuery == null || acousticQuery == null || playerState == null || gameClock == null || tuning == null || staticMemory == null || rememberObservation == null || clearCoveredFragments == null)
                throw new ArgumentNullException();
            tuning.Validate(); world = worldQuery; acoustics = acousticQuery; player = playerState;
            clock = gameClock; config = tuning; memory = staticMemory; remember = rememberObservation;
            clearCoveredMemory = clearCoveredFragments; probeLevelId = ContractGuard.Id(levelId);
        }

        public void Recompute()
        {
            EnsureLive();
            if (clock.Paused) return;
            double now = clock.Now; PlayerState state = player.Current;
            float angle, radius;
            GetView(state, out angle, out radius);
            var found = new Dictionary<string, Observation>();

            foreach (AcousticQueryResult result in acoustics.Sample(state.Position, state.Facing, now))
            {
                if (result == null || !result.Active || result.Discernibility <= 0 || result.Distance > radius ||
                    result.Occlusion >= 0.999f || !InSector(state.Position, state.Facing, result.ObservedSourcePosition, angle)) continue;
                string id = result.ObjectId ?? result.SourceId;
                // Acoustic results already carry the information the player is allowed to hear.
                found["sound:" + id] = new Observation(id, result.Category, MemoryPolicy.Never,
                    ObservationSource.PassiveSound, InformationKind.SoundLocation, result.ObservedSourcePosition,
                    new Segment2[0], result.MaterialId, result.SampledAt, result.SampledAt + 0.25, result.Semantic);
            }

            foreach (KeyValuePair<string, Observation> pair in transient)
                if (pair.Value.ExpiresAt > now && pair.Value.Source != ObservationSource.TactilePath)
                    found[pair.Key] = pair.Value;
            transient.Clear(); foreach (var pair in found) transient.Add(pair.Key, pair.Value);

            // Keep a live guide only while the ground beneath the player confirms tactile paving.
            if (IsTactileMaterial(world.MaterialAt(state.Position)))
            {
                foreach (WorldObjectState candidate in world.QueryNearby(state.Position, config.tactileRange))
                {
                    if (candidate.Definition.Semantic != PerceptionSemantic.Guide || !IsTactileMaterial(candidate.Definition.MaterialId)) continue;
                    Segment2 currentSegment;
                    if (!CurrentTactileSegment(candidate, state.Position, 0.4f, out currentSegment)) continue;
                    Observation tactile = new Observation(candidate.Definition.ObjectId, candidate.Definition.Category,
                        MemoryPolicy.Never, ObservationSource.TactilePath, InformationKind.Tactile,
                        state.Position, new[] { currentSegment }, candidate.Definition.MaterialId, now, now + 0.1,
                        PerceptionSemantic.Guide);
                    transient["path:" + candidate.Definition.ObjectId] = tactile;
                }
            }
            foreach (KeyValuePair<string, Observation> pair in transient)
                if (pair.Value.ExpiresAt > now) found[pair.Key] = pair.Value;
            current = new PerceptionFrame(++frameId, now, found.Values);
        }

        public void Probe(GameplayEvent successfulTap)
        {
            EnsureLive();
            if (successfulTap == null || successfulTap.Type != SemanticType.CaneTap || !(successfulTap.Payload is CanePayload))
                throw new ArgumentException("Probe requires a successful CaneTap event", "successfulTap");
            if (clock.Paused) return;
            Point2 center = successfulTap.WorldPosition ?? player.Current.Position;
            float radius = Math.Min(((CanePayload)successfulTap.Payload).Radius, config.caneRadius);
            if (radius <= 0) return;
            var cells = new List<ProbeCell>(); var boundary = new List<Segment2>();
            IWorldOcclusionQuery exact = world as IWorldOcclusionQuery;
            IReadOnlyList<Segment2> occluders = exact == null ? null : exact.QueryOcclusionBoundaries(center, radius);
            if (occluders != null) BuildCoverage(center, radius, occluders, cells, boundary);
            lastCoverage = new ProbeCoverage(successfulTap.EventId, successfulTap.SimulationTime, cells, boundary);
            clearCoveredMemory(lastCoverage);

            if (occluders == null) { RebuildAfterProbe(successfulTap.SimulationTime); return; }

            foreach (WorldObjectState candidate in world.QueryNearby(center, radius))
            {
                List<Segment2> contour = ConfirmedPieces(candidate, center, radius, occluders);
                if (contour.Count == 0) continue;
                var observation = new Observation(candidate.Definition.ObjectId, candidate.Definition.Category,
                    candidate.Definition.MemoryPolicy, ObservationSource.CaneEcho, InformationKind.LocalGeometry,
                    candidate.Position, contour, candidate.Definition.MaterialId, successfulTap.SimulationTime,
                    successfulTap.SimulationTime + config.echoSeconds, candidate.Definition.Semantic);
                transient["echo:" + candidate.Definition.ObjectId] = observation;
                remember(observation);
            }
            RebuildAfterProbe(successfulTap.SimulationTime);
        }

        public void Contact(GameplayEvent collision)
        {
            EnsureLive();
            if (collision == null || collision.Type != SemanticType.Collision || !(collision.Payload is CollisionPayload))
                throw new ArgumentException("Contact requires a collision event", "collision");
            if (clock.Paused || String.IsNullOrWhiteSpace(collision.ObjectId)) return;
            Point2 point = ((CollisionPayload)collision.Payload).ContactPoint;
            WorldObjectState target = null;
            foreach (WorldObjectState item in world.QueryNearby(point, Math.Min(config.caneRadius, 7f)))
                if (item.Definition.ObjectId == collision.ObjectId) { target = item; break; }
            if (target == null) return;
            IWorldOcclusionQuery exact = world as IWorldOcclusionQuery;
            if (exact == null) return;
            IReadOnlyList<Segment2> boundaries = exact.QueryOcclusionBoundaries(point, 0.55f);
            if (boundaries == null) return;
            List<Segment2> local = ConfirmContact(target, point, 0.55f, boundaries);
            if (local.Count == 0) return;
            var observation = new Observation(target.Definition.ObjectId, target.Definition.Category,
                target.Definition.MemoryPolicy, ObservationSource.BodyContact, InformationKind.LocalGeometry,
                point, local, target.Definition.MaterialId, collision.SimulationTime,
                collision.SimulationTime + config.painSeconds, target.Definition.Semantic);
            transient["contact:" + target.Definition.ObjectId] = observation;
            remember(observation);
            RebuildAfterProbe(collision.SimulationTime);
        }

        public void ClearTransient()
        {
            EnsureLive(); transient.Clear(); current = new PerceptionFrame(++frameId, clock.Now, new Observation[0]);
            lastCoverage = null;
        }
        public IReadOnlyList<StaticMemorySnapshot> Export() { return memory.Export(); }
        public void Restore(IReadOnlyList<StaticMemorySnapshot> fragments) { EnsureLive(); memory.Restore(fragments); ClearTransient(); }
        public void Dispose() { if (disposed) return; ClearTransient(); disposed = true; }

        private void RebuildAfterProbe(double time)
        {
            var list = new List<Observation>();
            foreach (Observation item in transient.Values) if (item.ExpiresAt > time) list.Add(item);
            foreach (Observation item in current.Observations)
                if (item.ExpiresAt > time && item.Source == ObservationSource.PassiveSound) list.Add(item);
            current = new PerceptionFrame(++frameId, time, list);
        }

        private List<Segment2> ConfirmedPieces(WorldObjectState candidate, Point2 center, float range, IReadOnlyList<Segment2> occluders)
        {
            var result = new List<Segment2>();
            foreach (Segment2 local in candidate.Definition.LocalContour)
            {
                Point2 a = Geometry2.Transform(local.A, candidate.Position, candidate.RotationDegrees);
                Point2 b = Geometry2.Transform(local.B, candidate.Position, candidate.RotationDegrees);
                AddVisiblePieces(a, b, center, range, occluders, result);
            }
            return result;
        }

        private List<Segment2> ConfirmContact(WorldObjectState candidate, Point2 point, float localRadius, IReadOnlyList<Segment2> occluders)
        {
            var result=new List<Segment2>();
            foreach(Segment2 local in candidate.Definition.LocalContour)
            {
                Point2 a=Geometry2.Transform(local.A,candidate.Position,candidate.RotationDegrees);
                Point2 b=Geometry2.Transform(local.B,candidate.Position,candidate.RotationDegrees);
                AddVisiblePieces(a, b, point, localRadius, occluders, result);
            }
            return result;
        }

        private bool CurrentTactileSegment(WorldObjectState candidate, Point2 footpoint, float tolerance, out Segment2 segment)
        {
            float best = tolerance;
            segment = default(Segment2);
            bool found = false;
            foreach (Segment2 local in candidate.Definition.LocalContour)
            {
                Point2 a = Geometry2.Transform(local.A, candidate.Position, candidate.RotationDegrees);
                Point2 b = Geometry2.Transform(local.B, candidate.Position, candidate.RotationDegrees);
                float dx = b.X - a.X, dy = b.Y - a.Y, lengthSq = dx * dx + dy * dy;
                if (lengthSq < 1e-8f) continue;
                float t = ((footpoint.X - a.X) * dx + (footpoint.Y - a.Y) * dy) / lengthSq;
                t = Math.Max(0, Math.Min(1, t));
                Point2 closest = Geometry2.Lerp(a, b, t);
                float distance = Geometry2.Distance(footpoint, closest);
                if (distance > best) continue;
                float half = Math.Min(0.4f, 0.4f / (float)Math.Sqrt(lengthSq));
                float lo = Math.Max(0, t - half), hi = Math.Min(1, t + half);
                if (hi <= lo) continue;
                segment = new Segment2(Geometry2.Lerp(a, b, lo), Geometry2.Lerp(a, b, hi));
                best = distance;
                found = true;
            }
            return found;
        }

        private static bool IsTactileMaterial(string material)
        {
            return String.Equals(material, "tactile_path", StringComparison.OrdinalIgnoreCase) ||
                String.Equals(material, "blind_path", StringComparison.OrdinalIgnoreCase);
        }

        private void BuildCoverage(Point2 center,float radius,IReadOnlyList<Segment2> occluders,List<ProbeCell> cells,List<Segment2> boundary)
        {
            // Clip each angular cell against only boundaries that cross that cell. A nearby
            // obstacle in one bearing must not shrink the proven coverage in open bearings.
            foreach (Segment2 edge in occluders) boundary.Add(edge);
            const int cellsPerCircle = 64;
            for (int i = 0; i < cellsPerCircle; i++)
            {
                double a = i * (2 * Math.PI / cellsPerCircle), b = (i + 1) * (2 * Math.PI / cellsPerCircle);
                float safe = radius;
                foreach (Segment2 edge in occluders)
                    if (IntersectsAngularWedge(center, a, b, edge))
                        safe = Math.Min(safe, DistanceToSegment(center, edge));
                safe = Math.Max(0, safe - 0.001f);
                if (safe <= 0.001f) continue;
                Point2 p = new Point2(center.X + (float)Math.Cos(a) * safe, center.Y + (float)Math.Sin(a) * safe);
                Point2 q = new Point2(center.X + (float)Math.Cos(b) * safe, center.Y + (float)Math.Sin(b) * safe);
                cells.Add(new ProbeCell(new[] { center, p, q }));
            }
        }

        private static bool IntersectsAngularWedge(Point2 center, double start, double end, Segment2 edge)
        {
            float startX = (float)Math.Cos(start), startY = (float)Math.Sin(start);
            float endX = (float)Math.Cos(end), endY = (float)Math.Sin(end);
            float ax = edge.A.X - center.X, ay = edge.A.Y - center.Y;
            float bx = edge.B.X - center.X, by = edge.B.Y - center.Y;
            if (ax * ax + ay * ay < 1e-10f || bx * bx + by * by < 1e-10f) return true;
            if (InsideWedge(ax, ay, startX, startY, endX, endY) ||
                InsideWedge(bx, by, startX, startY, endX, endY)) return true;
            return RayIntersectsSegment(startX, startY, edge, center) ||
                RayIntersectsSegment(endX, endY, edge, center);
        }

        private static bool InsideWedge(float x, float y, float startX, float startY, float endX, float endY)
        {
            return startX * y - startY * x >= -0.00001f &&
                x * endY - y * endX >= -0.00001f;
        }

        private static bool RayIntersectsSegment(float rayX, float rayY, Segment2 edge, Point2 origin)
        {
            float sx = edge.B.X - edge.A.X, sy = edge.B.Y - edge.A.Y;
            float qx = edge.A.X - origin.X, qy = edge.A.Y - origin.Y;
            float den = rayX * sy - rayY * sx;
            if (Math.Abs(den) < 1e-7f) return false;
            float alongRay = (qx * sy - qy * sx) / den;
            float alongEdge = (qx * rayY - qy * rayX) / den;
            return alongRay >= 0 && alongEdge >= 0 && alongEdge <= 1;
        }

        private void AddVisiblePieces(Point2 a, Point2 b, Point2 center, float range, IReadOnlyList<Segment2> occluders, List<Segment2> output)
        {
            var cuts = new List<float> { 0, 1 };
            AddCircleCuts(a, b, center, range, cuts);
            foreach (Segment2 edge in occluders)
            {
                AddRayCut(center, edge.A, a, b, cuts);
                AddRayCut(center, edge.B, a, b, cuts);
            }
            cuts.Sort();
            var unique = new List<float>();
            foreach (float t in cuts)
                if (t >= 0 && t <= 1 && (unique.Count == 0 || Math.Abs(t - unique[unique.Count - 1]) > 0.00001f)) unique.Add(t);
            for (int i = 0; i + 1 < unique.Count; i++)
            {
                float lo = unique[i], hi = unique[i + 1];
                if (hi - lo < 0.00001f) continue;
                Point2 mid = Geometry2.Lerp(a, b, (lo + hi) * 0.5f);
                if (Geometry2.Distance(center, mid) <= range + 0.00001f && !IsOccludedBeforeTarget(center, mid, occluders))
                    output.Add(new Segment2(Geometry2.Lerp(a, b, lo), Geometry2.Lerp(a, b, hi)));
            }
        }

        private static bool IsOccludedBeforeTarget(Point2 from, Point2 to, IReadOnlyList<Segment2> occluders)
        {
            float rx = to.X - from.X, ry = to.Y - from.Y;
            foreach (Segment2 edge in occluders)
            {
                float sx = edge.B.X - edge.A.X, sy = edge.B.Y - edge.A.Y;
                float den = rx * sy - ry * sx, qx = edge.A.X - from.X, qy = edge.A.Y - from.Y;
                if (Math.Abs(den) < 1e-7f) continue;
                float alongRay = (qx * sy - qy * sx) / den;
                float alongEdge = (qx * ry - qy * rx) / den;
                if (alongRay >= 0 && alongRay < 0.99999f && alongEdge >= 0 && alongEdge <= 1) return true;
            }
            return false;
        }

        private void AddCircleCuts(Point2 a, Point2 b, Point2 center, float radius, List<float> cuts)
        {
            float dx = b.X - a.X, dy = b.Y - a.Y, fx = a.X - center.X, fy = a.Y - center.Y;
            float aa = dx * dx + dy * dy;
            if (aa < 1e-10f) return;
            float bb = 2 * (fx * dx + fy * dy), cc = fx * fx + fy * fy - radius * radius;
            float disc = bb * bb - 4 * aa * cc;
            if (disc < 0) return;
            float root = (float)Math.Sqrt(disc);
            float t0 = (-bb - root) / (2 * aa), t1 = (-bb + root) / (2 * aa);
            if (t0 > 0 && t0 < 1) cuts.Add(t0);
            if (t1 > 0 && t1 < 1) cuts.Add(t1);
        }

        private static void AddRayCut(Point2 origin, Point2 through, Point2 a, Point2 b, List<float> cuts)
        {
            float rx = through.X - origin.X, ry = through.Y - origin.Y;
            float sx = b.X - a.X, sy = b.Y - a.Y;
            float den = rx * sy - ry * sx;
            if (Math.Abs(den) < 1e-7f) return;
            float qx = a.X - origin.X, qy = a.Y - origin.Y;
            float ray = (qx * sy - qy * sx) / den;
            float t = (qx * ry - qy * rx) / den;
            if (ray >= 0 && t > 0 && t < 1) cuts.Add(t);
        }

        private static float DistanceToSegment(Point2 p, Segment2 edge)
        {
            float x = edge.B.X - edge.A.X, y = edge.B.Y - edge.A.Y, len = x * x + y * y;
            if (len < 1e-10f) return Geometry2.Distance(p, edge.A);
            float t = ((p.X - edge.A.X) * x + (p.Y - edge.A.Y) * y) / len;
            t = Math.Max(0, Math.Min(1, t));
            return Geometry2.Distance(p, new Point2(edge.A.X + x * t, edge.A.Y + y * t));
        }

        private void GetView(PlayerState state,out float angle,out float radius)
        {
            if(state.Focus<config.lowFocusThreshold){angle=config.lowFocusAngle;radius=config.lowFocusRadius;return;}
            if(state.Mode==ActionMode.Listening){angle=config.listenAngle;radius=config.listenRadius;}
            else {angle=config.walkAngle;radius=config.walkRadius;}
        }
        private static bool InSector(Point2 origin,Point2 facing,Point2 point,float fullAngle)
        {
            float dx=point.X-origin.X,dy=point.Y-origin.Y,dist=(float)Math.Sqrt(dx*dx+dy*dy);
            if(dist<0.0001f)return true;
            float dot=(dx*facing.X+dy*facing.Y)/dist; double threshold=Math.Cos(fullAngle*0.5*Math.PI/180.0);
            return dot+0.00001>=threshold;
        }
        private void EnsureLive(){if(disposed)throw new ObjectDisposedException("PerceptionService");}
    }
}
