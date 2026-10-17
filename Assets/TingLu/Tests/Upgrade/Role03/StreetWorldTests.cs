using System;
using System.Collections.Generic;
using NUnit.Framework;
using TingLu.Config;
using TingLu.Contracts;
using TingLu.Levels;
using TingLu.World;

namespace TingLu.Tests.Upgrade.Role03
{
    public sealed class StreetWorldTests
    {
        private static StreetObject Box(string id, StreetKind kind, Point2 position, float halfWidth, float halfHeight,
            bool solid = false, bool vehicleHazard = false, bool blocksProbe = false, string sound = null,
            bool soundActive = false, float? damage = null, float? stun = null)
        {
            Point2[] polygon = StreetGeometry.Rect(position.X - halfWidth, position.Y - halfHeight,
                position.X + halfWidth, position.Y + halfHeight);
            return new StreetObject(id, kind, position, polygon, "brick", solid, vehicleHazard, blocksProbe,
                0, 1, sound, soundActive, PerceptionSemantic.Ordinary, damage, stun);
        }

        private static StreetWorld Create(IEnumerable<StreetSurface> surfaces, IEnumerable<StreetObject> objects,
            IEnumerable<StreetRoute> routes, StreetConfig config = null)
        {
            return new StreetWorld(surfaces, objects, routes, config ?? new StreetConfig());
        }

        private static StreetObject Find(StreetWorldFrame frame, string id)
        {
            for (int i = 0; i < frame.Objects.Count; i++) if (frame.Objects[i].Id == id) return frame.Objects[i];
            return null;
        }

        [Test]
        public void MaterialLookupUsesPriorityThenSmallestRegionAndReturnsNullOutside()
        {
            var surfaces = new[]
            {
                new StreetSurface("street", "sidewalk", StreetGeometry.Rect(-5, -5, 5, 5), 0),
                new StreetSurface("tactile", "tactile", StreetGeometry.Rect(-1, -1, 1, 1), 1),
                new StreetSurface("crossing", "crosswalk", StreetGeometry.Rect(-0.5f, -0.5f, 0.5f, 0.5f), 1)
            };
            var world = Create(surfaces, new StreetObject[0], new StreetRoute[0]);

            Assert.AreEqual("crosswalk", world.MaterialAt(new Point2(0, 0)));
            Assert.AreEqual("tactile", world.MaterialAt(new Point2(0.75f, 0)));
            Assert.AreEqual("sidewalk", world.MaterialAt(new Point2(3, 0)));
            Assert.IsNull(world.MaterialAt(new Point2(8, 0)));
        }

        [Test]
        public void MoveSweepsThinWallAndReturnsConfiguredContactEffects()
        {
            var wall = new StreetObject("wall", StreetKind.Permanent, new Point2(0, 0),
                StreetGeometry.Rect(1, -1, 1.1f, 1), "brick", true, false, true, 0, 2,
                null, false, PerceptionSemantic.Ordinary, 12, 0.4f);
            var world = Create(new StreetSurface[0], new[] { wall }, new StreetRoute[0]);

            StreetMoveResult result = world.Move(new Point2(0, 0), new Point2(2, 0), 0.2f);

            Assert.Less(result.Position.X, 0.81f, "a fast request must not tunnel through a thin wall");
            Assert.AreEqual(1, result.Contacts.Count);
            Assert.AreEqual("wall", result.Contacts[0].ObjectId);
            Assert.AreEqual(12, result.Contacts[0].Damage.Value);
            Assert.AreEqual(0.4f, result.Contacts[0].StunSeconds.Value, 0.0001f);
        }

        [Test]
        public void CapturedFrameIsImmutableAndTemporaryObjectsMoveOrDisappearOnlyOnNextCapture()
        {
            var box = Box("box", StreetKind.Temporary, new Point2(1, 0), 0.2f, 0.2f, solid: true);
            var world = Create(new StreetSurface[0], new[] { box }, new StreetRoute[0]);
            StreetWorldFrame before = world.Capture();

            world.MoveObject("box", new Point2(4, 0));
            StreetWorldFrame moved = world.Capture();
            Assert.AreEqual(1, Find(before, "box").Position.X, 0.0001f);
            Assert.AreEqual(4, Find(moved, "box").Position.X, 0.0001f);

            world.RemoveObject("box");
            Assert.IsNotNull(Find(moved, "box"));
            Assert.IsNull(Find(world.Capture(), "box"));
        }

        [Test]
        public void VehicleSweepUsesRelativePlayerMotionAndCannotTunnelAtHighSpeed()
        {
            var car = Box("car", StreetKind.Active, new Point2(-5, 0), 0.25f, 0.1f,
                vehicleHazard: true, sound: "street.car", soundActive: true, damage: 40);
            var route = new StreetRoute("car", new[] { new Point2(-5, 0), new Point2(5, 0) }, 10,
                null, 0, false);
            var world = Create(new StreetSurface[0], new[] { car }, new[] { route });
            world.Tick(1, false);

            IReadOnlyList<StreetContact> contacts = world.VehicleContacts(new Point2(0, -5), new Point2(0, 5), 0.1f);

            Assert.AreEqual(1, contacts.Count);
            Assert.AreEqual("car", contacts[0].ObjectId);
            Assert.IsTrue(contacts[0].Vehicle);
            Assert.AreEqual(40, contacts[0].Damage.Value);
        }

        [Test]
        public void VehicleContactEmitsOnceUntilThePlayerSeparates()
        {
            var car = Box("parked-cycle", StreetKind.Temporary, new Point2(0, 0), 0.5f, 0.3f,
                vehicleHazard: true, damage: 15);
            var world = Create(new StreetSurface[0], new[] { car }, new StreetRoute[0]);

            world.Tick(0.02, false);
            Assert.AreEqual(1, world.VehicleContacts(new Point2(0, 0), new Point2(0, 0), 0.1f).Count);

            world.Tick(0.02, false);
            Assert.AreEqual(0, world.VehicleContacts(new Point2(0, 0), new Point2(0, 0), 0.1f).Count,
                "continued overlap is the same contact");

            world.Tick(0.02, false);
            Assert.AreEqual(0, world.VehicleContacts(new Point2(2, 0), new Point2(2, 0), 0.1f).Count);

            world.Tick(0.02, false);
            Assert.AreEqual(1, world.VehicleContacts(new Point2(2, 0), new Point2(0, 0), 0.1f).Count,
                "a new contact is reported after separation");
        }

        [Test]
        public void VehicleBoundaryOverlapIsReportedOnceThenCanBeReentered()
        {
            var car = Box("moving-car", StreetKind.Active, new Point2(0, 0), 0.5f, 0.5f,
                vehicleHazard: true);
            var route = new StreetRoute("moving-car", new[] { new Point2(0, 0), new Point2(10, 0) },
                1, null, 0, false);
            var world = Create(new StreetSurface[0], new[] { car }, new[] { route });
            Point2 boundaryPlayer = new Point2(0.700005f, 0);

            world.Tick(0.02, false);
            IReadOnlyList<StreetContact> first = world.VehicleContacts(boundaryPlayer, boundaryPlayer, 0.18f);
            Assert.AreEqual(1, first.Count,
                "an endpoint accepted by the overlap epsilon must emit the first vehicle contact");
            Assert.AreEqual("moving-car", first[0].ObjectId);
            Assert.AreEqual(0, world.VehicleContacts(boundaryPlayer, boundaryPlayer, 0.18f).Count,
                "repeated queries in the same tick must not emit twice");

            world.Tick(0.02, false);
            Assert.AreEqual(0, world.VehicleContacts(boundaryPlayer, boundaryPlayer, 0.18f).Count,
                "deeper overlap on the next tick remains the same contact");

            Point2 separated = new Point2(3, 0);
            world.Tick(0.02, false);
            Assert.AreEqual(0, world.VehicleContacts(separated, separated, 0.18f).Count,
                "separating clears the active contact without reporting a new one");

            world.Tick(0.02, false);
            Assert.AreEqual(1, world.VehicleContacts(separated, boundaryPlayer, 0.18f).Count,
                "a new sweep reports again after separation");

            world.Tick(0.02, true);
            Assert.AreEqual(0, world.VehicleContacts(boundaryPlayer, boundaryPlayer, 0.18f).Count,
                "paused worlds suppress vehicle contacts");
            world.ImportTraffic(world.ExportTraffic());
            world.Tick(0.02, false);
            Assert.AreEqual(1, world.VehicleContacts(boundaryPlayer, boundaryPlayer, 0.18f).Count,
                "restoring traffic clears stale overlap state and allows the first contact");
        }

        [Test]
        public void RedSignalStopsOnlyItsAxisAtStopLineAndTrafficStateRestoresExactly()
        {
            var eastbound = Box("eastbound", StreetKind.Active, new Point2(0, 0), 0.25f, 0.1f,
                vehicleHazard: true, sound: "street.car", soundActive: true);
            var northbound = Box("northbound", StreetKind.Active, new Point2(0, -2), 0.25f, 0.1f,
                vehicleHazard: true, sound: "street.car", soundActive: true);
            var routes = new[]
            {
                new StreetRoute("eastbound", new[] { new Point2(0, 0), new Point2(20, 0) }, 1,
                    "ew", 0, false, 5),
                new StreetRoute("northbound", new[] { new Point2(0, -2), new Point2(0, 18) }, 1,
                    "ns", 0, false, 5)
            };
            var world = Create(new StreetSurface[0], new[] { eastbound, northbound }, routes);

            world.Tick(10, false);
            Assert.AreEqual("ns", world.SignalPhase);
            Assert.AreEqual(5, Find(world.Capture(), "eastbound").Position.X, 0.0001f);
            Assert.AreEqual(8, Find(world.Capture(), "northbound").Position.Y, 0.0001f,
                "the green axis must keep moving while the other queue waits");

            world.Tick(5, true);
            Assert.AreEqual(10, world.Capture().Time, 0.0001);
            Assert.AreEqual(5, Find(world.Capture(), "eastbound").Position.X, 0.0001f);

            world.Tick(7, false);
            Assert.AreEqual("ew", world.SignalPhase);
            Assert.AreEqual(5, Find(world.Capture(), "eastbound").Position.X, 0.0001f);
            world.Tick(2, false);
            Assert.AreEqual(7, Find(world.Capture(), "eastbound").Position.X, 0.0001f);

            StreetTrafficState saved = world.ExportTraffic();
            var restored = Create(new StreetSurface[0], new[] { eastbound, northbound }, routes);
            restored.ImportTraffic(saved);
            Assert.AreEqual(saved.Time, restored.Capture().Time, 0.0001);
            Assert.AreEqual("ew", restored.SignalPhase);
            Assert.AreEqual(7, Find(restored.Capture(), "eastbound").Position.X, 0.0001f);
            Assert.AreEqual(saved.Distances[0], restored.ExportTraffic().Distances[0], 0.0001);
        }

        [Test]
        public void RealCrossroadsSignalsFollowBothGreenRedAndClearanceBoundaries()
        {
            StreetLevel level = StreetLevelFactory.Create();
            var world = new StreetWorld(level.Surfaces, level.Objects, level.Routes, new StreetConfig());
            AssertSignalState(world, true, false, "the real north-south lamp starts green");
            TickTo(world, 13.999);
            AssertSignalState(world, true, false, "north-south remains green immediately before its boundary");
            TickTo(world, 14);
            AssertSignalState(world, false, false, "both axes are red at the first clearance boundary");
            TickTo(world, 16.999);
            AssertSignalState(world, false, false, "both axes remain red immediately before east-west green");
            TickTo(world, 17);
            AssertSignalState(world, false, true, "east-west becomes green at the second boundary");
            TickTo(world, 30.999);
            AssertSignalState(world, false, true, "east-west remains green immediately before its boundary");
            TickTo(world, 31);
            AssertSignalState(world, false, false, "both axes return to red for the second clearance phase");
            TickTo(world, 33.999);
            AssertSignalState(world, false, false, "both axes remain red immediately before the next NS green");
            TickTo(world, 34);
            AssertSignalState(world, true, false, "north-south becomes green again after the full cycle");
        }

        private static void TickTo(StreetWorld world, double time)
        { world.Tick(time - world.Capture().Time, false); }

        private static void AssertSignalState(StreetWorld world, bool nsGreen, bool ewGreen, string message)
        {
            StreetWorldFrame frame = world.Capture();
            Assert.AreEqual(nsGreen, Find(frame, "street.signal.ns").SoundActive, message + " (NS)");
            Assert.AreEqual(ewGreen, Find(frame, "street.signal.ew").SoundActive, message + " (EW)");
            Assert.AreEqual(nsGreen ? "ns" : ewGreen ? "ew" : "clearance", world.SignalPhase, message + " (phase)");
        }

        [Test]
        public void RealCrossroadsTrafficRestoreRecomputesLampStateInsteadOfKeepingOldPhase()
        {
            StreetLevel level = StreetLevelFactory.Create();
            var world = new StreetWorld(level.Surfaces, level.Objects, level.Routes, new StreetConfig());
            world.Tick(17, false);
            StreetTrafficState savedAtEastWestGreen = world.ExportTraffic();
            AssertSignalState(world, false, true, "source world is east-west green before saving");

            var restored = new StreetWorld(level.Surfaces, level.Objects, level.Routes, new StreetConfig());
            restored.ImportTraffic(savedAtEastWestGreen);

            Assert.AreEqual(savedAtEastWestGreen.Time, restored.Capture().Time, 0.0001);
            AssertSignalState(restored, false, true,
                "restoring traffic at an east-west sample must not retain the initial north-south lamp state");
        }

        [Test]
        public void ClosedRouteAdvancesAcrossItsClosingSegmentWithoutLosingAbsoluteDistance()
        {
            var car = Box("loop-car", StreetKind.Active, new Point2(0, 0), 0.1f, 0.1f,
                vehicleHazard: true);
            var route = new StreetRoute("loop-car", new[]
            {
                new Point2(0, 0), new Point2(2, 0), new Point2(2, 2), new Point2(0, 2)
            }, 1, null, 0, true);
            var world = Create(new StreetSurface[0], new[] { car }, new[] { route });

            world.Tick(7.9, false);
            Point2 beforeLap = Find(world.Capture(), "loop-car").Position;
            world.Tick(0.2, false);
            Point2 afterLap = Find(world.Capture(), "loop-car").Position;
            StreetTrafficState state = world.ExportTraffic();

            Assert.Less(StreetGeometry.Distance(beforeLap, afterLap), 0.3f,
                "the closing route segment must be swept continuously instead of teleporting to route start");
            Assert.Greater(state.Distances[0], 8, "exported distance remains absolute after one full lap");
        }
    }
}
