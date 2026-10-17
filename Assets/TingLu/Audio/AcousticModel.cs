using System;
using System.Collections.Generic;
using TingLu.Contracts;

namespace TingLu.Audio
{
    /// <summary>Shared source profile used for both perception and audible playback.</summary>
    public sealed class AcousticProfile
    {
        public readonly string SoundProfileId;
        public readonly float ReferenceDistance;
        public readonly float AudibleDistance;
        public readonly float OcclusionLoss;
        public readonly float Discernibility;
        public readonly string PropagationId;

        public AcousticProfile(string id, float referenceDistance, float audibleDistance,
            float occlusionLoss, float discernibility, string propagationId)
        {
            SoundProfileId = ContractGuard.Id(id);
            if (!IsFinite(referenceDistance) || !IsFinite(audibleDistance) ||
                !IsFinite(occlusionLoss) || !IsFinite(discernibility) || referenceDistance <= 0 || audibleDistance < referenceDistance ||
                occlusionLoss < 0 || occlusionLoss > 1 || discernibility < 0 || discernibility > 1)
                throw new ArgumentOutOfRangeException("acoustic profile");
            ReferenceDistance = referenceDistance;
            AudibleDistance = audibleDistance;
            OcclusionLoss = occlusionLoss;
            Discernibility = discernibility;
            PropagationId = propagationId ?? "direct";
        }

        private static bool IsFinite(float value) { return !float.IsNaN(value) && !float.IsInfinity(value); }
    }

    /// <summary>
    /// Pure query model. It samples only active sound-emitting world objects and returns the
    /// same attenuation/discernibility values consumed by the playback adapter.
    /// </summary>
    public sealed class AcousticModel : IAcousticQuery
    {
        private readonly IWorldQuery world;
        private readonly IDictionary<string, AcousticProfile> profiles;
        private readonly float maxAudibleDistance;

        public AcousticModel(IWorldQuery world, IEnumerable<AcousticProfile> acousticProfiles, float maxDistance)
        {
            this.world = world ?? throw new ArgumentNullException("world");
            if (acousticProfiles == null) throw new ArgumentNullException("acousticProfiles");
            if (!(maxDistance > 0) || float.IsInfinity(maxDistance) || float.IsNaN(maxDistance))
                throw new ArgumentOutOfRangeException("maxDistance");
            maxAudibleDistance = maxDistance;
            profiles = new Dictionary<string, AcousticProfile>(StringComparer.Ordinal);
            foreach (var profile in acousticProfiles)
            {
                if (profile == null) throw new ArgumentException("Null acoustic profile");
                if (profiles.ContainsKey(profile.SoundProfileId)) throw new ArgumentException("Duplicate sound profile: " + profile.SoundProfileId);
                profiles.Add(profile.SoundProfileId, profile);
            }
        }

        public IReadOnlyList<AcousticQueryResult> Sample(Point2 listener, Point2 facing, double time)
        {
            ContractGuard.Time(time);
            float facingLength = (float)Math.Sqrt(facing.X * facing.X + facing.Y * facing.Y);
            if (facingLength < 0.0001f) throw new ArgumentException("Facing must be non-zero", "facing");
            var directionFacing = new Point2(facing.X / facingLength, facing.Y / facingLength);
            var output = new List<AcousticQueryResult>();
            var candidates = world.QueryNearby(listener, maxAudibleDistance);
            for (int i = 0; i < candidates.Count; i++)
            {
                WorldObjectState candidate = candidates[i];
                if (candidate == null || !candidate.SoundActive || string.IsNullOrEmpty(candidate.Definition.SoundProfileId)) continue;
                AcousticProfile profile;
                if (!profiles.TryGetValue(candidate.Definition.SoundProfileId, out profile)) continue;

                float dx = candidate.Position.X - listener.X;
                float dy = candidate.Position.Y - listener.Y;
                float distance = (float)Math.Sqrt(dx * dx + dy * dy);
                if (distance > profile.AudibleDistance) continue;
                Point2 direction = distance < 0.0001f ? new Point2(0, 0) : new Point2(dx / distance, dy / distance);
                bool blocked = world.IsOccluded(listener, candidate.Position);
                float distanceGain = 1f / (1f + Math.Max(0, distance - profile.ReferenceDistance) / profile.ReferenceDistance);
                float occlusion = blocked ? profile.OcclusionLoss : 0f;
                float discernibility = Clamp01(profile.Discernibility * distanceGain * (1f - occlusion));
                string propagation = blocked ? profile.PropagationId + ".occluded" : profile.PropagationId;
                output.Add(new AcousticQueryResult(candidate.Definition.ObjectId,
                    candidate.Definition.ObjectId, true, candidate.Position, direction, distance,
                    discernibility, occlusion, propagation, time, candidate.Definition.Semantic,
                    candidate.Definition.Category, candidate.Definition.MaterialId));
            }
            return output.AsReadOnly();
        }

        public static float PlaybackGain(AcousticQueryResult result)
        {
            if (result == null || !result.Active) return 0f;
            return Clamp01(result.Discernibility);
        }

        private static float Clamp01(float value) { return Math.Max(0, Math.Min(1, value)); }
    }
}
