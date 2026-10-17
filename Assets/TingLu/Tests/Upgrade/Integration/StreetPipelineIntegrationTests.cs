using System;
using System.IO;
using NUnit.Framework;
using TingLu.Config;
using TingLu.Contracts;
using TingLu.Memory;
using TingLu.Perception;
using TingLu.Persistence;
using TingLu.Player;
using TingLu.Tests.Upgrade;
using TingLu.World;

namespace TingLu.Tests.Upgrade.Integration
{
    public sealed class StreetPipelineIntegrationTests
    {
        [Test]
        public void ProbeMemoryAndSaveRoundTripKeepOnlyPermittedGeometry()
        {
            string root = NewTemporaryRoot();
            try
            {
                var clock = new TestClock();
                var world = StreetFixtures.World();
                var player = new StreetPlayerController(new StreetConfig(), clock, world,
                    StreetFixtures.Player(new Point2(0, 0)));
                var perception = new StreetPerception(new StreetConfig(), clock);
                var memory = new StreetMemory();
                StreetProbeResult observed = null;
                player.Probed += request =>
                {
                    StreetWorldFrame sample = world.Capture();
                    Assert.AreEqual(request.Time, sample.Time, 1e-6);
                    observed = perception.Probe(request, sample);
                    memory.Apply(observed);
                };

                Advance(world, clock, player, new StreetInputFrame(new Point2(), probe: true), 0.02);

                Assert.IsNotNull(observed);
                Assert.Greater(observed.Coverage.Cells.Count, 0);
                Assert.Greater(memory.Current.Surfaces.Count, 0, "a probe confirms area, not just boundary lines");
                Assert.IsFalse(ContainsObject(memory.Current, "bird"), "an active sound source is not geometry memory");

                StreetTrafficState traffic = world.ExportTraffic();
                var save = new StreetSaveData("qa-pipeline", "spawn", player.Current, player.Current,
                    traffic, traffic, memory.Export(), 0, 0, new string[0]);
                var saves = new StreetSaveService(root, "UpgradePipelineTest");
                Assert.AreEqual(SaveStatus.Success, saves.Save(save));

                SaveResult<StreetSaveData> loaded = saves.Load("qa-pipeline");
                Assert.AreEqual(SaveStatus.Success, loaded.Status);
                Assert.Greater(loaded.Value.Memory.Surfaces.Count, 0);
                Assert.IsFalse(ContainsObject(loaded.Value.Memory, "bird"));
                foreach (StreetKnownSurface surface in loaded.Value.Memory.Surfaces)
                    Assert.AreEqual(0, surface.CurrentUntil, "saved geometry must reload stale");
                foreach (StreetKnownObject item in loaded.Value.Memory.Objects)
                    Assert.AreEqual(0, item.CurrentUntil, "saved object snapshots must reload stale");
            }
            finally
            {
                DeleteTemporaryRoot(root);
            }
        }

        [Test]
        public void MovingVehicleContactRestoresWorldAndPlayerThroughFailureEventOnce()
        {
            var clock = new TestClock();
            Point2 spawn = new Point2(0, 0);
            Point2[] carPolygon = StreetGeometry.Rect(-2.4f, -0.3f, -1.6f, 0.3f);
            var car = new StreetObject("moving-car", StreetKind.Active, new Point2(-2, 0), carPolygon,
                "metal", false, true, false, 0, 0.5f, "street.car", true);
            var route = new StreetRoute("moving-car", new[] { new Point2(-2, 0), new Point2(2, 0) },
                4, null, 0, false);
            var world = new StreetWorld(
                new[] { new StreetSurface("road", "asphalt", StreetGeometry.Rect(-5, -3, 5, 3)) },
                new[] { car }, new[] { route }, new StreetConfig());
            var player = new StreetPlayerController(new StreetConfig(), clock, world,
                StreetFixtures.Player(spawn));
            var memory = new StreetMemory();
            StreetTrafficState checkpointTraffic = world.ExportTraffic();
            PlayerState checkpointPlayer = player.Current;
            int failures = 0;
            int collisions = 0;

            player.Failed += (reason, objectId) =>
            {
                Assert.AreEqual(StreetFailure.Vehicle, reason);
                Assert.AreEqual("moving-car", objectId);
                failures++;
                collisions++;
                clock.Paused = true;
                world.ImportTraffic(checkpointTraffic);
                clock.Now = checkpointTraffic.Time;
                player.Restore(checkpointPlayer, checkpointTraffic.Time, checkpointTraffic.Time);
                memory.MarkStale();
                clock.Paused = false;
            };

            Advance(world, clock, player, new StreetInputFrame(new Point2()), 1.0);

            Assert.AreEqual(1, failures, "one continuous vehicle impact is one failure");
            Assert.AreEqual(1, collisions);
            Assert.AreEqual(spawn.X, player.Current.Position.X, 1e-5f);
            Assert.AreEqual(spawn.Y, player.Current.Position.Y, 1e-5f);
            Assert.AreEqual(0, world.ExportTraffic().Time, 1e-6, "traffic returns to the captured checkpoint time");
            Assert.AreEqual(world.ExportTraffic().Time, clock.Now, 1e-6);
        }

        private static void Advance(IStreetWorld world, TestClock clock, StreetPlayerController player,
            StreetInputFrame input, double delta)
        {
            world.Tick(delta, false);
            if (!clock.Paused) clock.Now += delta;
            player.Tick(input, delta);
        }

        private sealed class TestClock : IGameClock
        {
            public double Now { get; set; }
            public bool Paused { get; set; }
        }

        private static bool ContainsObject(StreetMemoryFrame frame, string id)
        {
            foreach (StreetKnownObject item in frame.Objects)
                if (StringComparer.Ordinal.Equals(item.ObjectId, id)) return true;
            return false;
        }

        private static string NewTemporaryRoot()
        {
            string root = Path.Combine(Path.GetTempPath(), "tinglu-upgrade-qa", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            return root;
        }

        private static void DeleteTemporaryRoot(string root)
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }
}
