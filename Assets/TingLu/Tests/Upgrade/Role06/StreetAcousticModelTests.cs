using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using TingLu.Audio;
using TingLu.Config;
using TingLu.Contracts;

namespace TingLu.Tests.Upgrade.Role06
{
    public sealed class StreetAcousticModelTests
    {
        private sealed class PlayerReader : IPlayerStateReader
        {
            public PlayerState Current { get; set; }
        }

        private sealed class FakePlayback : IAudioPlayback, IStreetAudioUpdater
        {
            public readonly List<AudioRequest> Played = new List<AudioRequest>();
            public readonly List<string> Stopped = new List<string>();
            public readonly List<string> Updated = new List<string>();
            public readonly List<AudioRequest> Started = new List<AudioRequest>();
            public Point2 LastPosition;
            public float LastGain, LastOcclusion;
            public int FailPlayAttempts;
            public double Now;
            public event System.Action<AudioReceipt> Receipt;

            public void Play(AudioRequest request)
            {
                Played.Add(request);
                if (FailPlayAttempts <= 0) { Start(request); return; }
                FailPlayAttempts--;
                Raise(new AudioReceipt(request.EventId, AudioStatus.Failed, Now, request.RecoveryGeneration,
                    "voice_limit_no_lower_priority_voice"));
                return;
            }
            public void Start(AudioRequest request)
            {
                Started.Add(request);
                Raise(new AudioReceipt(request.EventId, AudioStatus.Started, Now, request.RecoveryGeneration));
            }
            public void Stop(string eventId)
            {
                Stopped.Add(eventId);
                int stoppedGeneration = 0;
                for (int i = Played.Count - 1; i >= 0; i--)
                    if (Played[i].EventId == eventId) { stoppedGeneration = Played[i].RecoveryGeneration; break; }
                Raise(new AudioReceipt(eventId, AudioStatus.Stopped, Now, stoppedGeneration, "synchronous_fake_stop"));
            }
            public void UpdateListener(Point2 position, Point2 facing) { }
            public void Reset(int recoveryGeneration) { }
            public void SetPaused(bool paused) { }
            public void UpdateSource(string id, Point2 position, float gain, float occlusion)
            { Updated.Add(id); LastPosition = position; LastGain = gain; LastOcclusion = occlusion; }
            public void Dispose() { }
            public void Raise(AudioReceipt receipt) { if (Receipt != null) Receipt(receipt); }
        }

        [Test]
        public void PassiveSourcesRemainAvailableInEveryDirectionAndOutsideProbeSector()
        {
            StreetWorldFrame frame = Frame(0,
                Source("north", new Point2(0, 5), "street.walker"),
                Source("east", new Point2(5, 0), "street.cycle"),
                Source("south", new Point2(0, -5), "street.car", true),
                Source("west", new Point2(-5, 0), "street.bird"));
            var model = Model(() => frame, State(new Point2(0, 0), new Point2(0, 1), 100, 0));

            IReadOnlyList<AcousticQueryResult> north = model.Sample(new Point2(0, 0), new Point2(0, 1), 0);
            Assert.AreEqual(4, north.Count);
            Assert.Greater(Find(north, "north").Discernibility, Find(north, "south").Discernibility);
            Assert.IsTrue(Contains(north, "south"), "A rear vehicle must not be cut by the cane sector.");

            IReadOnlyList<AcousticQueryResult> east = model.Sample(new Point2(0, 0), new Point2(1, 0), 0);
            Assert.Greater(Find(east, "east").Discernibility, Find(east, "west").Discernibility);
            IReadOnlyList<AcousticQueryResult> south = model.Sample(new Point2(0, 0), new Point2(0, -1), 0);
            Assert.Greater(Find(south, "south").Discernibility, Find(south, "north").Discernibility);
            IReadOnlyList<AcousticQueryResult> west = model.Sample(new Point2(0, 0), new Point2(-1, 0), 0);
            Assert.Greater(Find(west, "west").Discernibility, Find(west, "east").Discernibility);
        }

        [Test]
        public void DistanceMaterialLossFocusAndConfusionShareTheAcousticResult()
        {
            StreetObject near = Source("car", new Point2(0, 6), "street.car", true);
            StreetObject far = Source("car", new Point2(0, 18), "street.car", true);
            float nearGain = Find(Model(Frame(0, near), State(new Point2(0, 0), new Point2(0, 1), 100, 0))
                .Sample(new Point2(0, 0), new Point2(0, 1), 0), "car").Discernibility;
            float farGain = Find(Model(Frame(0, far), State(new Point2(0, 0), new Point2(0, 1), 100, 0))
                .Sample(new Point2(0, 0), new Point2(0, 1), 0), "car").Discernibility;
            Assert.Greater(nearGain, farGain);

            StreetObject wall = new StreetObject("wall", StreetKind.Permanent, new Point2(0, 2.5f),
                StreetGeometry.Rect(-1, 2, 1, 3), "brick", false, false, false, 0.7f, 2f);
            AcousticQueryResult occluded = Find(Model(Frame(0, wall, near),
                State(new Point2(0, 0), new Point2(0, 1), 100, 0))
                .Sample(new Point2(0, 0), new Point2(0, 1), 0), "car");
            Assert.AreEqual(0.7f, occluded.Occlusion, 0.001f);
            Assert.Less(occluded.Discernibility, nearGain);

            AcousticQueryResult impaired = Find(Model(Frame(0, near),
                State(new Point2(0, 0), new Point2(0, 1), 15, 5))
                .Sample(new Point2(0, 0), new Point2(0, 1), 0), "car");
            Assert.Less(impaired.Discernibility, nearGain);
            Assert.IsNull(impaired.MaterialId, "An acoustic position must not carry ground material information.");
            Assert.IsNull(typeof(AcousticQueryResult).GetField("Polygon"));
            Assert.IsNull(typeof(AcousticQueryResult).GetProperty("ConfirmedContour"));
            Assert.IsNull(typeof(AcousticQueryResult).GetField("Polygon"));
            Assert.IsNull(typeof(AcousticQueryResult).GetProperty("ConfirmedContour"));
        }

        [Test]
        public void ConfusionUsesSharedGameplayMultiplierAndExpiresAtItsSimulationDeadline()
        {
            var config = new StreetConfig();
            config.Gameplay.confusionMultiplier = 0.3f;
            StreetObject source = Source("car", new Point2(0, 6), "street.car", true);
            var reader = new PlayerReader
            {
                Current = State(new Point2(0, 0), new Point2(0, 1), 100, 5)
            };
            StreetWorldFrame frame = Frame(0, source);
            var model = new StreetAcousticModel(() => frame, reader, config);

            float confused = Find(model.Sample(new Point2(0, 0), new Point2(0, 1), 0), "car").Discernibility;
            frame = Frame(5, source);
            reader.Current = State(new Point2(0, 0), new Point2(0, 1), 100, 5);
            float recovered = Find(model.Sample(new Point2(0, 0), new Point2(0, 1), 5), "car").Discernibility;

            Assert.AreEqual(recovered * config.Gameplay.confusionMultiplier, confused, 0.0001f);
        }

        [Test]
        public void SoundActivityComesFromTheSnapshotAndSourceMappingIsSampleScoped()
        {
            StreetObject silent = Source("signal", new Point2(0, 4), "street.signal.ns", false, false);
            StreetWorldFrame off = Frame(12, silent);
            StreetAcousticModel model = Model(off, State(new Point2(0, 0), new Point2(0, 1), 100, 0));
            Assert.AreEqual(0, model.Sample(new Point2(0, 0), new Point2(0, 1), 12).Count);
            Assert.IsNull(model.SoundFor("signal"));

            StreetObject sounding = Source("signal", new Point2(0, 4), "street.signal.ns", false, true);
            StreetWorldFrame on = Frame(12, sounding);
            model = Model(on, State(new Point2(0, 0), new Point2(0, 1), 100, 0));
            IReadOnlyList<AcousticQueryResult> result = model.Sample(new Point2(0, 0), new Point2(0, 1), 12);
            Assert.AreEqual("street.signal.ns", model.SoundFor(result[0].SourceId));
            Assert.IsNull(model.SoundFor("not-sampled"));
        }

        [Test]
        public void ControllerStartsPrioritizesUpdatesStopsAndResetsGeneration()
        {
            StreetWorldFrame frame = Frame(0, Source("car", new Point2(0, 5), "street.car", true));
            var model = Model(() => frame, State(new Point2(0, 0), new Point2(0, 1), 100, 0));
            var playback = new FakePlayback();
            var controller = new StreetAudioController(playback, model);
            IReadOnlyList<AcousticQueryResult> first = model.Sample(new Point2(0, 0), new Point2(0, 1), 0);
            controller.Sync(first, 2);

            Assert.AreEqual(1, playback.Played.Count);
            Assert.AreEqual("street.car", playback.Played[0].SoundId);
            Assert.AreEqual(FeedbackPriority.CriticalThreat, playback.Played[0].Priority);
            Assert.IsTrue(playback.Played[0].Loop);
            Assert.AreEqual(first[0].Discernibility, playback.Played[0].Gain, 0.0001f);
            Assert.AreEqual(first[0].Occlusion, playback.Played[0].Occlusion, 0.0001f);
            Assert.AreEqual(first[0].Discernibility, playback.Played[0].Gain, 0.0001f);
            Assert.AreEqual(first[0].Occlusion, playback.Played[0].Occlusion, 0.0001f);

            frame = Frame(0, Source("car", new Point2(2, 5), "street.car", true));
            IReadOnlyList<AcousticQueryResult> moved = model.Sample(new Point2(0, 0), new Point2(0, 1), 0);
            controller.Sync(moved, 2);
            Assert.AreEqual("street-source:car", playback.Updated[0]);
            Assert.AreEqual(new Point2(2, 5), playback.LastPosition);
            Assert.AreEqual(moved[0].Discernibility, playback.LastGain, 0.0001f);
            Assert.AreEqual(moved[0].Occlusion, playback.LastOcclusion, 0.0001f);
            Assert.AreEqual(moved[0].Discernibility, playback.LastGain, 0.0001f);
            Assert.AreEqual(moved[0].Occlusion, playback.LastOcclusion, 0.0001f);

            frame = Frame(0);
            controller.Sync(model.Sample(new Point2(0, 0), new Point2(0, 1), 0), 2);
            Assert.Contains("street-source:car", playback.Stopped);

            frame = Frame(0, Source("car", new Point2(0, 5), "street.car", true));
            controller.Sync(model.Sample(new Point2(0, 0), new Point2(0, 1), 0), 2);
            int stopsBeforeReset = playback.Stopped.Count;
            controller.Reset(3);
            Assert.AreEqual(stopsBeforeReset + 1, playback.Stopped.Count);
            controller.Sync(model.Sample(new Point2(0, 0), new Point2(0, 1), 0), 2);
            Assert.AreEqual(2, playback.Played.Count, "Old generations must not restart voices.");
            controller.Sync(model.Sample(new Point2(0, 0), new Point2(0, 1), 0), 3);
            Assert.AreEqual(3, playback.Played[playback.Played.Count - 1].RecoveryGeneration);
        }

        [Test]
        public void OrdinaryActiveSourcesStayAudibleAtAmbiencePriority()
        {
            StreetWorldFrame frame = Frame(0, Source("bird", new Point2(-3, 2), "street.bird"));
            var model = Model(() => frame, State(new Point2(0, 0), new Point2(0, 1), 100, 0));
            var playback = new FakePlayback();
            var controller = new StreetAudioController(playback, model);
            controller.Sync(model.Sample(new Point2(0, 0), new Point2(0, 1), 0), 1);
            Assert.AreEqual(1, playback.Played.Count);
            Assert.AreEqual(FeedbackPriority.Ambience, playback.Played[0].Priority);
            Assert.IsTrue(playback.Played[0].Loop);
        }

        [Test]
        public void RepeatedVoiceLimitFailuresUseCappedExponentialBackoff()
        {
            StreetWorldFrame frame = Frame(0, Source("bird", new Point2(0, 5), "street.bird"));
            var model = Model(() => frame, State(new Point2(0, 0), new Point2(0, 1), 100, 0));
            var playback = new FakePlayback { FailPlayAttempts = 100 };
            var controller = new StreetAudioController(playback, model);

            for (int tick = 0; tick <= 300; tick++)
            {
                double time = tick * 0.02;
                playback.Now = time;
                frame = Frame(time, Source("bird", new Point2(0, 5), "street.bird"));
                controller.Sync(model.Sample(new Point2(0, 0), new Point2(0, 1), time), 1);
                if (tick == 0) Assert.AreEqual(1, playback.Played.Count);
                if (tick == 24) Assert.AreEqual(1, playback.Played.Count, "Many substeps before the deadline must not call Play again.");
                if (tick == 25) Assert.AreEqual(2, playback.Played.Count, "The first retry follows 0.5 simulation seconds.");
                if (tick == 74) Assert.AreEqual(2, playback.Played.Count, "The second retry waits one simulation second.");
                if (tick == 75) Assert.AreEqual(3, playback.Played.Count);
                if (tick == 174) Assert.AreEqual(3, playback.Played.Count, "The backoff reaches its two second cap.");
                if (tick == 175) Assert.AreEqual(4, playback.Played.Count);
                if (tick == 274) Assert.AreEqual(4, playback.Played.Count, "The capped delay remains stable.");
                if (tick == 275) Assert.AreEqual(5, playback.Played.Count);
            }

            Assert.AreEqual(5, playback.Played.Count);
        }

        [Test]
        public void SourceRecoversAfterTwoPoolRejectionsAndStartedClearsBackoff()
        {
            StreetWorldFrame frame = Frame(0, Source("bird", new Point2(0, 5), "street.bird"));
            var model = Model(() => frame, State(new Point2(0, 0), new Point2(0, 1), 100, 0));
            var playback = new FakePlayback { FailPlayAttempts = 2 };
            var controller = new StreetAudioController(playback, model);

            for (int tick = 0; tick <= 100; tick++)
            {
                double time = tick * 0.02;
                playback.Now = time;
                frame = Frame(time, Source("bird", new Point2(0, 5), "street.bird"));
                controller.Sync(model.Sample(new Point2(0, 0), new Point2(0, 1), time), 1);
                if (tick == 24) Assert.AreEqual(1, playback.Played.Count);
                if (tick == 25) Assert.AreEqual(2, playback.Played.Count);
                if (tick == 74) Assert.AreEqual(2, playback.Played.Count);
                if (tick == 75) Assert.AreEqual(3, playback.Played.Count, "The source retries after the one second backoff.");
            }

            Assert.AreEqual(1, playback.Started.Count);
            Assert.AreEqual(3, playback.Played.Count, "Started keeps the source active and clears its backoff.");

            playback.FailPlayAttempts = 1;
            playback.Now = 2.0;
            playback.Raise(new AudioReceipt("street-source:bird", AudioStatus.Stopped, playback.Now, 1, "slot_reclaimed"));
            frame = Frame(2.0, Source("bird", new Point2(0, 5), "street.bird"));
            controller.Sync(model.Sample(new Point2(0, 0), new Point2(0, 1), 2.0), 1);
            Assert.AreEqual(4, playback.Played.Count);
            playback.Now = 2.48;
            frame = Frame(2.48, Source("bird", new Point2(0, 5), "street.bird"));
            controller.Sync(model.Sample(new Point2(0, 0), new Point2(0, 1), 2.48), 1);
            Assert.AreEqual(4, playback.Played.Count, "After Started, a later failure begins at the 0.5 second delay again.");
            playback.Now = 2.5;
            frame = Frame(2.5, Source("bird", new Point2(0, 5), "street.bird"));
            controller.Sync(model.Sample(new Point2(0, 0), new Point2(0, 1), 2.5), 1);
            Assert.AreEqual(5, playback.Played.Count);
        }

        [Test]
        public void PausedSampleTimeDoesNotRetryAndResetOrRemovalClearsBackoff()
        {
            StreetWorldFrame frame = Frame(0, Source("bird", new Point2(0, 5), "street.bird"));
            var model = Model(() => frame, State(new Point2(0, 0), new Point2(0, 1), 100, 0));
            var playback = new FakePlayback { FailPlayAttempts = 100 };
            var controller = new StreetAudioController(playback, model);
            IReadOnlyList<AcousticQueryResult> atStart = model.Sample(new Point2(0, 0), new Point2(0, 1), 0);
            controller.Sync(atStart, 1);

            for (int i = 0; i < 100; i++) controller.Sync(atStart, 1);
            Assert.AreEqual(1, playback.Played.Count, "Paused syncs with unchanged simulation time do not advance backoff.");

            controller.Reset(2);
            controller.Sync(model.Sample(new Point2(0, 0), new Point2(0, 1), 0), 2);
            Assert.AreEqual(2, playback.Played.Count, "Reset clears backoff and permits an immediate retry.");

            frame = Frame(0);
            controller.Sync(model.Sample(new Point2(0, 0), new Point2(0, 1), 0), 2);
            frame = Frame(0, Source("bird", new Point2(0, 5), "street.bird"));
            controller.Sync(model.Sample(new Point2(0, 0), new Point2(0, 1), 0), 2);
            Assert.AreEqual(3, playback.Played.Count, "Leaving and re-entering the sample clears the old backoff.");
        }

        [Test]
        public void EqualPriorityAdmissionIsStableAndSynchronousStopReceiptsAreSafe()
        {
            StreetWorldFrame frame = Frame(0,
                Source("zebra", new Point2(0, 5), "street.bird"),
                Source("alpha", new Point2(0, 5), "street.bird"));
            var model = Model(() => frame, State(new Point2(0, 0), new Point2(0, 1), 100, 0));
            var playback = new FakePlayback();
            var controller = new StreetAudioController(playback, model);
            controller.Sync(model.Sample(new Point2(0, 0), new Point2(0, 1), 0), 1);

            Assert.AreEqual("alpha", playback.Played[0].SourceId, "Same-priority sources use a stable source ID order.");
            Assert.AreEqual("zebra", playback.Played[1].SourceId);

            frame = Frame(0,
                Source("zebra", new Point2(1, 5), "street.bird"),
                Source("alpha", new Point2(0, 6), "street.walker"));
            controller.Sync(model.Sample(new Point2(0, 0), new Point2(0, 1), 0), 1);
            Assert.Contains("street-source:alpha", playback.Stopped, "Changing a source sound stops the previous voice.");
            Assert.Contains("street-source:zebra", playback.Updated, "The surviving voice remains mapped after synchronous Stop receipts.");
            Assert.AreEqual("street.walker", playback.Played[2].SoundId);

            frame = Frame(0, Source("alpha", new Point2(0, 6), "street.walker"));
            controller.Sync(model.Sample(new Point2(0, 0), new Point2(0, 1), 0), 1);
            Assert.Contains("street-source:zebra", playback.Stopped);
            frame = Frame(0,
                Source("zebra", new Point2(0, 5), "street.bird"),
                Source("alpha", new Point2(0, 6), "street.walker"));
            controller.Sync(model.Sample(new Point2(0, 0), new Point2(0, 1), 0), 1);
            Assert.AreEqual(4, playback.Played.Count, "A source can be started again after its synchronous stop receipt.");
        }

        [Test]
        public void VehicleHazardsAreAdmittedBeforeOrdinarySources()
        {
            StreetWorldFrame frame = Frame(0,
                Source("alpha-ordinary", new Point2(0, 5), "street.bird"),
                Source("zebra-vehicle", new Point2(0, 5), "street.car", true));
            var model = Model(() => frame, State(new Point2(0, 0), new Point2(0, 1), 100, 0));
            var playback = new FakePlayback();
            var controller = new StreetAudioController(playback, model);

            controller.Sync(model.Sample(new Point2(0, 0), new Point2(0, 1), 0), 1);

            Assert.AreEqual("zebra-vehicle", playback.Played[0].SourceId);
            Assert.AreEqual(FeedbackPriority.CriticalThreat, playback.Played[0].Priority);
            Assert.AreEqual("alpha-ordinary", playback.Played[1].SourceId);
            Assert.AreEqual(FeedbackPriority.Ambience, playback.Played[1].Priority);
        }

        [Test]
        public void CatalogSoundsAndEveryStreetMaterialHaveReplaceablePlaceholderClips()
        {
            Type bank = typeof(UnityAudioPlayback).Assembly.GetType("TingLu.Audio.PlaceholderBank");
            Assert.IsNotNull(bank);
            MethodInfo describe = bank.GetMethod("TryDescribe", BindingFlags.Public | BindingFlags.Static);
            Assert.IsNotNull(describe);
            foreach (string soundId in StreetCatalog.Sounds)
                Assert.IsTrue(Describes(describe, soundId), soundId);
            foreach (string materialId in StreetCatalog.Materials)
                Assert.IsTrue(Describes(describe, "footstep." + materialId), materialId);
            Assert.IsTrue(typeof(IStreetAudioUpdater).IsAssignableFrom(typeof(UnityAudioPlayback)));
        }

        private static bool Describes(MethodInfo method, string soundId)
        {
            var args = new object[] { soundId, 0f, 0f, false };
            return (bool)method.Invoke(null, args);
        }

        private static StreetAcousticModel Model(StreetWorldFrame frame, PlayerState state)
        { return Model(() => frame, state); }

        private static StreetAcousticModel Model(System.Func<StreetWorldFrame> capture, PlayerState state)
        {
            var reader = new PlayerReader { Current = state };
            return new StreetAcousticModel(capture, reader, new StreetConfig());
        }

        private static StreetWorldFrame Frame(double time, params StreetObject[] objects)
        { return new StreetWorldFrame(time, new StreetSurface[0], objects); }

        private static StreetObject Source(string id, Point2 position, string soundId, bool vehicle = false, bool active = true)
        {
            return new StreetObject(id, StreetKind.Active, position, new Point2[0], "asphalt",
                false, vehicle, false, 0, 0, soundId, active,
                vehicle ? PerceptionSemantic.Threat : PerceptionSemantic.Ordinary);
        }

        private static PlayerState State(Point2 position, Point2 facing, float focus, double confusedUntil)
        { return new PlayerState(PlayerMainState.Active, ActionMode.Walking, position, facing, 100, focus, 0, 0, confusedUntil); }

        private static AcousticQueryResult Find(IReadOnlyList<AcousticQueryResult> results, string id)
        {
            for (int i = 0; i < results.Count; i++) if (results[i].SourceId == id) return results[i];
            Assert.Fail("Missing source " + id);
            return null;
        }

        private static bool Contains(IReadOnlyList<AcousticQueryResult> results, string id)
        { for (int i = 0; i < results.Count; i++) if (results[i].SourceId == id) return true; return false; }
    }
}
