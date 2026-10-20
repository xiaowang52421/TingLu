using System;
using System.Collections.Generic;
using NUnit.Framework;
using TingLu.Config;
using TingLu.Contracts;
using TingLu.Player;
using TingLu.Tests.Upgrade;
using TingLu.World;

namespace TingLu.Tests.Upgrade.Role02
{
    public sealed class StreetPlayerControllerTests
    {
        private sealed class TestWorld : IStreetWorld
        {
            public double Time;
            public string Material = "tactile";
            public StreetContact[] MoveHits = new StreetContact[0];
            public StreetContact[] VehicleHits = new StreetContact[0];
            public bool RepeatMoveHits;
            public bool RepeatVehicleHits;
            public Point2 LastFrom, LastRequested;
            public float LastRadius;
            public int MoveCalls, VehicleCalls;

            public StreetWorldFrame Capture() { return new StreetWorldFrame(Time, new StreetSurface[0], new StreetObject[0]); }
            public void Tick(double delta, bool paused) { if (!paused) Time += delta; }
            public StreetMoveResult Move(Point2 from, Point2 requested, float radius)
            {
                MoveCalls++; LastFrom = from; LastRequested = requested; LastRadius = radius;
                StreetContact[] hits = MoveHits;
                if (!RepeatMoveHits) MoveHits = new StreetContact[0];
                return new StreetMoveResult(requested, hits);
            }
            public IReadOnlyList<StreetContact> VehicleContacts(Point2 from, Point2 to, float radius)
            {
                VehicleCalls++;
                StreetContact[] hits = VehicleHits;
                if (!RepeatVehicleHits) VehicleHits = new StreetContact[0];
                return hits;
            }
            public string MaterialAt(Point2 position) { return Material; }
            public StreetTrafficState ExportTraffic() { return new StreetTrafficState(Time, new string[0], new double[0]); }
            public void ImportTraffic(StreetTrafficState state) { Time = state.Time; }
            public string SignalPhase { get { return "ns"; } }
        }

        private FixtureClock clock;
        private TestWorld world;
        private StreetConfig config;
        private StreetPlayerController player;

        [SetUp]
        public void SetUp()
        {
            clock = new FixtureClock();
            world = new TestWorld();
            config = new StreetConfig();
            player = NewPlayer();
        }

        private StreetPlayerController NewPlayer(float courage = 100, float focus = 100,
            Point2 position = default(Point2), Point2? facing = null)
        {
            return new StreetPlayerController(config, clock, world,
                State(PlayerMainState.Active, position, facing ?? new Point2(0, 1), courage, focus, 0, 0, 0));
        }

        private static PlayerState State(PlayerMainState main, Point2 position, Point2 facing,
            float courage, float focus, double ready, double stunned, double confused)
        {
            return new PlayerState(main, ActionMode.Walking, position, facing, courage, focus, ready, stunned, confused);
        }

        [Test]
        public void MovementDoesNotTurnThePlayerAndUsesTheFixedWorldBasis()
        {
            player.Tick(new StreetInputFrame(new Point2(1, 0)), 1);

            Assert.That(player.Current.Position.X, Is.EqualTo(1.2f).Within(1e-5f));
            Assert.That(player.Current.Position.Y, Is.Zero.Within(1e-5f));
            Assert.That(player.Current.Facing.X, Is.Zero.Within(1e-5f));
            Assert.That(player.Current.Facing.Y, Is.EqualTo(1).Within(1e-5f));

            world = new TestWorld();
            player = NewPlayer();
            player.Tick(new StreetInputFrame(new Point2(1, 1)), 1);
            Assert.That(player.Current.Position.X, Is.EqualTo(0.8485281f).Within(1e-4f));
            Assert.That(player.Current.Position.Y, Is.EqualTo(0.8485281f).Within(1e-4f));
            Assert.That(player.Current.Facing.X, Is.Zero.Within(1e-5f));
            Assert.That(player.Current.Facing.Y, Is.EqualTo(1).Within(1e-5f));
        }

        [Test]
        public void TurningWorksWhileStationaryAndWhileARealWallBlocksMovement()
        {
            player.Tick(new StreetInputFrame(new Point2(), 90), 0);
            Assert.That(player.Current.Facing.X, Is.EqualTo(1).Within(1e-5f));
            Assert.That(player.Current.Facing.Y, Is.Zero.Within(1e-5f));

            var wall = new StreetObject("wall", StreetKind.Permanent, new Point2(0.5f, 0),
                StreetGeometry.Rect(0.4f, -1, 0.6f, 1), "brick", true, false, true);
            var blockedWorld = new StreetWorld(new StreetSurface[0], new[] { wall }, new StreetRoute[0], config);
            var blockedPlayer = new StreetPlayerController(config, clock, blockedWorld, StreetFixtures.Player());
            int contactCount = 0;
            blockedPlayer.Contacted += delegate { contactCount++; };

            blockedPlayer.Tick(new StreetInputFrame(new Point2(1, 0), 90), 1);

            Assert.That(blockedPlayer.Current.Position.X, Is.GreaterThan(0));
            Assert.That(blockedPlayer.Current.Position.X, Is.LessThan(0.4f));
            Assert.That(blockedPlayer.Current.Facing.X, Is.EqualTo(1).Within(1e-5f));
            Assert.That(blockedPlayer.Current.Facing.Y, Is.Zero.Within(1e-5f));
            Assert.AreEqual(1, contactCount);
        }

        [Test]
        public void ProbeSnapshotsPostMovePostTurnPoseAndStaysImmutableAfterLaterTurning()
        {
            StreetProbeRequest first = null;
            player.Probed += request => first = request;
            clock.Now = 4.25;

            player.Tick(new StreetInputFrame(new Point2(1, 0), 90, true), 0.5);

            Assert.IsNotNull(first);
            Assert.That(first.Time, Is.EqualTo(4.25));
            Assert.That(first.Origin.X, Is.EqualTo(0.6f).Within(1e-5f));
            Assert.That(first.Origin.Y, Is.Zero.Within(1e-5f));
            Assert.That(first.Facing.X, Is.EqualTo(1).Within(1e-5f));
            Assert.That(first.Facing.Y, Is.Zero.Within(1e-5f));
            Assert.That(first.Angle, Is.EqualTo(config.ProbeAngle));
            Assert.That(first.Radius, Is.EqualTo(config.Gameplay.caneRadius));
            Assert.That(first.Id, Is.Not.Empty);

            clock.Now += 0.1;
            player.Tick(new StreetInputFrame(new Point2(), 90), 0);
            Assert.That(first.Origin.X, Is.EqualTo(0.6f).Within(1e-5f));
            Assert.That(first.Facing.X, Is.EqualTo(1).Within(1e-5f));
            Assert.That(player.Current.Facing.X, Is.Zero.Within(1e-5f));
            Assert.That(player.Current.Facing.Y, Is.EqualTo(-1).Within(1e-5f));
        }

        [Test]
        public void PassiveFocusRecoversWhenIdleAndDrainsAtTheOriginalWalkingRate()
        {
            config.Gameplay.listenSpeed = 0.1f;
            config.Gameplay.focusWalkDrain = 2;
            config.Gameplay.focusListenGain = 8;
            player = NewPlayer(focus: 50);

            player.Tick(new StreetInputFrame(new Point2()), 1);
            Assert.AreEqual(58, player.Current.Focus, 0.0001f);
            Assert.That(player.Current.Position.X, Is.Zero);

            player.Tick(new StreetInputFrame(new Point2(1, 0)), 1);
            Assert.AreEqual(56, player.Current.Focus, 0.0001f);
            Assert.AreEqual(1.2f, player.Current.Position.X, 0.0001f,
                "passive listening must not force the old slower Listening speed");
            Assert.AreEqual(ActionMode.Walking, player.Current.Mode);
        }

        [Test]
        public void ZeroFocusStillAllowsProbeButCooldownBoundaryDoesNotSpendOrEmit()
        {
            config.Gameplay.focusListenGain = 0;
            player = NewPlayer(focus: 0);
            var probes = new List<StreetProbeRequest>();
            player.Probed += probes.Add;

            player.Tick(new StreetInputFrame(new Point2(), 0, true), 0);
            Assert.AreEqual(1, probes.Count);
            Assert.AreEqual(60f, probes[0].Angle, 0.0001f);
            Assert.AreEqual(3f, probes[0].Radius, 0.0001f);
            Assert.AreEqual(0, player.Current.Focus);
            Assert.AreEqual(3, player.Current.CaneReadyAt, 0.0001);

            clock.Now = 2.999;
            player.Tick(new StreetInputFrame(new Point2(), 0, true), 0);
            Assert.AreEqual(1, probes.Count);
            Assert.AreEqual(0, player.Current.Focus);
            Assert.AreEqual(3, player.Current.CaneReadyAt, 0.0001);

            clock.Now = 3;
            player.Tick(new StreetInputFrame(new Point2(), 0, true), 0);
            Assert.AreEqual(2, probes.Count, "the exact ready time is a valid cooldown boundary");
            Assert.AreEqual(0, player.Current.Focus);
            Assert.AreEqual(6, player.Current.CaneReadyAt, 0.0001);
        }

        [Test]
        public void ConfusionTemporarilyUsesTheConfiguredMovementMultiplier()
        {
            config.Gameplay.caneCooldown = 1;
            config.Gameplay.confusionTapCount = 2;
            config.Gameplay.confusionWindow = 8;
            config.Gameplay.confusionSeconds = 2;
            config.Gameplay.confusionMultiplier = 0.5f;
            player = NewPlayer();

            player.Tick(new StreetInputFrame(new Point2(), 0, true), 0);
            clock.Now = 1;
            player.Tick(new StreetInputFrame(new Point2(), 0, true), 0);
            Assert.AreEqual(3, player.Current.ConfusedUntil, 0.0001);

            clock.Now = 1.1;
            player.Tick(new StreetInputFrame(new Point2(1, 0)), 1);
            Assert.AreEqual(0.6f, player.Current.Position.X, 0.0001f);

            clock.Now = 3;
            player.Tick(new StreetInputFrame(new Point2(1, 0)), 1);
            Assert.AreEqual(1.8f, player.Current.Position.X, 0.0001f,
                "movement returns to full speed when confusion expires");
        }

        [Test]
        public void NullSurfaceMaterialDoesNotEmitFootstepNull()
        {
            world.Material = null;
            int steps = 0;
            player.Stepped += delegate { steps++; };

            player.Tick(new StreetInputFrame(new Point2(1, 0)), 1);

            Assert.AreEqual(0, steps);
        }

        [Test]
        public void OrdinaryContactIgnoresLegacyDamageAndDebouncesUntilSeparation()
        {
            config.Gameplay.damageStatic = 10;
            config.Gameplay.fallStunSeconds = 1.5f;
            config.Gameplay.contactDebounceSeconds = 0.5f;
            config.Gameplay.contactExitSeconds = 0.1f;
            var wall = new StreetContact("wall", new Point2(0.5f, 0), false);
            world.MoveHits = new[] { wall };
            int contacted = 0;
            var failed = new List<StreetFailure>();
            player.Contacted += delegate { contacted++; };
            player.Failed += (reason, id) => failed.Add(reason);

            player.Tick(new StreetInputFrame(new Point2(1, 0)), 0);
            Assert.AreEqual(100, player.Current.Courage, 0.0001f);
            Assert.AreEqual(PlayerMainState.Active, player.Current.MainState);
            Assert.AreEqual(0, player.Current.StunnedUntil, 0.0001);
            Assert.AreEqual(1, contacted);
            CollectionAssert.IsEmpty(failed);

            clock.Now = 1.5;
            player.Tick(new StreetInputFrame(new Point2()), 0);
            Assert.AreEqual(PlayerMainState.Active, player.Current.MainState);
            Assert.AreEqual(100, player.Current.Courage, 0.0001f);

            clock.Now = 1.6;
            world.MoveHits = new[] { wall };
            player.Tick(new StreetInputFrame(new Point2()), 0);
            Assert.AreEqual(100, player.Current.Courage, 0.0001f);
            Assert.AreEqual(2, contacted);
            CollectionAssert.IsEmpty(failed, "ordinary contact must not be converted into vehicle failure");
        }

        [TestCase(0.099, false)]
        [TestCase(0.1, true)]
        public void ContactExitThresholdRejectsEarlyReentryAndAcceptsItsExactBoundary(double separation, bool accepted)
        {
            config.Gameplay.damageStatic = 10;
            config.Gameplay.fallStunSeconds = 0;
            config.Gameplay.contactDebounceSeconds = 0;
            config.Gameplay.contactExitSeconds = 0.1f;
            int contactEvents=0; player.Contacted+=c=>contactEvents++;
            var wall = new StreetContact("wall", new Point2(0.5f, 0), false);
            world.MoveHits = new[] { wall };
            player.Tick(new StreetInputFrame(new Point2()), 0);

            clock.Now = 1.5;
            player.Tick(new StreetInputFrame(new Point2()), 0);
            clock.Now = 1.5 + separation;
            world.MoveHits = new[] { wall };
            player.Tick(new StreetInputFrame(new Point2()), 0);

            Assert.That(player.Current.Courage, Is.EqualTo(100));
            Assert.That(contactEvents, Is.EqualTo(accepted ? 2 : 1));
        }

        [Test]
        public void OrdinaryContactCannotExhaustCourageOrSuppressAProbe()
        {
            config.Gameplay.damageStatic = 100;
            var wall = new StreetContact("wall", new Point2(0.5f, 0), false);
            world.MoveHits = new[] { wall };
            var failures = new List<string>();
            StreetProbeRequest probe = null;
            int contacts = 0;
            player.Contacted += delegate { contacts++; };
            player.Failed += (reason, id) => failures.Add(reason + ":" + id);
            player.Probed += request => probe = request;

            player.Tick(new StreetInputFrame(new Point2(1, 0), 0, true), 1);

            CollectionAssert.IsEmpty(failures);
            Assert.AreEqual(1, contacts);
            Assert.IsNotNull(probe, "ordinary contact must allow the simultaneous probe");
            Assert.AreEqual(100, player.Current.Courage, 0.0001f);
            Assert.AreEqual(PlayerMainState.Active, player.Current.MainState);
        }

        [Test]
        public void LegacyOrdinaryDamageCannotInvokeSynchronousRestore()
        {
            config.Gameplay.damageStatic = 100;
            var wall = new StreetContact("wall", new Point2(0.5f, 0), false);
            world.MoveHits = new[] { wall };
            int failures = 0;
            StreetProbeRequest probe = null;
            player.Probed += request => probe = request;
            player.Failed += (reason, id) =>
            {
                Assert.AreEqual(StreetFailure.Courage, reason);
                Assert.AreEqual("wall", id);
                failures++;
                player.Restore(StreetFixtures.Player(new Point2(5, 6)), clock.Now, clock.Now);
            };

            player.Tick(new StreetInputFrame(new Point2(1, 0), 90, true), 1);

            Assert.AreEqual(0, failures);
            Assert.IsNotNull(probe, "ordinary contact cannot initiate recovery");
            Assert.AreEqual(PlayerMainState.Active, player.Current.MainState);
            Assert.AreEqual(config.Gameplay.walkSpeed, player.Current.Position.X, 0.0001f);
            Assert.AreEqual(0, player.Current.Position.Y, 0.0001f);
            Assert.AreEqual(100, player.Current.Courage, 0.0001f);
        }

        [Test]
        public void SimultaneousOrdinaryAndMultipleVehicleContactsPreferOneImmediateVehicleFailure()
        {
            world.MoveHits=new[]{new StreetContact("a.wall",new Point2(),false,100,10)};
            world.VehicleHits=new[]{new StreetContact("a.wall",new Point2(),false),new StreetContact("y.car",new Point2(),true),new StreetContact("z.cycle",new Point2(),true)};
            var events=new List<string>();int failures=0;
            player.Contacted+=c=>events.Add(c.ObjectId);
            player.Failed+=(reason,id)=>{Assert.AreEqual(StreetFailure.Vehicle,reason);failures++;player.Restore(StreetFixtures.Player(new Point2(5,6)),clock.Now,clock.Now);};
            player.Tick(new StreetInputFrame(new Point2(1,0)),.02);
            CollectionAssert.AreEqual(new[]{"y.car"},events);
            Assert.AreEqual(1,failures);Assert.AreEqual(5,player.Current.Position.X);Assert.AreEqual(6,player.Current.Position.Y);
            Assert.AreEqual(100,player.Current.Courage);
        }

        [Test]
        public void VehicleContactFailsImmediatelyOnceAndDoesNotSpendCourage()
        {
            player = NewPlayer(courage: 1);
            var car = new StreetContact("car", new Point2(), true, 100, 2);
            world.VehicleHits = new[] { car };
            world.RepeatVehicleHits = true;
            var failures = new List<string>();
            int contacts = 0;
            player.Failed += (reason, id) => failures.Add(reason + ":" + id);
            player.Contacted += delegate(StreetContact contact) { contacts++; };

            player.Tick(new StreetInputFrame(new Point2()), 0.02);
            clock.Now += 0.02;
            player.Tick(new StreetInputFrame(new Point2(1, 0), 0, true), 0.02);

            CollectionAssert.AreEqual(new[] { "Vehicle:car" }, failures);
            Assert.AreEqual(1, contacts);
            Assert.AreEqual(1, player.Current.Courage, 0.0001f);
            Assert.AreEqual(PlayerMainState.Failing, player.Current.MainState);
            Assert.AreEqual(0, player.Current.Position.X, 0.0001f,
                "the failed controller ignores later movement input until restore");
        }

        [TestCase(false)]
        [TestCase(true)]
        public void OrdinaryStunStillDetectsVehicleContactUnlessTheSimulationIsPaused(bool paused)
        {
            player = new StreetPlayerController(config,clock,world,State(PlayerMainState.Stunned,new Point2(),new Point2(0,1),100,100,0,1.5,0));
            Assert.That(player.Current.MainState, Is.EqualTo(PlayerMainState.Stunned));
            float courage = player.Current.Courage;
            int moveCalls = world.MoveCalls;
            int vehicleCalls = world.VehicleCalls;
            int probes = 0;
            var failures = new List<string>();
            player.Probed += delegate { probes++; };
            player.Failed += (reason, id) => failures.Add(reason + ":" + id);
            world.VehicleHits = new[] { new StreetContact("car", new Point2(), true) };
            clock.Now = 0.5;
            clock.Paused = paused;

            player.Tick(new StreetInputFrame(new Point2(1, 0), 90, true), 0.5);

            Assert.That(world.MoveCalls, Is.EqualTo(moveCalls), "stun must not move the player");
            Assert.That(world.VehicleCalls, Is.EqualTo(vehicleCalls + (paused ? 0 : 1)));
            Assert.That(player.Current.Position.X, Is.Zero);
            Assert.That(player.Current.Facing.Y, Is.EqualTo(1));
            Assert.That(player.Current.Courage, Is.EqualTo(courage));
            Assert.That(probes, Is.Zero);
            if (paused)
            {
                CollectionAssert.IsEmpty(failures);
                Assert.That(player.Current.MainState, Is.EqualTo(PlayerMainState.Stunned));
            }
            else
            {
                CollectionAssert.AreEqual(new[] { "Vehicle:car" }, failures);
                Assert.That(player.Current.MainState, Is.EqualTo(PlayerMainState.Failing));
            }
        }

        [Test]
        public void RealVehicleSweepHitsAStunnedStationaryPlayerBeforeStunExpires()
        {
            var car = new StreetObject("car", StreetKind.Active, new Point2(-5, 0),
                StreetGeometry.Rect(-5.25f, -0.1f, -4.75f, 0.1f), "brick", false, true, false);
            var route = new StreetRoute("car", new[] { new Point2(-5, 0), new Point2(5, 0) }, 10,
                null, 0, false);
            var realWorld = new StreetWorld(new StreetSurface[0], new[] { car }, new[] { route }, config);
            player = new StreetPlayerController(config, clock, realWorld,
                State(PlayerMainState.Stunned, new Point2(), new Point2(0, 1), 90, 100, 0, 2, 0));
            int failures = 0;
            int probes = 0;
            player.Failed += (reason, id) =>
            {
                Assert.That(reason, Is.EqualTo(StreetFailure.Vehicle));
                Assert.That(id, Is.EqualTo("car"));
                failures++;
            };
            player.Probed += delegate { probes++; };

            clock.Now = 1;
            realWorld.Tick(1, false);
            player.Tick(new StreetInputFrame(new Point2(1, 0), 90, true), 1);

            Assert.That(failures, Is.EqualTo(1));
            Assert.That(player.Current.MainState, Is.EqualTo(PlayerMainState.Failing));
            Assert.That(player.Current.Position.X, Is.Zero);
            Assert.That(player.Current.Facing.Y, Is.EqualTo(1));
            Assert.That(player.Current.Courage, Is.EqualTo(90));
            Assert.That(probes, Is.Zero);
        }

        [Test]
        public void SynchronousFailureRestoreIsNotOverwrittenByTheOldTick()
        {
            var car = new StreetContact("car", new Point2(), true);
            world.VehicleHits = new[] { car };
            int failures = 0;
            StreetProbeRequest probe = null;
            player.Probed += request => probe = request;
            player.Failed += (reason, id) =>
            {
                failures++;
                player.Restore(StreetFixtures.Player(new Point2(5, 6)), clock.Now, clock.Now);
            };

            player.Tick(new StreetInputFrame(new Point2(1, 0), 90, true), 1);

            Assert.AreEqual(1, failures);
            Assert.IsNull(probe, "vehicle failure must short-circuit a simultaneous probe");
            Assert.AreEqual(PlayerMainState.Active, player.Current.MainState);
            Assert.AreEqual(5, player.Current.Position.X, 0.0001f);
            Assert.AreEqual(6, player.Current.Position.Y, 0.0001f);
            Assert.AreEqual(0, player.Current.CaneReadyAt, 0.0001);
        }

        [Test]
        public void RealActiveWalkerDoesNotCauseVehicleFailure()
        {
            var walker = new StreetObject("walker", StreetKind.Active, new Point2(0, 0), new Point2[0],
                "brick", false, false, false, 0, 0, "street.walker", true, PerceptionSemantic.Ordinary);
            var realWorld = new StreetWorld(new StreetSurface[0], new[] { walker }, new StreetRoute[0], config);
            player = new StreetPlayerController(config, clock, realWorld, StreetFixtures.Player());
            int failures = 0;
            player.Failed += delegate { failures++; };

            realWorld.Tick(0.02, false);
            player.Tick(new StreetInputFrame(new Point2()), 0.02);

            Assert.AreEqual(0, failures);
            Assert.AreEqual(PlayerMainState.Active, player.Current.MainState);
        }
    }
}
