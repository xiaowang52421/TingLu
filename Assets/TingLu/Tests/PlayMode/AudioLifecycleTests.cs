#if UNITY_INCLUDE_TESTS
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using TingLu.Audio;
using TingLu.Bootstrap;
using TingLu.Contracts;
using TingLu.Persistence;

namespace TingLu.Tests.PlayMode
{
    public sealed class AudioLifecycleTests
    {
        private GameObject root;
        private UnityAudioPlayback playback;
        private DemoSession session;
        private string saveRoot;
        private readonly List<AudioReceipt> receipts = new List<AudioReceipt>();

        [SetUp]
        public void SetUp()
        {
            root = new GameObject("TingLu runtime test");
            var listener = new GameObject("Test listener");
            listener.transform.SetParent(root.transform);
            listener.AddComponent<AudioListener>();
            playback = root.AddComponent<UnityAudioPlayback>();
            playback.ConfigureListener(listener.transform);
            var clock = new ClockBridge();
            playback.Initialize(clock, 0);
            playback.Receipt += receipts.Add;
            saveRoot = Path.Combine(Path.GetTempPath(), "TingLu-PlayMode", Guid.NewGuid().ToString("N"));
            session = new DemoSession(playback, new SilentSpeech(), new LocalSaveService(saveRoot));
            clock.Target = session.Clock;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (session != null) session.Dispose();
            if (root != null) UnityEngine.Object.Destroy(root);
            yield return null;
            if (Directory.Exists(saveRoot)) Directory.Delete(saveRoot, true);
            receipts.Clear();
        }

        [UnityTest]
        public IEnumerator AssembledGameplayUsesRealSourcesAndPauseDefersWorldStart()
        {
            session.Start(false);
            session.Tick(new InputFrame(new Point2(0, 1), false, true, false, MenuAction.None), 0.1);
            yield return null;
            Assert.Greater(session.Player.Current.Position.Y, 0);
            Assert.IsTrue(receipts.Exists(receipt => receipt.Status == AudioStatus.Started));
            Assert.Greater(root.GetComponentsInChildren<AudioSource>().Length, 0);
            Assert.AreEqual(1, root.GetComponentsInChildren<AudioListener>().Length);
            session.Pause();
            playback.Play(new AudioRequest("paused-test", "cane.tap", new Point2(2, 3), FeedbackPriority.Navigation, session.Generation));
            yield return null;
            Assert.IsFalse(receipts.Exists(receipt => receipt.EventId == "paused-test" && receipt.Status == AudioStatus.Started));
            session.Resume();
            yield return null;
            Assert.IsTrue(receipts.Exists(receipt => receipt.EventId == "paused-test" && receipt.Status == AudioStatus.Started));
        }

        [UnityTest]
        public IEnumerator SpatialSourcesKeepIndependentPositionsAndStaleGenerationFails()
        {
            session.Start(false);
            playback.Reset(session.Generation);
            playback.Play(new AudioRequest("left-test", "ambience.bird", new Point2(-2, 3), FeedbackPriority.Ambience, session.Generation, "left", 1, 0, true));
            playback.Play(new AudioRequest("right-test", "ambience.bird", new Point2(2, 4), FeedbackPriority.Ambience, session.Generation, "right", 1, 0, true));
            playback.UpdateSource("left", new Point2(-4, 5), 0.5f, 0);
            yield return null;
            var sources = root.GetComponentsInChildren<AudioSource>();
            Assert.IsTrue(Array.Exists(sources, source => source.transform.position == new Vector3(-4, 0, 5)));
            Assert.IsTrue(Array.Exists(sources, source => source.transform.position == new Vector3(2, 0, 4)));
            playback.Play(new AudioRequest("stale-test", "cane.tap", null, FeedbackPriority.Navigation, session.Generation - 1));
            Assert.IsTrue(receipts.Exists(receipt => receipt.EventId == "stale-test" && receipt.Status == AudioStatus.Failed));
            Assert.IsFalse(receipts.Exists(receipt => receipt.EventId == "stale-test" && receipt.Status == AudioStatus.Started));
        }

        private sealed class ClockBridge : IGameClock
        {
            public IGameClock Target;
            public double Now { get { return Target == null ? 0 : Target.Now; } }
            public bool Paused { get { return Target != null && Target.Paused; } }
        }
        // Speech is isolated here: these tests verify real Unity world audio, not TTS or hearing.
        private sealed class SilentSpeech : IAudioSpeech
        {
            public void Speak(SpeechRequest request) { }
            public void StopSpeech(string eventId) { }
        }
    }
}
#endif
