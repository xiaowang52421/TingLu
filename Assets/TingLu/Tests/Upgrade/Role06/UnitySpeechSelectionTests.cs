using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using NUnit.Framework;
using TingLu.Audio;
using TingLu.Contracts;
using UnityEngine;
using Object = UnityEngine.Object;

namespace TingLu.Tests.Upgrade.Role06
{
    public sealed class UnitySpeechSelectionTests
    {
        private sealed class TestClock : IGameClock
        {
            public double Now { get { return 0; } }
            public bool Paused { get { return true; } }
        }

        private sealed class UnitySpeechFixture : System.IDisposable
        {
            public readonly GameObject ListenerObject;
            public readonly GameObject BackendObject;
            public readonly UnityAudioPlayback Playback;
            public readonly List<AudioReceipt> Receipts = new List<AudioReceipt>();

            public UnitySpeechFixture()
            {
                ListenerObject = new GameObject("Role06 speech retry listener");
                ListenerObject.AddComponent<AudioListener>().enabled = false;
                BackendObject = new GameObject("Role06 speech retry backend");
                Playback = BackendObject.AddComponent<UnityAudioPlayback>();
                typeof(UnityAudioPlayback).GetField("listenerAnchor", BindingFlags.Instance | BindingFlags.NonPublic)
                    .SetValue(Playback, ListenerObject.transform);
                typeof(UnityAudioPlayback).GetField("voiceLimit", BindingFlags.Instance | BindingFlags.NonPublic)
                    .SetValue(Playback, 1);
                Playback.Receipt += Receipts.Add;
                Playback.Initialize(new TestClock(), 7);
            }

            public void Dispose()
            {
                if (Playback != null) Playback.Dispose();
                if (BackendObject != null) Object.DestroyImmediate(BackendObject);
                if (ListenerObject != null) Object.DestroyImmediate(ListenerObject);
            }
        }

        [Test]
        public void ManagedSpeechSkipsDisabledAndNonChineseVoicesAndTriesTheNextEnabledChineseVoice()
        {
            Type candidateType = typeof(UnityAudioPlayback).Assembly.GetType("TingLu.Audio.SpeechVoiceCandidate");
            Type speechType = typeof(UnityAudioPlayback).Assembly.GetType("TingLu.Audio.SystemSpeechClip");
            Assert.IsNotNull(candidateType);
            Assert.IsNotNull(speechType);
            var candidates = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(candidateType));
            candidates.Add(NewCandidate(candidateType, "Disabled Chinese", "zh-CN", false));
            candidates.Add(NewCandidate(candidateType, "Enabled English", "en-US", true));
            candidates.Add(NewCandidate(candidateType, "First enabled Chinese", "zh-CN", true));
            candidates.Add(NewCandidate(candidateType, "Second enabled Chinese", "zh-TW", true));

            var attempted = new List<string>();
            MethodInfo select = speechType.GetMethod("TrySelectFirstEnabledChineseVoice", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.IsNotNull(select);
            object[] args = {
                candidates,
                new Func<string, bool>(name => { attempted.Add(name); return name == "Second enabled Chinese"; }),
                null,
                null
            };

            Assert.IsTrue((bool)select.Invoke(null, args));
            CollectionAssert.AreEqual(new[] { "First enabled Chinese", "Second enabled Chinese" }, attempted);
            Assert.AreEqual("Second enabled Chinese", args[2]);
            Assert.IsNull(args[3]);
        }

        [Test]
        public void NoEnabledChineseVoiceReturnsUnavailableWithoutAttemptingSelection()
        {
            Type candidateType = typeof(UnityAudioPlayback).Assembly.GetType("TingLu.Audio.SpeechVoiceCandidate");
            Type speechType = typeof(UnityAudioPlayback).Assembly.GetType("TingLu.Audio.SystemSpeechClip");
            Assert.IsNotNull(candidateType);
            Assert.IsNotNull(speechType);
            var candidates = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(candidateType));
            candidates.Add(NewCandidate(candidateType, "Disabled Chinese", "zh-CN", false));
            candidates.Add(NewCandidate(candidateType, "Enabled English", "en-US", true));
            var attempted = new List<string>();
            MethodInfo select = speechType.GetMethod("TrySelectFirstEnabledChineseVoice", BindingFlags.NonPublic | BindingFlags.Static);
            object[] args = { candidates, new Func<string, bool>(name => { attempted.Add(name); return true; }), null, null };

            Assert.IsFalse((bool)select.Invoke(null, args));
            Assert.AreEqual(0, attempted.Count);
            Assert.IsNull(args[2]);
            StringAssert.Contains("no enabled Chinese", (string)args[3]);
        }

        [Test]
        public void CachedMenuFocusLoadsARealResourceClipAndCopiesItsPcm()
        {
            Type cacheType = typeof(UnityAudioPlayback).Assembly.GetType("TingLu.Audio.StreetSpeechCache");
            Assert.IsNotNull(cacheType);
            MethodInfo load = cacheType.GetMethod("TryLoad", BindingFlags.NonPublic | BindingFlags.Static);
            MethodInfo route = cacheType.GetMethod("TryLoadOrFallback", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.IsNotNull(load);
            Assert.IsNotNull(route);

            object wave = null;
            int fallbackCalls = 0;
            object[] routeArgs =
            {
                "开始新游戏",
                new Func<string, bool>(text => { wave = load.Invoke(null, new object[] { text }); return wave != null; }),
                new Action<string>(text => fallbackCalls++)
            };
            Assert.IsTrue((bool)route.Invoke(null, routeArgs));
            Assert.AreEqual(0, fallbackCalls);
            Assert.IsNotNull(wave, "The generated Resources clip should resolve for a shipped menu focus line.");
            Type waveType = wave.GetType();
            float[] samples = (float[])waveType.GetField("Samples").GetValue(wave);
            int frames = (int)waveType.GetField("FrameCount").GetValue(wave);
            int channels = (int)waveType.GetField("Channels").GetValue(wave);
            int sampleRate = (int)waveType.GetField("SampleRate").GetValue(wave);
            Assert.IsNotNull(samples);
            Assert.AreEqual(frames * channels, samples.Length);
            Assert.Greater(frames, 0);
            Assert.Greater(channels, 0);
            Assert.Greater(sampleRate, 0);
            Assert.IsTrue(Array.Exists(samples, sample => Mathf.Abs(sample) > 0.0001f), "The imported clip should contain audible PCM samples.");
        }

        [Test]
        public void MissingCacheEntryInvokesDynamicSpeechFallbackOnce()
        {
            Type cacheType = typeof(UnityAudioPlayback).Assembly.GetType("TingLu.Audio.StreetSpeechCache");
            MethodInfo load = cacheType.GetMethod("TryLoad", BindingFlags.NonPublic | BindingFlags.Static);
            MethodInfo route = cacheType.GetMethod("TryLoadOrFallback", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.IsNotNull(load);
            Assert.IsNotNull(route);
            string missingText = "一条不在随包缓存里的临时动态提示";
            int loadCalls = 0;
            int fallbackCalls = 0;
            object[] args =
            {
                missingText,
                new Func<string, bool>(text => { loadCalls++; return load.Invoke(null, new object[] { text }) != null; }),
                new Action<string>(text => { Assert.AreEqual(missingText, text); fallbackCalls++; })
            };

            Assert.IsFalse((bool)route.Invoke(null, args));
            Assert.AreEqual(1, loadCalls);
            Assert.AreEqual(1, fallbackCalls);
        }

        [Test]
        public void RealUnityPoolFullSpeechRetriesAfterReleaseWhilePausedAndTimeScaleIsZero()
        {
            float previousTimeScale = Time.timeScale;
            Time.timeScale = 0f;
            using (var fixture = new UnitySpeechFixture())
            {
                try
                {
                    fixture.Playback.Play(new AudioRequest("retry_pool_blocker", "feedback.heartbeat", null,
                        FeedbackPriority.CriticalThreat, 7, loop: true));
                    fixture.Playback.SetPaused(true);
                    fixture.Playback.Speak(new SpeechRequest("retry_pool_full", "开始新游戏", null,
                        FeedbackPriority.Interface, 7));

                    InvokeUpdate(fixture.Playback);
                    InvokeUpdate(fixture.Playback);
                    AssertPendingWave(fixture.Playback, "retry_pool_full");
                    Assert.AreEqual(0, CountReceipts(fixture.Receipts, "retry_pool_full", AudioStatus.Started));

                    fixture.Playback.Stop("retry_pool_blocker");
                    Thread.Sleep(300);
                    InvokeUpdate(fixture.Playback);

                    Assert.AreEqual(1, CountReceipts(fixture.Receipts, "retry_pool_full", AudioStatus.Started),
                        "The retained cached PCM should start once a real Unity AudioSource slot is released.");
                    Assert.IsNull(GetPendingRetry(fixture.Playback));
                }
                finally { Time.timeScale = previousTimeScale; }
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public void PendingPoolRetryDoesNotReplayAfterCancellationOrReset(bool reset)
        {
            float previousTimeScale = Time.timeScale;
            Time.timeScale = 0f;
            using (var fixture = new UnitySpeechFixture())
            {
                try
                {
                    fixture.Playback.Play(new AudioRequest("retry_cancel_blocker", "feedback.heartbeat", null,
                        FeedbackPriority.CriticalThreat, 7, loop: true));
                    fixture.Playback.SetPaused(true);
                    fixture.Playback.Speak(new SpeechRequest("retry_cancelled", "开始新游戏", "retry-cancel-key",
                        FeedbackPriority.Interface, 7));
                    InvokeUpdate(fixture.Playback);
                    InvokeUpdate(fixture.Playback);
                    AssertPendingWave(fixture.Playback, "retry_cancelled");

                    if (reset) fixture.Playback.Reset(8);
                    else fixture.Playback.StopSpeech("retry_cancelled");
                    fixture.Playback.Stop("retry_cancel_blocker");
                    Thread.Sleep(300);
                    InvokeUpdate(fixture.Playback);

                    Assert.AreEqual(0, CountReceipts(fixture.Receipts, "retry_cancelled", AudioStatus.Started));
                    Assert.IsNull(GetPendingRetry(fixture.Playback));
                }
                finally { Time.timeScale = previousTimeScale; }
            }
        }

        private static void InvokeUpdate(UnityAudioPlayback playback)
        {
            typeof(UnityAudioPlayback).GetMethod("Update", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(playback, null);
        }

        private static object GetPendingRetry(UnityAudioPlayback playback)
        {
            return typeof(UnityAudioPlayback).GetField("pendingSpeechRetry", BindingFlags.Instance | BindingFlags.NonPublic)
                .GetValue(playback);
        }

        private static void AssertPendingWave(UnityAudioPlayback playback, string eventId)
        {
            object retry = GetPendingRetry(playback);
            Assert.IsNotNull(retry, "The real backend should retain the rendered utterance after pool exhaustion.");
            object work = retry.GetType().GetField("Work").GetValue(retry);
            Assert.AreEqual(eventId, work.GetType().GetField("Request").GetValue(work).GetType()
                .GetProperty("EventId").GetValue(work.GetType().GetField("Request").GetValue(work), null));
            Assert.IsNotNull(work.GetType().GetField("Wave").GetValue(work), "Retry must reuse PCM rather than synthesize again.");
        }

        private static int CountReceipts(List<AudioReceipt> receipts, string eventId, AudioStatus status)
        {
            int count = 0;
            for (int i = 0; i < receipts.Count; i++)
                if (receipts[i].EventId == eventId && receipts[i].Status == status) count++;
            return count;
        }

        private static object NewCandidate(Type candidateType, string name, string culture, bool enabled)
        { return Activator.CreateInstance(candidateType, new object[] { name, culture, enabled }); }
    }
}
