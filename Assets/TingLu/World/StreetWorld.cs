using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using TingLu.Config;
using TingLu.Contracts;

namespace TingLu.World
{
    /// <summary>
    /// Deterministic street simulation. Definitions are immutable contract values; all movement is
    /// represented by translated copies so a captured frame can never change underneath a reader.
    /// </summary>
    public sealed class StreetWorld : IStreetWorld
    {
        private const double TimeEpsilon = 0.0000001;
        private const float DistanceEpsilon = 0.00001f;
        private const float ContactSkin = 0.002f;

        private sealed class RouteRuntime
        {
            public StreetRoute Route;
            public double Length;
            public double Distance;
            public readonly List<double> VertexDistances = new List<double>();
            public readonly List<Point2> Vertices = new List<Point2>();
        }

        private sealed class MotionSegment
        {
            public double StartFraction;
            public double EndFraction;
            public Point2 Start;
            public Point2 End;
        }

        private readonly StreetConfig config;
        private readonly List<StreetSurface> surfaces;
        private readonly Dictionary<string, StreetObject> objects = new Dictionary<string, StreetObject>(StringComparer.Ordinal);
        private readonly List<string> objectIds = new List<string>();
        private readonly List<RouteRuntime> routes = new List<RouteRuntime>();
        private readonly Dictionary<string, RouteRuntime> routesByObject = new Dictionary<string, RouteRuntime>(StringComparer.Ordinal);
        private readonly Dictionary<string, List<MotionSegment>> lastMotion = new Dictionary<string, List<MotionSegment>>(StringComparer.Ordinal);
        private readonly Dictionary<string, bool> configuredSound = new Dictionary<string, bool>(StringComparer.Ordinal);
        private readonly HashSet<string> activePhysicalContacts = new HashSet<string>(StringComparer.Ordinal);
        private readonly HashSet<string> restoreOverlaps = new HashSet<string>(StringComparer.Ordinal);
        private bool seedRestoreOverlaps;
        private readonly HashSet<string> activeVehicleContacts = new HashSet<string>(StringComparer.Ordinal);
        private readonly HashSet<string> vehicleContactsThisTick = new HashSet<string>(StringComparer.Ordinal);
        private double time;
        private bool suppressVehicleContacts;

        public StreetWorld(IEnumerable<StreetSurface> surfaces, IEnumerable<StreetObject> objects,
            IEnumerable<StreetRoute> routeDefinitions, StreetConfig config)
        {
            if (surfaces == null) throw new ArgumentNullException("surfaces");
            if (objects == null) throw new ArgumentNullException("objects");
            if (routeDefinitions == null) throw new ArgumentNullException("routes");
            this.config = config ?? throw new ArgumentNullException("config");
            this.config.Validate();
            if (double.IsNaN(config.SignalGreenSeconds) || double.IsInfinity(config.SignalGreenSeconds) ||
                double.IsNaN(config.SignalClearanceSeconds) || double.IsInfinity(config.SignalClearanceSeconds))
                throw new ArgumentException("Signal durations must be finite", "config");
            this.surfaces = new List<StreetSurface>(surfaces);
            var surfaceIds = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < this.surfaces.Count; i++)
            {
                if (this.surfaces[i] == null) throw new ArgumentException("Surface cannot be null", "surfaces");
                if (!surfaceIds.Add(this.surfaces[i].Id)) throw new ArgumentException("Duplicate surface " + this.surfaces[i].Id, "surfaces");
            }

            foreach (StreetObject item in objects)
            {
                ValidateObject(item);
                if (this.objects.ContainsKey(item.Id)) throw new ArgumentException("Duplicate world object " + item.Id, "objects");
                this.objects.Add(item.Id, CopyObject(item, item.Position));
                configuredSound.Add(item.Id, item.SoundActive);
                objectIds.Add(item.Id);
            }
            objectIds.Sort(StringComparer.Ordinal);

            foreach (StreetRoute route in routeDefinitions)
            {
                if (route == null) throw new ArgumentException("Route cannot be null", "routes");
                StreetObject item;
                if (!this.objects.TryGetValue(route.ObjectId, out item))
                    throw new ArgumentException("Route references missing object " + route.ObjectId, "routes");
                if (route.SignalAxis != null && !route.StopLineDistance.HasValue)
                    throw new ArgumentException("Signal-controlled routes need a stop line: " + route.ObjectId, "routes");
                if (route.Offset < 0) throw new ArgumentException("Route offset is an initial cumulative distance and cannot be negative: " + route.ObjectId, "routes");
                if (routesByObject.ContainsKey(route.ObjectId))
                    throw new ArgumentException("Object has more than one route " + route.ObjectId, "routes");
                RouteRuntime runtime = CreateRoute(route);
                if (route.StopLineDistance.HasValue && route.StopLineDistance.Value > runtime.Length + DistanceEpsilon)
                    throw new ArgumentException("Stop line lies beyond its route: " + route.ObjectId, "routes");
                double initialDistance = route.Offset;
                if (route.Loop) initialDistance = PositiveModulo(initialDistance, runtime.Length);
                else initialDistance = Clamp(initialDistance, 0, runtime.Length);
                runtime.Distance = initialDistance;
                this.objects[route.ObjectId] = CopyObject(item, PositionAt(runtime, initialDistance));
                this.routes.Add(runtime);
                routesByObject.Add(route.ObjectId, runtime);
            }
            this.routes.Sort(delegate(RouteRuntime a, RouteRuntime b)
            { return StringComparer.Ordinal.Compare(a.Route.ObjectId, b.Route.ObjectId); });
            time = 0;
            SyncSignalSources();
            ResetMotion(0);
            SyncMovementSounds();
        }

        public string SignalPhase
        {
            get { return PhaseAt(time); }
        }

        public StreetWorldFrame Capture()
        {
            var snapshot = new List<StreetObject>(objectIds.Count);
            for (int i = 0; i < objectIds.Count; i++) snapshot.Add(objects[objectIds[i]]);
            return new StreetWorldFrame(time, surfaces, snapshot, NextPhaseChange(time));
        }

        public void Tick(double delta, bool paused)
        {
            ContractGuard.Time(delta);
            double newTime = time + (paused ? 0 : delta);
            if (double.IsNaN(newTime) || double.IsInfinity(newTime)) throw new ArgumentOutOfRangeException("delta", "World time overflow");
            if (paused || delta == 0)
            {
                vehicleContactsThisTick.Clear();
                suppressVehicleContacts = paused;
                ResetMotion(delta);
                return;
            }

            double startTime = time;
            vehicleContactsThisTick.Clear();
            foreach (RouteRuntime route in routes)
            {
                List<MotionSegment> segments = AdvanceRoute(route, startTime, delta);
                lastMotion[route.Route.ObjectId] = segments;
                objects[route.Route.ObjectId] = CopyObject(objects[route.Route.ObjectId], PositionAt(route, route.Distance));
            }
            time = newTime;
            SyncSignalSources();
            suppressVehicleContacts = false;
            SyncMovementSounds();
        }

        public StreetMoveResult Move(Point2 from, Point2 requested, float radius)
        {
            ValidateRadius(radius);
            SeedRestoredOverlaps(from, radius);
            Point2 current = from;
            Point2 target = requested;
            var contacts = new List<StreetContact>();
            var contacted = new HashSet<string>(StringComparer.Ordinal);

            // Sweep the body along the requested segment. After a hit, remove the inward component
            // so the player can continue alongside a wall without tunnelling through its corners.
            for (int pass = 0; pass < 4; pass++)
            {
                Point2 delta = Subtract(target, current);
                float distance = Length(delta);
                if (distance <= DistanceEpsilon) break;

                bool found = false;
                float bestT = float.PositiveInfinity;
                StreetObject bestObject = null;
                Point2 bestPoint = current;
                Point2 bestNormal = new Point2();
                for (int i = 0; i < objectIds.Count; i++)
                {
                    StreetObject item = objects[objectIds[i]];
                    // Movement runs against the world pose captured for this frame. Moving actors
                    // still occupy that final pose and must block the player like every solid.
                    if (!item.Solid || item.Polygon.Count < 3) continue;
                    float hitT;
                    Point2 hitPoint, normal;
                    if (!SweepCirclePolygon(current, target, radius, item.Polygon, out hitT, out hitPoint, out normal)) continue;
                    if (hitT <= 0.000001f && Dot(Subtract(target, current), normal) >= -DistanceEpsilon) continue;
                    if (!found || hitT < bestT - 0.000001f ||
                        (Math.Abs(hitT - bestT) <= 0.000001f && StringComparer.Ordinal.Compare(item.Id, bestObject.Id) < 0))
                    {
                        found = true; bestT = hitT; bestObject = item; bestPoint = hitPoint; bestNormal = normal;
                    }
                }
                if (!found) { current = target; break; }

                if (contacted.Add(bestObject.Id))
                {
                    activePhysicalContacts.Add(bestObject.Id);
                    if (!restoreOverlaps.Contains(bestObject.Id))
                        contacts.Add(new StreetContact(bestObject.Id, bestPoint, false,
                            bestObject.ContactDamage, bestObject.ContactStun));
                }

                float safeT = Math.Max(0, bestT - ContactSkin / distance);
                Point2 safe = Lerp(current, target, safeT);
                Point2 remainder = Subtract(target, safe);
                float into = Dot(remainder, bestNormal);
                if (into < 0)
                    remainder = Subtract(remainder, Scale(bestNormal, into));
                current = safe;
                target = Add(safe, remainder);

                // An initial overlap has no approach movement to project; preserve the safe origin.
                if (bestT <= 0.000001f && Length(remainder) >= distance - DistanceEpsilon) break;
            }
            // Keep restored overlaps through the following relative contact sweep: its
            // origin can still be inside this object while this Move ends outside it.
            // VehicleContacts releases the marker after suppressing that exit interval.
            return new StreetMoveResult(current, contacts);
        }

        private void SeedRestoredOverlaps(Point2 playerPosition, float radius)
        {
            if (!seedRestoreOverlaps) return;
            seedRestoreOverlaps = false;
            for (int i = 0; i < objectIds.Count; i++)
            {
                StreetObject item = objects[objectIds[i]];
                if (!item.Solid || item.Polygon.Count < 3) continue;
                Point2 actorPosition = item.Position;
                List<MotionSegment> motion;
                if (lastMotion.TryGetValue(item.Id, out motion) && motion.Count > 0) actorPosition = motion[0].Start;
                Point2 nearest;
                if (CircleOverlapsPolygon(Subtract(playerPosition, actorPosition), radius + ContactSkin * 2,
                    LocalPolygon(item), out nearest)) restoreOverlaps.Add(item.Id);
            }
        }

        private void ClearSeparatedRestoreOverlaps(Point2 playerPosition, float radius)
        {
            if (restoreOverlaps.Count == 0) return;
            var clear = new List<string>();
            foreach (string id in restoreOverlaps)
            {
                StreetObject item;
                if (!objects.TryGetValue(id, out item)) { clear.Add(id); continue; }
                Point2 nearest;
                if (!CircleOverlapsPolygon(Subtract(playerPosition, item.Position), radius + ContactSkin * 2,
                    LocalPolygon(item), out nearest)) clear.Add(id);
            }
            for (int i = 0; i < clear.Count; i++) restoreOverlaps.Remove(clear[i]);
        }

        public IReadOnlyList<StreetContact> VehicleContacts(Point2 from, Point2 to, float radius)
        {
            ValidateRadius(radius);
            var result = new List<StreetContact>();
            if (suppressVehicleContacts) return result.AsReadOnly();
            SeedRestoredOverlaps(from, radius);
            var overlappingNow = new HashSet<string>(StringComparer.Ordinal);
            var dangerousOverlapsNow = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < objectIds.Count; i++)
            {
                StreetObject item = objects[objectIds[i]];
                if (!item.Solid || item.Polygon.Count < 3) continue;
                List<MotionSegment> segments;
                if (!lastMotion.TryGetValue(item.Id, out segments))
                    segments = new List<MotionSegment> { new MotionSegment { StartFraction=0, EndFraction=1, Start=item.Position, End=item.Position } };
                Point2[] local = LocalPolygon(item);
                Point2 nearest;
                bool overlaps = CircleOverlapsPolygon(Subtract(to, item.Position), radius + (activePhysicalContacts.Contains(item.Id) ? ContactSkin * 2 : 0), local, out nearest);
                Point2 restoreNearest;
                bool restoredOverlapRemains = CircleOverlapsPolygon(Subtract(to, item.Position), radius + ContactSkin * 2, local, out restoreNearest);
                if (overlaps) overlappingNow.Add(item.Id);
                bool hit = false, dangerous = false;
                Point2 contactPoint = Add(item.Position, nearest);
                for (int j = 0; j < segments.Count; j++)
                {
                    MotionSegment segment = segments[j];
                    Point2 playerStart = Lerp(from, to, (float)segment.StartFraction);
                    Point2 playerEnd = Lerp(from, to, (float)segment.EndFraction);
                    float hitT;
                    Point2 localPoint, normal;
                    Point2 relativeStart = Subtract(playerStart, segment.Start);
                    Point2 relativeEnd = Subtract(playerEnd, segment.End);
                    bool swept = SweepCirclePolygon(relativeStart, relativeEnd, radius,
                        local, out hitT, out localPoint, out normal);
                    bool segmentMoving = Distance(segment.Start, segment.End) > DistanceEpsilon;
                    Point2 endpointNearest;
                    bool startsInside = CircleOverlapsPolygon(relativeStart, radius, local, out endpointNearest);
                    bool endsInside = CircleOverlapsPolygon(relativeEnd, radius, local, out endpointNearest);
                    if (!swept && endsInside)
                    {
                        swept = true;
                        hitT = 1;
                        localPoint = endpointNearest;
                    }
                    if (!swept) continue;
                    // A later moving subinterval can qualify even if an earlier waiting interval touched.
                    if (!hit || segmentMoving) contactPoint = Add(Lerp(segment.Start, segment.End, hitT), localPoint);
                    hit = true;
                    if (item.IsVehicle && segmentMoving)
                    {
                        // Starting inside a contact can be a restored pose or an actor leaving an
                        // ordinary overlap. Only treat it as a new impact when relative motion
                        // increases penetration; outside-to-inside sweeps remain unambiguous hits.
                        dangerous |= !startsInside ||
                            (endsInside && OverlapDepth(relativeEnd, radius, local) >
                                OverlapDepth(relativeStart, radius, local) + DistanceEpsilon) ||
                            IsMovingIntoContact(relativeStart, relativeEnd, radius, local);
                    }
                }
                hit |= overlaps;
                bool suppressed = restoreOverlaps.Contains(item.Id);
                if (suppressed && !restoredOverlapRemains) restoreOverlaps.Remove(item.Id);
                if (!hit || suppressed) continue;
                if (dangerous)
                {
                    if (overlaps) dangerousOverlapsNow.Add(item.Id);
                    if (!activeVehicleContacts.Contains(item.Id) && vehicleContactsThisTick.Add(item.Id))
                        result.Add(new StreetContact(item.Id, contactPoint, true, item.ContactDamage, item.ContactStun));
                }
                else
                    result.Add(new StreetContact(item.Id, contactPoint, false, item.ContactDamage, item.ContactStun));
            }
            seedRestoreOverlaps = false;
            activeVehicleContacts.Clear();
            foreach (string id in overlappingNow) if (dangerousOverlapsNow.Contains(id)) activeVehicleContacts.Add(id);
            activePhysicalContacts.Clear();
            foreach (string id in overlappingNow) activePhysicalContacts.Add(id);
            result.Sort(delegate(StreetContact a, StreetContact b) { return StringComparer.Ordinal.Compare(a.ObjectId, b.ObjectId); });
            return result.AsReadOnly();
        }

        private void SyncMovementSounds()
        {
            foreach (string id in objectIds)
            {
                StreetObject item = objects[id];
                if (item.ActorKind == StreetActorKind.None || item.Polygon.Count < 3) continue;
                // At route end/red light, movement sounds stop; bird and signal sources retain their rules.
                RouteRuntime route;
                bool moving = routesByObject.TryGetValue(id, out route) &&
                    (route.Route.Loop || route.Distance < route.Length - DistanceEpsilon) &&
                    !(route.Route.SignalAxis != null && route.Route.StopLineDistance.HasValue &&
                      Math.Abs(NextStopLine(route, route.Distance) - route.Distance) <= DistanceEpsilon && !IsGreen(route.Route.SignalAxis, time));
                objects[id] = CopyObject(item, item.Position, configuredSound[id] && moving);
            }
        }

        public string MaterialAt(Point2 position)
        {
            StreetSurface best = null;
            float bestArea = float.PositiveInfinity;
            for (int i = 0; i < surfaces.Count; i++)
            {
                StreetSurface surface = surfaces[i];
                if (!PointInPolygon(position, surface.Polygon)) continue;
                float area = Math.Abs(StreetGeometry.Area(surface.Polygon));
                if (best == null || surface.Priority > best.Priority ||
                    (surface.Priority == best.Priority && (area < bestArea - DistanceEpsilon ||
                     (Math.Abs(area - bestArea) <= DistanceEpsilon && StringComparer.Ordinal.Compare(surface.Id, best.Id) < 0))) )
                {
                    best = surface;
                    bestArea = area;
                }
            }
            return best == null ? null : best.MaterialId;
        }

        public StreetTrafficState ExportTraffic()
        {
            var ids = new List<string>(routes.Count);
            var distances = new List<double>(routes.Count);
            for (int i = 0; i < routes.Count; i++)
            {
                ids.Add(routes[i].Route.ObjectId);
                distances.Add(routes[i].Distance);
            }
            return new StreetTrafficState(time, ids, distances);
        }

        public void ImportTraffic(StreetTrafficState state)
        {
            if (state == null) throw new ArgumentNullException("state");
            if (state.ObjectIds.Count != routes.Count) throw new ArgumentException("Traffic route set does not match this world", "state");
            var imported = new Dictionary<string, double>(StringComparer.Ordinal);
            for (int i = 0; i < state.ObjectIds.Count; i++)
            {
                string id = ContractGuard.Id(state.ObjectIds[i]);
                RouteRuntime route;
                double distance = state.Distances[i];
                if (!routesByObject.TryGetValue(id, out route) || imported.ContainsKey(id))
                    throw new ArgumentException("Traffic state has an unknown or duplicate route " + id, "state");
                if (distance < 0 || double.IsNaN(distance) || double.IsInfinity(distance) ||
                    (!route.Route.Loop && distance > route.Length + DistanceEpsilon))
                    throw new ArgumentException("Traffic state has an invalid route distance for " + id, "state");
                imported.Add(id, distance);
            }
            if (imported.Count != routes.Count) throw new ArgumentException("Traffic state is missing a route", "state");

            time = ContractGuard.Time(state.Time);
            for (int i = 0; i < routes.Count; i++)
            {
                RouteRuntime route = routes[i];
                route.Distance = imported[route.Route.ObjectId];
                objects[route.Route.ObjectId] = CopyObject(objects[route.Route.ObjectId], PositionAt(route, route.Distance));
            }
            SyncSignalSources();
            suppressVehicleContacts = true;
            activeVehicleContacts.Clear();
            activePhysicalContacts.Clear();
            restoreOverlaps.Clear();
            seedRestoreOverlaps = true;
            vehicleContactsThisTick.Clear();
            ResetMotion(0);
            SyncMovementSounds();
        }

        public void MoveObject(string id, Point2 newPosition)
        {
            StreetObject item = GetObject(id);
            if (item.Kind != StreetKind.Temporary || routesByObject.ContainsKey(id))
                throw new InvalidOperationException("Only unrouted temporary objects may be moved through this testing hook");
            objects[id] = CopyObject(item, newPosition);
        }

        public void RemoveObject(string id)
        {
            StreetObject item = GetObject(id);
            if (item.Kind != StreetKind.Temporary || routesByObject.ContainsKey(id))
                throw new InvalidOperationException("Only unrouted temporary objects may be removed through this testing hook");
            objects.Remove(id);
            objectIds.Remove(id);
        }

        private List<MotionSegment> AdvanceRoute(RouteRuntime route, double startTime, double delta)
        {
            var motion = new List<MotionSegment>();
            double elapsed = 0;
            int iterations = 0;
            while (elapsed < delta - TimeEpsilon)
            {
                if (++iterations > 100000) throw new ArgumentOutOfRangeException("delta", "Route simulation step is too large");
                double remaining = delta - elapsed;
                double progress = route.Distance;
                if (!route.Route.Loop && progress >= route.Length - DistanceEpsilon)
                {
                    AddMotion(motion, route, progress, progress, elapsed, elapsed + remaining, delta);
                    elapsed = delta;
                    break;
                }

                double now = startTime + elapsed;
                bool signalControlled = route.Route.SignalAxis != null && route.Route.StopLineDistance.HasValue;
                bool green = !signalControlled || IsGreen(route.Route.SignalAxis, now);
                double stopLine = signalControlled ? NextStopLine(route, progress) : double.PositiveInfinity;
                bool atStopLine = signalControlled && Math.Abs(stopLine - progress) <= DistanceEpsilon;

                if (signalControlled && atStopLine && !green)
                {
                    double wait = Math.Min(remaining, NextPhaseChange(now) - now);
                    if (wait <= TimeEpsilon) wait = Math.Min(remaining, TimeEpsilon * 10);
                    AddMotion(motion, route, progress, progress, elapsed, elapsed + wait, delta);
                    elapsed += wait;
                    continue;
                }

                double untilChange = signalControlled ? NextPhaseChange(now) - now : double.PositiveInfinity;
                double untilStop = signalControlled && stopLine > progress + DistanceEpsilon
                    ? (stopLine - progress) / route.Route.Speed : double.PositiveInfinity;
                double nextVertex = NextVertexDistance(route, progress);
                double untilVertex = double.IsPositiveInfinity(nextVertex) ? double.PositiveInfinity : Math.Max(0, (nextVertex - progress) / route.Route.Speed);
                double duration = Math.Min(remaining, Math.Min(untilChange, Math.Min(untilStop, untilVertex)));
                if (double.IsPositiveInfinity(duration) || duration <= TimeEpsilon)
                {
                    duration = Math.Min(remaining, Math.Max(TimeEpsilon * 10, DistanceEpsilon / route.Route.Speed));
                }
                double nextProgress = progress + route.Route.Speed * duration;
                if (!route.Route.Loop) nextProgress = Math.Min(route.Length, nextProgress);
                AddMotion(motion, route, progress, nextProgress, elapsed, elapsed + duration, delta);
                route.Distance = nextProgress;
                elapsed += duration;
            }
            if (motion.Count == 0) AddMotion(motion, route, route.Distance, route.Distance, 0, delta, delta);
            return motion;
        }

        private void AddMotion(List<MotionSegment> motion, RouteRuntime route, double from, double to,
            double startTime, double endTime, double delta)
        {
            double denominator = delta > 0 ? delta : 1;
            motion.Add(new MotionSegment
            {
                StartFraction = startTime / denominator,
                EndFraction = endTime / denominator,
                Start = PositionAt(route, from),
                End = PositionAt(route, to)
            });
        }

        private void ResetMotion(double delta)
        {
            lastMotion.Clear();
            for (int i = 0; i < routes.Count; i++)
            {
                RouteRuntime route = routes[i];
                Point2 position = PositionAt(route, route.Distance);
                var motion = new List<MotionSegment>();
                motion.Add(new MotionSegment { StartFraction = 0, EndFraction = 1, Start = position, End = position });
                lastMotion.Add(route.Route.ObjectId, motion);
            }
        }

        private double NextStopLine(RouteRuntime route, double progress)
        {
            double line = route.Route.Loop ? PositiveModulo(route.Route.StopLineDistance.Value, route.Length) : route.Route.StopLineDistance.Value;
            if (!route.Route.Loop)
            {
                if (Math.Abs(progress - line) <= DistanceEpsilon) return progress;
                return progress < line - DistanceEpsilon ? line : double.PositiveInfinity;
            }
            if (progress < line - DistanceEpsilon) return line;
            double passedLaps = (progress - line) / route.Length;
            if (Math.Abs(progress - line) <= DistanceEpsilon ||
                (passedLaps >= 0 && Math.Abs(passedLaps - Math.Round(passedLaps)) <= DistanceEpsilon / Math.Max(1, route.Length)))
                return progress;
            double laps = Math.Floor((progress - line) / route.Length) + 1;
            return line + laps * route.Length;
        }

        private static double NextVertexDistance(RouteRuntime route, double progress)
        {
            if (!route.Route.Loop)
            {
                for (int i = 1; i < route.VertexDistances.Count; i++)
                    if (route.VertexDistances[i] > progress + DistanceEpsilon) return route.VertexDistances[i];
                return double.PositiveInfinity;
            }
            double lap = Math.Floor(progress / route.Length);
            double local = progress - lap * route.Length;
            for (int i = 1; i < route.VertexDistances.Count; i++)
                if (route.VertexDistances[i] > local + DistanceEpsilon) return lap * route.Length + route.VertexDistances[i];
            return (lap + 1) * route.Length;
        }

        private string PhaseAt(double at)
        {
            double green = config.SignalGreenSeconds;
            double clearance = config.SignalClearanceSeconds;
            double period = 2 * green + 2 * clearance;
            double phase = PositiveModulo(at, period);
            if (phase < green) return "ns";
            if (phase < green + clearance) return "clearance";
            if (phase < 2 * green + clearance) return "ew";
            return "clearance";
        }

        private void SyncSignalSources()
        {
            string phase = PhaseAt(time);
            for (int i = 0; i < objectIds.Count; i++)
            {
                string id = objectIds[i];
                StreetObject item = objects[id];
                bool active;
                if (StringComparer.Ordinal.Equals(item.SoundId, "street.signal.ns")) active = phase == "ns";
                else if (StringComparer.Ordinal.Equals(item.SoundId, "street.signal.ew")) active = phase == "ew";
                else continue;
                if (item.SoundActive != active) objects[id] = CopyObject(item, item.Position, active);
            }
        }

        private bool IsGreen(string axis, double at)
        {
            string phase = PhaseAt(at);
            return StringComparer.Ordinal.Equals(axis, phase);
        }

        private double NextPhaseChange(double at)
        {
            double green = config.SignalGreenSeconds;
            double clearance = config.SignalClearanceSeconds;
            double period = 2 * green + 2 * clearance;
            double phase = PositiveModulo(at, period);
            double boundary;
            if (phase < green) boundary = green;
            else if (phase < green + clearance) boundary = green + clearance;
            else if (phase < 2 * green + clearance) boundary = 2 * green + clearance;
            else boundary = period;
            return at + Math.Max(TimeEpsilon, boundary - phase);
        }

        private static RouteRuntime CreateRoute(StreetRoute route)
        {
            var runtime = new RouteRuntime { Route = route };
            for (int i = 0; i < route.Points.Count; i++)
            {
                if (runtime.Vertices.Count == 0 || Distance(runtime.Vertices[runtime.Vertices.Count - 1], route.Points[i]) > DistanceEpsilon)
                    runtime.Vertices.Add(route.Points[i]);
            }
            if (route.Loop && runtime.Vertices.Count > 1 && Distance(runtime.Vertices[0], runtime.Vertices[runtime.Vertices.Count - 1]) <= DistanceEpsilon)
                runtime.Vertices.RemoveAt(runtime.Vertices.Count - 1);
            if (runtime.Vertices.Count < 2) throw new ArgumentException("Route needs at least two distinct points: " + route.ObjectId);

            runtime.VertexDistances.Add(0);
            double length = 0;
            for (int i = 1; i < runtime.Vertices.Count; i++)
            {
                length += Distance(runtime.Vertices[i - 1], runtime.Vertices[i]);
                runtime.VertexDistances.Add(length);
            }
            if (route.Loop) length += Distance(runtime.Vertices[runtime.Vertices.Count - 1], runtime.Vertices[0]);
            if (!(length > DistanceEpsilon) || double.IsInfinity(length)) throw new ArgumentException("Route length must be positive: " + route.ObjectId);
            runtime.Length = length;
            return runtime;
        }

        private static Point2 PositionAt(RouteRuntime route, double distance)
        {
            double d = route.Route.Loop ? PositiveModulo(distance, route.Length) : Clamp(distance, 0, route.Length);
            for (int i = 1; i < route.Vertices.Count; i++)
            {
                double end = route.VertexDistances[i];
                if (d <= end + DistanceEpsilon)
                {
                    double start = route.VertexDistances[i - 1];
                    double t = end - start <= DistanceEpsilon ? 0 : (d - start) / (end - start);
                    return Lerp(route.Vertices[i - 1], route.Vertices[i], (float)Clamp(t, 0, 1));
                }
            }
            if (route.Route.Loop)
            {
                double start = route.VertexDistances[route.VertexDistances.Count - 1];
                double end = route.Length;
                double t = end - start <= DistanceEpsilon ? 0 : (d - start) / (end - start);
                return Lerp(route.Vertices[route.Vertices.Count - 1], route.Vertices[0], (float)Clamp(t, 0, 1));
            }
            return route.Vertices[route.Vertices.Count - 1];
        }

        private static StreetObject CopyObject(StreetObject source, Point2 position)
        {
            return CopyObject(source, position, source.SoundActive);
        }

        private static StreetObject CopyObject(StreetObject source, Point2 position, bool soundActive)
        {
            Point2 shift = Subtract(position, source.Position);
            var polygon = new List<Point2>(source.Polygon.Count);
            for (int i = 0; i < source.Polygon.Count; i++) polygon.Add(Add(source.Polygon[i], shift));
            return new StreetObject(source.Id, source.Kind, position, polygon, source.MaterialId, source.Solid,
                source.VehicleHazard, source.BlocksProbe, source.AcousticLoss, source.Height, source.SoundId,
                soundActive, source.Semantic, source.ContactDamage, source.ContactStun, source.ActorKind);
        }

        private static Point2[] LocalPolygon(StreetObject item)
        {
            var result = new Point2[item.Polygon.Count];
            for (int i = 0; i < result.Length; i++) result[i] = Subtract(item.Polygon[i], item.Position);
            return result;
        }

        private static void ValidateObject(StreetObject item)
        {
            if (item == null) throw new ArgumentException("Object cannot be null", "objects");
            if (float.IsNaN(item.Height) || float.IsInfinity(item.Height) || item.Height < 0)
                throw new ArgumentException("Object height must be finite and non-negative: " + item.Id);
            if (float.IsNaN(item.AcousticLoss) || float.IsInfinity(item.AcousticLoss) || item.AcousticLoss < 0 || item.AcousticLoss > 1)
                throw new ArgumentException("Acoustic loss must be between 0 and 1: " + item.Id);
            if ((item.ContactDamage.HasValue && (float.IsNaN(item.ContactDamage.Value) || float.IsInfinity(item.ContactDamage.Value) || item.ContactDamage.Value < 0)) ||
                (item.ContactStun.HasValue && (float.IsNaN(item.ContactStun.Value) || float.IsInfinity(item.ContactStun.Value) || item.ContactStun.Value < 0)))
                throw new ArgumentException("Contact effects must be finite and non-negative: " + item.Id);
            if ((item.Solid || item.VehicleHazard || item.BlocksProbe) && item.Polygon.Count < 3)
                throw new ArgumentException("Physical or occluding objects need a polygon: " + item.Id);
        }

        private StreetObject GetObject(string id)
        {
            ContractGuard.Id(id);
            StreetObject item;
            if (!objects.TryGetValue(id, out item)) throw new KeyNotFoundException(id);
            return item;
        }

        private static void ValidateRadius(float radius)
        {
            if (radius < 0 || float.IsNaN(radius) || float.IsInfinity(radius)) throw new ArgumentOutOfRangeException("radius");
        }

        private static bool SweepCirclePolygon(Point2 from, Point2 to, float radius, IReadOnlyList<Point2> polygon,
            out float hitTime, out Point2 contact, out Point2 normal)
        {
            hitTime = 0; contact = from; normal = new Point2(0, 1);
            if (polygon == null || polygon.Count < 3) return false;
            Point2 nearest;
            if (CircleOverlapsPolygon(from, radius, polygon, out nearest))
            {
                contact = nearest;
                normal = CollisionNormal(from, nearest, polygon);
                return true;
            }

            Point2 movement = Subtract(to, from);
            if (Length(movement) <= DistanceEpsilon) return false;
            float best = float.PositiveInfinity;
            for (int i = 0; i < polygon.Count; i++)
            {
                Point2 a = polygon[i], b = polygon[(i + 1) % polygon.Count];
                Point2 edge = Subtract(b, a);
                float edgeLength = Length(edge);
                if (edgeLength <= DistanceEpsilon) continue;

                float d0 = Cross(edge, Subtract(from, a)) / edgeLength;
                float dv = Cross(edge, movement) / edgeLength;
                if (Math.Abs(dv) > 1e-8f)
                {
                    float[] targets = radius <= DistanceEpsilon ? new[] { 0f } : new[] { -radius, radius };
                    for (int k = 0; k < targets.Length; k++)
                    {
                        float t = (targets[k] - d0) / dv;
                        if (t < -DistanceEpsilon || t > 1 + DistanceEpsilon || t >= best) continue;
                        Point2 center = Lerp(from, to, Clamp01(t));
                        float projection = Dot(Subtract(center, a), edge) / Dot(edge, edge);
                        if (projection < -DistanceEpsilon || projection > 1 + DistanceEpsilon) continue;
                        Point2 q = ClosestPoint(center, new Segment2(a, b));
                        if (Distance(center, q) <= radius + 0.0001f) best = Clamp01(t);
                    }
                }

                float endpointTime;
                if (SweepPointCircle(from, movement, a, radius, out endpointTime) && endpointTime < best) best = endpointTime;
                if (SweepPointCircle(from, movement, b, radius, out endpointTime) && endpointTime < best) best = endpointTime;
            }
            if (float.IsPositiveInfinity(best)) return false;
            hitTime = best;
            Point2 at = Lerp(from, to, best);
            contact = ClosestBoundaryPoint(at, polygon);
            normal = CollisionNormal(at, contact, polygon);
            return true;
        }

        private static bool CircleOverlapsPolygon(Point2 center, float radius, IReadOnlyList<Point2> polygon, out Point2 nearest)
        {
            nearest = ClosestBoundaryPoint(center, polygon);
            return PointInPolygon(center, polygon) || Distance(center, nearest) <= radius + DistanceEpsilon;
        }

        private static float OverlapDepth(Point2 center, float radius, IReadOnlyList<Point2> polygon)
        {
            float distance = Distance(center, ClosestBoundaryPoint(center, polygon));
            return PointInPolygon(center, polygon) ? radius + distance : radius - distance;
        }

        private static bool IsMovingIntoContact(Point2 start, Point2 end, float radius,
            IReadOnlyList<Point2> polygon)
        {
            Point2 relativeMotion = Subtract(end, start);
            float distance = Length(relativeMotion);
            if (distance <= DistanceEpsilon) return false;

            // A sweep that begins overlapped reports t=0 and cannot tell an entering vehicle
            // from one leaving the contact. Compare penetration a short distance into this
            // same straight motion segment, small enough to stay near the contact onset even
            // when a fast vehicle crosses the whole player body before the segment ends.
            float probeDistance = Math.Min(ContactSkin, distance * 0.25f);
            if (probeDistance <= DistanceEpsilon) return false;
            Point2 probe = Lerp(start, end, probeDistance / distance);
            return OverlapDepth(probe, radius, polygon) >
                OverlapDepth(start, radius, polygon) + DistanceEpsilon;
        }

        private static bool SweepPointCircle(Point2 origin, Point2 movement, Point2 center, float radius, out float time)
        {
            Point2 relative = Subtract(origin, center);
            float a = Dot(movement, movement);
            float b = 2 * Dot(relative, movement);
            float c = Dot(relative, relative) - radius * radius;
            time = 0;
            if (c <= 0) return true;
            float discriminant = b * b - 4 * a * c;
            if (a <= 1e-12f || discriminant < 0) return false;
            float t = (-b - (float)Math.Sqrt(discriminant)) / (2 * a);
            if (t < -DistanceEpsilon || t > 1 + DistanceEpsilon) return false;
            time = Clamp01(t);
            return true;
        }

        private static Point2 ClosestBoundaryPoint(Point2 point, IReadOnlyList<Point2> polygon)
        {
            Point2 best = polygon[0];
            float bestDistance = float.PositiveInfinity;
            for (int i = 0; i < polygon.Count; i++)
            {
                Point2 candidate = ClosestPoint(point, new Segment2(polygon[i], polygon[(i + 1) % polygon.Count]));
                float d = Distance(point, candidate);
                if (d < bestDistance) { bestDistance = d; best = candidate; }
            }
            return best;
        }

        private static Point2 CollisionNormal(Point2 center, Point2 contact, IReadOnlyList<Point2> polygon)
        {
            Point2 delta = Subtract(center, contact);
            float length = Length(delta);
            if (length > DistanceEpsilon)
            {
                Point2 result = Scale(delta, 1 / length);
                return PointInPolygon(center, polygon) ? Scale(result, -1) : result;
            }
            Point2 closestEdge = Subtract(polygon[1], polygon[0]);
            float best = float.PositiveInfinity;
            for (int i = 0; i < polygon.Count; i++)
            {
                Point2 a = polygon[i], b = polygon[(i + 1) % polygon.Count];
                Point2 edge = Subtract(b, a);
                float d = Distance(contact, ClosestPoint(contact, new Segment2(a, b)));
                if (d < best) { best = d; closestEdge = edge; }
            }
            float edgeLength = Length(closestEdge);
            if (edgeLength <= DistanceEpsilon) return new Point2(0, 1);
            float winding = StreetGeometry.Area(polygon);
            float sign = winding >= 0 ? 1 : -1;
            return new Point2(sign * closestEdge.Y / edgeLength, -sign * closestEdge.X / edgeLength);
        }

        private static bool PointInPolygon(Point2 point, IReadOnlyList<Point2> polygon)
        {
            bool inside = false;
            for (int i = 0, j = polygon.Count - 1; i < polygon.Count; j = i++)
            {
                Point2 a = polygon[i], b = polygon[j];
                if (Distance(point, ClosestPoint(point, new Segment2(a, b))) <= DistanceEpsilon) return true;
                if ((a.Y > point.Y) != (b.Y > point.Y) &&
                    point.X < (b.X - a.X) * (point.Y - a.Y) / (b.Y - a.Y) + a.X) inside = !inside;
            }
            return inside;
        }

        private static Point2 ClosestPoint(Point2 p, Segment2 segment)
        {
            Point2 ab = Subtract(segment.B, segment.A);
            float lengthSq = Dot(ab, ab);
            if (lengthSq <= 1e-12f) return segment.A;
            float t = Clamp01(Dot(Subtract(p, segment.A), ab) / lengthSq);
            return Add(segment.A, Scale(ab, t));
        }

        private static Point2 Add(Point2 a, Point2 b) { return new Point2(a.X + b.X, a.Y + b.Y); }
        private static Point2 Subtract(Point2 a, Point2 b) { return new Point2(a.X - b.X, a.Y - b.Y); }
        private static Point2 Scale(Point2 p, float scale) { return new Point2(p.X * scale, p.Y * scale); }
        private static Point2 Lerp(Point2 a, Point2 b, float t) { return new Point2(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t); }
        private static float Dot(Point2 a, Point2 b) { return a.X * b.X + a.Y * b.Y; }
        private static float Cross(Point2 a, Point2 b) { return a.X * b.Y - a.Y * b.X; }
        private static float Length(Point2 p) { return (float)Math.Sqrt(p.X * p.X + p.Y * p.Y); }
        private static float Distance(Point2 a, Point2 b) { return Length(Subtract(a, b)); }
        private static float Clamp01(float value) { return Math.Max(0, Math.Min(1, value)); }
        private static double Clamp(double value, double min, double max) { return Math.Max(min, Math.Min(max, value)); }
        private static double PositiveModulo(double value, double modulo)
        { double result = value % modulo; return result < 0 ? result + modulo : result; }
    }
}
