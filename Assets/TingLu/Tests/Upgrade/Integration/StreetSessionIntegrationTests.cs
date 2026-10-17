using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using NUnit.Framework;
using TingLu.Bootstrap;
using TingLu.Config;
using TingLu.Contracts;
using TingLu.Levels;
using TingLu.Memory;
using TingLu.Perception;
using TingLu.Persistence;
using TingLu.Player;
using TingLu.UI;
using TingLu.World;

namespace TingLu.Tests.Upgrade.Integration
{
    public sealed class StreetSessionIntegrationTests
    {
        [Test]
        public void A25_PauseFreezesTheWholeSessionAndResumeSamplesTheRestoredTimeline()
        {
            string root = NewTemporaryRoot();
            var audio = new RecordingAudio();
            StreetSession session = null;
            try
            {
                var saves = new StreetSaveService(root, "A25PauseResume");
                session = new StreetSession(StreetLevelFactory.Create(), audio, audio, saves, new StreetConfig());
                session.Menu.Activate(MenuOption.Start);
                Assert.IsTrue(session.Running);
                Assert.AreEqual(MenuScreen.Playing, session.Menu.Screen);

                session.Tick(new StreetInputFrame(new Point2(), probe: true), 0.02);
                Assert.Greater(session.Memory.Current.Surfaces.Count, 0, "the probe must travel through Player, Perception and Memory");
                Assert.Greater(session.Perception.Observations.Count, 0, "live street sources must reach Perception");

                session.Tick(new StreetInputFrame(new Point2(), pause: true), 0.02);
                Assert.IsTrue(session.Clock.Paused);
                Assert.IsTrue(audio.Paused);
                Assert.AreEqual(MenuScreen.Pause, session.Menu.Screen);

                PlayerState pausedPlayer = session.Player.Current;
                StreetTrafficState pausedTraffic = session.World.ExportTraffic();
                StreetMemoryFrame pausedMemory = session.Memory.Export();
                PerceptionFrame pausedPerception = session.Perception;
                int playedBeforeWait = audio.Played.Count;
                double pausedTime = session.Clock.Now;

                session.Tick(new StreetInputFrame(new Point2(0, 1), 90, true), 1.0);
                Assert.AreEqual(pausedTime, session.Clock.Now, 1e-9);
                Assert.AreEqual(pausedTraffic.Time, session.World.ExportTraffic().Time, 1e-9);
                Assert.AreEqual(pausedPlayer.Position.X, session.Player.Current.Position.X, 1e-6f);
                Assert.AreEqual(pausedPlayer.Position.Y, session.Player.Current.Position.Y, 1e-6f);
                Assert.AreEqual(pausedPlayer.Facing.X, session.Player.Current.Facing.X, 1e-6f);
                Assert.AreEqual(pausedPlayer.Facing.Y, session.Player.Current.Facing.Y, 1e-6f);
                Assert.AreEqual(pausedPlayer.CaneReadyAt, session.Player.Current.CaneReadyAt, 1e-9);
                Assert.AreEqual(pausedMemory.Surfaces.Count, session.Memory.Current.Surfaces.Count);
                Assert.AreEqual(pausedPerception.FrameId, session.Perception.FrameId);
                Assert.AreEqual(playedBeforeWait, audio.Played.Count, "paused time cannot emit new source or footstep events");

                session.Tick(new StreetInputFrame(new Point2(), pause: true), 1.0);
                Assert.IsFalse(session.Clock.Paused);
                Assert.IsFalse(audio.Paused);
                Assert.AreEqual(MenuScreen.Playing, session.Menu.Screen);
                Assert.AreEqual(session.Clock.Now, session.Perception.SampledAt, 1e-9,
                    "the first resumed perception frame must be sampled on the restored clock");
                Assert.Greater(session.Perception.Observations.Count, 0,
                    "resume must rebuild perception before gameplay resumes");
                foreach (AcousticQueryResult source in session.Sources)
                    Assert.AreEqual(session.Clock.Now, source.SampledAt, 1e-9);
                Assert.AreEqual(session.Generation, audio.LastResetGeneration);
            }
            finally
            {
                if (session != null) session.Dispose();
                DeleteTemporaryRoot(root);
            }
        }

        [Test]
        public void SessionSubmitsCaneTapFeedbackBeforeApplyingProbeMemory()
        {
            string root = NewTemporaryRoot();
            var audio = new RecordingAudio();
            StreetSession session = null;
            try
            {
                session = new StreetSession(StreetLevelFactory.Create(), audio, audio,
                    new StreetSaveService(root, "ProbeFeedbackOrdering"), new StreetConfig());
                session.Menu.Activate(MenuOption.Start);
                int surfacesAtCaneTapSubmission = -1;
                audio.OnPlay = request =>
                {
                    if (request.SoundId == "cane.tap" && surfacesAtCaneTapSubmission < 0)
                        surfacesAtCaneTapSubmission = session.Memory.Current.Surfaces.Count;
                };

                session.Tick(new StreetInputFrame(new Point2(), probe: true), 0.02);

                Assert.GreaterOrEqual(surfacesAtCaneTapSubmission, 0,
                    "the initial probe must submit its cane tap through the production playback adapter");
                Assert.AreEqual(0, surfacesAtCaneTapSubmission,
                    "the cane tap request should be submitted before the probe applies its memory result");
                Assert.Greater(session.Memory.Current.Surfaces.Count, 0,
                    "the same completed probe must then apply permitted ground memory");
            }
            finally
            {
                if (session != null) session.Dispose();
                DeleteTemporaryRoot(root);
            }
        }

        [Test]
        public void FirstSaveEnablesContinueAndContinueLoadsTheSameStreetRun()
        {
            string root = NewTemporaryRoot();
            var audio = new RecordingAudio();
            StreetSession session = null;
            try
            {
                session = new StreetSession(StreetLevelFactory.Create(), audio, audio,
                    new StreetSaveService(root, "ContinueRun"), new StreetConfig());
                session.Menu.Activate(MenuOption.Start);
                Assert.IsTrue(session.Running);
                Point2 startingPosition = session.Player.Current.Position;

                session.Menu.Back();
                Assert.AreEqual(MenuScreen.Main, session.Menu.Screen);
                MenuItem continueItem = FindMenuItem(session.Menu, MenuOption.Continue);
                Assert.IsNotNull(continueItem);
                Assert.IsTrue(continueItem.Enabled, "the successful first save must update the live menu");

                session.Menu.Activate(MenuOption.Continue);
                Assert.IsTrue(session.Running);
                Assert.AreEqual(MenuScreen.Playing, session.Menu.Screen);
                Assert.AreEqual(startingPosition.X, session.Player.Current.Position.X, 1e-5f);
                Assert.AreEqual(startingPosition.Y, session.Player.Current.Position.Y, 1e-5f);
                Assert.AreEqual(session.Clock.Now, session.Perception.SampledAt, 1e-9);
            }
            finally
            {
                if (session != null) session.Dispose();
                DeleteTemporaryRoot(root);
            }
        }

        [Test]
        public void ContinueRestoresStaleExplorationAndResamplesLiveSourcesFromWorld()
        {
            string root = NewTemporaryRoot();
            StreetSession first = null;
            StreetSession continued = null;
            try
            {
                var saves = new StreetSaveService(root, "A26MemoryResume");
                first = new StreetSession(StreetLevelFactory.Create(), new RecordingAudio(), null, saves,
                    new StreetConfig());
                first.Menu.Activate(MenuOption.Start);
                Assert.IsTrue(first.Running);

                first.Tick(new StreetInputFrame(new Point2(), probe: true), 0.02);
                Assert.Greater(first.Memory.Current.Surfaces.Count, 0, "the production probe must reveal permanent ground area");
                StreetKnownObject observedObstacle = FindRemembered(first.Memory.Current, "obstacle.bicycle.west");
                Assert.IsNotNull(observedObstacle, "the spawn-facing probe must see the nearby temporary bicycle");
                Assert.AreEqual(StreetKind.Temporary, observedObstacle.Kind);
                Assert.Greater(observedObstacle.Contour.Count, 0, "the probe must retain only the obstacle contour it observed");
                Assert.IsTrue(first.Sources.Any(source => source.Active), "the live world must produce active acoustic sources");

                double exploredArea = SurfaceArea(first.Memory.Current);
                Segment2[] exploredContour = observedObstacle.Contour.ToArray();

                WalkTo(first, new Point2(-5.5f, -11.3f));
                WalkTo(first, new Point2(-6.5f, -11.3f));
                WalkTo(first, new Point2(-6.5f, -8.7f));
                WalkTo(first, new Point2(-5.5f, -8.7f));
                WalkTo(first, new Point2(-5.5f, 5.3f));
                Assert.AreEqual("checkpoint.west.approach", first.CheckpointId);
                WalkTo(first, new Point2(5.5f, 5.3f));
                Assert.AreEqual("checkpoint.east.landing", first.CheckpointId);
                Assert.AreEqual(0, first.Recoveries, "the marked crosswalk should remain passable before entering a vehicle lane");
                Assert.AreEqual(0, first.Collisions);
                WalkTo(first, new Point2(1.35f, 5.3f));
                double impactWaitStarted = first.Clock.Now;
                int waitTicks = 0;
                while (first.Recoveries == 0 && first.Clock.Now - impactWaitStarted < 80.0)
                {
                    first.Tick(new StreetInputFrame(new Point2()), 0.02);
                    if (++waitTicks > 5000) break;
                }
                Assert.AreEqual(1, first.Recoveries, "a real routed vehicle contact must restore the production session once");
                Assert.AreEqual(1, first.Collisions);
                Assert.AreEqual("checkpoint.east.landing", first.CheckpointId);
                Assert.AreEqual(exploredArea, SurfaceArea(first.Memory.Current), 1e-4,
                    "vehicle recovery must preserve permanent exploration");
                StreetKnownObject afterRecovery = FindRemembered(first.Memory.Current, "obstacle.bicycle.west");
                Assert.IsNotNull(afterRecovery, "vehicle recovery must keep the previously observed temporary contour");
                Assert.AreEqual(0, afterRecovery.CurrentUntil, "recovery must stale the saved temporary observation");

                Assert.AreEqual(SaveStatus.Success, first.Save());
                SaveResult<StreetSaveData> persisted = saves.Load(StreetLevelFactory.LevelId);
                Assert.AreEqual(SaveStatus.Success, persisted.Status);
                Assert.Greater(persisted.Value.Memory.Surfaces.Count, 0);
                Assert.IsFalse(persisted.Value.Memory.Objects.Any(item => item.Kind == StreetKind.Active),
                    "active sound sources have no representation in saved geometry memory");

                first.Menu.Back();
                Assert.AreEqual(MenuScreen.Main, first.Menu.Screen);

                continued = new StreetSession(StreetLevelFactory.Create(), new RecordingAudio(), null, saves,
                    new StreetConfig());
                Assert.AreEqual(0, continued.Sources.Count,
                    "the new session must begin with no cached acoustic samples before Continue");
                continued.Menu.Activate(MenuOption.Continue);

                Assert.IsTrue(continued.Running);
                Assert.AreEqual(exploredArea, SurfaceArea(continued.Memory.Current), 1e-4,
                    "Continue must preserve the accumulated permanent ground area");
                StreetKnownObject staleObstacle = FindRemembered(continued.Memory.Current, "obstacle.bicycle.west");
                Assert.IsNotNull(staleObstacle, "Continue must retain the last observed local temporary contour");
                Assert.AreEqual(StreetKind.Temporary, staleObstacle.Kind);
                Assert.AreEqual(0, staleObstacle.CurrentUntil,
                    "a saved obstacle snapshot must be stale until the player probes it again");
                AssertContourEqual(exploredContour, staleObstacle.Contour);

                Assert.Greater(continued.Sources.Count, 0,
                    "Continue must recalculate currently audible sources from the restored world");
                Assert.AreEqual(continued.Clock.Now, continued.Perception.SampledAt, 1e-9);
                StreetWorldFrame liveWorld = continued.World.Capture();
                foreach (AcousticQueryResult source in continued.Sources)
                {
                    Assert.AreEqual(continued.Clock.Now, source.SampledAt, 1e-9,
                        "a resumed source must use the restored simulation time");
                    StreetObject liveObject = liveWorld.Objects.FirstOrDefault(item => item.Id == source.SourceId);
                    Assert.IsNotNull(liveObject, "every resumed sound must resolve to a current world object");
                    Assert.AreEqual(liveObject.Position.X, source.ObservedSourcePosition.X, 1e-5f);
                    Assert.AreEqual(liveObject.Position.Y, source.ObservedSourcePosition.Y, 1e-5f);
                    Assert.IsFalse(continued.Memory.Current.Objects.Any(item => item.ObjectId == source.SourceId),
                        "an active sound source must not be restored as remembered geometry");
                }
            }
            finally
            {
                if (continued != null) continued.Dispose();
                if (first != null) first.Dispose();
                DeleteTemporaryRoot(root);
            }
        }

        [Test]
        public void SignalObservationsExpireAndRetryThenContinueNeverResurrectAnOldLampState()
        {
            string root = NewTemporaryRoot();
            StreetSession session = null;
            StreetSession continued = null;
            try
            {
                var saves = new StreetSaveService(root, "SignalObservationLifecycle");
                StreetLevel level = StreetLevelFactory.Create();
                session = new StreetSession(level, new RecordingAudio(), null, saves, new StreetConfig());
                session.Menu.Activate(MenuOption.Start);
                Assert.IsTrue(session.Running);

                // This is a positioning fixture for the signal-perception lifecycle. It does
                // not claim that the player walked the route to this lamp.
                session.Player.Restore(new PlayerState(PlayerMainState.Active, ActionMode.Walking,
                    new Point2(-9f, 5.3f), new Point2(1f, 0f), 100, 100, 0, 0, 0),
                    session.Clock.Now, session.Clock.Now);
                session.Tick(new StreetInputFrame(new Point2(), probe: true), 0.02);
                Assert.That(session.Signals.Count, Is.EqualTo(1),
                    "a normal production Tick with a nearby real lamp must publish one signal observation");
                Assert.That(session.Signals[0].ObjectId, Is.EqualTo("street.signal.ns"));
                Assert.That(session.Signals[0].IsGreen, Is.True);
                double expiry = session.Signals[0].ExpiresAt;
                Assert.That(expiry, Is.GreaterThan(session.Clock.Now));

                session.Tick(new StreetInputFrame(new Point2()), expiry - session.Clock.Now + 0.02);
                Assert.That(session.Signals, Is.Empty,
                    "the short-lived lamp phase must be pruned when the world clock passes its expiry");
                session.Tick(new StreetInputFrame(new Point2()), 0.02);
                Assert.That(session.Signals, Is.Empty,
                    "a normal Tick without Probe must not rebuild an expired signal observation");

                // Re-arm the cane only for this test fixture so Retry is challenged with
                // a genuinely live cached lamp observation, not an already-empty list.
                session.Player.Restore(new PlayerState(PlayerMainState.Active, ActionMode.Walking,
                    new Point2(-9f, 5.3f), new Point2(1f, 0f), 100, 100, session.Clock.Now, 0, 0),
                    session.Clock.Now, session.Clock.Now);
                session.Tick(new StreetInputFrame(new Point2(), probe: true), 0.02);
                Assert.That(session.Signals.Count, Is.EqualTo(1),
                    "a fresh Probe before Retry must repopulate the cached lamp observation");

                session.Tick(new StreetInputFrame(new Point2(), pause: true), 0.02);
                Assert.AreEqual(MenuScreen.Pause, session.Menu.Screen);
                session.Menu.Activate(MenuOption.Retry);
                Assert.IsTrue(session.Running);
                Assert.That(session.Signals, Is.Empty,
                    "Retry recovery must clear the cached lamp phase before normal play resumes");

                session.Menu.Back();
                Assert.AreEqual(MenuScreen.Main, session.Menu.Screen);
                continued = new StreetSession(level, new RecordingAudio(), null, saves, new StreetConfig());
                Assert.That(continued.Signals, Is.Empty,
                    "a newly constructed session must begin without cached lamp observations");
                continued.Menu.Activate(MenuOption.Continue);
                Assert.IsTrue(continued.Running);
                Assert.That(continued.Signals, Is.Empty,
                    "Continue may restore world and memory, but it must not resurrect the old lamp phase");
                continued.Tick(new StreetInputFrame(new Point2()), 0.02);
                Assert.That(continued.Signals, Is.Empty,
                    "Continue without a new Probe must keep signal observations empty");
            }
            finally
            {
                if (continued != null) continued.Dispose();
                if (session != null) session.Dispose();
                DeleteTemporaryRoot(root);
            }
        }

        [Test]
        public void RealRouteContinuousProbesMeasurePerceptionAndMemoryStages()
        {
            const double frameSeconds = 0.02;
            var config = new StreetConfig();
            double minimumProbeInterval = config.Gameplay.caneCooldown + 1.1;
            // The readiness check happens before each fixed step, while Player receives input
            // after that step advances simulation time. Allow two 20 ms steps plus float noise.
            double maximumProbeInterval = minimumProbeInterval + 2 * frameSeconds + 0.000001;
            StreetLevel level = StreetLevelFactory.Create();
            var clock = new SimulationClock();
            var world = new StreetWorld(level.Surfaces, level.Objects, level.Routes, config);
            var player = new StreetPlayerController(config, clock, world, level.Spawn);
            var perception = new StreetPerception(config, clock);
            var memory = new StreetMemory();
            var samples = new List<ProbePerformanceSample>();
            bool stationaryRepeatPhase = false;
            int playerContacts = 0;
            StreetFailure? playerFailure = null;
            player.Contacted += contact => playerContacts++;
            player.Failed += (reason, objectId) => playerFailure = reason;

            player.Probed += request =>
            {
                StreetWorldFrame frame = world.Capture();
                Assert.AreEqual(clock.Now, request.Time, 1e-9,
                    "probe request must use the live simulation time");
                Assert.AreEqual(request.Time, frame.Time, 1e-9,
                    "perception input must be the same frozen world sample as the request");
                Assert.IsNotNull(world.MaterialAt(request.Origin),
                    "each probe must start on a legal, non-empty ground surface");

                int oldMemoryCount = memory.Current.Surfaces.Count;
                int oldMemoryObjectCount = memory.Current.Objects.Count;
                double oldMemoryArea = TotalSurfaceArea(memory.Current);
                var timer = Stopwatch.StartNew();
                StreetProbeResult result = perception.Probe(request, frame);
                timer.Stop();
                double perceptionMilliseconds = timer.Elapsed.TotalMilliseconds;

                Assert.IsNotNull(result);
                Assert.Greater(result.Coverage.Cells.Count, 0,
                    "a real route probe must preserve non-empty confirmed coverage");
                Assert.Greater(result.Surfaces.Count, 0,
                    "a real route probe must observe permitted ground geometry");
                double coverageArea = CoverageArea(result.Coverage);
                double observedArea = SurfaceArea(result.Surfaces);
                Assert.Greater(coverageArea, 0,
                    "coverage cells must retain positive area");
                Assert.Greater(observedArea, 0,
                    "observed ground fragments must retain positive area");
                int invalidBoundarySegments = CountInvalidSegments(result.Coverage.OcclusionBoundary);

                timer.Restart();
                memory.Apply(result);
                timer.Stop();
                double applyMilliseconds = timer.Elapsed.TotalMilliseconds;
                double newMemoryArea = TotalSurfaceArea(memory.Current);
                Assert.Greater(memory.Current.Surfaces.Count, 0);
                Assert.Greater(newMemoryArea, 0);
                Assert.GreaterOrEqual(newMemoryArea + 0.001, oldMemoryArea,
                    "confirmed ground area must not collapse while the route is explored");

                var sample = new ProbePerformanceSample
                {
                    Phase = stationaryRepeatPhase ? "stationary-repeat" : "route",
                    Time = request.Time,
                    X = request.Origin.X,
                    Y = request.Origin.Y,
                    CoverageCells = result.Coverage.Cells.Count,
                    BoundarySegments = result.Coverage.OcclusionBoundary.Count,
                    InvalidBoundarySegments = invalidBoundarySegments,
                    ObservedSurfaces = result.Surfaces.Count,
                    MemorySurfacesBefore = oldMemoryCount,
                    MemorySurfacesAfter = memory.Current.Surfaces.Count,
                    MemoryObjectsBefore = oldMemoryObjectCount,
                    MemoryObjectsAfter = memory.Current.Objects.Count,
                    CoverageArea = coverageArea,
                    ObservedArea = observedArea,
                    MemoryArea = newMemoryArea,
                    PerceptionMilliseconds = perceptionMilliseconds,
                    ApplyMilliseconds = applyMilliseconds
                };
                samples.Add(sample);
                TestContext.WriteLine(sample.ToProgressLine());
                TestContext.Progress.WriteLine(sample.ToProgressLine());
            };

            // Retain the diagnostic's original cadence and waypoints: initial cane tap, then probe
            // as soon as the three-second cooldown plus 1.1 seconds has elapsed.
            Advance(world, clock, player, new StreetInputFrame(new Point2(), probe: true), frameSeconds);
            Point2[] targets =
            {
                new Point2(-5.5f, -11.3f),
                new Point2(-6.5f, -11.3f),
                new Point2(-6.5f, -8.7f),
                new Point2(-5.5f, -8.7f),
                new Point2(-5.5f, 5.3f)
            };

            foreach (Point2 target in targets)
            {
                int steps = 0;
                while (StreetGeometry.Distance(player.Current.Position, target) > 0.035f && steps < 2000)
                {
                    Point2 from = player.Current.Position;
                    float dx = target.X - from.X;
                    float dy = target.Y - from.Y;
                    float distance = StreetGeometry.Distance(from, target);
                    Point2 facing = player.Current.Facing;
                    double turn = (Math.Atan2(dx, dy) - Math.Atan2(facing.X, facing.Y)) * 180 / Math.PI;
                    turn = (turn + 540) % 360 - 180;
                    bool probe = clock.Now >= player.Current.CaneReadyAt + 1.1 && player.Current.Focus > 45;
                    Advance(world, clock, player,
                        new StreetInputFrame(new Point2(dx / distance, dy / distance), (float)turn, probe), frameSeconds);
                    steps++;
                }

                Assert.Less(steps, 2000,
                    "the production Player must reach the next historical probe-route target");
                Assert.Less(StreetGeometry.Distance(player.Current.Position, target), 0.08f,
                    "the production Player stopped short of a route target");
                TestContext.Progress.WriteLine(string.Format(CultureInfo.InvariantCulture,
                    "ROUTE reached=({0:F3},{1:F3}) t={2:F2} probes={3}",
                    player.Current.Position.X, player.Current.Position.Y, clock.Now, samples.Count));
            }

            int repeatedProbeStart = samples.Count;
            Assert.AreEqual(5, repeatedProbeStart,
                "the historical route cadence must produce the initial probe plus four route probes");
            stationaryRepeatPhase = true;
            const int repeatedProbeCount = 8;
            int stationaryFrames = 0;
            while (samples.Count < repeatedProbeStart + repeatedProbeCount &&
                stationaryFrames < 10000 && !playerFailure.HasValue)
            {
                bool probe = clock.Now >= player.Current.CaneReadyAt + 1.1 && player.Current.Focus > 45;
                Advance(world, clock, player, new StreetInputFrame(new Point2(), probe: probe), frameSeconds);
                stationaryFrames++;
            }

            Assert.AreEqual(repeatedProbeCount, samples.Count - repeatedProbeStart,
                "the legal stationary checkpoint must allow eight cooldown-spaced fresh probes");
            Assert.Less(stationaryFrames, 10000,
                "the player must keep the ordinary focus/cooldown state needed for the repeated probes");
            Assert.Less(StreetGeometry.Distance(player.Current.Position, targets[targets.Length - 1]),
                0.08f, "stationary probes must not move the player away from the west checkpoint");

            ProbePerformanceSample firstRepeatedProbe = samples[repeatedProbeStart];
            double firstStationaryInterval =
                firstRepeatedProbe.Time - samples[repeatedProbeStart - 1].Time;
            Assert.That(firstStationaryInterval, Is.InRange(minimumProbeInterval, maximumProbeInterval),
                string.Format(CultureInfo.InvariantCulture,
                    "the first stationary probe must follow cooldown plus 1.1 s, within fixed-step quantization " +
                    "[{0:F6}, {1:F6}] s; actual={2:R} s",
                    minimumProbeInterval, maximumProbeInterval, firstStationaryInterval));
            int maxRepeatedMemorySurfaces = firstRepeatedProbe.MemorySurfacesAfter;
            for (int i = repeatedProbeStart + 1; i < samples.Count; i++)
            {
                ProbePerformanceSample previous = samples[i - 1];
                ProbePerformanceSample sample = samples[i];
                double stationaryInterval = sample.Time - previous.Time;
                Assert.That(stationaryInterval, Is.InRange(minimumProbeInterval, maximumProbeInterval),
                    string.Format(CultureInfo.InvariantCulture,
                        "repeated probes must follow cooldown plus 1.1 s, within fixed-step quantization " +
                        "[{0:F6}, {1:F6}] s; probe index={2}, actual={3:R} s",
                        minimumProbeInterval, maximumProbeInterval, i - repeatedProbeStart + 1,
                        stationaryInterval));
                Assert.AreEqual(firstRepeatedProbe.CoverageCells, sample.CoverageCells,
                    "stationary probes should retain the same coverage partition");
                Assert.AreEqual(firstRepeatedProbe.ObservedSurfaces, sample.ObservedSurfaces,
                    "stationary probes should retain the same observed ground fragments");
                Assert.AreEqual(firstRepeatedProbe.CoverageArea, sample.CoverageArea, 0.001,
                    "stationary probes should preserve their confirmed coverage area");
                Assert.AreEqual(firstRepeatedProbe.ObservedArea, sample.ObservedArea, 0.001,
                    "stationary probes should preserve observed surface area");
                maxRepeatedMemorySurfaces = Math.Max(maxRepeatedMemorySurfaces, sample.MemorySurfacesAfter);
            }

            double repeatedAreaTolerance = Math.Max(0.02, firstRepeatedProbe.MemoryArea * 0.005);
            ProbePerformanceSample lastRepeatedProbe = samples[samples.Count - 1];
            Assert.AreEqual(firstRepeatedProbe.MemoryArea, lastRepeatedProbe.MemoryArea, repeatedAreaTolerance,
                "repeated coverage must not materially grow or erase already-known ground area");
            Assert.LessOrEqual(maxRepeatedMemorySurfaces,
                firstRepeatedProbe.MemorySurfacesAfter + Math.Max(64, firstRepeatedProbe.MemorySurfacesAfter / 4),
                "repeated coverage must not cause unbounded surface-fragment proliferation");

            // Initial overlap clipping may settle at the second repeat. Once it
            // has settled, identical input geometry must not manufacture fresh
            // boundary fragments on every subsequent probe.
            ProbePerformanceSample settledProbe = samples[repeatedProbeStart + 2];
            for (int i = repeatedProbeStart + 3; i < samples.Count; i++)
            {
                Assert.AreEqual(settledProbe.MemorySurfacesAfter, samples[i].MemorySurfacesAfter,
                    "identical stationary coverage must retain the settled fragment count");
                Assert.AreEqual(settledProbe.MemoryArea, samples[i].MemoryArea, 0.0000001,
                    "identical stationary coverage must retain the settled known area");
            }

            Assert.IsFalse(playerFailure.HasValue,
                "the historical exploration route should reach its checkpoint without vehicle failure");
            Assert.AreEqual(0, playerContacts,
                "the historical route must continue to avoid the known bicycle and other obstacles");
            Assert.AreEqual(13, samples.Count,
                "the measurement consists of five historical route probes plus eight stationary probes");
            Assert.IsTrue(samples.Any(sample => Math.Abs(sample.Time - 8.28) <= 0.2),
                "the replay must include the former t=8.28 performance hotspot");
            Assert.IsTrue(samples.Any(sample => sample.BoundarySegments > 0),
                "at least one real probe must retain an occlusion boundary at the bicycle/obstacle");
            Assert.IsTrue(samples.All(sample => sample.InvalidBoundarySegments == 0),
                "reported occlusion boundaries must contain finite, non-zero segments");
            Assert.Greater(TotalSurfaceArea(memory.Current), samples[0].MemoryArea,
                "continued exploration must add confirmed ground area");

            double totalPerceptionMilliseconds = samples.Sum(sample => sample.PerceptionMilliseconds);
            double totalApplyMilliseconds = samples.Sum(sample => sample.ApplyMilliseconds);
            TimingSummary perceptionSummary = Summarize(samples, sample => sample.PerceptionMilliseconds);
            TimingSummary applySummary = Summarize(samples, sample => sample.ApplyMilliseconds);
            double finalArea = TotalSurfaceArea(memory.Current);
            TestContext.Progress.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "SUMMARY probes={0} perceptionMs(sum/median/p95/max)={1:F3}/{2:F3}/{3:F3}/{4:F3} " +
                "applyMs(sum/median/p95/max)={5:F3}/{6:F3}/{7:F3}/{8:F3} " +
                "finalMemorySurfaces={9} finalMemoryArea={10:F6} finalTime={11:F2} position=({12:F3},{13:F3})",
                samples.Count, totalPerceptionMilliseconds, perceptionSummary.Median, perceptionSummary.P95,
                perceptionSummary.Maximum, totalApplyMilliseconds, applySummary.Median, applySummary.P95,
                applySummary.Maximum, memory.Current.Surfaces.Count, finalArea, clock.Now,
                player.Current.Position.X, player.Current.Position.Y));
            WriteProbePerformanceEvidenceIfConfigured(samples, totalPerceptionMilliseconds,
                totalApplyMilliseconds, perceptionSummary, applySummary,
                memory.Current.Surfaces.Count, finalArea, clock.Now,
                player.Current.Position);
        }

        [Test]
        public void A24_RealVehicleContactBeforeAnyCheckpointRestoresSpawnAndStaysSafe()
        {
            string root = NewTemporaryRoot();
            StreetSession session = null;
            var sessionErrors = new List<string>();
            var contactDiagnostics = new List<string>();
            try
            {
                StreetLevel level = StreetLevelFactory.Create();
                var audio = new RecordingAudio();
                session = new StreetSession(level, audio, audio,
                    new StreetSaveService(root, "A24NoCheckpointRecovery"), new StreetConfig());
                session.Error += message => sessionErrors.Add(message);
                session.Menu.Activate(MenuOption.Start);
                session.Player.Contacted += contact => contactDiagnostics.Add(
                    DescribeContact(session, contact, "A24"));

                StreetRoute southboundTraffic = level.Routes.FirstOrDefault(route => route.ObjectId == "vehicle.ns.southbound");
                Assert.IsNotNull(southboundTraffic);
                Assert.AreEqual("ns", southboundTraffic.SignalAxis);
                StreetFailure? failure = null;
                string failedObjectId = null;
                session.Player.Failed += (reason, objectId) => { failure = reason; failedObjectId = objectId; };

                Assert.IsTrue(session.Running);
                Assert.AreEqual("spawn", session.CheckpointId);
                Assert.AreEqual(new Point2(-5.5f, -16), level.Spawn.Position);

                // Walk from the real spawn to a routed southbound traffic lane, well before the first checkpoint.
                WalkTo(session, new Point2(-1.35f, -16));
                Assert.AreEqual("spawn", session.CheckpointId,
                    "the production west-side route must not activate a checkpoint before the collision");
                Assert.AreEqual(0, session.Collisions);
                Assert.AreEqual(0, session.Recoveries);

                string waitDiagnostics;
                int waitTicks = WaitForSessionConditionWithDiagnostics(session,
                    () => session.Recoveries > 0, 5000, sessionErrors, contactDiagnostics, out waitDiagnostics);
                Assert.Less(waitTicks, 5000,
                    "a real routed vehicle should eventually sweep through the player's lane; " + waitDiagnostics);
                Assert.AreEqual(1, session.Collisions);
                Assert.AreEqual(1, session.Recoveries);
                Assert.AreEqual(StreetFailure.Vehicle, failure,
                    "the session must recover from a real routed vehicle failure, not a static contact");
                Assert.AreEqual("vehicle.ns.southbound", failedObjectId);
                Assert.AreEqual("spawn", session.CheckpointId);
                Assert.AreEqual(level.Spawn.Position.X, session.Player.Current.Position.X, 1e-5f);
                Assert.AreEqual(level.Spawn.Position.Y, session.Player.Current.Position.Y, 1e-5f);

                // Continue five simulated seconds at spawn to catch a duplicate or immediate re-contact.
                for (int i = 0; i < 250; i++)
                    session.Tick(new StreetInputFrame(new Point2()), 0.02);

                Assert.AreEqual(1, session.Collisions);
                Assert.AreEqual(1, session.Recoveries);
                Assert.AreEqual("spawn", session.CheckpointId);
                Assert.AreEqual(level.Spawn.Position.X, session.Player.Current.Position.X, 1e-5f);
                Assert.AreEqual(level.Spawn.Position.Y, session.Player.Current.Position.Y, 1e-5f);
            }
            finally
            {
                if (session != null) session.Dispose();
                DeleteTemporaryRoot(root);
            }
        }

        [Test]
        public void ProductionWorldPlayerAndSimulationClockReproduceSouthboundVehicleContact()
        {
            const double frameSeconds = 0.02;
            var config = new StreetConfig();
            StreetLevel level = StreetLevelFactory.Create();
            var clock = new SimulationClock();
            clock.SetPaused(true);
            clock.Reset(0);
            var world = new StreetWorld(level.Surfaces, level.Objects, level.Routes, config);
            var player = new StreetPlayerController(config, clock, world, level.Spawn);
            var contactDiagnostics = new List<string>();
            var samples = new List<string>();
            StreetFailure? failure = null;
            player.Contacted += contact => contactDiagnostics.Add(
                DescribeContact(clock, player, world, contact, "WorldPlayer"));
            player.Failed += (reason, objectId) => failure = reason;
            clock.SetPaused(false);

            // Match the production path to the lane: 172 eastward 20 ms frames from the real spawn,
            // then hold position while the routed southbound vehicle approaches.
            for (int i = 0; i < 172; i++)
            {
                world.Tick(frameSeconds, false);
                clock.Advance(frameSeconds);
                player.Tick(new StreetInputFrame(new Point2(1, 0)), frameSeconds);
            }

            Assert.AreEqual(-1.372f, player.Current.Position.X, 0.01f,
                "the fixed-frame approach should reproduce the production lane position");
            Assert.AreEqual(-16f, player.Current.Position.Y, 0.001f);

            int waitFrames = 0;
            while (!failure.HasValue && waitFrames < 100 && clock.Now < 4.5)
            {
                world.Tick(frameSeconds, false);
                clock.Advance(frameSeconds);
                player.Tick(new StreetInputFrame(new Point2()), frameSeconds);
                StreetWorldFrame frame = world.Capture();
                if (clock.Now >= 3.8 && clock.Now <= 4.5)
                    samples.Add(DescribeVehicleFrame(clock.Now, player.Current.Position, frame));
                waitFrames++;
            }

            string diagnostics = string.Join(" | ", samples.ToArray()) +
                " | contacts=" + (contactDiagnostics.Count == 0 ? "none" : string.Join(" | ", contactDiagnostics.ToArray()));
            Assert.AreEqual(StreetFailure.Vehicle, failure,
                "the isolated production World + Player + SimulationClock path should detect the routed vehicle; " + diagnostics);
            Assert.AreEqual(1, contactDiagnostics.Count, diagnostics);
            Assert.GreaterOrEqual(clock.Now, 3.8, diagnostics);
            Assert.LessOrEqual(clock.Now, 4.5, diagnostics);
        }

        [Test]
        public void A22_CrossingWithRedNorthSouthTrafficRemainsFreeAndKeepsTrafficRunning()
        {
            string root = NewTemporaryRoot();
            StreetSession session = null;
            try
            {
                StreetLevel level = StreetLevelFactory.Create();
                var config = new StreetConfig();
                var audio = new RecordingAudio();
                session = new StreetSession(level, audio, audio,
                    new StreetSaveService(root, "A22RedLightCrossing"), config);
                session.Menu.Activate(MenuOption.Start);

                // The north crosswalk is an east-west walking path across the north-south vehicle axis.
                Assert.IsNotNull(level.Surfaces.FirstOrDefault(surface => surface.Id == "crosswalk.ns.north"));
                StreetRoute northboundTraffic = level.Routes.FirstOrDefault(route => route.ObjectId == "vehicle.ns.northbound");
                Assert.IsNotNull(northboundTraffic);
                Assert.AreEqual("ns", northboundTraffic.SignalAxis,
                    "the cars crossing the pedestrian path are controlled by the north-south signal axis");

                WalkTo(session, new Point2(-5.5f, -11.3f));
                WalkTo(session, new Point2(-6.5f, -11.3f));
                WalkTo(session, new Point2(-6.5f, -8.7f));
                WalkTo(session, new Point2(-5.5f, -8.7f));
                WalkTo(session, level.Checkpoints[0].Position);
                Assert.AreEqual("checkpoint.west.approach", session.CheckpointId);
                Assert.AreEqual(0, session.Collisions);
                Assert.AreEqual(0, session.Recoveries);

                // This is an east-west pedestrian crossing over north-south traffic. During NS,
                // the vehicles have right of way and the EW crossing cue is red. Wait until the
                // actual routed vehicles have cleared the crosswalk, while preserving time for
                // the full walk before the NS phase ends.
                double nsEnd = config.SignalGreenSeconds;
                double cycle = 2 * (config.SignalGreenSeconds + config.SignalClearanceSeconds);
                double crossingSeconds = 11.0 / config.Gameplay.walkSpeed;
                double crossingWindow = crossingSeconds + 0.25;
                int alignTicks = 0;
                while (alignTicks < 4000)
                {
                    double phaseOffset = session.Clock.Now % cycle;
                    StreetWorldFrame alignmentFrame = session.World.Capture();
                    StreetObject southbound = alignmentFrame.Objects.FirstOrDefault(item => item.Id == "vehicle.ns.southbound");
                    StreetObject northbound = alignmentFrame.Objects.FirstOrDefault(item => item.Id == "vehicle.ns.northbound");
                    bool bothVehiclesPastCrosswalk = southbound != null && northbound != null &&
                        southbound.Position.Y < 3.8f && northbound.Position.Y > 6.8f;
                    if (session.World.SignalPhase == "ns" &&
                        phaseOffset + crossingWindow <= nsEnd && bothVehiclesPastCrosswalk)
                        break;
                    session.Tick(new StreetInputFrame(new Point2()), 0.02);
                    alignTicks++;
                }
                Assert.Less(alignTicks, 4000,
                    "both routed NS vehicles must clear the crosswalk while a full EW-crossing window remains");
                Assert.AreEqual("ns", session.World.SignalPhase);

                StreetWorldFrame beforeCrossing = session.World.Capture();
                StreetObject nsSignal = beforeCrossing.Objects.FirstOrDefault(item => item.Id == "street.signal.ns");
                StreetObject ewSignal = beforeCrossing.Objects.FirstOrDefault(item => item.Id == "street.signal.ew");
                Assert.IsNotNull(nsSignal);
                Assert.IsNotNull(ewSignal);
                Assert.IsTrue(nsSignal.SoundActive, "NS traffic phase must be active while the EW crossing cue is red");
                Assert.IsFalse(ewSignal.SoundActive, "the EW crossing cue must remain red for the whole walk");
                StreetObject nsSouthboundBefore = beforeCrossing.Objects.FirstOrDefault(item => item.Id == "vehicle.ns.southbound");
                StreetObject nsNorthboundBefore = beforeCrossing.Objects.FirstOrDefault(item => item.Id == "vehicle.ns.northbound");
                Assert.IsNotNull(nsSouthboundBefore);
                Assert.IsNotNull(nsNorthboundBefore);
                Assert.Less(nsSouthboundBefore.Position.Y, 5.3f - 1.5f,
                    "the southbound NS vehicle must already have passed the crosswalk before the crossing begins");
                Assert.Greater(nsNorthboundBefore.Position.Y, 5.3f + 1.5f,
                    "the northbound NS vehicle must already have passed the crosswalk before the crossing begins");
                PlayerState beforePlayer = session.Player.Current;
                Assert.AreEqual(0, session.Collisions);
                Assert.AreEqual(0, session.Recoveries);
                audio.Spoken.Clear();

                WalkTo(session, level.Checkpoints[1].Position);

                Assert.AreEqual("ns", session.World.SignalPhase,
                    "the EW crossing must finish during the same NS vehicle phase");
                Assert.AreEqual("checkpoint.east.landing", session.CheckpointId);
                Assert.AreEqual(0, session.Collisions, "the crossing schedule must avoid legal vehicle contact");
                Assert.AreEqual(0, session.Recoveries);
                Assert.AreEqual(beforePlayer.Courage, session.Player.Current.Courage, 1e-6f,
                    "red-light crossing cannot reduce courage");
                Assert.Less(StreetGeometry.Distance(session.Player.Current.Position, level.Checkpoints[1].Position), 0.08f);

                StreetWorldFrame afterCrossing = session.World.Capture();
                StreetObject nsSouthboundAfter = afterCrossing.Objects.FirstOrDefault(item => item.Id == "vehicle.ns.southbound");
                StreetObject nsNorthboundAfter = afterCrossing.Objects.FirstOrDefault(item => item.Id == "vehicle.ns.northbound");
                Assert.IsNotNull(nsSouthboundAfter);
                Assert.IsNotNull(nsNorthboundAfter);
                Assert.Greater(StreetGeometry.Distance(nsSouthboundBefore.Position, nsSouthboundAfter.Position), 0.5f,
                    "real southbound NS traffic must continue moving during the crossing");
                Assert.Greater(StreetGeometry.Distance(nsNorthboundBefore.Position, nsNorthboundAfter.Position), 0.5f,
                    "real northbound NS traffic must continue moving during the crossing");
                Assert.IsFalse(audio.Spoken.Any(request => request.Text.Contains("车道") || request.Text.Contains("入机动车道")),
                    "the red-light crossing must not emit a lane-entry warning");
            }
            finally
            {
                if (session != null) session.Dispose();
                DeleteTemporaryRoot(root);
            }
        }

        [Test]
        public void OrdinaryVehicleFreeObstacleContactPlaysCollisionBumpThroughSession()
        {
            string root = NewTemporaryRoot();
            StreetSession session = null;
            try
            {
                var audio = new RecordingAudio();
                session = new StreetSession(StreetLevelFactory.Create(), audio, audio,
                    new StreetSaveService(root, "OrdinaryContactAudio"), new StreetConfig());
                session.Menu.Activate(MenuOption.Start);

                StreetContact ordinaryContact = null;
                session.Player.Contacted += contact =>
                {
                    if (contact != null && !contact.Vehicle) ordinaryContact = contact;
                };
                int ticks = 0;
                while (ordinaryContact == null && ticks < 1000)
                {
                    session.Tick(new StreetInputFrame(new Point2(0, 1)), 0.02);
                    ticks++;
                }

                Assert.Less(ticks, 1000, "walking into the real west-side bicycle must produce an ordinary contact");
                Assert.AreEqual("obstacle.bicycle.west", ordinaryContact.ObjectId);
                Assert.IsFalse(ordinaryContact.Vehicle);
                Assert.IsTrue(audio.Played.Any(request => request.SoundId == "collision.bump"),
                    "the production Session must route ordinary contact feedback to playback");
                Assert.AreEqual(1, session.Collisions);
                Assert.AreEqual(0, session.Recoveries, "ordinary obstacle contact must not trigger vehicle recovery");
            }
            finally
            {
                if (session != null) session.Dispose();
                DeleteTemporaryRoot(root);
            }
        }

        [Test]
        public void RecoveryBackendFailurePausesProtectsLastGoodSaveAndRetryRestoresPlay()
        {
            string root = NewTemporaryRoot();
            StreetSession session = null;
            RecordingAudio audio = null;
            var sessionErrors = new List<string>();
            var contactDiagnostics = new List<string>();
            try
            {
                StreetLevel level = StreetLevelFactory.Create();
                var saves = new StreetSaveService(root, "RecoveryFaultGuard");
                audio = new RecordingAudio();
                session = new StreetSession(level, audio, audio, saves, new StreetConfig());
                session.Error += message => sessionErrors.Add(message);
                session.Menu.Activate(MenuOption.Start);
                session.Player.Contacted += contact => contactDiagnostics.Add(
                    DescribeContact(session, contact, "RecoveryFault"));
                Assert.AreEqual(SaveStatus.Success, session.Save());
                SaveResult<StreetSaveData> lastGood = saves.Load(level.Id);
                Assert.AreEqual(SaveStatus.Success, lastGood.Status);
                Assert.AreEqual("spawn", lastGood.Value.CheckpointId);
                Point2 lastGoodPosition = lastGood.Value.Player.Position;
                double lastGoodTrafficTime = lastGood.Value.Traffic.Time;
                int lastGoodCollisions = lastGood.Value.Collisions;

                StreetFailure? failure = null;
                session.Player.Failed += (reason, objectId) => failure = reason;
                audio.Spoken.Clear();
                audio.ThrowOnReset = true;

                // Enter the real southbound lane; the backend throws only when the actual vehicle failure
                // drives the production Session through its recovery reset.
                WalkTo(session, new Point2(-1.35f, -16));
                string waitDiagnostics;
                int waitTicks = WaitForSessionConditionWithDiagnostics(session,
                    () => audio.ResetFailureObserved, 5000, sessionErrors, contactDiagnostics, out waitDiagnostics);

                Assert.Less(waitTicks, 5000,
                    "the routed vehicle must reach the player and enter recovery; " + waitDiagnostics);
                Assert.AreEqual(StreetFailure.Vehicle, failure);
                Assert.IsTrue(audio.ResetFailureObserved, "the injected backend failure must occur in recovery");
                Assert.IsTrue(session.Clock.Paused);
                Assert.AreEqual(MenuScreen.Pause, session.Menu.Screen);
                Assert.IsTrue(audio.Spoken.Any(request => request.Text == "出了点问题，请先回到首页，再试一次。"),
                    "fault reporting must use the fixed safe instruction");

                session.Resume();
                Assert.IsTrue(session.Clock.Paused, "direct Resume must not advance a faulted partial session");
                session.Menu.Activate(MenuOption.Resume);
                Assert.IsTrue(session.Clock.Paused, "the menu must not resume a faulted partial session");
                Assert.AreEqual(MenuScreen.Pause, session.Menu.Screen,
                    "the menu must not claim Playing after a rejected resume");

                Assert.AreEqual(SaveStatus.IoFailure, session.Save(),
                    "a faulted partial recovery must refuse to write a replacement save");
                SaveResult<StreetSaveData> afterFaultSave = saves.Load(level.Id);
                Assert.AreEqual(SaveStatus.Success, afterFaultSave.Status);
                Assert.AreEqual(lastGood.Value.CheckpointId, afterFaultSave.Value.CheckpointId);
                Assert.AreEqual(lastGoodPosition.X, afterFaultSave.Value.Player.Position.X, 1e-5f);
                Assert.AreEqual(lastGoodPosition.Y, afterFaultSave.Value.Player.Position.Y, 1e-5f);
                Assert.AreEqual(lastGoodTrafficTime, afterFaultSave.Value.Traffic.Time, 1e-9);
                Assert.AreEqual(lastGoodCollisions, afterFaultSave.Value.Collisions);

                audio.ThrowOnReset = false;
                session.Menu.Activate(MenuOption.Retry);
                Assert.IsFalse(session.Clock.Paused, "Retry with a healthy backend must restore a usable session");
                Assert.AreEqual(MenuScreen.Playing, session.Menu.Screen);
                Assert.AreEqual("spawn", session.CheckpointId);
                Assert.AreEqual(level.Spawn.Position.X, session.Player.Current.Position.X, 1e-5f);
                Assert.AreEqual(level.Spawn.Position.Y, session.Player.Current.Position.Y, 1e-5f);
                Assert.AreEqual(SaveStatus.Success, session.Save());
            }
            finally
            {
                if (audio != null) audio.ThrowOnReset = false;
                if (session != null) session.Dispose();
                DeleteTemporaryRoot(root);
            }
        }

        private static StreetKnownObject FindRemembered(StreetMemoryFrame memory, string id)
        {
            return memory.Objects.FirstOrDefault(item => item.ObjectId == id);
        }

        private static double SurfaceArea(StreetMemoryFrame memory)
        {
            return memory.Surfaces.Sum(surface => Math.Abs(StreetGeometry.Area(surface.Polygon)));
        }

        private static double TotalSurfaceArea(StreetMemoryFrame memory)
        {
            return memory.Surfaces.Sum(surface => (double)Math.Abs(StreetGeometry.Area(surface.Polygon)));
        }

        private static double CoverageArea(ProbeCoverage coverage)
        {
            double area = 0;
            foreach (ProbeCell cell in coverage.Cells)
                area += Math.Abs((double)StreetGeometry.Area(cell.Polygon));
            return area;
        }

        private static double SurfaceArea(IEnumerable<StreetKnownSurface> surfaces)
        {
            return surfaces.Sum(surface => Math.Abs((double)StreetGeometry.Area(surface.Polygon)));
        }

        private static TimingSummary Summarize(IList<ProbePerformanceSample> samples,
            Func<ProbePerformanceSample, double> selector)
        {
            double[] sorted = samples.Select(selector).OrderBy(value => value).ToArray();
            int p95Index = Math.Max(0, (int)Math.Ceiling(sorted.Length * 0.95) - 1);
            double median = sorted.Length % 2 == 0
                ? (sorted[sorted.Length / 2 - 1] + sorted[sorted.Length / 2]) * 0.5
                : sorted[sorted.Length / 2];
            return new TimingSummary(median, sorted[p95Index], sorted[sorted.Length - 1]);
        }

        private sealed class TimingSummary
        {
            public readonly double Median, P95, Maximum;
            public TimingSummary(double median, double p95, double maximum)
            {
                Median = median;
                P95 = p95;
                Maximum = maximum;
            }
        }

        private static int CountInvalidSegments(IReadOnlyList<Segment2> segments)
        {
            int invalid = 0;
            foreach (Segment2 segment in segments)
            {
                float length = StreetGeometry.Distance(segment.A, segment.B);
                if (float.IsNaN(length) || float.IsInfinity(length) || length <= 1e-6f)
                    invalid++;
            }
            return invalid;
        }

        private static void WriteProbePerformanceEvidenceIfConfigured(
            IList<ProbePerformanceSample> samples, double totalPerceptionMilliseconds,
            double totalApplyMilliseconds, TimingSummary perceptionSummary,
            TimingSummary applySummary, int finalSurfaceCount, double finalArea,
            double finalTime, Point2 finalPosition)
        {
            string evidenceRoot = Environment.GetEnvironmentVariable("TINGLU_UPGRADE_EVIDENCE_ROOT");
            if (String.IsNullOrWhiteSpace(evidenceRoot))
            {
                TestContext.Progress.WriteLine(
                    "EVIDENCE_JSON skipped: TINGLU_UPGRADE_EVIDENCE_ROOT is unset.");
                return;
            }

            Directory.CreateDirectory(evidenceRoot);
            string fileName = "probe-performance-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-ffff", CultureInfo.InvariantCulture) +
                "-" + Guid.NewGuid().ToString("N").Substring(0, 8) + ".json";
            string path = Path.Combine(evidenceRoot, fileName);
            var json = new StringBuilder();
            json.AppendLine("{");
            json.AppendLine("  \"measurement\": \"unity-editor-real-route-probe\",");
            json.AppendLine("  \"timeBasis\": \"Unity EditMode Stopwatch; not device audio latency or human experience\",");
            json.AppendFormat(CultureInfo.InvariantCulture,
                "  \"probeCount\": {0},\n" +
                "  \"perceptionMilliseconds\": {{ \"sum\": {1:R}, \"median\": {2:R}, \"p95\": {3:R}, \"max\": {4:R} }},\n" +
                "  \"memoryApplyMilliseconds\": {{ \"sum\": {5:R}, \"median\": {6:R}, \"p95\": {7:R}, \"max\": {8:R} }},\n" +
                "  \"finalMemorySurfaceCount\": {9},\n  \"finalMemoryArea\": {10:R},\n" +
                "  \"finalSimulationTime\": {11:R},\n  \"finalPosition\": [ {12:R}, {13:R} ],\n" +
                "  \"probes\": [\n",
                samples.Count, totalPerceptionMilliseconds, perceptionSummary.Median, perceptionSummary.P95,
                perceptionSummary.Maximum, totalApplyMilliseconds, applySummary.Median, applySummary.P95,
                applySummary.Maximum, finalSurfaceCount, finalArea, finalTime, finalPosition.X, finalPosition.Y);
            for (int i = 0; i < samples.Count; i++)
            {
                ProbePerformanceSample sample = samples[i];
                json.AppendFormat(CultureInfo.InvariantCulture,
                    "    {{ \"phase\": \"{0}\", \"time\": {1:R}, \"origin\": [ {2:R}, {3:R} ], \"coverageCells\": {4}, " +
                    "\"boundarySegments\": {5}, \"invalidBoundarySegments\": {6}, \"observedSurfaces\": {7}, " +
                    "\"memorySurfacesBefore\": {8}, \"memorySurfacesAfter\": {9}, " +
                    "\"memoryObjectsBefore\": {10}, \"memoryObjectsAfter\": {11}, " +
                    "\"coverageArea\": {12:R}, \"observedArea\": {13:R}, \"memoryArea\": {14:R}, " +
                    "\"perceptionMilliseconds\": {15:R}, \"memoryApplyMilliseconds\": {16:R} }}{17}\n",
                    sample.Phase, sample.Time, sample.X, sample.Y, sample.CoverageCells, sample.BoundarySegments,
                    sample.InvalidBoundarySegments, sample.ObservedSurfaces,
                    sample.MemorySurfacesBefore, sample.MemorySurfacesAfter,
                    sample.MemoryObjectsBefore, sample.MemoryObjectsAfter,
                    sample.CoverageArea, sample.ObservedArea, sample.MemoryArea,
                    sample.PerceptionMilliseconds, sample.ApplyMilliseconds,
                    i + 1 == samples.Count ? "" : ",");
            }
            json.AppendLine("  ]");
            json.AppendLine("}");
            File.WriteAllText(path, json.ToString(), new UTF8Encoding(false));
            TestContext.Progress.WriteLine("EVIDENCE_JSON " + path);
        }

        private static void WalkTo(StreetSession session, Point2 target)
        {
            int steps = 0;
            while (StreetGeometry.Distance(session.Player.Current.Position, target) > 0.035f && steps < 2000)
            {
                Point2 from = session.Player.Current.Position;
                float dx = target.X - from.X;
                float dy = target.Y - from.Y;
                float length = StreetGeometry.Distance(from, target);
                session.Tick(new StreetInputFrame(new Point2(dx / length, dy / length)), 0.02);
                steps++;
            }
            Assert.Less(steps, 2000,
                "Could not reach " + target.X + "," + target.Y + "; current=" +
                session.Player.Current.Position.X + "," + session.Player.Current.Position.Y);
            Assert.Less(StreetGeometry.Distance(session.Player.Current.Position, target), 0.08f,
                "Stopped short while walking to " + target.X + "," + target.Y);
        }

        private static void Advance(StreetWorld world, SimulationClock clock,
            StreetPlayerController player, StreetInputFrame input, double delta)
        {
            world.Tick(delta, false);
            clock.Advance(delta);
            player.Tick(input, delta);
        }

        private static int WaitForSessionConditionWithDiagnostics(StreetSession session,
            Func<bool> condition, int maxTicks, IList<string> sessionErrors,
            IList<string> contactDiagnostics, out string diagnostics)
        {
            var samples = new List<string>();
            samples.Add(DescribeSession(session, 0, "before wait", sessionErrors, contactDiagnostics));
            int ticks = 0;
            while (!condition() && ticks < maxTicks)
            {
                session.Tick(new StreetInputFrame(new Point2()), 0.02);
                ticks++;
                if (ticks % 250 == 0 || (session.Clock.Now >= 3.8 && session.Clock.Now <= 4.5))
                    samples.Add(DescribeSession(session, ticks, "wait", sessionErrors, contactDiagnostics));
            }
            if (ticks % 250 != 0)
                samples.Add(DescribeSession(session, ticks, "end wait", sessionErrors, contactDiagnostics));
            diagnostics = string.Join(" | ", samples.ToArray());
            return ticks;
        }

        private static string DescribeSession(StreetSession session, int tick, string label,
            IList<string> sessionErrors, IList<string> contactDiagnostics)
        {
            if (session.Player == null || session.World == null)
                return string.Format("{0} tick={1} clock={2:F2} paused={3} running={4} menu={5} " +
                    "player/world not initialized; errors={6} contacts={7}", label, tick, session.Clock.Now,
                    session.Clock.Paused, session.Running, session.Menu.Screen,
                    sessionErrors.Count == 0 ? "none" : string.Join(" -> ", sessionErrors.ToArray()),
                    contactDiagnostics.Count == 0 ? "none" : string.Join(" -> ", contactDiagnostics.ToArray()));
            PlayerState player = session.Player.Current;
            StreetWorldFrame frame = session.World.Capture();
            StreetObject southbound = frame.Objects.FirstOrDefault(item => item.Id == "vehicle.ns.southbound");
            StreetObject northbound = frame.Objects.FirstOrDefault(item => item.Id == "vehicle.ns.northbound");
            return string.Format(
                "{0} tick={1} clock={2:F2} world={3:F2} paused={4} running={5} menu={6} phase={7} " +
                "player=({8:F3},{9:F3}) state={10} courage={11:F1} focus={12:F1} " +
                "south=({13:F3},{14:F3}) hazard={15} polygon={16} north=({17:F3},{18:F3}) " +
                "checkpoint={19} collisions={20} recoveries={21} errors={22} contacts={23}",
                label, tick, session.Clock.Now, frame.Time, session.Clock.Paused, session.Running,
                session.Menu.Screen, session.World.SignalPhase, player.Position.X, player.Position.Y,
                player.MainState, player.Courage, player.Focus,
                southbound == null ? float.NaN : southbound.Position.X,
                southbound == null ? float.NaN : southbound.Position.Y,
                southbound != null && southbound.VehicleHazard,
                southbound == null ? "missing" : DescribePolygon(southbound.Polygon),
                northbound == null ? float.NaN : northbound.Position.X,
                northbound == null ? float.NaN : northbound.Position.Y,
                session.CheckpointId, session.Collisions, session.Recoveries,
                sessionErrors.Count == 0 ? "none" : string.Join(" -> ", sessionErrors.ToArray()),
                contactDiagnostics.Count == 0 ? "none" : string.Join(" -> ", contactDiagnostics.ToArray()));
        }

        private static string DescribeContact(StreetSession session, StreetContact contact, string label)
        {
            return DescribeContact(session.Clock, session.Player, session.World, contact, label);
        }

        private static string DescribeContact(SimulationClock clock, StreetPlayerController player,
            StreetWorld world, StreetContact contact, string label)
        {
            return DescribeContact(clock.Now, player.Current.Position, world.Capture(), contact, label);
        }

        private static string DescribeContact(double time, Point2 playerPosition, StreetWorldFrame frame,
            StreetContact contact, string label)
        {
            StreetObject item = frame.Objects.FirstOrDefault(candidate => candidate.Id == contact.ObjectId);
            return string.Format("{0} t={1:F3} contact={2} vehicle={3} point=({4:F3},{5:F3}) " +
                "player=({6:F3},{7:F3}) frame={8:F3} hazard={9} polygon={10}",
                label, time, contact.ObjectId, contact.Vehicle, contact.Point.X, contact.Point.Y,
                playerPosition.X, playerPosition.Y, frame.Time, item != null && item.VehicleHazard,
                item == null ? "missing" : DescribePolygon(item.Polygon));
        }

        private static string DescribeVehicleFrame(double time, Point2 playerPosition, StreetWorldFrame frame)
        {
            StreetObject vehicle = frame.Objects.FirstOrDefault(item => item.Id == "vehicle.ns.southbound");
            return string.Format("t={0:F3} player=({1:F3},{2:F3}) vehicle={3} hazard={4} polygon={5}",
                time, playerPosition.X, playerPosition.Y,
                vehicle == null ? "missing" : vehicle.Position.X.ToString("F3") + "," + vehicle.Position.Y.ToString("F3"),
                vehicle != null && vehicle.VehicleHazard,
                vehicle == null ? "missing" : DescribePolygon(vehicle.Polygon));
        }

        private static string DescribePolygon(IReadOnlyList<Point2> polygon)
        {
            return "[" + string.Join(";", polygon.Select(point =>
                string.Format("{0:F3},{1:F3}", point.X, point.Y)).ToArray()) + "]";
        }

        private static void AssertContourEqual(IReadOnlyList<Segment2> expected, IReadOnlyList<Segment2> actual)
        {
            Assert.AreEqual(expected.Count, actual.Count, "the remembered obstacle contour must keep the observed local detail");
            for (int i = 0; i < expected.Count; i++)
            {
                Assert.AreEqual(expected[i].A.X, actual[i].A.X, 1e-5f);
                Assert.AreEqual(expected[i].A.Y, actual[i].A.Y, 1e-5f);
                Assert.AreEqual(expected[i].B.X, actual[i].B.X, 1e-5f);
                Assert.AreEqual(expected[i].B.Y, actual[i].B.Y, 1e-5f);
            }
        }

        private static MenuItem FindMenuItem(AccessibleMenu menu, MenuOption action)
        {
            foreach (MenuItem item in menu.Items)
                if (item.Action == action) return item;
            return null;
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

        private sealed class RecordingAudio : IAudioPlayback, IAudioSpeech
        {
            public event Action<AudioReceipt> Receipt;
            public readonly List<AudioRequest> Played = new List<AudioRequest>();
            public readonly List<SpeechRequest> Spoken = new List<SpeechRequest>();
            public Action<AudioRequest> OnPlay { get; set; }
            public bool Paused { get; private set; }
            public bool ThrowOnReset { get; set; }
            public bool ResetFailureObserved { get; private set; }
            public int LastResetGeneration { get; private set; }
            public void Play(AudioRequest request)
            {
                Played.Add(request);
                if (OnPlay != null) OnPlay(request);
            }
            public void Stop(string eventId) { }
            public void UpdateListener(Point2 position, Point2 facing) { }
            public void Reset(int recoveryGeneration)
            {
                LastResetGeneration = recoveryGeneration;
                if (!ThrowOnReset) return;
                ResetFailureObserved = true;
                throw new InvalidOperationException("injected audio reset failure");
            }
            public void SetPaused(bool paused) { Paused = paused; }
            public void Speak(SpeechRequest request) { Spoken.Add(request); }
            public void StopSpeech(string eventId) { }
            public void Dispose() { }
        }

        private sealed class ProbePerformanceSample
        {
            public string Phase;
            public double Time;
            public float X, Y;
            public int CoverageCells, BoundarySegments, ObservedSurfaces;
            public int MemorySurfacesBefore, MemorySurfacesAfter;
            public int InvalidBoundarySegments, MemoryObjectsBefore, MemoryObjectsAfter;
            public double CoverageArea, ObservedArea, MemoryArea;
            public double PerceptionMilliseconds, ApplyMilliseconds;

            public string ToProgressLine()
            {
                return string.Format(CultureInfo.InvariantCulture,
                    "PROBE phase={0} t={1:F2} origin=({2:F3},{3:F3}) cells={4} boundary={5} " +
                    "observedSurfaces={6} memorySurfaces={7}->{8} memoryObjects={9}->{10} " +
                    "coverageArea={11:F4} observedArea={12:F4} memoryArea={13:F4} " +
                    "perceptionMs={14:F3} applyMs={15:F3}",
                    Phase, Time, X, Y, CoverageCells, BoundarySegments, ObservedSurfaces,
                    MemorySurfacesBefore, MemorySurfacesAfter, MemoryObjectsBefore, MemoryObjectsAfter,
                    CoverageArea, ObservedArea, MemoryArea, PerceptionMilliseconds, ApplyMilliseconds);
            }
        }
    }
}
