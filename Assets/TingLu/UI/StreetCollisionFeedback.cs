using System;
using TingLu.Config;
using UnityEngine;

namespace TingLu.UI
{
    // Presentation time never rewinds with traffic/checkpoints; the session advances it only in play.
    public sealed class StreetCollisionFeedback
    {
        private readonly StreetCollisionFeedbackConfig config;
        private double time, started = double.NegativeInfinity, lastTrigger = double.NegativeInfinity;
        public int TriggerCount { get; private set; }
        public StreetCollisionFeedback(StreetCollisionFeedbackConfig configuration)
        { config = configuration ?? throw new ArgumentNullException("configuration"); }
        public double Age { get { return time - started; } }
        public float Alpha
        {
            get
            {
                double age = Age;
                if (age < 0 || age >= config.Duration) return 0;
                double flashDuration = Math.Min(.18, config.Duration * .35);
                if (age < flashDuration)
                {
                    float progress = Mathf.Clamp01((float)(age / flashDuration));
                    float attackDuration = (float)Math.Min(.025, flashDuration);
                    float attack = Mathf.Lerp(.65f, 1f, Mathf.Clamp01((float)age / attackDuration));
                    float phase = progress * Mathf.PI * 4f;
                    float flicker = .65f + .35f * Mathf.Pow(Mathf.Cos(phase * .5f), 2);
                    float envelope = Mathf.Lerp(1f, .72f, progress);
                    float flash = flicker * envelope;
                    return config.Intensity * attack * flash;
                }

                float fadeProgress = Mathf.Clamp01((float)((age - flashDuration) /
                    Math.Max(1e-6, config.Duration - flashDuration)));
                return config.Intensity * .72f * (1 - Mathf.SmoothStep(0, 1, fadeProgress));
            }
        }
        public void Advance(double delta)
        { if (delta < 0 || double.IsNaN(delta) || double.IsInfinity(delta)) throw new ArgumentOutOfRangeException("delta"); time += delta; }
        public bool Trigger()
        {
            // A live pulse keeps its original end time. Later contacts merge into it instead
            // of restarting the flash, while the repeat interval throttles short re-entries.
            if (Age < config.Duration) return false;
            if (time - lastTrigger + 1e-7 < config.RepeatInterval) return false;
            lastTrigger = started = time; TriggerCount++; return true;
        }
        public void Clear() { time=0; started=lastTrigger=double.NegativeInfinity; TriggerCount=0; }

        public static float EdgeAlpha(float distanceFromEdge, float edgeWidth)
        {
            if (edgeWidth <= 0 || distanceFromEdge >= edgeWidth) return 0;
            if (distanceFromEdge <= 0) return 1;
            float t = Mathf.Clamp01(distanceFromEdge / edgeWidth);
            return 1 - t * t * (3 - 2 * t);
        }

        public static float ScreenEdgeAlpha(float screenX, float screenY,
            int screenWidth, int screenHeight, float edgeWidthRatio)
        {
            if (screenWidth <= 0 || screenHeight <= 0 || edgeWidthRatio <= 0) return 0;
            float edgeWidth = Mathf.Min(screenWidth, screenHeight) * edgeWidthRatio;
            float horizontalDistance = Mathf.Min(screenX, screenWidth - screenX);
            float verticalDistance = Mathf.Min(screenY, screenHeight - screenY);
            return EdgeAlpha(Mathf.Min(horizontalDistance, verticalDistance), edgeWidth);
        }
    }
}
