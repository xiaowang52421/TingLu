using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
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

namespace TingLu.Diagnostics
{
    /// <summary>Pure managed checks over production module sources; no Unity or NUnit dependency.</summary>
    public static class AcceptanceChecks
    {
        private sealed class MutablePlayer : IPlayerStateReader
        {
            public PlayerState Current { get; set; }
        }

        private sealed class AcousticStub : IAcousticQuery
        {
            public readonly List<AcousticQueryResult> Values = new List<AcousticQueryResult>();
            public IReadOnlyList<AcousticQueryResult> Sample(Point2 listener, Point2 facing, double time)
            { return Values.AsReadOnly(); }
        }

        private sealed class SpeechStub : IAudioSpeech
        {
            public readonly List<SpeechRequest> Requests = new List<SpeechRequest>();
            public void Speak(SpeechRequest request) { Requests.Add(request); }
            public void StopSpeech(string eventId) { }
        }

        private sealed class CheckpointStub : ICheckpointService
        {
            public CheckpointSnapshot Latest { get; private set; }
            public int Activations;
            public int Restores;
            public Action OnRestore;
            public void Activate(string checkpointId) { Activations++; Latest = Snapshot(checkpointId); }
            public void RestoreLatest() { Restores++; if (OnRestore != null) OnRestore(); }
        }

        private sealed class SaveStub : ISaveService
        {
            public ResumeData Resume;
            public PlayerProgress Progress = new PlayerProgress(new string[0], new Dictionary<string, int>(), new string[0], new string[0]);
            public SaveResult<ResumeData> LoadResume() { return new SaveResult<ResumeData>(Resume == null ? SaveStatus.Missing : SaveStatus.Success, Resume); }
            public SaveStatus SaveResume(ResumeData data) { Resume = data; return SaveStatus.Success; }
            public SaveResult<PlayerProgress> LoadProgress() { return new SaveResult<PlayerProgress>(SaveStatus.Success, Progress); }
            public SaveStatus SaveProgress(PlayerProgress data) { Progress = data; return SaveStatus.Success; }
            public SaveResult<UserSettings> LoadSettings() { return new SaveResult<UserSettings>(SaveStatus.Missing, null); }
            public SaveStatus SaveSettings(UserSettings data) { return SaveStatus.Success; }
        }

        public static AcceptanceCheckResult[] Run(string workspace)
        {
            var results = new List<AcceptanceCheckResult>();
            Check(results, "T01_silent_and_tactile", T01);
            Check(results, "T02_sound_location_and_occlusion", T02);
            Check(results, "T03_local_memory_coverage", T03);
            Check(results, "T04_same_id_fragments", T04);
            Check(results, "T05_dynamic_not_remembered", T05);
            Check(results, "T06_tap_cooldown_and_confusion", T06);
            Check(results, "T07_listening_focus_collision_restore", T07);
            Check(results, "T08_contact_dedup_and_reentry", T08);
            Check(results, "T09_threat_receipt_gate", T09);
            Check(results, "T11_menu_callbacks_generation", T11);
            Check(results, "T12_resume_roundtrip_rewards", delegate { T12(workspace); });
            Check(results, "T13_corrupt_backup_future_protection", delegate { T13(workspace); });
            Check(results, "T14_star_boundaries", T14);
            Check(results, "T15_hidden_world_equivalence", T15);
            Check(results, "T15_tiny_occluder_coverage", T15NarrowOccluder);
            Check(results, "T15_blind_path_requires_underfoot_material", T15BlindPath);
            Check(results, "T16_sound_beats_require_perception", T16SoundBeatGates);
            Check(results, "T17_failure_recovery_preserves_attempt", T17FailureRecovery);
            Check(results, "T18_first_level_continuous_tactile_path", T18FirstLevelTactilePath);
            CollectionContractCheck(results);
            return results.ToArray();
        }

        private static void Check(List<AcceptanceCheckResult> results, string id, Action action)
        {
            try { action(); results.Add(new AcceptanceCheckResult { Id = id, Status = "PASS", Detail = "断言满足" }); }
            catch (Exception ex) { results.Add(new AcceptanceCheckResult { Id = id, Status = "FAIL", Detail = ex.GetType().Name + ": " + ex.Message }); }
        }

        private static void T01()
        {
            var world = new WorldRegistry();
            world.Register(Static("pole", Box(-.2f, -.2f, .2f, .2f), "concrete"), new Point2(0, 2), 0, false);
            world.AddGroundSurface("tactile_path", Square(-1, -1, 1, 1));
            var path = new ObjectDefinition("blind-path", ObjectCategory.Static, MemoryPolicy.ConfirmedStaticGeometry,
                new[] { new Segment2(new Point2(-2, .1f), new Point2(2, .1f)) }, "tactile_path", null, PerceptionSemantic.Guide);
            world.Register(path, new Point2(0, 0), 0, false);
            var memory = new StaticMemoryStore(); var acoustic = new AcousticStub();
            var clock = new SimulationClock();
            var player = new MutablePlayer { Current = State(new Point2(0, 0), new Point2(0, 1)) };
            var service = NewPerception(world, acoustic, player, clock, memory);
            service.Recompute();
            Require(!service.Current.Observations.Any(x => x.ObjectId == "pole"), "silent pole appeared without a probe");
            Observation tactile = service.Current.Observations.FirstOrDefault(x => x.Source == ObservationSource.TactilePath);
            Require(tactile != null && tactile.ConfirmedContour.Count > 0, "nearby unoccluded tactile path was not confirmed (observations=" + String.Join(",", service.Current.Observations.Select(x => x.ObjectId + "/" + x.Source)) + ")");
            var echoWorld = new WorldRegistry();
            echoWorld.Register(Static("pole", Box(-.2f, -.2f, .2f, .2f), "concrete"), new Point2(0, 2), 0, false);
            var echoService = NewPerception(echoWorld, acoustic, player, clock, new StaticMemoryStore());
            echoService.Probe(new GameplayEvent("tap", clock.Now, "1-1", null, new Point2(0, 0), SemanticType.CaneTap, FeedbackPriority.Navigation, new CanePayload(7)));
            Observation echo = echoService.Current.Observations.FirstOrDefault(x => x.ObjectId == "pole");
            Require(echo != null && echo.Source == ObservationSource.CaneEcho && echo.ConfirmedContour.Count > 0,
                "successful cane tap did not expose local static geometry (obs=" + echoService.Current.Observations.Count + ", coverage=" + echoService.LastProbeCoverage.Cells.Count + ")");
        }

        private static void T02()
        {
            var world = new WorldRegistry(); var memory = new StaticMemoryStore(); var clock = new SimulationClock();
            var acoustic = new AcousticStub();
            acoustic.Values.Add(new AcousticQueryResult("sound", "car", true, new Point2(0, 2), new Point2(0, 1), 2, .8f, 0, "p", 0, PerceptionSemantic.Threat, ObjectCategory.Dynamic, "metal"));
            var player = new MutablePlayer { Current = State(new Point2(0, 0), new Point2(0, 1)) };
            PerceptionService service = NewPerception(world, acoustic, player, clock, memory); service.Recompute();
            Observation sound = service.Current.Observations.Single();
            Require(sound.Kind == InformationKind.SoundLocation && sound.ConfirmedContour.Count == 0 && sound.Semantic == PerceptionSemantic.Threat,
                "passive sound was not a semantic location-only observation");
            acoustic.Values[0] = new AcousticQueryResult("sound", "car", true, new Point2(0, 2), new Point2(0, 1), 2, .8f, 1, "p", 0, PerceptionSemantic.Threat, ObjectCategory.Dynamic, "metal");
            clock.Advance(.3); service.Recompute(); Require(service.Current.Observations.Count == 0, "fully occluded source remained audible to perception");
        }

        private static void T03()
        {
            var store = new StaticMemoryStore(); store.Observe(StaticObservation("fence", new Segment2(new Point2(-2, 0), new Point2(2, 0))));
            store.Clear(Coverage("probe", Square(-1, -1, 1, 1)));
            Require(store.Fragments.Count == 2, "local probe did not split outside memory into two fragments");
            float left = store.Fragments.Min(x => x.WorldContour.Min(s => Math.Min(s.A.X, s.B.X)));
            float right = store.Fragments.Max(x => x.WorldContour.Max(s => Math.Max(s.A.X, s.B.X)));
            Require(left < -1.9f && right > 1.9f, "outside snapshot endpoints were lost");
        }

        private static void T04()
        {
            var store = new StaticMemoryStore();
            store.Observe(StaticObservation("fence", new Segment2(new Point2(-4, 0), new Point2(-2, 0))));
            store.Observe(StaticObservation("fence", new Segment2(new Point2(2, 0), new Point2(4, 0))));
            store.Clear(Coverage("probe", Square(-4.1f, -.5f, -1.9f, .5f)));
            Require(store.Fragments.Count == 1 && store.Fragments[0].ObjectId == "fence" && store.Fragments[0].WorldContour[0].A.X > 1.9f,
                "confirming one ID location erased the remote fragment");
        }

        private static void T05()
        {
            var store = new StaticMemoryStore();
            var dynamic = new Observation("walker", ObjectCategory.Dynamic, MemoryPolicy.Never, ObservationSource.CaneEcho,
                InformationKind.LocalGeometry, new Point2(0, 0), new[] { new Segment2(new Point2(0, 0), new Point2(1, 0)) }, "cloth", 0, .8);
            store.Observe(dynamic); Require(store.Fragments.Count == 0, "dynamic geometry entered permanent memory");
        }

        private static void T06()
        {
            var clock = new SimulationClock(); using (var bus = new EventBus())
            {
                var world = new WorldRegistry(); var player = NewController(clock, bus, world, State(new Point2(0, 0), new Point2(0, 1)));
                player.Tick(new InputFrame(new Point2(0, 0), false, true, false, MenuAction.None), .01);
                Require(player.Current.CaneReadyAt == 3, "first tap did not start three second cooldown");
                clock.Advance(1); float after = player.Current.Focus;
                player.Tick(new InputFrame(new Point2(0, 0), false, true, false, MenuAction.None), .001);
                Require(player.Current.Focus == after, "tap inside cooldown spent focus");
                clock.Advance(2); player.Tick(new InputFrame(new Point2(0, 0), false, true, false, MenuAction.None), .001);
                clock.Advance(3); player.Tick(new InputFrame(new Point2(0, 0), false, true, false, MenuAction.None), .001);
                Require(player.Current.ConfusedUntil >= 10, "three valid taps in the eight second window did not trigger confusion");
            }
        }

        private static void T07()
        {
            var clock = new SimulationClock(); using (var bus = new EventBus())
            {
                var world = new WorldRegistry(); var initial = State(new Point2(0, 0), new Point2(0, 1), 100, 50);
                var player = NewController(clock, bus, world, initial);
                player.Tick(new InputFrame(new Point2(0, 1), true, false, false, MenuAction.None), 1);
                Require(Math.Abs(player.Current.Focus - 58) < .01f && player.Current.Mode == ActionMode.Listening, "moving while listening did not gain eight focus per second");
                player.NotifyCollision("pole", 10, .1, new Point2(0, 1));
                Require(Math.Abs(player.Current.Courage - 90) < .01f, "collision did not reduce courage");
                var restored = State(new Point2(2, 3), new Point2(1, 0), 42, 27);
                player.Restore(restored, clock.Now, clock.Now);
                Require(player.Current.Focus == 27 && player.Current.Courage == 42, "restore mixed focus and courage");
            }
        }

        private static void T08()
        {
            var clock = new SimulationClock(); using (var bus = new EventBus())
            {
                var dedup = NewController(clock, bus, new WorldRegistry(), State(new Point2(0, 0), new Point2(0, 1)));
                Require(dedup.NotifyCollision("wall", 10, 0, new Point2(0, 0)), "initial wall contact did not count");
                dedup.SetMainState(PlayerMainState.Active);
                Require(!dedup.NotifyCollision("wall", 10, 0, new Point2(0, 0)), "continuous wall contact counted twice");
                dedup.SetContact("wall", false); clock.Advance(.05); dedup.SetContact("wall", true);
                Require(!dedup.NotifyCollision("wall", 10, 0, new Point2(0, 0)), "brief contact jitter bypassed debounce");
                clock.Advance(1);
                var reentry = NewController(clock, bus, new WorldRegistry(), State(new Point2(0, 0), new Point2(0, 1)));
                Require(reentry.NotifyCollision("other-wall", 10, 0, new Point2(0, 0)), "second object contact setup failed");
                reentry.SetMainState(PlayerMainState.Active); reentry.SetContact("other-wall", false);
                clock.Advance(.61);
                bool rehit = reentry.NotifyCollision("other-wall", 10, 0, new Point2(0, 0));
                Require(rehit, "valid leave and re-entry was not counted (state=" + reentry.Current.MainState + ", time=" + clock.Now + ")");
                Require(reentry.Current.Courage == 80, "re-entry damage total was incorrect");
            }
        }

        private static void T09()
        {
            var gate = new ThreatReceiptGate(3); int started = 0, cancelled = 0; double time = -1;
            gate.Request("a", 1, 0, t => { started++; time = t; }, () => cancelled++);
            gate.Receive(new AudioReceipt("a", AudioStatus.Ready, .1, 1));
            Require(started == 0, "Ready started a threat");
            gate.Receive(new AudioReceipt("a", AudioStatus.Started, .4, 1));
            Require(started == 1 && time == .4, "Started receipt timestamp was not used");
            gate.Request("b", 1, 1, t => started++, () => cancelled++); gate.Tick(4);
            Require(cancelled == 1 && gate.StateOf("b") == ThreatAudioState.Cancelled, "missing audio did not cancel the threat");
            gate.Request("c", 1, 5, t => started++, () => cancelled++); gate.Reset(2);
            gate.Receive(new AudioReceipt("c", AudioStatus.Started, 5.1, 1));
            Require(started == 1 && cancelled == 2, "stale generation receipt restarted a threat");
        }

        private static void T11()
        {
            var speech = new SpeechStub(); int paused = 0, resumed = 0, returned = 0, saved = 0;
            var menu = new AccessibleMenu(); menu.Initialize(null, true, "公交站", new MenuCallbacks {
                Pause = () => paused++, Resume = () => resumed++, ReturnToMenu = () => returned++, SaveOptions = x => saved++
            }, speech, 4);
            menu.SetPlaying(); menu.HandleInput(new InputFrame(new Point2(0, 0), false, false, true, MenuAction.None));
            Require(paused == 1 && menu.Screen == MenuScreen.Pause, "pause callback or screen transition failed");
            menu.HandleInput(new InputFrame(new Point2(0, 0), false, false, true, MenuAction.None));
            Require(resumed == 1 && menu.Screen == MenuScreen.Playing, "resume callback or screen transition failed");
            menu.OpenPause(); menu.Back(); Require(returned == 1 && menu.Screen == MenuScreen.Main, "return-to-menu callback failed");
            Require(speech.Requests.All(x => x.RecoveryGeneration == 4), "menu speech changed recovery generation unexpectedly");
            menu.OpenSettings(); menu.AdjustFocused(1); Require(saved == 1, "setting change was not sent to save callback");
            menu.Initialize(null, true, "公交站", new MenuCallbacks(), speech, 5);
            Require(speech.Requests.Last().RecoveryGeneration == 5, "menu focus speech used the previous recovery generation after reinitialization");
            menu.Initialize(null, true, "公交站", new MenuCallbacks(), speech, 5);
            Require(speech.Requests.Last().RecoveryGeneration == 5, "menu focus speech used the previous recovery generation after reinitialization");
        }

        private static void T12(string workspace)
        {
            string root = Path.Combine(workspace, "save-roundtrip"); Directory.CreateDirectory(root);
            var service = new LocalSaveService(root); var cp = Snapshot("turn");
            var resume = new ResumeData(cp, new AttemptStats("attempt-1", 2, new[] { FirstStepLevel.FragmentObjectId }),
                new[] { "start-thought", "gatekeeper" }, new[] { "start", "turn" }, true);
            Require(service.SaveResume(resume) == SaveStatus.Success, "resume save failed");
            ResumeData loaded = service.LoadResume().Value;
            Require(loaded != null && loaded.Finished && loaded.Attempt.Collisions == 2 && loaded.FiredBeatIds.Contains("gatekeeper") && loaded.ActivatedCheckpointIds.Contains("turn"),
                "resume markers or attempt statistics did not roundtrip");
            Require(FirstStepScoring.Calculate(loaded.Attempt, loaded.Checkpoint.Player.Courage, FirstStepLevel.FragmentObjectId).Stars != 3,
                "a collided attempt incorrectly earned three stars");
        }

        private static void T13(string workspace)
        {
            string root = Path.Combine(workspace, "save-corrupt-backup"); Directory.CreateDirectory(root);
            var service = new LocalSaveService(root); service.SaveProgress(new PlayerProgress(new[] { "1-1" }, new Dictionary<string, int> { { "1-1", 1 } }, new string[0], new string[0]));
            service.SaveProgress(new PlayerProgress(new[] { "1-1" }, new Dictionary<string, int> { { "1-1", 2 } }, new string[0], new string[0]));
            string file = Path.Combine(root, "TingLu", "progress.json"); File.WriteAllText(file, "broken");
            SaveResult<PlayerProgress> backup = service.LoadProgress();
            Require(backup.Status == SaveStatus.Success && backup.FromBackup && backup.Value.BestStars["1-1"] == 1, "corrupt primary did not fall back to valid backup");

            string futureRoot = Path.Combine(workspace, "save-future"); Directory.CreateDirectory(futureRoot); var futureService = new LocalSaveService(futureRoot);
            string dir = Path.Combine(futureRoot, "TingLu"); Directory.CreateDirectory(dir); string future = Path.Combine(dir, "settings.json");
            const string payload = "{}"; string checksum;
            using (var sha = SHA256.Create()) checksum = BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(payload))).Replace("-", "").ToLowerInvariant();
            string envelope = "{\"version\":99,\"checksum\":\"" + checksum + "\",\"payload\":\"{}\"}";
            File.WriteAllText(future, envelope); string before = File.ReadAllText(future);
            Require(futureService.LoadSettings().Status == SaveStatus.FutureVersion, "valid future save was not classified as future");
            Require(futureService.SaveSettings(new UserSettings(DisplayMode.Standard, OutputMode.Headphones, 1, true, true)) == SaveStatus.FutureVersion,
                "future save was overwritten");
            Require(File.ReadAllText(future) == before, "future save bytes changed during rejected write");
        }

        private static void T14()
        {
            var noHitFragment = new AttemptStats("a", 0, new[] { "f" });
            Require(FirstStepScoring.Calculate(noHitFragment, 69, "f").Stars == 3, "zero collision plus fragment should score three stars regardless of courage threshold");
            Require(FirstStepScoring.Calculate(new AttemptStats("b", 1, new[] { "f" }), 69, "f").Stars == 1, "69 courage boundary was wrong");
            Require(FirstStepScoring.Calculate(new AttemptStats("c", 1, new string[0]), 70, "f").Stars == 2, "70 courage boundary was wrong");
            var high = new PlayerProgress(new string[0], new Dictionary<string, int> { { "1-1", 3 } }, new string[0], new string[0]);
            Require(FirstStepScoring.Complete(high, FirstStepScoring.Calculate(new AttemptStats("d", 4, new string[0]), 0, "f"), "1-1").BestStars["1-1"] == 3,
                "best historical stars decreased");
        }

        private static void T15()
        {
            string a = ObserveSignature(false); string b = ObserveSignature(true);
            Require(a == b, "same legal observations produced different player-facing perception signatures");
        }

        private static void T15NarrowOccluder()
        {
            var world = new WorldRegistry(); world.AddOccluder(new Segment2(new Point2(2, -.2f), new Point2(2, .2f)));
            var memory = new StaticMemoryStore(); memory.Observe(StaticObservation("far-fence", new Segment2(new Point2(-1, 3), new Point2(1, 3))));
            var acoustic = new AcousticStub(); var clock = new SimulationClock(); var player = new MutablePlayer { Current = State(new Point2(0, 0), new Point2(0, 1)) };
            var service = NewPerception(world, acoustic, player, clock, memory);
            var tap = new GameplayEvent("probe", 0, "1-1", null, new Point2(0, 0), SemanticType.CaneTap, FeedbackPriority.Navigation, new CanePayload(7));
            service.Probe(tap);
            Require(memory.Fragments.Count == 0, "a narrow nearby occluder prevented refresh of an open bearing within probe range");
        }

        private static void T15BlindPath()
        {
            var world = new WorldRegistry(); world.AddGroundSurface("sidewalk", Square(-1, -1, 1, 1));
            var path = new ObjectDefinition("path", ObjectCategory.Static, MemoryPolicy.ConfirmedStaticGeometry,
                new[] { new Segment2(new Point2(-.3f, .1f), new Point2(.3f, .1f)) }, "tactile_path", null, PerceptionSemantic.Guide);
            world.Register(path, new Point2(0, 0), 0, false);
            var player = new MutablePlayer { Current = State(new Point2(0, 0), new Point2(0, 1)) };
            var service = NewPerception(world, new AcousticStub(), player, new SimulationClock(), new StaticMemoryStore()); service.Recompute();
            Require(!service.Current.Observations.Any(x => x.Source == ObservationSource.TactilePath),
                "path geometry was confirmed without tactile paving beneath the player");
        }

        private static void T16SoundBeatGates()
        {
            FirstStepBeat caneLesson = new FirstStepLevel().Beats.Single(x => x.Id == "cane-lesson");
            Require(caneLesson.Text == "想确认前面的路况，可以轻敲一下杖。" &&
                caneLesson.Text.IndexOf("电线杆", StringComparison.Ordinal) < 0 && caneLesson.Text.IndexOf("单车", StringComparison.Ordinal) < 0,
                "cane teaching named an obstacle before the player confirmed it");
            Require(BeatFiresAt(new Point2(2, 9), FirstStepLevel.BirdObjectId, "bird-landmark", true), "perceived bird landmark did not fire");
            Require(!BeatFiresAt(new Point2(2, 9), FirstStepLevel.BirdObjectId, "bird-landmark", false), "unheard bird landmark leaked through proximity");
            Require(BeatFiresAt(new Point2(0, 36), FirstStepLevel.WalkerObjectId, "walker-demo", true), "perceived walker greeting did not fire");
            Require(!BeatFiresAt(new Point2(0, 36), FirstStepLevel.WalkerObjectId, "walker-demo", false), "unheard walker greeting leaked through proximity");
            Require(BeatFiresAt(new Point2(5, 40.8f), FirstStepLevel.FragmentObjectId, "sound-fragment-01", true), "perceived fragment did not announce itself");
            Require(!BeatFiresAt(new Point2(5, 40.8f), FirstStepLevel.FragmentObjectId, "sound-fragment-01", false), "unheard fragment leaked through proximity");
            Require(BeatFiresAt(new Point2(25, 40), FirstStepLevel.GatekeeperObjectId, "gatekeeper", true), "perceived gatekeeper greeting did not fire");
            Require(!BeatFiresAt(new Point2(25, 40), FirstStepLevel.GatekeeperObjectId, "gatekeeper", false), "unheard gatekeeper greeting leaked through proximity");
        }

        private static bool BeatFiresAt(Point2 position, string perceivedObjectId, string expectedBeatId, bool perceived)
        {
            var player = new MutablePlayer { Current = State(position, new Point2(0, 1)) };
            using (var bus = new EventBus())
            {
                var saves = new SaveStub(); var checkpoints = new CheckpointStub(); var presented = new List<string>();
                var flow = new FirstStepFlow(new FirstStepLevel(), player, bus, checkpoints, saves, new AttemptTracker("a"),
                    () => saves.Progress, x => saves.Progress = x, x => presented.Add(x.Id), () => 0,
                    id => perceived && id == perceivedObjectId);
                flow.Start(); flow.Tick();
                return presented.Contains(expectedBeatId);
            }
        }

        private static void T17FailureRecovery()
        {
            double now = 0; var player = new MutablePlayer { Current = State(new Point2(0, 0), new Point2(0, 1)) };
            using (var bus = new EventBus())
            {
                var saves = new SaveStub(); var checkpoints = new CheckpointStub(); var attempt = new AttemptTracker("failure-attempt");
                var flow = new FirstStepFlow(new FirstStepLevel(), player, bus, checkpoints, saves, attempt,
                    () => saves.Progress, x => saves.Progress = x, x => { }, () => now);
                flow.Start();
                var safe = player.Current; var blackout = new List<bool>();
                checkpoints.OnRestore = () => player.Current = safe;
                flow.AttachFailureRecovery(new FailureRecovery(player, checkpoints, () => now, value => blackout.Add(value), flow.SaveResume, 2));
                bus.Publish(new GameplayEvent("collision", now, FirstStepLevel.LevelId, "pole", new Point2(0, 1),
                    SemanticType.Collision, FeedbackPriority.CriticalThreat, new CollisionPayload(100, 0, new Point2(0, 1))));
                Require(flow.Attempt.Snapshot().Collisions == 1, "collision statistic was not recorded before recovery");
                player.Current = new PlayerState(PlayerMainState.Failing, ActionMode.Walking, safe.Position, safe.Facing, 0, safe.Focus, 0, 0, 0);
                flow.Tick(); Require(checkpoints.Restores == 0 && blackout.SequenceEqual(new[] { true }), "failure blackout did not begin before recovery");
                now = 1.99; flow.Tick(); Require(checkpoints.Restores == 0, "failure recovered before the full breathing interval");
                now = 2; flow.Tick();
                Require(checkpoints.Restores == 1 && blackout.SequenceEqual(new[] { true, false }), "failure did not restore the safe checkpoint after two seconds");
                Require(saves.Resume != null && saves.Resume.Attempt.Collisions == 1,
                    "checkpoint recovery rolled back the collision count in the saved attempt");
            }
        }

        private static void T18FirstLevelTactilePath()
        {
            var objects = FirstStepWorldFactory.Objects().Where(x => x.Definition.Semantic == PerceptionSemantic.Guide).ToArray();
            Require(objects.Length > 20, "first level did not provide enough short guide segments for its route");
            foreach (FirstStepWorldObject guide in objects)
            {
                Require(guide.Definition.MemoryPolicy == MemoryPolicy.Never, "tactile guide can enter permanent memory");
                Require(guide.Definition.LocalContour.Count == 1 &&
                    Geometry2.Distance(guide.Definition.LocalContour[0].A, guide.Definition.LocalContour[0].B) <= 3.0001f,
                    "a guide segment exceeded the nearby-query length bound");
            }

            var world = new WorldRegistry();
            foreach (FirstStepWorldObject obj in FirstStepWorldFactory.Objects())
                world.Register(obj.Definition, obj.Position, obj.Rotation, obj.SoundActive);
            foreach (FirstStepGround surface in FirstStepWorldFactory.Ground()) world.AddGroundSurface(surface.Material, surface.Polygon);
            Require(world.MaterialAt(new Point2(5, 40)) == "tactile_path", "garden surface overrode the narrow tactile strip");
            Require(world.MaterialAt(new Point2(18, 40)) == "tactile_path", "asphalt surface overrode the narrow tactile strip");
            Require(world.QueryNearby(new Point2(0, 5), 6).Any(x => x.Definition.Semantic == PerceptionSemantic.Guide),
                "nearby-query did not return the continuous route within six metres");

            var player = new MutablePlayer { Current = State(new Point2(0, 5), new Point2(0, 1)) };
            var memory = new StaticMemoryStore();
            var service = NewPerception(world, new AcousticStub(), player, new SimulationClock(), memory);
            service.Recompute();
            Require(service.Current.Observations.Any(x => x.Source == ObservationSource.TactilePath && x.Semantic == PerceptionSemantic.Guide),
                "walking on the first-level tactile strip produced no live tactile observation");
            Require(memory.Fragments.Count == 0, "live tactile guidance was written as permanent memory");
            player.Current = State(new Point2(.8f, 5), new Point2(0, 1)); service.Recompute();
            Require(!service.Current.Observations.Any(x => x.Source == ObservationSource.TactilePath),
                "leaving the tactile strip did not remove the live tactile observation");

            MovementResult onPath = world.ResolveMovement(new Point2(0, 5), new Point2(0, 6), .18f);
            Require(onPath.ContactObjectId == null && Geometry2.Distance(onPath.Position, new Point2(0, 6)) < .001f,
                "tactile strip blocked movement along the sidewalk");
            MovementResult throughPole = world.ResolveMovement(new Point2(0, 14), new Point2(0, 16), .18f);
            Require(throughPole.ContactObjectId == "l1-1/pole/01", "guide path removed the pole collision instead of routing around it");
            MovementResult aroundPole = world.ResolveMovement(new Point2(-.65f, 14.5f), new Point2(-.65f, 15.5f), .18f);
            Require(aroundPole.ContactObjectId == null, "the tactile detour around the pole remained blocked");

            var noGuideOcclusion = world.QueryOcclusionBoundaries(new Point2(0, 5), 1);
            Require(noGuideOcclusion.Count == 0, "tactile guide line was exposed as an acoustic/probe occluder");
        }

        private static void CollectionContractCheck(List<AcceptanceCheckResult> results)
        {
            var constructor = typeof(FirstStepFlow).GetConstructors().FirstOrDefault(c => {
                var p = c.GetParameters(); return p.Length == 11 && p[10].ParameterType == typeof(Func<string, bool>);
            });
            if (constructor == null)
            {
                results.Add(new AcceptanceCheckResult { Id = "T12_collection_requires_perceived_source", Status = "NOT RUN",
                    Detail = "FirstStepFlow is awaiting the planned isPerceived API; the 10-argument module version still collects by position only." });
                return;
            }
            RunCollectionFlow(constructor, true, true);
            RunCollectionFlow(constructor, false, false);
            results.Add(new AcceptanceCheckResult { Id = "T12_collection_requires_perceived_source", Status = "PASS", Detail = "只在碎片声源已感知且进入近处时收集，奖励ID为l1-1/fragment/01" });
        }

        private static void RunCollectionFlow(System.Reflection.ConstructorInfo constructor, bool perceived, bool shouldCollect)
        {
            var player = new MutablePlayer { Current = State(new Point2(5, 40.8f), new Point2(0, 1)) };
            using (var bus = new EventBus())
            {
                var saves = new SaveStub(); var checkpoints = new CheckpointStub();
                var args = new object[] { new FirstStepLevel(), player, bus, checkpoints, saves, new AttemptTracker("a"),
                    new Func<PlayerProgress>(() => saves.Progress), new Action<PlayerProgress>(x => saves.Progress = x),
                    new Action<FirstStepBeat>(_ => { }), new Func<double>(() => 0),
                    new Func<string, bool>(id => perceived && id == "l1-1/fragment/01") };
                var flow = (FirstStepFlow)constructor.Invoke(args); flow.Start(); flow.Tick();
                bool collected = flow.Attempt.Snapshot().CollectedIds.Contains("l1-1/fragment/01");
                Require(collected == shouldCollect, perceived ? "heard fragment did not grant its stable object ID" : "unperceived fragment was collected by proximity");
            }
        }

        private static string ObserveSignature(bool hiddenObject)
        {
            var world = new WorldRegistry();
            if (hiddenObject) world.Register(Static("hidden-pole", Box(-.1f, -.1f, .1f, .1f), "concrete"), new Point2(1, 2), 0, false);
            var acoustic = new AcousticStub();
            acoustic.Values.Add(new AcousticQueryResult("bird-call", "bird", true, new Point2(0, 2), new Point2(0, 1), 2, .7f, 0, "bird", 1,
                PerceptionSemantic.Ordinary, ObjectCategory.Event, "air"));
            var player = new MutablePlayer { Current = State(new Point2(0, 0), new Point2(0, 1)) };
            var service = NewPerception(world, acoustic, player, new SimulationClock(), new StaticMemoryStore()); service.Recompute();
            return String.Join("|", service.Current.Observations.Select(x => x.Source + ":" + x.Kind + ":" + x.Semantic + ":" + x.ConfirmedContour.Count).OrderBy(x => x));
        }

        private static PerceptionService NewPerception(IWorldQuery world, IAcousticQuery acoustic, IPlayerStateReader player,
            IGameClock clock, StaticMemoryStore memory)
        { return new PerceptionService(world, acoustic, player, clock, new GameplayConfig(), memory, memory.Observe, memory.Clear, "1-1"); }

        private static PlayerController NewController(IGameClock clock, IEventBus bus, IWorldQuery world, PlayerState state)
        { return new PlayerController(new GameplayConfig(), clock, bus, world, "1-1", state); }

        private static PlayerState State(Point2 position, Point2 facing, float courage = 100, float focus = 100)
        { return new PlayerState(PlayerMainState.Active, ActionMode.Walking, position, facing, courage, focus, 0, 0, 0); }

        private static CheckpointSnapshot Snapshot(string id)
        { return new CheckpointSnapshot("1-1", id, 0, State(new Point2(0, 0), new Point2(0, 1)), new StaticMemorySnapshot[0], new WorldEventSnapshot[0], 7); }

        private static ObjectDefinition Static(string id, Segment2[] contour, string material)
        { return new ObjectDefinition(id, ObjectCategory.Static, MemoryPolicy.ConfirmedStaticGeometry, contour, material, null); }

        private static Segment2[] Box(float left, float bottom, float right, float top)
        { return new[] { new Segment2(new Point2(left, bottom), new Point2(right, bottom)), new Segment2(new Point2(right, bottom), new Point2(right, top)), new Segment2(new Point2(right, top), new Point2(left, top)), new Segment2(new Point2(left, top), new Point2(left, bottom)) }; }

        private static Observation StaticObservation(string id, Segment2 contour)
        { return new Observation(id, ObjectCategory.Static, MemoryPolicy.ConfirmedStaticGeometry, ObservationSource.CaneEcho, InformationKind.LocalGeometry, contour.A, new[] { contour }, "concrete", 0, .8); }

        private static ProbeCoverage Coverage(string id, IEnumerable<Point2> polygon)
        { return new ProbeCoverage(id, 1, new[] { new ProbeCell(polygon) }, new Segment2[0]); }

        private static Point2[] Square(float left, float bottom, float right, float top)
        { return new[] { new Point2(left, bottom), new Point2(right, bottom), new Point2(right, top), new Point2(left, top) }; }

        private static void Require(bool condition, string message)
        { if (!condition) throw new InvalidOperationException(message); }
    }
}
