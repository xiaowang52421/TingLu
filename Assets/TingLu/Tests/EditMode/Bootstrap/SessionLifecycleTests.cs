#if UNITY_INCLUDE_TESTS
using System;
using System.IO;
using NUnit.Framework;
using TingLu.Bootstrap;
using TingLu.Contracts;
using TingLu.Persistence;
using TingLu.UI;

namespace TingLu.Tests.Bootstrap
{
    public sealed class SessionLifecycleTests
    {
        private string saveRoot;
        private LocalSaveService storage;
        private Playback playback;
        private DemoSession session;

        [SetUp]
        public void SetUp()
        {
            saveRoot = Path.Combine(Path.GetTempPath(), "TingLu-UnityTests", Guid.NewGuid().ToString("N"));
            storage = new LocalSaveService(saveRoot);
            playback = new Playback();
            session = new DemoSession(playback, playback, storage);
        }

        [TearDown]
        public void TearDown()
        {
            try { if (session != null) session.Dispose(); }
            finally { if (Directory.Exists(saveRoot)) Directory.Delete(saveRoot, true); }
        }

        [Test]
        public void MenuPauseFreezesMovementAndContinueLoadsSameAttempt()
        {
            Tick(menu: MenuAction.Confirm);
            for (int i = 0; i < 20; i++) Tick(y: 1);
            Tick(pause: true);
            double time = session.Clock.Now;
            Point2 position = session.Player.Current.Position;
            session.Tick(new InputFrame(new Point2(0, 1), false, true, false, MenuAction.None), 5);
            Assert.AreEqual(time, session.Clock.Now);
            Assert.AreEqual(position.Y, session.Player.Current.Position.Y);
            Assert.IsTrue(playback.Paused);
            string attempt = storage.LoadResume().Value.Attempt.AttemptId;
            Tick(pause: true);
            Assert.IsFalse(playback.Paused);
            session.ReturnToMenu();
            Tick(menu: MenuAction.Next);
            Tick(menu: MenuAction.Confirm);
            Assert.IsTrue(session.Running);
            Assert.AreEqual(MenuScreen.Playing, session.Menu.Screen);
            Assert.AreEqual(attempt, storage.LoadResume().Value.Attempt.AttemptId);
        }

        [Test]
        public void FailureHidesPresentationAndKeepsCollisionAcrossRecoveryAndReload()
        {
            session.Start(false);
            session.Player.NotifyCollision("l1-1/pole/01", 100, 0, new Point2(0, 15));
            Assert.IsTrue(session.Blackout);
            Tick();
            session.Tick(new InputFrame(new Point2(0, 0), false, false, false, MenuAction.None), 2.1);
            Assert.IsFalse(session.Blackout);
            Assert.AreEqual(100, session.Player.Current.Courage);
            session.ReturnToMenu();
            Assert.AreEqual(1, storage.LoadResume().Value.Attempt.Collisions);
            session.Start(true);
            session.ReturnToMenu();
            Assert.AreEqual(1, storage.LoadResume().Value.Attempt.Collisions);
        }

        [Test]
        public void DisposedSessionReleasesPlaybackAndRejectsFurtherInput()
        {
            session.Start(false);
            session.Dispose();
            Assert.IsTrue(playback.Disposed);
            Assert.Throws<ObjectDisposedException>(() => Tick());
        }

        private void Tick(float y = 0, bool pause = false, MenuAction menu = MenuAction.None)
        {
            session.Tick(new InputFrame(new Point2(0, y), false, false, pause, menu), 0.05);
        }

        // These tests exercise the imported assemblies and disk storage, not device output.
        private sealed class Playback : IAudioPlayback, IAudioSpeech
        {
            public event Action<AudioReceipt> Receipt;
            public bool Paused, Disposed;
            public void Play(AudioRequest request)
            {
                if (Receipt != null) Receipt(new AudioReceipt(request.EventId, AudioStatus.Started, 0, request.RecoveryGeneration));
            }
            public void Speak(SpeechRequest request) { }
            public void StopSpeech(string eventId) { }
            public void Stop(string eventId) { }
            public void UpdateListener(Point2 position, Point2 facing) { }
            public void Reset(int generation) { }
            public void SetPaused(bool paused) { Paused = paused; }
            public void Dispose() { Disposed = true; }
        }
    }
}
#endif
