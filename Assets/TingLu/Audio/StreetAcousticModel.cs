using System;
using System.Collections.Generic;
using TingLu.Config;
using TingLu.Contracts;

namespace TingLu.Audio
{
    /// <summary>Produces the one frozen acoustic sample shared by playback and perception.</summary>
    public sealed class StreetAcousticModel : IAcousticQuery
    {
        private const double SampleTimeTolerance = 0.0001;
        private const float RearClarityFloor = 0.72f;
        private const float LowFocusClarityFloor = 0.45f;
        private readonly Func<StreetWorldFrame> capture;
        private readonly IPlayerStateReader player;
        private readonly StreetConfig config;
        private Dictionary<string, string> sampledSounds = new Dictionary<string, string>(StringComparer.Ordinal);
        private HashSet<string> sampledVehicles = new HashSet<string>(StringComparer.Ordinal);
        private static readonly HashSet<string> CatalogSounds = CreateCatalogSounds();

        public StreetAcousticModel(Func<StreetWorldFrame> capture, IPlayerStateReader player, StreetConfig config)
        {
            this.capture = capture ?? throw new ArgumentNullException("capture");
            this.player = player ?? throw new ArgumentNullException("player");
            this.config = config ?? throw new ArgumentNullException("config");
            config.Validate();
        }

        public IReadOnlyList<AcousticQueryResult> Sample(Point2 listener, Point2 facing, double time)
        {
            ContractGuard.Time(time);
            Point2 unitFacing = StreetGeometry.Unit(facing);
            StreetWorldFrame frame = capture();
            if (frame == null) throw new InvalidOperationException("Street acoustic capture returned null.");
            if (Math.Abs(frame.Time - time) > SampleTimeTolerance)
                throw new InvalidOperationException("Acoustic sample time must match its world snapshot.");
            PlayerState state = player.Current;
            if (state == null) throw new InvalidOperationException("Street acoustic sampling requires a player state.");

            var results = new List<AcousticQueryResult>();
            var sounds = new Dictionary<string, string>(StringComparer.Ordinal);
            var vehicles = new HashSet<string>(StringComparer.Ordinal);
            float radius = config.AcousticRadius;
            float referenceDistance = Math.Max(1f, radius * 0.18f);
            float focusScale = FocusScale(state.Focus);
            float confusionScale = time < state.ConfusedUntil ? config.Gameplay.confusionMultiplier : 1f;

            for (int i = 0; i < frame.Objects.Count; i++)
            {
                StreetObject source = frame.Objects[i];
                if (!source.SoundActive || string.IsNullOrWhiteSpace(source.SoundId) || !CatalogSounds.Contains(source.SoundId))
                    continue;

                float dx = source.Position.X - listener.X;
                float dy = source.Position.Y - listener.Y;
                float distance = (float)Math.Sqrt(dx * dx + dy * dy);
                if (distance > radius) continue;

                Point2 direction = distance < StreetGeometry.Epsilon
                    ? new Point2(0, 0)
                    : new Point2(dx / distance, dy / distance);
                float occlusion = MeasureOcclusion(frame.Objects, source.Id, listener, direction, distance);
                float distanceScale = 1f / (1f + distance / referenceDistance);
                float directionalScale = distance < StreetGeometry.Epsilon
                    ? 1f
                    : RearClarityFloor + (1f - RearClarityFloor) * ((Dot(unitFacing, direction) + 1f) * 0.5f);
                float discernibility = Clamp01(distanceScale * directionalScale * focusScale * confusionScale * (1f - occlusion));
                if (discernibility <= 0f) continue;

                sounds[source.Id] = source.SoundId;
                if (source.VehicleHazard) vehicles.Add(source.Id);
                results.Add(new AcousticQueryResult(source.Id, source.Id, true, source.Position, direction,
                    distance, discernibility, occlusion, occlusion <= 0f ? "street.direct" : "street.loss",
                    frame.Time, source.Semantic, ObjectCategory.Event, null));
            }

            sampledSounds = sounds;
            sampledVehicles = vehicles;
            return results.AsReadOnly();
        }

        /// <summary>Resolves playback IDs only for sources in the most recent acoustic sample.</summary>
        public string SoundFor(string sourceId)
        {
            string sound;
            return sourceId != null && sampledSounds.TryGetValue(sourceId, out sound) ? sound : null;
        }

        /// <summary>Vehicle hazard priority remains separate from the source's perception category.</summary>
        public bool IsVehicleHazard(string sourceId)
        { return sourceId != null && sampledVehicles.Contains(sourceId); }

        internal void ClearSample()
        {
            sampledSounds.Clear();
            sampledVehicles.Clear();
        }

        private float FocusScale(float focus)
        {
            float threshold = config.Gameplay.lowFocusThreshold;
            if (threshold <= 0f || focus >= threshold) return 1f;
            return LowFocusClarityFloor + (1f - LowFocusClarityFloor) * Clamp01(focus / threshold);
        }

        private static float MeasureOcclusion(IReadOnlyList<StreetObject> objects, string sourceId,
            Point2 listener, Point2 direction, float sourceDistance)
        {
            if (sourceDistance < StreetGeometry.Epsilon) return 0f;
            float transmission = 1f;
            for (int i = 0; i < objects.Count; i++)
            {
                StreetObject blocker = objects[i];
                float loss = blocker.AcousticLoss;
                if (blocker.Id == sourceId || loss <= 0f || blocker.Polygon.Count < 3)
                    continue;

                bool crossedBeforeSource = false;
                for (int edge = 0; edge < blocker.Polygon.Count; edge++)
                {
                    var boundary = new Segment2(blocker.Polygon[edge], blocker.Polygon[(edge + 1) % blocker.Polygon.Count]);
                    float hitDistance;
                    if (StreetGeometry.RaySegment(listener, direction, boundary, out hitDistance) &&
                        hitDistance > StreetGeometry.Epsilon && hitDistance < sourceDistance - StreetGeometry.Epsilon)
                    {
                        crossedBeforeSource = true;
                        break;
                    }
                }
                if (crossedBeforeSource)
                    transmission *= 1f - Clamp01(loss);
            }
            return Clamp01(1f - transmission);
        }

        private static float Dot(Point2 a, Point2 b) { return a.X * b.X + a.Y * b.Y; }
        private static float Clamp01(float value) { return Math.Max(0f, Math.Min(1f, value)); }

        private static HashSet<string> CreateCatalogSounds()
        { return new HashSet<string>(StreetCatalog.Sounds, StringComparer.Ordinal); }
    }
}
