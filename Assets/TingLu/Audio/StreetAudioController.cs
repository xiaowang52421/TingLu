using System;
using System.Collections.Generic;
using TingLu.Contracts;

namespace TingLu.Audio
{
    /// <summary>Synchronizes looping active-source voices from a single acoustic result list.</summary>
    public sealed class StreetAudioController
    {
        private const int MaximumBackoffLevel = 3;
        private const double InitialRetryDelaySeconds = 0.5;
        private const double MaximumRetryDelaySeconds = 2.0;

        private sealed class ActiveVoice
        {
            public string EventId;
            public string SoundId;
            public int Generation;
            public int Attempt;
        }

        private sealed class StartFailure
        {
            public string SoundId;
            public int Generation;
            public int BackoffLevel;
            public double RetryAfter;
        }

        private readonly IAudioPlayback playback;
        private readonly StreetAcousticModel acoustics;
        private readonly Dictionary<string, ActiveVoice> active = new Dictionary<string, ActiveVoice>(StringComparer.Ordinal);
        private readonly Dictionary<string, string> sourceByEvent = new Dictionary<string, string>(StringComparer.Ordinal);
        private readonly Dictionary<string, StartFailure> startFailures = new Dictionary<string, StartFailure>(StringComparer.Ordinal);
        private int generation;
        private bool hasGeneration;
        private bool listeningForReceipts;

        public StreetAudioController(IAudioPlayback playback, StreetAcousticModel acoustics)
        {
            this.playback = playback ?? throw new ArgumentNullException("playback");
            this.acoustics = acoustics ?? throw new ArgumentNullException("acoustics");
        }

        public void Sync(IReadOnlyList<AcousticQueryResult> results, int recoveryGeneration)
        {
            if (results == null) throw new ArgumentNullException("results");
            if (recoveryGeneration < 0) throw new ArgumentOutOfRangeException("recoveryGeneration");
            if (hasGeneration && recoveryGeneration < generation) return;
            if (!hasGeneration)
            {
                generation = recoveryGeneration;
                hasGeneration = true;
            }
            else if (recoveryGeneration > generation)
            {
                StopAll();
                startFailures.Clear();
                generation = recoveryGeneration;
            }

            var desired = new HashSet<string>(StringComparer.Ordinal);
            var ordered = new List<AcousticQueryResult>();
            for (int i = 0; i < results.Count; i++)
            {
                AcousticQueryResult result = results[i];
                if (result == null || !result.Active || result.Discernibility <= 0f) continue;
                if (string.IsNullOrEmpty(acoustics.SoundFor(result.SourceId))) continue;
                ordered.Add(result);
            }

            // Stable ordering makes equal-priority admission independent of world enumeration order.
            ordered.Sort(delegate(AcousticQueryResult left, AcousticQueryResult right)
            {
                bool leftHazard = acoustics.IsVehicleHazard(left.SourceId);
                bool rightHazard = acoustics.IsVehicleHazard(right.SourceId);
                if (leftHazard != rightHazard) return leftHazard ? -1 : 1;
                return string.CompareOrdinal(left.SourceId, right.SourceId);
            });

            for (int i = 0; i < ordered.Count; i++)
            {
                AcousticQueryResult result = ordered[i];
                string sourceId = result.SourceId;
                string soundId = acoustics.SoundFor(sourceId);
                if (!desired.Add(sourceId)) continue;
                ActiveVoice voice;
                if (active.TryGetValue(sourceId, out voice) && voice.SoundId == soundId && voice.Generation == generation)
                {
                    var updater = playback as IStreetAudioUpdater;
                    if (updater != null)
                        updater.UpdateSource(voice.EventId, result.ObservedSourcePosition, result.Discernibility, result.Occlusion);
                    else
                        RestartVoice(sourceId, voice, soundId, result);
                    continue;
                }

                if (voice != null) StopVoice(sourceId, voice);
                int attempt;
                if (CanStartVoice(sourceId, soundId, result, out attempt))
                    StartVoice(sourceId, soundId, result, attempt);
            }

            var missing = new List<string>();
            foreach (var item in active)
                if (!desired.Contains(item.Key)) missing.Add(item.Key);
            for (int i = 0; i < missing.Count; i++)
            {
                ActiveVoice voice;
                if (active.TryGetValue(missing[i], out voice)) StopVoice(missing[i], voice);
            }

            missing.Clear();
            foreach (var failure in startFailures)
                if (!desired.Contains(failure.Key)) missing.Add(failure.Key);
            for (int i = 0; i < missing.Count; i++) startFailures.Remove(missing[i]);
        }

        public void Reset(int recoveryGeneration)
        {
            if (recoveryGeneration < 0) throw new ArgumentOutOfRangeException("recoveryGeneration");
            if (hasGeneration && recoveryGeneration < generation) return;
            StopAll();
            startFailures.Clear();
            acoustics.ClearSample();
            generation = recoveryGeneration;
            hasGeneration = true;
        }

        private bool CanStartVoice(string sourceId, string soundId, AcousticQueryResult result, out int attempt)
        {
            StartFailure failure;
            if (!startFailures.TryGetValue(sourceId, out failure) || failure.SoundId != soundId || failure.Generation != generation)
            {
                startFailures.Remove(sourceId);
                attempt = 1;
                return true;
            }

            if (result.SampledAt < failure.RetryAfter)
            {
                attempt = 0;
                return false;
            }

            attempt = Math.Min(MaximumBackoffLevel, failure.BackoffLevel + 1);
            return true;
        }

        private static double RetryDelay(int backoffLevel)
        {
            double delay = InitialRetryDelaySeconds;
            for (int i = 1; i < backoffLevel && delay < MaximumRetryDelaySeconds; i++)
                delay = Math.Min(MaximumRetryDelaySeconds, delay * 2.0);
            return delay;
        }

        private void StartVoice(string sourceId, string soundId, AcousticQueryResult result, int attempt)
        {
            string eventId = "street-source:" + sourceId;
            var voice = new ActiveVoice
            {
                EventId = eventId,
                SoundId = soundId,
                Generation = generation,
                Attempt = attempt
            };
            active[sourceId] = voice;
            sourceByEvent[eventId] = sourceId;
            SubscribeReceipts();
            FeedbackPriority priority = acoustics.IsVehicleHazard(sourceId)
                ? FeedbackPriority.CriticalThreat
                : FeedbackPriority.Ambience;
            playback.Play(new AudioRequest(eventId, soundId, result.ObservedSourcePosition, priority,
                generation, sourceId, result.Discernibility, result.Occlusion, true));
        }

        private void RestartVoice(string sourceId, ActiveVoice voice, string soundId, AcousticQueryResult result)
        {
            StopVoice(sourceId, voice);
            StartVoice(sourceId, soundId, result, 1);
        }

        private void StopVoice(string sourceId, ActiveVoice voice)
        {
            ActiveVoice current;
            if (active.TryGetValue(sourceId, out current) && object.ReferenceEquals(current, voice))
                active.Remove(sourceId);
            string mappedSource;
            if (sourceByEvent.TryGetValue(voice.EventId, out mappedSource) && mappedSource == sourceId)
                sourceByEvent.Remove(voice.EventId);
            UnsubscribeIfIdle();
            // Stop can emit Stopped synchronously. Detach the voice before calling the backend.
            playback.Stop(voice.EventId);
        }

        private void StopAll()
        {
            var events = new List<string>(sourceByEvent.Keys);
            active.Clear();
            sourceByEvent.Clear();
            UnsubscribeReceipts();
            for (int i = 0; i < events.Count; i++) playback.Stop(events[i]);
        }

        private void SubscribeReceipts()
        {
            if (listeningForReceipts) return;
            playback.Receipt += OnReceipt;
            listeningForReceipts = true;
        }

        private void OnReceipt(AudioReceipt receipt)
        {
            if (receipt == null || receipt.RecoveryGeneration != generation) return;
            string sourceId;
            if (!sourceByEvent.TryGetValue(receipt.EventId, out sourceId)) return;
            ActiveVoice voice;
            if (!active.TryGetValue(sourceId, out voice) || voice.EventId != receipt.EventId || voice.Generation != receipt.RecoveryGeneration) return;
            if (receipt.Status == AudioStatus.Ready || receipt.Status == AudioStatus.Started)
            {
                if (receipt.Status == AudioStatus.Started) startFailures.Remove(sourceId);
                return;
            }
            if (receipt.Status != AudioStatus.Failed && receipt.Status != AudioStatus.Stopped) return;
            active.Remove(sourceId);
            sourceByEvent.Remove(receipt.EventId);
            if (receipt.Status == AudioStatus.Failed)
            {
                startFailures[sourceId] = new StartFailure
                {
                    SoundId = voice.SoundId,
                    Generation = voice.Generation,
                    BackoffLevel = voice.Attempt,
                    RetryAfter = receipt.SimulationTime + RetryDelay(voice.Attempt)
                };
            }
            UnsubscribeIfIdle();
        }

        private void UnsubscribeIfIdle()
        { if (active.Count == 0) UnsubscribeReceipts(); }

        private void UnsubscribeReceipts()
        {
            if (!listeningForReceipts) return;
            playback.Receipt -= OnReceipt;
            listeningForReceipts = false;
        }
    }
}
