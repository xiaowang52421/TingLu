using System;
using System.Collections.Generic;
using NUnit.Framework;
using TingLu.Config;
using TingLu.Contracts;
using TingLu.Levels;
using TingLu.World;

namespace TingLu.Tests.Upgrade.Role09
{
    public sealed class StreetLevelFactoryTests
    {
        [Test]
        public void FactoryBuildsContractValidLevelAndStableStreetCatalogIds()
        {
            StreetLevel level = StreetLevelFactory.Create();
            var world = new StreetWorld(level.Surfaces, level.Objects, level.Routes, new StreetConfig());
            var ids = new HashSet<string>(StringComparer.Ordinal);
            var surfaceIds = new HashSet<string>(StringComparer.Ordinal);

            Assert.AreEqual("street.crossroads.v1", level.Id);
            Assert.AreEqual("十字路口", level.Title);
            Assert.AreEqual(new Point2(-5.5f, -16), level.Spawn.Position);
            Assert.AreEqual(2, level.Checkpoints.Count);
            Assert.AreEqual(new Point2(-5.5f, 5.3f), level.Checkpoints[0].Position);
            Assert.AreEqual(new Point2(5.5f, 5.3f), level.Checkpoints[1].Position);
            Assert.AreEqual(new Point2(9.45f, 18), level.Goal);
            Assert.Greater(level.GoalRadius, 0);
            Assert.AreEqual("ns", world.SignalPhase);

            for (int i = 0; i < level.Surfaces.Count; i++)
            {
                StreetSurface surface = level.Surfaces[i];
                Assert.IsTrue(surfaceIds.Add(surface.Id), "Duplicate surface id: " + surface.Id);
                Assert.IsTrue(Contains(StreetCatalog.Materials, surface.MaterialId), "Unknown surface material: " + surface.MaterialId);
            }

            for (int i = 0; i < level.Objects.Count; i++)
            {
                StreetObject item = level.Objects[i];
                Assert.IsTrue(ids.Add(item.Id), "Duplicate object id: " + item.Id);
                Assert.IsTrue(Contains(StreetCatalog.Materials, item.MaterialId), "Unknown material: " + item.MaterialId);
                if (item.SoundId != null)
                    Assert.IsTrue(Contains(StreetCatalog.Sounds, item.SoundId), "Unknown sound: " + item.SoundId);
                if (item.Id.StartsWith("building.", StringComparison.Ordinal))
                    Assert.AreEqual(0.7f, item.AcousticLoss, 0.0001f, "Building attenuation is a tuning baseline, not zero");
            }

            StreetObject parkedCar = Find(world.Capture(), "vehicle.parked.se");
            Assert.IsTrue(parkedCar.Solid);
            Assert.IsFalse(parkedCar.VehicleHazard, "A parked car is an ordinary static collision, not a traffic accident");
            Assert.AreEqual(15f, parkedCar.ContactDamage.Value, 0.0001f);

            Assert.IsNotNull(Find(world.Capture(), "street.signal.ns"));
            Assert.IsNotNull(Find(world.Capture(), "street.signal.ew"));
            Assert.IsTrue(Find(world.Capture(), "street.signal.ns").SoundActive);
            Assert.IsFalse(Find(world.Capture(), "street.signal.ew").SoundActive);
        }

        [Test]
        public void MainRouteDetoursAroundOccupiedTactilePathsAndReachesBothCheckpointsAndGoal()
        {
            StreetLevel level = StreetLevelFactory.Create();
            var config = new StreetConfig();
            var world = new StreetWorld(level.Surfaces, level.Objects, level.Routes, config);
            Point2 player = level.Spawn.Position;

            AssertSafe(world, config, player, "spawn");
            Walk(world, config, ref player, new Point2(-6.65f, -11.2f));
            Walk(world, config, ref player, new Point2(-6.65f, -8.8f));
            Walk(world, config, ref player, new Point2(-5.5f, -7f));
            Walk(world, config, ref player, level.Checkpoints[0].Position);
            Assert.AreEqual(0, StreetGeometry.Distance(player, level.Checkpoints[0].Position), 0.001f);
            AssertSafe(world, config, player, "west approach checkpoint");

            Walk(world, config, ref player, level.Checkpoints[1].Position);
            Assert.AreEqual(0, StreetGeometry.Distance(player, level.Checkpoints[1].Position), 0.001f);
            AssertSafe(world, config, player, "east landing checkpoint");

            Walk(world, config, ref player, new Point2(5.5f, 7.8f));
            Walk(world, config, ref player, new Point2(6.65f, 8.2f));
            Walk(world, config, ref player, new Point2(8.9f, 8.2f));
            Walk(world, config, ref player, level.Goal);
            Assert.AreEqual(0, StreetGeometry.Distance(player, level.Goal), 0.001f);
            AssertSafe(world, config, player, "alley goal");
        }

        [Test]
        public void SignalRoutesStopBeforeCrosswalksWithVehicleNoseAndMarginClearance()
        {
            StreetLevel level = StreetLevelFactory.Create();
            var world = new StreetWorld(level.Surfaces, level.Objects, level.Routes, new StreetConfig());
            StreetWorldFrame atStart = world.Capture();
            List<StreetRoute> signalVehicleRoutes = GetSignalControlledVehicleRoutes(level, atStart);

            Assert.AreEqual(6, level.Routes.Count, "Four signal-controlled vehicles and two non-signal active routes should be retained.");
            Assert.AreEqual(4, signalVehicleRoutes.Count);
            AssertSignalVehicleRouteIds(signalVehicleRoutes);
            AssertNonSignalActiveRoute(level, atStart, "rider.east.sidewalk", new Point2(12, -5.55f), true);
            AssertNonSignalActiveRoute(level, atStart, "walker.north.west", new Point2(-5.5f, 14), false);
            for (int i = 0; i < signalVehicleRoutes.Count; i++)
            {
                StreetRoute route = signalVehicleRoutes[i];
                Assert.IsNotNull(route.SignalAxis);
                Assert.IsTrue(route.StopLineDistance.HasValue);
                Assert.AreEqual(StreetLevelFactory.StopLineDistance, route.StopLineDistance.Value, 0.0001f);
                Assert.AreEqual(route.StopLineDistance.Value, route.Offset, 0.0001);
                Assert.IsTrue(route.Loop, "Street traffic must continue after clearing the intersection.");

                StreetObject vehicle = Find(atStart, route.ObjectId);
                Assert.IsNotNull(vehicle);
                Assert.AreEqual(StreetKind.Active, vehicle.Kind);
                Assert.IsTrue(vehicle.VehicleHazard);
                AssertRightHandLane(route, vehicle.Position, "initial stop-line position");
                float approachLength = StreetGeometry.Distance(route.Points[0], route.Points[1]);
                Assert.Less(route.StopLineDistance.Value, approachLength, "The stop line must be on the approach leg.");
                Point2 expected = PointAtDistance(route, route.StopLineDistance.Value);
                float axisPosition = route.SignalAxis == "ns" ? vehicle.Position.Y : vehicle.Position.X;
                float stopCoordinate = route.SignalAxis == "ns" ? expected.Y : expected.X;
                Assert.AreEqual(stopCoordinate, axisPosition, 0.001f);
                AssertRightHandLane(route, expected, "configured stop line");
                AssertClosedRouteReturnsOutsidePlayableCore(route);
            }

            world.Tick(0.1, false);
            StreetWorldFrame duringNorthSouthGreen = world.Capture();
            Assert.AreEqual(-7.2f, Find(duringNorthSouthGreen, "vehicle.ew.eastbound").Position.X, 0.001f,
                "the red east-west route must wait at its stop line");
            Assert.Less(Find(duringNorthSouthGreen, "vehicle.ns.southbound").Position.Y, 7.2f,
                "the green north-south route must enter the intersection");
            AssertRightHandLanePositions(signalVehicleRoutes, duringNorthSouthGreen, "north-south green movement");
            world.Tick(13.9, false);
            Assert.AreEqual("clearance", world.SignalPhase);
            Assert.IsFalse(Find(world.Capture(), "street.signal.ns").SoundActive);
            Assert.IsFalse(Find(world.Capture(), "street.signal.ew").SoundActive);
            world.Tick(3, false);
            Assert.AreEqual("ew", world.SignalPhase);
            Assert.IsTrue(Find(world.Capture(), "street.signal.ew").SoundActive);
            world.Tick(0.1, false);
            StreetWorldFrame duringEastWestGreen = world.Capture();
            Assert.Greater(Find(duringEastWestGreen, "vehicle.ew.eastbound").Position.X, -7.2f,
                "the east-west route must leave its stop line on green");
            AssertRightHandLane(FindRoute(level, "vehicle.ew.eastbound"),
                Find(duringEastWestGreen, "vehicle.ew.eastbound").Position, "eastbound green movement");
            AssertRightHandLane(FindRoute(level, "vehicle.ew.westbound"),
                Find(duringEastWestGreen, "vehicle.ew.westbound").Position, "westbound green movement");
        }

        [Test]
        public void VehiclesKeepCirculatingOnOneWayRoutesAcrossTwoSignalCyclesWithoutCrossingBuildings()
        {
            StreetLevel level = StreetLevelFactory.Create();
            var world = new StreetWorld(level.Surfaces, level.Objects, level.Routes, new StreetConfig());
            StreetWorldFrame initial = world.Capture();
            var initialPositions = new Dictionary<string, Point2>(StringComparer.Ordinal);
            var priorPositions = new Dictionary<string, Point2>(StringComparer.Ordinal);
            var movedDuringCycles = new HashSet<string>(StringComparer.Ordinal);
            List<StreetRoute> signalVehicleRoutes = GetSignalControlledVehicleRoutes(level, initial);

            Assert.AreEqual(6, level.Routes.Count, "Non-signal active routes must remain in the traffic fixture.");
            Assert.AreEqual(4, signalVehicleRoutes.Count);
            AssertSignalVehicleRouteIds(signalVehicleRoutes);
            AssertNonSignalActiveRoute(level, initial, "rider.east.sidewalk", new Point2(12, -5.55f), true);
            AssertNonSignalActiveRoute(level, initial, "walker.north.west", new Point2(-5.5f, 14), false);
            for (int i = 0; i < signalVehicleRoutes.Count; i++)
            {
                StreetRoute route = signalVehicleRoutes[i];
                StreetObject vehicle = Find(initial, route.ObjectId);
                initialPositions.Add(route.ObjectId, vehicle.Position);
                priorPositions.Add(route.ObjectId, vehicle.Position);
                double routeLength = RouteLength(route);
                Assert.Greater(routeLength, route.StopLineDistance.Value);
                AssertRoutesClearOfBuildings(route, initial);
            }

            const int twoCyclesAndMargin = 3410; // 68.2 simulated seconds at a fixed 20 ms step.
            for (int i = 0; i < twoCyclesAndMargin; i++)
            {
                world.Tick(0.02, false);
                StreetWorldFrame frame = world.Capture();
                for (int j = 0; j < signalVehicleRoutes.Count; j++)
                {
                    string id = signalVehicleRoutes[j].ObjectId;
                    Point2 current = Find(frame, id).Position;
                    if (StreetGeometry.Distance(priorPositions[id], current) > 0.001f)
                        movedDuringCycles.Add(id);
                    priorPositions[id] = current;
                }
            }

            Assert.AreEqual("ns", world.SignalPhase, "The 34-second signal cycle must complete twice.");
            StreetTrafficState traffic = world.ExportTraffic();
            StreetWorldFrame after = world.Capture();
            Assert.AreEqual(level.Routes.Count, traffic.ObjectIds.Count);
            AssertNonSignalActiveRoute(level, after, "rider.east.sidewalk", new Point2(22, -5.55f), true);
            AssertNonSignalActiveRoute(level, after, "walker.north.west", new Point2(-5.5f, 21), false);
            IndexOf(traffic.ObjectIds, "rider.east.sidewalk");
            IndexOf(traffic.ObjectIds, "walker.north.west");
            for (int i = 0; i < signalVehicleRoutes.Count; i++)
            {
                StreetRoute route = signalVehicleRoutes[i];
                StreetObject vehicle = Find(after, route.ObjectId);
                Assert.IsTrue(movedDuringCycles.Contains(route.ObjectId),
                    route.ObjectId + " must move during the two signal cycles.");
                Assert.Greater(StreetGeometry.Distance(initialPositions[route.ObjectId], vehicle.Position), 0.5f,
                    route.ObjectId + " must still be moving after two signal cycles.");
                int trafficIndex = IndexOf(traffic.ObjectIds, route.ObjectId);
                Assert.Greater(traffic.Distances[trafficIndex] - route.Offset, RouteLength(route) * 0.75,
                    route.ObjectId + " must have traversed most of its directed loop across two signal cycles.");
                AssertRoutesClearOfBuildings(route, after);
            }
        }

        [Test]
        public void EightRampsConnectRoadAndSidewalkAtTheCorrectPublicEdges()
        {
            StreetLevel level = StreetLevelFactory.Create();
            var ramps = new List<StreetSurface>();
            for (int i = 0; i < level.Surfaces.Count; i++)
                if (level.Surfaces[i].MaterialId == "ramp") ramps.Add(level.Surfaces[i]);

            Assert.AreEqual(8, ramps.Count, "The crossroads must expose all four corner crossings as paired ramps.");
            for (int i = 0; i < ramps.Count; i++)
            {
                StreetSurface ramp = ramps[i];
                bool northSouthCrossing = ramp.Id.StartsWith("ramp.ns.", StringComparison.Ordinal);
                bool west = ramp.Id.IndexOf(".west", StringComparison.Ordinal) >= 0;
                Assert.IsTrue(northSouthCrossing || ramp.Id.StartsWith("ramp.ew.", StringComparison.Ordinal),
                    "Unexpected ramp id: " + ramp.Id);

                Point2 roadEdge;
                Point2 sidewalkEdge;
                if (northSouthCrossing)
                {
                    // The north/south crosswalks run along X.  Their paired
                    // ramps cross the west/east curb strips into the NS road.
                    float y = ramp.Id.IndexOf(".north.", StringComparison.Ordinal) >= 0 ? 5.2f : -5.2f;
                    roadEdge = new Point2(west ? -3.8f : 3.8f, y);
                    sidewalkEdge = new Point2(west ? -4.5f : 4.5f, y);
                    Assert.AreEqual(0f, ramp.HeightSlopeY, 0.00001f,
                        ramp.Id + " must not slope along the crosswalk's Y-constant edge.");
                    Assert.Greater(Math.Abs(ramp.HeightSlopeX), 0.00001f,
                        ramp.Id + " must slope across the west/east curb.");
                    Assert.AreEqual(0f, Surface(level, "road.ns").HeightAt(roadEdge), 0.0001f);
                    Assert.AreEqual(0.15f, Surface(level, west ? "sidewalk.west" : "sidewalk.east").HeightAt(sidewalkEdge), 0.0001f);
                }
                else
                {
                    // The west/east crosswalks run along Y.  Their paired
                    // ramps cross the south/north curb strips into the EW road.
                    float x = west ? -5.2f : 5.2f;
                    bool south = ramp.Id.IndexOf(".south", StringComparison.Ordinal) >= 0;
                    roadEdge = new Point2(x, south ? -3.8f : 3.8f);
                    sidewalkEdge = new Point2(x, south ? -4.5f : 4.5f);
                    Assert.AreEqual(0f, ramp.HeightSlopeX, 0.00001f,
                        ramp.Id + " must not slope along the crosswalk's X-constant edge.");
                    Assert.Greater(Math.Abs(ramp.HeightSlopeY), 0.00001f,
                        ramp.Id + " must slope across the south/north curb.");
                    Assert.AreEqual(0f, Surface(level, "road.ew").HeightAt(roadEdge), 0.0001f);
                    Assert.AreEqual(0.15f, Surface(level, south ? "sidewalk.south" : "sidewalk.north").HeightAt(sidewalkEdge), 0.0001f);
                }

                Assert.AreEqual(0f, ramp.HeightAt(roadEdge), 0.0001f,
                    ramp.Id + " road edge must meet the road at height 0.");
                Assert.AreEqual(0.15f, ramp.HeightAt(sidewalkEdge), 0.0001f,
                    ramp.Id + " sidewalk edge must meet the sidewalk at height 0.15.");
                Assert.AreEqual(0f, EffectiveHeight(level, roadEdge), 0.0001f,
                    ramp.Id + " must meet the winning road/crosswalk surface at its low edge.");
                Assert.AreEqual(0.15f, EffectiveHeight(level, sidewalkEdge), 0.0001f,
                    ramp.Id + " must meet the winning sidewalk surface at its high edge.");
                Point2 midpoint = new Point2((roadEdge.X + sidewalkEdge.X) * 0.5f,
                    (roadEdge.Y + sidewalkEdge.Y) * 0.5f);
                Assert.AreEqual(0.075f, ramp.HeightAt(midpoint), 0.0001f,
                    ramp.Id + " midpoint must be halfway up the ramp.");
                Assert.AreEqual(0.075f, EffectiveHeight(level, midpoint), 0.0001f,
                    ramp.Id + " midpoint must win the surface overlay used by the crossing.");
            }
        }

        [Test]
        public void EveryCrosswalkDirectionIsWalkableWithAContinuousEffectiveHeightProfile()
        {
            StreetLevel level = StreetLevelFactory.Create();
            var world = new StreetWorld(level.Surfaces, level.Objects, level.Routes, new StreetConfig());
            var crossings = new[]
            {
                new Segment2(new Point2(-6.3f, 5.2f), new Point2(6.3f, 5.2f)),
                new Segment2(new Point2(-6.3f, -5.2f), new Point2(6.3f, -5.2f)),
                new Segment2(new Point2(-5.2f, -6.3f), new Point2(-5.2f, 6.3f)),
                new Segment2(new Point2(5.2f, -6.3f), new Point2(5.2f, 6.3f))
            };
            var config = new StreetConfig();
            for (int i = 0; i < crossings.Length; i++)
            {
                Segment2 crossing = crossings[i];
                AssertWalkable(world, config, crossing.A, crossing.B);
                float previous = EffectiveHeight(level, crossing.A);
                float maximumJump = 0;
                const int samples = 630;
                for (int sample = 1; sample <= samples; sample++)
                {
                    float t = sample / (float)samples;
                    float current = EffectiveHeight(level, StreetGeometry.Lerp(crossing.A, crossing.B, t));
                    maximumJump = Math.Max(maximumJump, Math.Abs(current - previous));
                    previous = current;
                }
                Assert.LessOrEqual(maximumJump, 0.0045f,
                    "Crosswalk " + i + " exceeds the 0.2143 height/unit ramp gradient at 2 cm sampling.");
            }
        }

        private static List<StreetRoute> GetSignalControlledVehicleRoutes(StreetLevel level, StreetWorldFrame frame)
        {
            var result = new List<StreetRoute>();
            for (int i = 0; i < level.Routes.Count; i++)
            {
                StreetRoute route = level.Routes[i];
                if (route.SignalAxis == null && !route.StopLineDistance.HasValue) continue;

                Assert.IsNotNull(route.SignalAxis, route.ObjectId + " has a stop line but no signal axis.");
                Assert.IsTrue(route.StopLineDistance.HasValue, route.ObjectId + " has a signal axis but no stop line.");
                StreetObject vehicle = Find(frame, route.ObjectId);
                Assert.IsNotNull(vehicle, route.ObjectId + " is missing from the world.");
                Assert.AreEqual(StreetKind.Active, vehicle.Kind);
                Assert.IsTrue(vehicle.VehicleHazard, route.ObjectId + " is signal controlled but is not a vehicle hazard.");
                result.Add(route);
            }
            return result;
        }

        private static void AssertSignalVehicleRouteIds(IReadOnlyList<StreetRoute> routes)
        {
            var ids = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < routes.Count; i++) ids.Add(routes[i].ObjectId);
            string[] expectedIds =
            {
                "vehicle.ns.southbound", "vehicle.ns.northbound",
                "vehicle.ew.eastbound", "vehicle.ew.westbound"
            };
            for (int i = 0; i < expectedIds.Length; i++)
                Assert.IsTrue(ids.Contains(expectedIds[i]), "Missing signal-controlled vehicle route " + expectedIds[i]);
        }

        private static void AssertNonSignalActiveRoute(StreetLevel level, StreetWorldFrame frame, string id,
            Point2 expectedPosition, bool expectedVehicleHazard)
        {
            StreetRoute route = FindRoute(level, id);
            Assert.IsNotNull(route, "Missing non-signal route " + id);
            Assert.IsNull(route.SignalAxis, id + " should not be signal controlled.");
            Assert.IsFalse(route.StopLineDistance.HasValue, id + " should not have a stop line.");
            Assert.IsFalse(route.Loop, id + " should preserve its one-way route.");

            StreetObject actor = Find(frame, id);
            Assert.IsNotNull(actor, "Missing active route object " + id);
            Assert.AreEqual(StreetKind.Active, actor.Kind);
            Assert.AreEqual(expectedVehicleHazard, actor.VehicleHazard, id + " has the wrong vehicle-hazard classification.");
            Assert.AreEqual(expectedPosition, actor.Position);
        }

        private static StreetRoute FindRoute(StreetLevel level, string id)
        {
            for (int i = 0; i < level.Routes.Count; i++)
                if (StringComparer.Ordinal.Equals(level.Routes[i].ObjectId, id)) return level.Routes[i];
            return null;
        }

        private static void Walk(StreetWorld world, StreetConfig config, ref Point2 player, Point2 target)
        {
            float distance = StreetGeometry.Distance(player, target);
            if (distance < 0.001f) return;
            Point2 direction = new Point2((target.X - player.X) / distance, (target.Y - player.Y) / distance);
            float speed = config.Gameplay.walkSpeed;
            while (distance > 0.0001f)
            {
                const double step = 0.02;
                float advance = Math.Min(distance, speed * (float)step);
                Point2 requested = new Point2(player.X + direction.X * advance, player.Y + direction.Y * advance);
                world.Tick(step, false);
                StreetMoveResult result = world.Move(player, requested, config.BodyRadius);
                Assert.AreEqual(0, result.Contacts.Count, "Static obstacle blocks route toward " + target.X + "," + target.Y);
                Assert.AreEqual(0, StreetGeometry.Distance(requested, result.Position), 0.005f,
                    "Route was blocked before " + target.X + "," + target.Y);
                IReadOnlyList<StreetContact> vehicles = world.VehicleContacts(player, result.Position, config.BodyRadius);
                Assert.AreEqual(0, vehicles.Count, "Vehicle collision while walking toward " + target.X + "," + target.Y);
                player = result.Position;
                distance = StreetGeometry.Distance(player, target);
            }
        }

        private static void AssertSafe(StreetWorld world, StreetConfig config, Point2 position, string label)
        {
            StreetMoveResult move = world.Move(position, position, config.BodyRadius);
            Assert.AreEqual(0, move.Contacts.Count, label + " overlaps a solid object");
            Assert.AreEqual(0, world.VehicleContacts(position, position, config.BodyRadius).Count,
                label + " overlaps a vehicle");
        }

        private static StreetObject Find(StreetWorldFrame frame, string id)
        {
            for (int i = 0; i < frame.Objects.Count; i++)
                if (frame.Objects[i].Id == id) return frame.Objects[i];
            return null;
        }

        private static StreetSurface Surface(StreetLevel level, string id)
        {
            for (int i = 0; i < level.Surfaces.Count; i++)
                if (StringComparer.Ordinal.Equals(level.Surfaces[i].Id, id)) return level.Surfaces[i];
            Assert.Fail("Missing surface " + id);
            return null;
        }

        private static float EffectiveHeight(StreetLevel level, Point2 point)
        {
            StreetSurface best = null;
            float bestArea = float.PositiveInfinity;
            for (int i = 0; i < level.Surfaces.Count; i++)
            {
                StreetSurface candidate = level.Surfaces[i];
                if (!StreetGeometry.Contains(candidate.Polygon, point)) continue;
                float area = Math.Abs(StreetGeometry.Area(candidate.Polygon));
                if (best == null || candidate.Priority > best.Priority ||
                    (candidate.Priority == best.Priority &&
                     (area < bestArea - StreetGeometry.Epsilon ||
                      (Math.Abs(area - bestArea) <= StreetGeometry.Epsilon &&
                       StringComparer.Ordinal.Compare(candidate.Id, best.Id) < 0))))
                {
                    best = candidate;
                    bestArea = area;
                }
            }
            Assert.IsNotNull(best, "Crosswalk sample does not lie on a walkable surface at " + point.X + "," + point.Y);
            return best.HeightAt(point);
        }

        private static void AssertWalkable(StreetWorld world, StreetConfig config, Point2 from, Point2 to)
        {
            Point2 current = from;
            float distance = StreetGeometry.Distance(from, to);
            Point2 direction = new Point2((to.X - from.X) / distance, (to.Y - from.Y) / distance);
            while (distance > 0.0001f)
            {
                float advance = Math.Min(distance, config.Gameplay.walkSpeed * 0.02f);
                Point2 requested = new Point2(current.X + direction.X * advance, current.Y + direction.Y * advance);
                StreetMoveResult result = world.Move(current, requested, config.BodyRadius);
                Assert.AreEqual(0, result.Contacts.Count,
                    "A crossing lane must remain walkable at " + requested.X + "," + requested.Y);
                Assert.AreEqual(0, StreetGeometry.Distance(requested, result.Position), 0.005f,
                    "A crossing lane was blocked at " + requested.X + "," + requested.Y);
                current = result.Position;
                distance = StreetGeometry.Distance(current, to);
            }
        }

        private static Point2 PointAtDistance(StreetRoute route, double distance)
        {
            double traveled = 0;
            for (int i = 1; i < route.Points.Count; i++)
            {
                Point2 from = route.Points[i - 1];
                Point2 to = route.Points[i];
                double segment = StreetGeometry.Distance(from, to);
                if (distance <= traveled + segment)
                    return StreetGeometry.Lerp(from, to, (float)((distance - traveled) / segment));
                traveled += segment;
            }
            Assert.Fail("Distance lies beyond route length.");
            return new Point2();
        }

        private static double RouteLength(StreetRoute route)
        {
            double length = 0;
            for (int i = 1; i < route.Points.Count; i++)
                length += StreetGeometry.Distance(route.Points[i - 1], route.Points[i]);
            return length;
        }

        private static int IndexOf(IReadOnlyList<string> values, string value)
        {
            for (int i = 0; i < values.Count; i++)
                if (StringComparer.Ordinal.Equals(values[i], value)) return i;
            Assert.Fail("Traffic state omitted route object " + value);
            return -1;
        }

        private static void AssertRightHandLanePositions(IReadOnlyList<StreetRoute> routes,
            StreetWorldFrame frame, string stage)
        {
            for (int i = 0; i < routes.Count; i++)
            {
                StreetRoute route = routes[i];
                AssertRightHandLane(route, Find(frame, route.ObjectId).Position, stage);
            }
        }

        private static void AssertRightHandLane(StreetRoute route, Point2 position, string stage)
        {
            Point2 start = route.Points[0];
            Point2 next = route.Points[1];
            float dx = next.X - start.X;
            float dy = next.Y - start.Y;
            float length = (float)Math.Sqrt(dx * dx + dy * dy);
            Assert.Greater(length, 0, route.ObjectId + " has no approach direction.");

            // With north at +Y, this is the unit vector on the vehicle's right.
            float rightX = dy / length;
            float rightY = -dx / length;
            float signedLaneOffset = position.X * rightX + position.Y * rightY;
            Assert.Greater(signedLaneOffset, 0.5f,
                route.ObjectId + " must stay on the right side of its approach at " + stage + ".");
        }

        private static void AssertClosedRouteReturnsOutsidePlayableCore(StreetRoute route)
        {
            Assert.IsTrue(route.Loop, route.ObjectId + " must be a closed traffic route.");
            var playableCore = StreetGeometry.Rect(-23.9f, -23.9f, 23.9f, 23.9f);
            for (int i = 2; i < route.Points.Count; i++)
            {
                var segment = new Segment2(route.Points[i - 1], route.Points[i]);
                Segment2 clipped;
                Assert.IsFalse(StreetGeometry.ClipSegment(segment, playableCore, out clipped),
                    route.ObjectId + " return segment " + (i - 1) + " re-enters the playable road/building area.");
            }
        }

        private static void AssertRoutesClearOfBuildings(StreetRoute route, StreetWorldFrame frame)
        {
            for (int i = 1; i < route.Points.Count; i++)
            {
                var segment = new Segment2(route.Points[i - 1], route.Points[i]);
                for (int j = 0; j < frame.Objects.Count; j++)
                {
                    StreetObject item = frame.Objects[j];
                    if (item.Kind != StreetKind.Permanent || !item.Solid) continue;
                    float minX = item.Polygon[0].X, maxX = minX;
                    float minY = item.Polygon[0].Y, maxY = minY;
                    for (int k = 1; k < item.Polygon.Count; k++)
                    {
                        minX = Math.Min(minX, item.Polygon[k].X);
                        maxX = Math.Max(maxX, item.Polygon[k].X);
                        minY = Math.Min(minY, item.Polygon[k].Y);
                        maxY = Math.Max(maxY, item.Polygon[k].Y);
                    }
                    var expanded = StreetGeometry.Rect(minX - 0.85f, minY - 0.85f, maxX + 0.85f, maxY + 0.85f);
                    Segment2 clipped;
                    Assert.IsFalse(StreetGeometry.ClipSegment(segment, expanded, out clipped),
                        route.ObjectId + " route intersects building clearance at " + item.Id);
                }
            }
        }

        private static bool Contains(string[] values, string value)
        {
            for (int i = 0; i < values.Length; i++)
                if (StringComparer.Ordinal.Equals(values[i], value)) return true;
            return false;
        }
    }
}
