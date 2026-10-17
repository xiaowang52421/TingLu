using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using TingLu.Audio;
using TingLu.Config;
using TingLu.Contracts;
using UnityEngine;
using Object = UnityEngine.Object;

namespace TingLu.Tests.Upgrade.Role06
{
    public sealed class UnityAudioPoolRetryTests
    {
        private sealed class TestClock : IGameClock
        {
            public double Now { get; set; }
            public bool Paused { get; set; }
        }

        private sealed class PlayerReader : IPlayerStateReader
        {
            public PlayerState Current { get; set; }
        }

        private sealed class PlaybackFixture : IDisposable
        {
            public readonly GameObject ListenerObject;
            public readonly GameObject BackendObject;
            public readonly TestClock Clock;
            public readonly UnityAudioPlayback Playback;
            public readonly List<AudioReceipt> Receipts = new List<AudioReceipt>();

            public PlaybackFixture(int voiceLimit, int generation)
            {
                Clock = new TestClock();
                ListenerObject = new GameObject("Role06 pool retry listener");
                ListenerObject.AddComponent<AudioListener>().enabled = false;
                BackendObject = new GameObject("Role06 pool retry backend");
                Playback = BackendObject.AddComponent<UnityAudioPlayback>();
                typeof(UnityAudioPlayback).GetField("listenerAnchor", BindingFlags.Instance | BindingFlags.NonPublic)
                    .SetValue(Playback, ListenerObject.transform);
                typeof(UnityAudioPlayback).GetField("voiceLimit", BindingFlags.Instance | BindingFlags.NonPublic)
                    .SetValue(Playback, voiceLimit);
                Playback.Receipt += Receipts.Add;
                Playback.Initialize(Clock, generation);
            }

            public void Dispose()
            {
                if (Playback != null) Playback.Dispose();
                if (BackendObject != null) Object.DestroyImmediate(BackendObject);
                if (ListenerObject != null) Object.DestroyImmediate(ListenerObject);
            }
        }

        [Test]
        public void RealPoolRetainsCriticalThreatAndStartsAmbienceAfterReleaseAndSimulationRetry()
        {
            using (var fixture = new PlaybackFixture(1, 7))
            {
                StreetObject bird = Source("bird", new Point2(0, 5), "street.bird", false);
                StreetObject car = Source("car", new Point2(0, 4), "street.car", true);
                StreetWorldFrame frame = Frame(0, bird, car);
                StreetAcousticModel model = Model(() => frame);
                var controller = new StreetAudioController(fixture.Playback, model);

                controller.Sync(Sample(model, 0), 7);

                Assert.AreEqual(1, Count(fixture.Receipts, "street-source:car", AudioStatus.Started),
                    "The real Unity backend should start the vehicle voice first.");
                Assert.AreEqual(0, Count(fixture.Receipts, "street-source:car", AudioStatus.Stopped),
                    "A lower-priority ambience request must not evict the CriticalThreat voice.");
                Assert.AreEqual(1, Count(fixture.Receipts, "street-source:bird", AudioStatus.Failed),
                    "The full real pool must report the ambience rejection.");
                Assert.AreEqual(0, Count(fixture.Receipts, "street-source:bird", AudioStatus.Started));
                AudioReceipt rejection = Find(fixture.Receipts, "street-source:bird", AudioStatus.Failed);
                StringAssert.Contains("voice_limit_no_lower_priority_voice", rejection.Error);
                StringAssert.Contains("priority=Ambience", rejection.Error);
                StringAssert.Contains("active[0]=car,priority=CriticalThreat", rejection.Error);
                AssertActiveVoice(fixture.Playback, "street-source:car", FeedbackPriority.CriticalThreat);

                fixture.Clock.Paused = true;
                fixture.Playback.SetPaused(true);
                frame = Frame(0, bird);
                IReadOnlyList<AcousticQueryResult> pausedSample = Sample(model, 0);
                controller.Sync(pausedSample, 7);
                for (int i = 0; i < 100; i++) controller.Sync(pausedSample, 7);
                Assert.AreEqual(1, Count(fixture.Receipts, "street-source:car", AudioStatus.Stopped),
                    "Removing the threat releases its actual UnityAudioPlayback slot.");
                Assert.AreEqual(1, Count(fixture.Receipts, "street-source:bird", AudioStatus.Failed),
                    "Paused repeated syncs at a frozen simulation timestamp must not retry.");
                Assert.AreEqual(0, Count(fixture.Receipts, "street-source:bird", AudioStatus.Started));
                int readyBeforeRetry = Count(fixture.Receipts, "street-source:bird", AudioStatus.Ready);
                int startedBeforeRetry = Count(fixture.Receipts, "street-source:bird", AudioStatus.Started);

                fixture.Playback.SetPaused(false);
                fixture.Clock.Paused = false;
                fixture.Clock.Now = 0.5;
                frame = Frame(0.5, bird);
                controller.Sync(Sample(model, 0.5), 7);

                Assert.AreEqual(1, Count(fixture.Receipts, "street-source:bird", AudioStatus.Failed));
                Assert.AreEqual(readyBeforeRetry + 1, Count(fixture.Receipts, "street-source:bird", AudioStatus.Ready),
                    "After the threat leaves and the 0.5 second simulation backoff expires, the real backend must admit the source.");
                Assert.AreEqual(startedBeforeRetry + 1, Count(fixture.Receipts, "street-source:bird", AudioStatus.Started),
                    "Started is the backend's software acceptance receipt.");
                Assert.AreEqual(0.5, Find(fixture.Receipts, "street-source:bird", AudioStatus.Started).SimulationTime, 0.0001);
                AssertActiveVoice(fixture.Playback, "street-source:bird", FeedbackPriority.Ambience);
                controller.Sync(Sample(model, 0.5), 7);
                Assert.AreEqual(1, Count(fixture.Receipts, "street-source:bird", AudioStatus.Started),
                    "A successful retry remains active instead of restarting each sync.");
            }
        }

        [Test]
        public void RealPoolResetCancelsBackoffAndRejectsOldGenerationBeforeCurrentGenerationStarts()
        {
            using (var fixture = new PlaybackFixture(1, 7))
            {
                StreetObject bird = Source("bird", new Point2(0, 5), "street.bird", false);
                StreetObject car = Source("car", new Point2(0, 4), "street.car", true);
                StreetWorldFrame frame = Frame(0, bird, car);
                StreetAcousticModel model = Model(() => frame);
                var controller = new StreetAudioController(fixture.Playback, model);
                controller.Sync(Sample(model, 0), 7);

                Assert.AreEqual(1, Count(fixture.Receipts, "street-source:bird", AudioStatus.Failed));
                frame = Frame(0.5, bird);
                IReadOnlyList<AcousticQueryResult> staleResults = Sample(model, 0.5);
                int staleRequestsBeforeReset = CountGeneration(fixture.Receipts,
                    "street-source:bird", AudioStatus.Requested, 7);
                int staleReadyBeforeReset = CountGeneration(fixture.Receipts,
                    "street-source:bird", AudioStatus.Ready, 7);
                int staleStartedBeforeReset = CountGeneration(fixture.Receipts,
                    "street-source:bird", AudioStatus.Started, 7);

                controller.Reset(8);
                fixture.Playback.Reset(8);
                controller.Sync(staleResults, 7);

                Assert.AreEqual(0, Count(fixture.Receipts, "street-source:bird", AudioStatus.Started),
                    "An old-generation sample cannot revive a request after Reset, even after the pool slot is free.");
                Assert.AreEqual(staleRequestsBeforeReset, CountGeneration(fixture.Receipts,
                    "street-source:bird", AudioStatus.Requested, 7),
                    "The controller must reject the stale generation before it reaches UnityAudioPlayback.");
                Assert.AreEqual(staleReadyBeforeReset, CountGeneration(fixture.Receipts,
                    "street-source:bird", AudioStatus.Ready, 7),
                    "Reset must not emit a new Ready receipt for the old generation.");
                Assert.AreEqual(staleStartedBeforeReset, CountGeneration(fixture.Receipts,
                    "street-source:bird", AudioStatus.Started, 7));

                fixture.Clock.Now = 0.1;
                frame = Frame(0.1, bird);
                int currentReadyBeforeStart = CountGeneration(fixture.Receipts,
                    "street-source:bird", AudioStatus.Ready, 8);
                int currentStartedBeforeStart = CountGeneration(fixture.Receipts,
                    "street-source:bird", AudioStatus.Started, 8);
                controller.Sync(Sample(model, 0.1), 8);

                Assert.AreEqual(currentReadyBeforeStart + 1, CountGeneration(fixture.Receipts,
                    "street-source:bird", AudioStatus.Ready, 8),
                    "The current generation should get a Ready receipt from the actual pool.");
                Assert.AreEqual(currentStartedBeforeStart + 1, CountGeneration(fixture.Receipts,
                    "street-source:bird", AudioStatus.Started, 8),
                    "Reset clears the old backoff so the current generation can start through the real backend.");
                Assert.AreEqual(0, CountGeneration(fixture.Receipts, "street-source:bird", AudioStatus.Started, 7));
                AssertActiveVoice(fixture.Playback, "street-source:bird", FeedbackPriority.Ambience);
            }
        }

        private static StreetAcousticModel Model(Func<StreetWorldFrame> capture)
        {
            var reader = new PlayerReader
            {
                Current = new PlayerState(PlayerMainState.Active, ActionMode.Walking,
                    new Point2(0, 0), new Point2(0, 1), 100, 100, 0, 0, 0)
            };
            return new StreetAcousticModel(capture, reader, new StreetConfig());
        }

        private static IReadOnlyList<AcousticQueryResult> Sample(StreetAcousticModel model, double time)
        { return model.Sample(new Point2(0, 0), new Point2(0, 1), time); }

        private static StreetWorldFrame Frame(double time, params StreetObject[] objects)
        { return new StreetWorldFrame(time, new StreetSurface[0], objects); }

        private static StreetObject Source(string id, Point2 position, string soundId, bool vehicle)
        {
            return new StreetObject(id, StreetKind.Active, position, new Point2[0], "asphalt",
                false, vehicle, false, 0, 0, soundId, true,
                vehicle ? PerceptionSemantic.Threat : PerceptionSemantic.Ordinary);
        }

        private static int Count(IReadOnlyList<AudioReceipt> receipts, string eventId, AudioStatus status)
        { return CountGeneration(receipts, eventId, status, -1); }

        private static int CountGeneration(IReadOnlyList<AudioReceipt> receipts, string eventId,
            AudioStatus status, int generation)
        {
            int count = 0;
            for (int i = 0; i < receipts.Count; i++)
                if (receipts[i].EventId == eventId && receipts[i].Status == status &&
                    (generation < 0 || receipts[i].RecoveryGeneration == generation)) count++;
            return count;
        }

        private static AudioReceipt Find(IReadOnlyList<AudioReceipt> receipts, string eventId, AudioStatus status)
        {
            for (int i = 0; i < receipts.Count; i++)
                if (receipts[i].EventId == eventId && receipts[i].Status == status) return receipts[i];
            Assert.Fail("Missing " + status + " receipt for " + eventId);
            return null;
        }

        private static void AssertActiveVoice(UnityAudioPlayback playback, string eventId, FeedbackPriority priority)
        {
            IList voices = (IList)typeof(UnityAudioPlayback).GetField("voices", BindingFlags.Instance | BindingFlags.NonPublic)
                .GetValue(playback);
            for (int i = 0; i < voices.Count; i++)
            {
                object voice = voices[i];
                Type voiceType = voice.GetType();
                if ((string)voiceType.GetField("EventId").GetValue(voice) != eventId) continue;
                Assert.AreEqual(priority, voiceType.GetField("Priority").GetValue(voice));
                Assert.IsNotNull(voiceType.GetField("Source").GetValue(voice), "The accepted voice is owned by a real AudioSource.");
                return;
            }
            Assert.Fail("Expected the real UnityAudioPlayback pool to retain " + eventId);
        }
    }
}
