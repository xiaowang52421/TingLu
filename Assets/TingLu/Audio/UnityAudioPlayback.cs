using System;
using System.Collections.Generic;
using System.Collections;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Diagnostics;
using System.Threading;
using System.Security.Cryptography;
using System.Text;
using TingLu.Contracts;
using UnityEngine;

namespace TingLu.Audio
{
    /// <summary>
    /// Replaceable Unity Audio adapter for M0. Its generated sounds are engineering placeholders,
    /// not final material cues or HRTF output. Inject the clock and the listener that owns the
    /// scene's single AudioListener; this component never creates a listener or searches a scene.
    /// </summary>
    public sealed class UnityAudioPlayback : MonoBehaviour, IAudioPlayback, IAudioSpeech, IStreetAudioUpdater
    {
        [SerializeField] private Transform listenerAnchor;
        [SerializeField, Range(1, 32)] private int voiceLimit = 12;
        [SerializeField, Range(0f, 1f)] private float masterVolume = 1f;
        [SerializeField, Range(0f, 2f)] private float voiceprintIntensity = 1f;
        [SerializeField] private float spatialMinDistance = 1f;
        [SerializeField] private float spatialMaxDistance = 26f;

        private sealed class Voice
        {
            public AudioSource Source;
            public FeedbackPriority Priority;
            public string EventId;
            public string SourceId;
            public string SupersessionKey;
            public int Generation;
            public float Gain = 1f;
            public float Occlusion;
            public bool PendingStart;
            public bool IsSpeech;
            public string SpeechProvider;
            public int SpeechFrames;
        }

        private sealed class SpeechWork
        {
            public SpeechRequest Request;
            public SpeechWave Wave;
            public string Error;
        }

        private sealed class SpeechRetry
        {
            public SpeechWork Work;
            public double NextAttemptAt;
            public double NextDelay;
        }

        private const double SpeechRetryInitialSeconds = 0.25;
        private const double SpeechRetryMaximumSeconds = 2.0;

        private readonly List<Voice> voices = new List<Voice>();
        private readonly Dictionary<string, AudioClip> clips = new Dictionary<string, AudioClip>(StringComparer.Ordinal);
        private readonly SpeechPriorityQueue speechQueue = new SpeechPriorityQueue();
        private readonly Queue<SpeechWork> renderedSpeech = new Queue<SpeechWork>();
        private readonly HashSet<int> speechRenderLifecycles = new HashSet<int>();
        private readonly HashSet<string> cancelledSpeech = new HashSet<string>(StringComparer.Ordinal);
        private readonly object speechLock = new object();
        private SpeechRequest renderingRequest;
        private SpeechRetry pendingSpeechRetry;
        private IGameClock clock;
        private volatile int generation;
        private volatile bool disposed;
        private bool paused;
        private bool ambienceDucked;
        private int lifecycle;

        public event Action<AudioReceipt> Receipt;

        private void Update()
        {
            if (!paused)
            {
                for (int i = 0; i < voices.Count; i++)
                {
                    Voice voice = voices[i];
                    if (voice.Source == null || string.IsNullOrEmpty(voice.EventId) || voice.Source.isPlaying) continue;
                    ReleaseSpeechClip(voice);
                    voice.EventId = null;
                    voice.SourceId = null;
                    voice.SupersessionKey = null;
                    voice.PendingStart = false;
                    voice.IsSpeech = false;
                }
            }
            SpeechWork completed = null;
            if (pendingSpeechRetry == null && !HasPlayingSpeech())
                lock (speechLock)
                    if (renderedSpeech.Count > 0) completed = renderedSpeech.Dequeue();
            if (completed != null) FinishSpeech(completed);
            RetryPendingSpeech();
            StartNextSpeech();
            bool speechActive = false;
            for (int i = 0; i < voices.Count; i++)
                if (voices[i].IsSpeech && voices[i].Source != null && voices[i].Source.isPlaying)
                { speechActive = true; break; }
            lock (speechLock) speechActive = speechActive || renderingRequest != null;
            speechActive = speechActive || speechQueue.Count > 0 || pendingSpeechRetry != null;
            if (ambienceDucked != speechActive) SetAmbienceDucked(speechActive);
        }

        /// <summary>Call once from the owning bootstrap after assigning listenerAnchor.</summary>
        public void Initialize(IGameClock simulationClock, int recoveryGeneration)
        {
            if (simulationClock == null) throw new ArgumentNullException("simulationClock");
            if (listenerAnchor == null || listenerAnchor.GetComponent<AudioListener>() == null)
                throw new InvalidOperationException("Assign the scene's AudioListener transform to listenerAnchor.");
            if (clock != null && recoveryGeneration != generation) Reset(recoveryGeneration);
            clock = simulationClock;
            generation = recoveryGeneration;
            lifecycle++;
            disposed = false;
        }

        public void ConfigureListener(Transform anchor)
        {
            if (anchor == null || anchor.GetComponent<AudioListener>() == null)
                throw new InvalidOperationException("The assigned transform must own the scene AudioListener.");
            listenerAnchor = anchor;
        }

        public void UpdateSource(string eventOrSourceId, Point2 position, float gain, float occlusion)
        {
            if (string.IsNullOrWhiteSpace(eventOrSourceId)) return;
            if (!(gain >= 0f && gain <= 1f) || !(occlusion >= 0f && occlusion <= 1f))
                throw new ArgumentOutOfRangeException("gain");
            for (int i = 0; i < voices.Count; i++)
            {
                Voice voice = voices[i];
                if (voice.Source == null || (voice.EventId != eventOrSourceId && voice.SourceId != eventOrSourceId)) continue;
                voice.Gain = gain;
                voice.Occlusion = occlusion;
                voice.Source.transform.position = new Vector3(position.X, 0f, position.Y);
                voice.Source.volume = EffectiveVolume(voice);
            }
        }

        public void Play(AudioRequest request)
        {
            if (disposed) return;
            if (request == null) throw new ArgumentNullException("request");
            Emit(request.EventId, AudioStatus.Requested, request.RecoveryGeneration, null);
            if (clock == null || request.RecoveryGeneration != generation)
            {
                Emit(request.EventId, AudioStatus.Failed, request.RecoveryGeneration, "backend_not_ready_or_generation_stale");
                return;
            }

            AudioClip clip;
            if (!TryGetClip(request.SoundId, out clip))
            {
                Emit(request.EventId, AudioStatus.Failed, request.RecoveryGeneration, "unknown_sound_id:" + request.SoundId);
                return;
            }

            var source = AcquireVoice(request.Priority);
            if (source == null)
            {
                Emit(request.EventId, AudioStatus.Failed, request.RecoveryGeneration, VoiceLimitReason(request));
                return;
            }

            Emit(request.EventId, AudioStatus.Ready, request.RecoveryGeneration, null);
            source.clip = clip;
            source.loop = request.Loop;
            source.spatialBlend = request.Position.HasValue ? 1f : 0f;
            source.minDistance = Mathf.Max(0.01f, spatialMinDistance);
            source.maxDistance = Mathf.Max(source.minDistance, spatialMaxDistance);
            source.dopplerLevel = 0f;
                source.rolloffMode = AudioRolloffMode.Custom;
                source.SetCustomCurve(AudioSourceCurveType.CustomRolloff, AnimationCurve.Linear(0f, 1f, 1f, 1f));
            if (request.Position.HasValue)
            {
                var p = request.Position.Value;
                source.transform.position = new Vector3(p.X, 0f, p.Y);
            }
            var voice = FindVoice(source);
            voice.EventId = request.EventId;
            voice.SourceId = request.SourceId;
            voice.IsSpeech = false;
            voice.Priority = request.Priority;
            voice.Generation = request.RecoveryGeneration;
            voice.Gain = request.Gain;
            voice.Occlusion = request.Occlusion;
            source.volume = EffectiveVolume(voice);
            if (paused && request.Priority != FeedbackPriority.Interface) voice.PendingStart = true;
            else StartVoice(voice);
        }

        public void Stop(string eventId)
        {
            if (string.IsNullOrWhiteSpace(eventId)) return;
            SpeechRequest[] waiting = speechQueue.RemoveEvent(eventId);
            for (int i = 0; i < waiting.Length; i++) Emit(waiting[i].EventId, AudioStatus.Stopped, waiting[i].RecoveryGeneration, "stopped_before_render");
            List<SpeechWork> ready = RemoveRendered(work => work.Request.EventId == eventId);
            for (int i = 0; i < ready.Count; i++) Emit(eventId, AudioStatus.Stopped, ready[i].Request.RecoveryGeneration, "stopped_before_playback");
            StopPendingSpeech(work => work.Request.EventId == eventId, "stopped_before_playback");
            lock (speechLock)
                if (renderingRequest != null && renderingRequest.EventId == eventId)
                { cancelledSpeech.Add(eventId); Emit(eventId, AudioStatus.Stopped, renderingRequest.RecoveryGeneration, "stopped_during_render"); }
            for (int i = voices.Count - 1; i >= 0; i--)
            {
                var voice = voices[i];
                if (voice.EventId != eventId) continue;
                if (voice.Source == null) continue;
                voice.Source.Stop();
                ReleaseSpeechClip(voice);
                voice.EventId = null;
                voice.SourceId = null;
                voice.SupersessionKey = null;
                voice.PendingStart = false;
                voice.IsSpeech = false;
                Emit(eventId, AudioStatus.Stopped, voice.Generation, null);
            }
        }

        public void Speak(SpeechRequest request)
        {
            if (disposed) return;
            if (request == null) throw new ArgumentNullException("request");
            Emit(request.EventId, AudioStatus.Requested, request.RecoveryGeneration, null);
            if (clock == null || request.RecoveryGeneration != generation)
            {
                Emit(request.EventId, AudioStatus.Failed, request.RecoveryGeneration, "backend_not_ready_or_generation_stale");
                return;
            }
            if (!string.IsNullOrEmpty(request.SupersessionKey)) StopSuperseded(request.SupersessionKey);
            SpeechRequest[] replaced = speechQueue.Enqueue(request);
            for (int i = 0; i < replaced.Length; i++)
                Emit(replaced[i].EventId, AudioStatus.Stopped, replaced[i].RecoveryGeneration, "superseded_before_render");
        }

        public void StopSpeech(string eventId) { Stop(eventId); }

        public void UpdateListener(Point2 position, Point2 facing)
        {
            if (listenerAnchor == null) return;
            float length = Mathf.Sqrt(facing.X * facing.X + facing.Y * facing.Y);
            if (length < 0.0001f) return;
            listenerAnchor.position = new Vector3(position.X, 0f, position.Y);
            var forward = new Vector3(facing.X / length, 0f, facing.Y / length);
            listenerAnchor.rotation = Quaternion.LookRotation(forward, Vector3.up);
        }

        public void Reset(int recoveryGeneration)
        {
            if (recoveryGeneration < generation) return;
            lifecycle++;
            for (int i = voices.Count - 1; i >= 0; i--)
            {
                var voice = voices[i];
                if (!string.IsNullOrEmpty(voice.EventId))
                    Emit(voice.EventId, AudioStatus.Stopped, voice.Generation, "recovery_reset");
                if (voice.Source == null) continue;
                voice.Source.Stop();
                ReleaseSpeechClip(voice);
                voice.EventId = null;
                voice.SourceId = null;
                voice.SupersessionKey = null;
                voice.PendingStart = false;
                voice.IsSpeech = false;
            }
            SpeechRequest[] waiting = speechQueue.Clear();
            for (int i = 0; i < waiting.Length; i++)
                Emit(waiting[i].EventId, AudioStatus.Stopped, waiting[i].RecoveryGeneration, "recovery_reset");
            List<SpeechWork> ready = RemoveRendered(work => true);
            for (int i = 0; i < ready.Count; i++) Emit(ready[i].Request.EventId, AudioStatus.Stopped, ready[i].Request.RecoveryGeneration, "recovery_reset");
            StopPendingSpeech(work => true, "recovery_reset");
            SpeechRequest rendering;
            lock (speechLock)
            {
                rendering = renderingRequest; renderingRequest = null;
                cancelledSpeech.Clear();
            }
            if (rendering != null) Emit(rendering.EventId, AudioStatus.Stopped, rendering.RecoveryGeneration, "recovery_reset");
            generation = recoveryGeneration;
        }

        public void SetPaused(bool value)
        {
            bool enteringPause = value && !paused;
            paused = value;
            if (enteringPause) StopQueuedWorldSpeech();
            for (int i = 0; i < voices.Count; i++)
            {
                Voice voice = voices[i];
                if (voice.Source == null || string.IsNullOrEmpty(voice.EventId) || voice.Priority == FeedbackPriority.Interface) continue;
                if (paused)
                {
                    if (voice.IsSpeech)
                    {
                        string interrupted = voice.EventId;
                        voice.Source.Stop(); ReleaseSpeechClip(voice);
                        voice.EventId = null; voice.SourceId = null; voice.SupersessionKey = null;
                        voice.PendingStart = false; voice.IsSpeech = false;
                        Emit(interrupted, AudioStatus.Stopped, voice.Generation, "paused_interrupted_speech");
                        continue;
                    }
                    if (voice.Source.isPlaying) voice.Source.Pause();
                }
                else if (voice.PendingStart) StartVoice(voice);
                else voice.Source.UnPause();
            }
        }

        public void SetMasterVolume(float value)
        {
            masterVolume = Mathf.Clamp01(value);
            for (int i = 0; i < voices.Count; i++)
                if (voices[i].Source != null) voices[i].Source.volume = EffectiveVolume(voices[i]);
        }

        public void SetVoiceprintIntensity(float value)
        {
            voiceprintIntensity = Mathf.Clamp(value, 0f, 2f);
            for (int i = 0; i < voices.Count; i++)
                if (voices[i].Source != null) voices[i].Source.volume = EffectiveVolume(voices[i]);
        }

        public void SetAmbienceDucked(bool value)
        {
            ambienceDucked = value;
            for (int i = 0; i < voices.Count; i++)
            {
                Voice voice = voices[i];
                if (voice.Source == null || voice.Priority != FeedbackPriority.Ambience) continue;
                voice.Source.volume = EffectiveVolume(voice);
            }
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            Reset(generation);
            for (int i = 0; i < voices.Count; i++)
                if (voices[i].Source != null) DestroyOwnedObject(voices[i].Source.gameObject);
            foreach (var clip in clips.Values) if (clip != null) DestroyOwnedObject(clip);
            voices.Clear(); clips.Clear(); Receipt = null;
            speechQueue.Clear();
            lock (speechLock) renderedSpeech.Clear();
            lock (speechLock) speechRenderLifecycles.Clear();
        }

        private AudioSource AcquireVoice(FeedbackPriority requested)
        {
            for (int i = 0; i < voices.Count; i++)
                if (voices[i].Source != null && !voices[i].Source.isPlaying && string.IsNullOrEmpty(voices[i].EventId)) return voices[i].Source;
            if (voices.Count < Mathf.Max(1, voiceLimit))
            {
                var child = new GameObject("Audio voice " + voices.Count);
                child.transform.SetParent(transform, false);
                var source = child.AddComponent<AudioSource>();
                source.playOnAwake = false;
            source.rolloffMode = AudioRolloffMode.Custom;
            source.SetCustomCurve(AudioSourceCurveType.CustomRolloff, AnimationCurve.Linear(0f, 1f, 1f, 1f));
                voices.Add(new Voice { Source = source });
                return source;
            }

            int victim = -1;
            for (int i = 0; i < voices.Count; i++)
            {
                if (voices[i].Source == null || (int)voices[i].Priority >= (int)requested) continue;
                if (victim < 0 || (int)voices[i].Priority < (int)voices[victim].Priority) victim = i;
            }
            if (victim < 0) return null;
            var selected = voices[victim];
            if (!string.IsNullOrEmpty(selected.EventId)) Emit(selected.EventId, AudioStatus.Stopped, selected.Generation, "preempted_by_higher_priority");
            selected.Source.Stop(); ReleaseSpeechClip(selected); selected.EventId = null; selected.SourceId = null; selected.SupersessionKey = null; selected.PendingStart = false; selected.IsSpeech = false;
            return selected.Source;
        }

        private Voice FindVoice(AudioSource source)
        {
            for (int i = 0; i < voices.Count; i++) if (voices[i].Source == source) return voices[i];
            throw new InvalidOperationException("Audio voice pool lost a source");
        }

        private string VoiceLimitReason(AudioRequest request)
        {
            // Preserve the stable reason prefix and add a bounded competition
            // snapshot so diagnostics can distinguish pool saturation from a
            // higher-priority active voice without flooding the log.
            var summary = new StringBuilder("voice_limit_no_lower_priority_voice;request=");
            summary.Append(request.SoundId ?? "").Append(";priority=").Append(request.Priority);
            int count = 0;
            for (int i = 0; i < voices.Count && count < 4; i++)
            {
                Voice voice = voices[i];
                if (voice.Source == null || string.IsNullOrEmpty(voice.EventId)) continue;
                summary.Append(";active[").Append(count).Append("]=").Append(voice.SourceId ?? voice.EventId)
                    .Append(",priority=").Append(voice.Priority);
                count++;
            }
            summary.Append(";activeCount=").Append(count);
            return summary.ToString();
        }

        private void StopSuperseded(string key)
        {
            SpeechRequest[] waiting = speechQueue.RemoveSupersession(key);
            for (int i = 0; i < waiting.Length; i++) Emit(waiting[i].EventId, AudioStatus.Stopped, waiting[i].RecoveryGeneration, "superseded");
            List<SpeechWork> ready = RemoveRendered(work => work.Request.SupersessionKey == key);
            for (int i = 0; i < ready.Count; i++) Emit(ready[i].Request.EventId, AudioStatus.Stopped, ready[i].Request.RecoveryGeneration, "superseded");
            StopPendingSpeech(work => work.Request.SupersessionKey == key, "superseded");
            for (int i = 0; i < voices.Count; i++)
            {
                Voice voice = voices[i];
                if (voice.SupersessionKey != key) continue;
                if (!string.IsNullOrEmpty(voice.EventId)) Emit(voice.EventId, AudioStatus.Stopped, voice.Generation, "superseded");
                if (voice.Source == null) continue;
                voice.Source.Stop(); ReleaseSpeechClip(voice);
                voice.EventId = null; voice.SourceId = null; voice.SupersessionKey = null; voice.PendingStart = false; voice.IsSpeech = false;
            }
            lock (speechLock)
                if (renderingRequest != null && renderingRequest.SupersessionKey == key)
                { cancelledSpeech.Add(renderingRequest.EventId); Emit(renderingRequest.EventId, AudioStatus.Stopped, renderingRequest.RecoveryGeneration, "superseded_during_render"); }
        }

        private void StopQueuedWorldSpeech()
        {
            SpeechRequest[] waiting = speechQueue.RemoveWhere(request => request.Priority != FeedbackPriority.Interface);
            for (int i = 0; i < waiting.Length; i++)
                Emit(waiting[i].EventId, AudioStatus.Stopped, waiting[i].RecoveryGeneration, "paused_world_speech_discarded");
            List<SpeechWork> ready = RemoveRendered(work => work.Request.Priority != FeedbackPriority.Interface);
            for (int i = 0; i < ready.Count; i++)
                Emit(ready[i].Request.EventId, AudioStatus.Stopped, ready[i].Request.RecoveryGeneration, "paused_world_speech_discarded");
            StopPendingSpeech(work => work.Request.Priority != FeedbackPriority.Interface, "paused_world_speech_discarded");
            lock (speechLock)
                if (renderingRequest != null && renderingRequest.Priority != FeedbackPriority.Interface)
                {
                    cancelledSpeech.Add(renderingRequest.EventId);
                    Emit(renderingRequest.EventId, AudioStatus.Stopped, renderingRequest.RecoveryGeneration, "paused_during_world_speech_render");
                }
        }

        private void StartNextSpeech()
        {
            if (disposed || pendingSpeechRetry != null || speechQueue.Count == 0) return;
            // Keep synthesized speech intelligible. Do not start the next utterance until the
            // current one has finished (or was explicitly stopped/superseded).
            for (int i = 0; i < voices.Count; i++)
            {
                Voice active = voices[i];
                if (!active.IsSpeech || active.Source == null) continue;
                if (active.Source.isPlaying || (!paused && active.PendingStart)) return;
            }
            lock (speechLock)
                if (renderedSpeech.Count > 0 || renderingRequest != null || speechRenderLifecycles.Count > 0) return;
            SpeechRequest request = paused ? speechQueue.Peek(FeedbackPriority.Interface) : speechQueue.Peek();
            if (request == null) return;
            int renderLifecycle = lifecycle;
            if (request.RecoveryGeneration != generation)
            {
                speechQueue.Dequeue();
                Emit(request.EventId, AudioStatus.Stopped, request.RecoveryGeneration, "stale_generation_before_render");
                return;
            }
            lock (speechLock)
            {
                if (speechRenderLifecycles.Contains(renderLifecycle) || speechRenderLifecycles.Count >= 2) return;
                speechRenderLifecycles.Add(renderLifecycle);
            }
            speechQueue.RemoveEvent(request.EventId);
            lock (speechLock) renderingRequest = request;
            SpeechWave cached = null;
            bool cacheHit = StreetSpeechCache.TryLoadOrFallback(request.Text,
                delegate(string text) { cached = StreetSpeechCache.TryLoad(text); return cached != null; },
                delegate(string text) { StartSystemSpeechFallback(request, renderLifecycle); });
            if (cacheHit)
            {
                lock (speechLock)
                {
                    speechRenderLifecycles.Remove(renderLifecycle);
                    if (renderingRequest == request) renderingRequest = null;
                    if (!disposed && renderLifecycle == lifecycle && request.RecoveryGeneration == generation)
                        renderedSpeech.Enqueue(new SpeechWork { Request = request, Wave = cached });
                }
                return;
            }
        }

        private void StartSystemSpeechFallback(SpeechRequest request, int renderLifecycle)
        {
            ThreadPool.QueueUserWorkItem(delegate
            {
                SpeechWave wave;
                string error;
                bool ok = SystemSpeechClip.TryRender(request.Text, out wave, out error);
                lock (speechLock)
                {
                    speechRenderLifecycles.Remove(renderLifecycle);
                    if (renderingRequest == request) renderingRequest = null;
                    if (!disposed && renderLifecycle == lifecycle && request.RecoveryGeneration == generation)
                        renderedSpeech.Enqueue(new SpeechWork { Request = request, Wave = ok ? wave : null, Error = error });
                }
            });
        }

        private List<SpeechWork> RemoveRendered(Predicate<SpeechWork> match)
        {
            var removed = new List<SpeechWork>();
            int count;
            lock (speechLock) count = renderedSpeech.Count;
            for (int i = 0; i < count; i++)
            {
                SpeechWork work;
                lock (speechLock) work = renderedSpeech.Dequeue();
                if (match(work)) removed.Add(work);
                else lock (speechLock) renderedSpeech.Enqueue(work);
            }
            return removed;
        }

        private void FinishSpeech(SpeechWork work)
        {
            SpeechRequest request = work.Request;
            lock (speechLock)
                if (cancelledSpeech.Remove(request.EventId)) return;
            if (disposed || request.RecoveryGeneration != generation)
            {
                if (pendingSpeechRetry != null && pendingSpeechRetry.Work == work) pendingSpeechRetry = null;
                return;
            }
            if (paused && request.Priority != FeedbackPriority.Interface)
            {
                if (pendingSpeechRetry != null && pendingSpeechRetry.Work == work) pendingSpeechRetry = null;
                Emit(request.EventId, AudioStatus.Stopped, request.RecoveryGeneration, "paused_before_playback");
                StartNextSpeech();
                return;
            }
            if (work.Wave == null)
            {
                if (pendingSpeechRetry != null && pendingSpeechRetry.Work == work) pendingSpeechRetry = null;
                Emit(request.EventId, AudioStatus.Failed, request.RecoveryGeneration, work.Error);
                StartNextSpeech();
                return;
            }
            AudioSource source = AcquireVoice(request.Priority);
            if (source == null)
            {
                ScheduleSpeechRetry(work);
                return;
            }
            if (pendingSpeechRetry != null && pendingSpeechRetry.Work == work) pendingSpeechRetry = null;
            AudioClip clip = AudioClip.Create("speech_" + Guid.NewGuid().ToString("N"), work.Wave.FrameCount,
                work.Wave.Channels, work.Wave.SampleRate, false);
            clip.SetData(work.Wave.Samples, 0);
            Emit(request.EventId, AudioStatus.Ready, request.RecoveryGeneration, null);
            source.clip = clip; source.loop = false; source.spatialBlend = 0f;
            Voice voice = FindVoice(source);
            voice.EventId = request.EventId; voice.SourceId = null; voice.SupersessionKey = request.SupersessionKey;
            voice.Priority = request.Priority; voice.Generation = request.RecoveryGeneration; voice.Gain = 1f;
            voice.IsSpeech = true;
            voice.SpeechProvider = work.Wave.Provider;
            voice.SpeechFrames = work.Wave.FrameCount;
            source.volume = EffectiveVolume(voice);
            if (paused && request.Priority != FeedbackPriority.Interface) voice.PendingStart = true;
            else StartVoice(voice);
            StartNextSpeech();
        }

        private void ScheduleSpeechRetry(SpeechWork work)
        {
            if (disposed || work == null || work.Request.RecoveryGeneration != generation) return;
            double now = Time.realtimeSinceStartupAsDouble;
            if (pendingSpeechRetry == null || pendingSpeechRetry.Work != work)
                pendingSpeechRetry = new SpeechRetry
                {
                    Work = work,
                    NextAttemptAt = now + SpeechRetryInitialSeconds,
                    NextDelay = Math.Min(SpeechRetryMaximumSeconds, SpeechRetryInitialSeconds * 2.0)
                };
            else
            {
                pendingSpeechRetry.NextAttemptAt = now + pendingSpeechRetry.NextDelay;
                pendingSpeechRetry.NextDelay = Math.Min(SpeechRetryMaximumSeconds, pendingSpeechRetry.NextDelay * 2.0);
            }
        }

        private void RetryPendingSpeech()
        {
            SpeechRetry retry = pendingSpeechRetry;
            if (retry == null) return;
            SpeechRequest request = retry.Work.Request;
            if (disposed || request.RecoveryGeneration != generation)
            {
                pendingSpeechRetry = null;
                return;
            }
            if (paused && request.Priority != FeedbackPriority.Interface)
            {
                StopPendingSpeech(work => work == retry.Work, "paused_before_playback");
                return;
            }
            double now = Time.realtimeSinceStartupAsDouble;
            if (now < retry.NextAttemptAt) return;
            if (HasPlayingSpeech())
            {
                retry.NextAttemptAt = now + retry.NextDelay;
                retry.NextDelay = Math.Min(SpeechRetryMaximumSeconds, retry.NextDelay * 2.0);
                return;
            }
            FinishSpeech(retry.Work);
        }

        private bool HasPlayingSpeech()
        {
            for (int i = 0; i < voices.Count; i++)
            {
                Voice voice = voices[i];
                if (voice.IsSpeech && voice.Source != null &&
                    (voice.Source.isPlaying || (!paused && voice.PendingStart))) return true;
            }
            return false;
        }

        private void StopPendingSpeech(Predicate<SpeechWork> match, string reason)
        {
            if (pendingSpeechRetry == null || !match(pendingSpeechRetry.Work)) return;
            SpeechWork work = pendingSpeechRetry.Work;
            pendingSpeechRetry = null;
            Emit(work.Request.EventId, AudioStatus.Stopped, work.Request.RecoveryGeneration, reason);
        }

        private void StartVoice(Voice voice)
        {
            if (voice == null || voice.Source == null || string.IsNullOrEmpty(voice.EventId)) return;
            voice.PendingStart = false;
            voice.Source.Play();
            if (voice.IsSpeech && IsAudioDiagnosticsEnabled())
                UnityEngine.Debug.Log("TingLu speech started; backend=" + voice.SpeechProvider + "; frames=" + voice.SpeechFrames);
            // Started means Unity accepted the software playback command, not that hardware rendered it.
            Emit(voice.EventId, AudioStatus.Started, voice.Generation, null);
        }

        private void ReleaseSpeechClip(Voice voice)
        {
            if (voice.Source != null && voice.Source.clip != null && voice.Source.clip.name.StartsWith("speech_", StringComparison.Ordinal))
                DestroyOwnedObject(voice.Source.clip);
            if (voice.Source != null) voice.Source.clip = null;
        }

        private static void DestroyOwnedObject(UnityEngine.Object ownedObject)
        {
            if (ownedObject == null) return;
            if (Application.isPlaying) UnityEngine.Object.Destroy(ownedObject);
            else UnityEngine.Object.DestroyImmediate(ownedObject);
        }

        private bool TryGetClip(string id, out AudioClip clip)
        {
            if (clips.TryGetValue(id, out clip)) return true;
            float frequency; float duration; bool noise;
            if (!PlaceholderBank.TryDescribe(id, out frequency, out duration, out noise)) return false;
            const int sampleRate = 22050;
            int count = Mathf.Max(64, Mathf.CeilToInt(sampleRate * duration));
            clip = AudioClip.Create("placeholder_" + id, count, 1, sampleRate, false);
            var samples = new float[count];
            uint seed = 2166136261u;
            for (int c = 0; c < id.Length; c++) seed = (seed ^ id[c]) * 16777619u;
            for (int i = 0; i < count; i++)
            {
                float t = (float)i / sampleRate;
                float envelope = Mathf.Min(1f, t * 20f) * Mathf.Min(1f, (duration - t) * 10f);
                float wave = Mathf.Sin(2f * Mathf.PI * frequency * t);
                if (noise)
                {
                    seed = seed * 1664525u + 1013904223u;
                    float noiseSample = ((seed >> 8) / 8388607.5f) - 1f;
                    wave = wave * 0.3f + noiseSample * 0.7f;
                }
                samples[i] = wave * envelope * 0.16f;
            }
            clip.SetData(samples, 0);
            clips.Add(id, clip);
            return true;
        }

        private void Emit(string eventId, AudioStatus status, int receiptGeneration, string error)
        {
            var callback = Receipt;
            if (callback != null) callback(new AudioReceipt(eventId, status,
                clock == null ? 0 : Math.Max(0, clock.Now), receiptGeneration, error));
        }

        private float EffectiveVolume(Voice voice)
        {
            float intensity = voice.Priority == FeedbackPriority.Navigation ? voiceprintIntensity : 1f;
            return masterVolume * voice.Gain * intensity * (ambienceDucked && voice.Priority == FeedbackPriority.Ambience ? 0.28f : 1f);
        }

        private static bool IsAudioDiagnosticsEnabled()
        { return string.Equals(Environment.GetEnvironmentVariable("TINGLU_AUDIO_DIAGNOSTICS"), "1", StringComparison.Ordinal); }
    }

    /// <summary>Generated tones are temporary test cues; no recorded licensed assets are included.</summary>
    internal static class PlaceholderBank
    {
        public static bool TryDescribe(string id, out float hz, out float seconds, out bool noise)
        {
            noise = false; seconds = 0.16f; hz = 440f;
            switch (id)
            {
                case "street.car": hz = 92; noise = true; seconds = 0.8f; return true;
                case "street.electric": hz = 540; noise = true; seconds = 0.62f; return true;
                case "street.cycle": hz = 780; seconds = 0.24f; return true;
                case "street.walker": hz = 235; noise = true; seconds = 0.32f; return true;
                case "street.bird": hz = 1480; seconds = 0.3f; return true;
                case "street.signal.ns": hz = 720; seconds = 0.42f; return true;
                case "street.signal.ew": hz = 960; seconds = 0.42f; return true;
                case "footstep.asphalt": hz = 175; noise = true; seconds = 0.09f; return true;
                case "footstep.sidewalk": hz = 205; noise = true; seconds = 0.09f; return true;
                case "footstep.tactile": hz = 265; noise = true; seconds = 0.1f; return true;
                case "footstep.tactile.dots": hz = 340; noise = true; seconds = 0.11f; return true;
                case "footstep.crosswalk": hz = 155; noise = true; seconds = 0.09f; return true;
                case "footstep.ramp": hz = 220; noise = true; seconds = 0.1f; return true;
                case "footstep.curb": hz = 125; noise = true; seconds = 0.12f; return true;
                case "footstep.brick": hz = 290; noise = true; seconds = 0.1f; return true;
                case "footstep.metal": hz = 610; noise = true; seconds = 0.11f; return true;
                case "footstep.cardboard": hz = 105; noise = true; seconds = 0.12f; return true;
                case "cane.tap": hz = 850; seconds = 0.12f; return true;
                case "echo.ping": hz = 1240; seconds = 0.22f; return true;
                case "ambience.bird": hz = 1500; seconds = 0.3f; return true;
                case "ambience.pedestrian": hz = 380; noise = true; seconds = 0.24f; return true;
                case "ambience.guard": hz = 290; seconds = 0.23f; return true;
                case "collision.bump": hz = 105; noise = true; seconds = 0.17f; return true;
                case "feedback.breath": hz = 120; noise = true; seconds = 0.5f; return true;
                case "feedback.heartbeat": hz = 65; seconds = 0.18f; return true;
                case "feedback.checkpoint": hz = 620; seconds = 0.28f; return true;
                case "feedback.menu": hz = 520; seconds = 0.08f; return true;
                default: return false;
            }
        }
    }

    internal sealed class SpeechVoiceCandidate
    {
        public readonly string Name;
        public readonly string Culture;
        public readonly bool Enabled;

        public SpeechVoiceCandidate(string name, string culture, bool enabled)
        { Name = name; Culture = culture; Enabled = enabled; }
    }

    internal sealed class SpeechWave
    {
        public readonly float[] Samples;
        public readonly int FrameCount, Channels, SampleRate;
        public readonly string Provider;
        public SpeechWave(float[] samples, int frames, int channels, int rate, string provider = "unknown")
        { Samples = samples; FrameCount = frames; Channels = channels; SampleRate = rate; Provider = provider; }
    }

    internal static class StreetSpeechCache
    {
        private const string ResourceRoot = "TingLuSpeech/";
        internal const string Provider = "cached:bundled System.Speech Chinese voice";

        internal static string KeyFor(string text)
        {
            byte[] bytes = System.Text.Encoding.UTF8.GetBytes(text ?? string.Empty);
            byte[] digest;
            using (SHA256 sha = SHA256.Create()) digest = sha.ComputeHash(bytes);
            const string alphabet = "0123456789abcdef";
            char[] hex = new char[digest.Length * 2];
            for (int i = 0; i < digest.Length; i++)
            { hex[i * 2] = alphabet[digest[i] >> 4]; hex[i * 2 + 1] = alphabet[digest[i] & 0x0f]; }
            return new string(hex);
        }

        internal static SpeechWave TryLoad(string text)
        {
            if (string.IsNullOrEmpty(text)) return null;
            try
            {
                AudioClip clip = Resources.Load<AudioClip>(ResourceRoot + KeyFor(text));
                if (clip == null || clip.samples < 1 || clip.channels < 1 || clip.frequency < 1) return null;
                if (clip.loadState == AudioDataLoadState.Unloaded && !clip.LoadAudioData()) return null;
                if (clip.loadState != AudioDataLoadState.Loaded) return null;
                int sampleCount = checked(clip.samples * clip.channels);
                var samples = new float[sampleCount];
                if (!clip.GetData(samples, 0)) return null;
                return new SpeechWave(samples, clip.samples, clip.channels, clip.frequency, Provider);
            }
            catch { return null; }
        }

        internal static bool TryLoadOrFallback(string text, Func<string, bool> cacheLoader, Action<string> fallback)
        {
            if (cacheLoader != null && cacheLoader(text)) return true;
            if (fallback != null) fallback(text);
            return false;
        }
    }

    /// <summary>Windows System.Speech, late-bound to avoid a Unity assembly/platform dependency.</summary>
    internal static class SystemSpeechClip
    {
        public static bool TryRender(string text, out SpeechWave wave, out string error)
        {
            wave = null; error = null;
            string managedError;
            if (TryRenderSystemSpeech(text, out wave, out managedError)) return true;
            if (Environment.OSVersion.Platform == PlatformID.Win32NT)
            {
                string sapiError;
                if (TryRenderSapi(text, out wave, out sapiError)) return true;
                string powershellError;
                if (TryRenderWindowsPowerShell(text, out wave, out powershellError)) return true;
                error = managedError + "; " + sapiError + "; " + powershellError;
                return false;
            }
            error = managedError;
            return false;
        }

        private static bool TryRenderSystemSpeech(string text, out SpeechWave wave, out string error)
        {
            wave = null; error = null;
            object synth = null;
            System.IO.MemoryStream stream = null;
            try
            {
                Type type = Type.GetType("System.Speech.Synthesis.SpeechSynthesizer, System.Speech", false);
                if (type == null) { error = "speech_unavailable:System.Speech assembly not found"; return false; }
                synth = Activator.CreateInstance(type);
                if (!SelectChineseManagedVoice(type, synth, out error)) return false;
                var setOutput = type.GetMethod("SetOutputToWaveStream", new[] { typeof(System.IO.Stream) });
                var speak = type.GetMethod("Speak", new[] { typeof(string) });
                var silence = type.GetMethod("SetOutputToNull", Type.EmptyTypes);
                if (setOutput == null || speak == null) { error = "speech_unavailable:required methods missing"; return false; }
                stream = new System.IO.MemoryStream();
                setOutput.Invoke(synth, new object[] { stream });
                speak.Invoke(synth, new object[] { text });
                if (silence != null) silence.Invoke(synth, null);
                int dataOffset, dataLength, channels, sampleRate, bits;
                if (!TryReadWave(stream.ToArray(), out dataOffset, out dataLength, out channels, out sampleRate, out bits))
                { error = "speech_render_failed:unsupported wave format"; return false; }
                byte[] wav = stream.ToArray();
                int bytesPerSample = bits / 8;
                int frameCount = dataLength / bytesPerSample / channels;
                if (frameCount < 1) { error = "speech_render_failed:empty output"; return false; }
                float[] samples = new float[frameCount * channels];
                for (int i = 0; i < samples.Length; i++)
                {
                    int offset = dataOffset + i * bytesPerSample;
                    short sample = (short)(wav[offset] | (wav[offset + 1] << 8));
                    samples[i] = sample / 32768f;
                }
                wave = new SpeechWave(samples, frameCount, channels, sampleRate, "System.Speech");
                return true;
            }
            catch (Exception e)
            {
                Exception cause = e is System.Reflection.TargetInvocationException && e.InnerException != null ? e.InnerException : e;
                error = "speech_render_failed:" + cause.Message;
                wave = null;
                return false;
            }
            finally
            {
                if (synth is IDisposable) ((IDisposable)synth).Dispose();
                if (stream != null) stream.Dispose();
            }
        }

        private static bool SelectChineseManagedVoice(Type synthType, object synth, out string error)
        {
            error = null;
            MethodInfo getVoices = synthType.GetMethod("GetInstalledVoices", Type.EmptyTypes);
            MethodInfo selectVoice = synthType.GetMethod("SelectVoice", new[] { typeof(string) });
            if (getVoices == null || selectVoice == null) { error = "speech_unavailable:voice enumeration unavailable"; return false; }
            object installed = getVoices.Invoke(synth, null);
            IEnumerable voices = installed as IEnumerable;
            var candidates = new List<SpeechVoiceCandidate>();
            if (voices != null)
                foreach (object voice in voices)
                {
                    PropertyInfo enabledProperty = voice.GetType().GetProperty("Enabled");
                    PropertyInfo infoProperty = voice.GetType().GetProperty("VoiceInfo");
                    if (enabledProperty == null || infoProperty == null) continue;
                    bool enabled = (bool)enabledProperty.GetValue(voice, null);
                    object info = infoProperty.GetValue(voice, null);
                    object cultureInfo = info.GetType().GetProperty("Culture").GetValue(info, null);
                    string culture = (string)cultureInfo.GetType().GetProperty("Name").GetValue(cultureInfo, null);
                    string name = (string)info.GetType().GetProperty("Name").GetValue(info, null);
                    candidates.Add(new SpeechVoiceCandidate(name, culture, enabled));
                }
            var selectionErrors = new List<string>();
            string selectedName;
            string selectionError;
            bool selected = TrySelectFirstEnabledChineseVoice(candidates, delegate(string name)
            {
                try { selectVoice.Invoke(synth, new object[] { name }); return true; }
                catch (Exception e)
                {
                    Exception cause = e is TargetInvocationException && e.InnerException != null ? e.InnerException : e;
                    selectionErrors.Add(name + ":" + cause.Message);
                    return false;
                }
            }, out selectedName, out selectionError);
            if (selected) return true;
            error = "speech_unavailable:" + selectionError;
            if (selectionErrors.Count > 0) error += "; " + string.Join("; ", selectionErrors.ToArray());
            return false;
        }

        internal static bool TrySelectFirstEnabledChineseVoice(IEnumerable<SpeechVoiceCandidate> candidates,
            Func<string, bool> trySelect, out string selectedName, out string error)
        {
            selectedName = null;
            error = null;
            if (candidates == null) { error = "voice enumeration unavailable"; return false; }
            bool hasEnabledChineseVoice = false;
            foreach (SpeechVoiceCandidate candidate in candidates)
            {
                if (candidate == null || !candidate.Enabled || string.IsNullOrEmpty(candidate.Name) ||
                    string.IsNullOrEmpty(candidate.Culture) ||
                    !candidate.Culture.StartsWith("zh", StringComparison.OrdinalIgnoreCase)) continue;
                hasEnabledChineseVoice = true;
                if (trySelect != null && trySelect(candidate.Name))
                {
                    selectedName = candidate.Name;
                    return true;
                }
            }
            error = hasEnabledChineseVoice ? "no enabled Chinese System.Speech voice could be selected" :
                "no enabled Chinese System.Speech voice installed";
            return false;
        }

        private static bool TryRenderSapi(string text, out SpeechWave wave, out string error)
        {
            wave = null; error = null;
            object voice = null, stream = null, tokenSet = null, token = null;
            string path = null;
            try
            {
                Type voiceType = Type.GetTypeFromProgID("SAPI.SpVoice");
                Type streamType = Type.GetTypeFromProgID("SAPI.SpFileStream");
                if (voiceType == null || streamType == null) { error = "speech_unavailable:Windows SAPI is not registered"; return false; }
                voice = Activator.CreateInstance(voiceType);
                tokenSet = voiceType.InvokeMember("GetVoices", BindingFlags.InvokeMethod, null, voice, new object[] { "Language=804", "" });
                int count = (int)tokenSet.GetType().InvokeMember("Count", BindingFlags.GetProperty, null, tokenSet, null);
                if (count == 0)
                {
                    Marshal.ReleaseComObject(tokenSet); tokenSet = null;
                    tokenSet = voiceType.InvokeMember("GetVoices", BindingFlags.InvokeMethod, null, voice, new object[] { "Language=404", "" });
                    count = (int)tokenSet.GetType().InvokeMember("Count", BindingFlags.GetProperty, null, tokenSet, null);
                }
                if (count == 0) { error = "speech_unavailable:no Chinese Windows SAPI voice installed"; return false; }
                token = tokenSet.GetType().InvokeMember("Item", BindingFlags.InvokeMethod, null, tokenSet, new object[] { 0 });
                voiceType.InvokeMember("Voice", BindingFlags.SetProperty, null, voice, new[] { token });
                path = Path.Combine(Path.GetTempPath(), "tinglu_speech_" + Guid.NewGuid().ToString("N") + ".wav");
                stream = Activator.CreateInstance(streamType);
                streamType.InvokeMember("Open", BindingFlags.InvokeMethod, null, stream, new object[] { path, 3, false });
                voiceType.InvokeMember("AudioOutputStream", BindingFlags.SetProperty, null, voice, new[] { stream });
                voiceType.InvokeMember("Speak", BindingFlags.InvokeMethod, null, voice, new object[] { text, 0 });
                streamType.InvokeMember("Close", BindingFlags.InvokeMethod, null, stream, null);
                byte[] bytes = File.ReadAllBytes(path);
                int offset, length, channels, rate, bits;
                if (!TryReadWave(bytes, out offset, out length, out channels, out rate, out bits))
                { error = "speech_render_failed:SAPI returned an unsupported wave format"; return false; }
                int frames = length / (bits / 8) / channels;
                if (frames < 1) { error = "speech_render_failed:SAPI returned empty audio"; return false; }
                float[] samples = new float[frames * channels];
                for (int i = 0; i < samples.Length; i++)
                { int p = offset + i * 2; samples[i] = (short)(bytes[p] | (bytes[p + 1] << 8)) / 32768f; }
                wave = new SpeechWave(samples, frames, channels, rate, "Windows SAPI COM");
                return true;
            }
            catch (Exception e)
            {
                error = "speech_render_failed:SAPI:" + (e is TargetInvocationException && e.InnerException != null ? e.InnerException.Message : e.Message);
                wave = null;
                return false;
            }
            finally
            {
                ReleaseCom(token); ReleaseCom(tokenSet); ReleaseCom(stream); ReleaseCom(voice);
                if (path != null) try { if (File.Exists(path)) File.Delete(path); } catch { }
            }
        }

        private static bool TryRenderWindowsPowerShell(string text, out SpeechWave wave, out string error)
        {
            wave = null; error = null;
            string windows = Environment.GetEnvironmentVariable("WINDIR");
            if (string.IsNullOrEmpty(windows)) { error = "speech_unavailable:WINDIR is not set"; return false; }
            string shellDirectory = IntPtr.Size == 4 ? "Sysnative" : "System32";
            string executable = Path.Combine(windows, shellDirectory, "WindowsPowerShell\\v1.0\\powershell.exe");
            if (!File.Exists(executable))
                executable = Path.Combine(windows, "System32", "WindowsPowerShell\\v1.0\\powershell.exe");
            if (!File.Exists(executable)) { error = "speech_unavailable:Windows PowerShell was not found"; return false; }

            string path = Path.Combine(Path.GetTempPath(), "tinglu_speech_" + Guid.NewGuid().ToString("N") + ".wav");
            string encodedText = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(text ?? string.Empty));
            string encodedPath = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(path));
            string script = BuildPowerShellSpeechScript(encodedText, encodedPath);
            Process process = null;
            try
            {
                var start = new ProcessStartInfo();
                start.FileName = executable;
                start.Arguments = "-NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand " +
                    Convert.ToBase64String(System.Text.Encoding.Unicode.GetBytes(script));
                start.UseShellExecute = false;
                start.CreateNoWindow = true;
                start.WindowStyle = ProcessWindowStyle.Hidden;
                start.RedirectStandardError = true;
                start.RedirectStandardOutput = true;
                process = Process.Start(start);
                if (process == null) { error = "speech_render_failed:PowerShell process did not start"; return false; }
                System.Threading.Tasks.Task<string> standardError = process.StandardError.ReadToEndAsync();
                System.Threading.Tasks.Task<string> standardOutput = process.StandardOutput.ReadToEndAsync();
                if (!process.WaitForExit(20000))
                {
                    try { process.Kill(); process.WaitForExit(); } catch { }
                    error = "speech_render_failed:PowerShell synthesis timed out";
                    return false;
                }
                process.WaitForExit();
                string childError = standardError.Result.Trim();
                standardOutput.Wait();
                if (process.ExitCode != 0)
                {
                    error = "speech_render_failed:PowerShell exit " + process.ExitCode +
                        (string.IsNullOrEmpty(childError) ? string.Empty : ":" + childError);
                    return false;
                }
                byte[] bytes = File.ReadAllBytes(path);
                int offset, length, channels, rate, bits;
                if (!TryReadWave(bytes, out offset, out length, out channels, out rate, out bits))
                { error = "speech_render_failed:PowerShell returned an unsupported WAV"; return false; }
                int frames = length / (bits / 8) / channels;
                if (frames < 1) { error = "speech_render_failed:PowerShell returned empty WAV"; return false; }
                float[] samples = new float[frames * channels];
                for (int i = 0; i < samples.Length; i++)
                {
                    int sampleOffset = offset + i * (bits / 8);
                    short sample = (short)(bytes[sampleOffset] | (bytes[sampleOffset + 1] << 8));
                    samples[i] = sample / 32768f;
                }
                wave = new SpeechWave(samples, frames, channels, rate, "Windows PowerShell System.Speech");
                return true;
            }
            catch (Exception e)
            {
                error = "speech_render_failed:PowerShell:" + e.GetType().Name;
                wave = null;
                return false;
            }
            finally
            {
                if (process != null) process.Dispose();
                try { if (File.Exists(path)) File.Delete(path); } catch { }
            }
        }

        internal static string BuildPowerShellSpeechScript(string encodedText, string encodedPath)
        {
            return "$ErrorActionPreference='Stop';$code=0;" +
                "$t=[Text.Encoding]::UTF8.GetString([Convert]::FromBase64String('" + encodedText + "'));" +
                "$p=[Text.Encoding]::UTF8.GetString([Convert]::FromBase64String('" + encodedPath + "'));" +
                "$s=$null;try{" +
                "Add-Type -AssemblyName System.Speech;" +
                "$s=New-Object System.Speech.Synthesis.SpeechSynthesizer;" +
                "$candidates=@($s.GetInstalledVoices()|Where-Object{$_.Enabled -and $_.VoiceInfo.Culture.Name -like 'zh*'});" +
                "if($candidates.Count -eq 0){$code=22;throw 'no_enabled_chinese_voice'};" +
                "$rendered=$false;$failures=New-Object System.Collections.Generic.List[string];" +
                "foreach($v in $candidates){try{" +
                "$s.SetOutputToNull();$s.SelectVoice($v.VoiceInfo.Name);$s.SetOutputToWaveFile($p);$s.Speak($t);$s.SetOutputToNull();" +
                "if(-not(Test-Path -LiteralPath $p) -or (Get-Item -LiteralPath $p).Length -le 44){throw 'empty_or_missing_wave'};" +
                "$rendered=$true;break" +
                "}catch{$failure=$_.Exception.Message;$failures.Add($v.VoiceInfo.Name+':'+$failure);" +
                "try{$s.SetOutputToNull()}catch{};Remove-Item -LiteralPath $p -Force -ErrorAction SilentlyContinue}};" +
                "if(-not $rendered){$code=24;throw ('no_chinese_voice_succeeded:' + [string]::Join('|',$failures))}" +
                "}catch{[Console]::Error.WriteLine($_.Exception.Message);if($code -eq 0){$code=23}}" +
                "finally{if($null -ne $s){try{$s.SetOutputToNull()}catch{};$s.Dispose()}};exit $code";
        }

        private static void ReleaseCom(object value)
        { if (value != null && Marshal.IsComObject(value)) try { Marshal.FinalReleaseComObject(value); } catch { } }

        private static bool TryReadWave(byte[] bytes, out int dataOffset, out int dataLength, out int channels, out int sampleRate, out int bits)
        {
            dataOffset = dataLength = channels = sampleRate = bits = 0;
            if (bytes == null || bytes.Length < 44 || ReadAscii(bytes, 0, 4) != "RIFF" || ReadAscii(bytes, 8, 4) != "WAVE") return false;
            int format = 0;
            for (int p = 12; p + 8 <= bytes.Length;)
            {
                string tag = ReadAscii(bytes, p, 4);
                int length = BitConverter.ToInt32(bytes, p + 4);
                int begin = p + 8;
                if (length < 0 || begin + length > bytes.Length) return false;
                if (tag == "fmt " && length >= 16)
                {
                    format = BitConverter.ToInt16(bytes, begin);
                    channels = BitConverter.ToInt16(bytes, begin + 2);
                    sampleRate = BitConverter.ToInt32(bytes, begin + 4);
                    bits = BitConverter.ToInt16(bytes, begin + 14);
                }
                if (tag == "data") { dataOffset = begin; dataLength = length; }
                p = begin + length + (length & 1);
            }
            return format == 1 && channels > 0 && sampleRate > 0 && bits == 16 && dataOffset > 0 && dataLength > 0;
        }

        private static string ReadAscii(byte[] bytes, int offset, int count)
        { return System.Text.Encoding.ASCII.GetString(bytes, offset, count); }
    }
}
