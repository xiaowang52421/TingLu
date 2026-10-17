using System;
namespace TingLu.Contracts
{
    public abstract class EventPayload { }
    public sealed class CanePayload : EventPayload
    {
        public float Radius { get; private set; }
        public CanePayload(float radius) { Radius = radius; }
    }
    public sealed class FootstepPayload : EventPayload
    {
        public string MaterialId { get; private set; }
        public FootstepPayload(string material) { MaterialId = ContractGuard.Id(material); }
    }
    public sealed class CollisionPayload : EventPayload
    {
        public float Damage { get; private set; }
        public double StunSeconds { get; private set; }
        public Point2 ContactPoint { get; private set; }
        public CollisionPayload(float damage, double stun, Point2 contact) { Damage = damage; StunSeconds = ContractGuard.Time(stun); ContactPoint = contact; }
    }
    public sealed class SemanticPayload : EventPayload
    {
        public string MessageId { get; private set; }
        public string RelatedId { get; private set; }
        public SemanticPayload(string messageId, string relatedId = null) { MessageId = ContractGuard.Id(messageId); RelatedId = relatedId; }
    }
    public sealed class GameplayEvent
    {
        public string EventId { get; private set; }
        public double SimulationTime { get; private set; }
        public string LevelId { get; private set; }
        public string ObjectId { get; private set; }
        public Point2? WorldPosition { get; private set; }
        public SemanticType Type { get; private set; }
        public FeedbackPriority Priority { get; private set; }
        public EventPayload Payload { get; private set; }
        public GameplayEvent(string id, double time, string level, string objectId, Point2? position, SemanticType type, FeedbackPriority priority, EventPayload payload)
        {
            bool valid = type == SemanticType.CaneTap ? payload is CanePayload : type == SemanticType.Footstep ? payload is FootstepPayload : type == SemanticType.Collision ? payload is CollisionPayload : payload is SemanticPayload;
            if (!valid) throw new ArgumentException("Payload does not match semantic type");
            EventId = ContractGuard.Id(id); SimulationTime = ContractGuard.Time(time); LevelId = ContractGuard.Id(level);
            ObjectId = objectId; WorldPosition = position; Type = type; Priority = priority; Payload = payload;
        }
    }
    public sealed class AudioRequest
    {
        public string EventId { get; private set; }
        public string SoundId { get; private set; }
        public Point2? Position { get; private set; }
        public FeedbackPriority Priority { get; private set; }
        public int RecoveryGeneration { get; private set; }
        public string SourceId { get; private set; }
        public float Gain { get; private set; }
        public float Occlusion { get; private set; }
        public bool Loop { get; private set; }
        public AudioRequest(string id, string sound, Point2? position, FeedbackPriority priority, int generation, string sourceId = null, float gain = 1, float occlusion = 0, bool loop = false)
        {
            if (!(gain >= 0 && gain <= 1) || !(occlusion >= 0 && occlusion <= 1)) throw new ArgumentOutOfRangeException("audio gain");
            EventId = ContractGuard.Id(id); SoundId = ContractGuard.Id(sound); Position = position; Priority = priority; RecoveryGeneration = generation; SourceId = sourceId; Gain = gain; Occlusion = occlusion; Loop = loop;
        }
    }
    public sealed class SpeechRequest
    {
        public string EventId { get; private set; }
        public string Text { get; private set; }
        public string SupersessionKey { get; private set; }
        public FeedbackPriority Priority { get; private set; }
        public int RecoveryGeneration { get; private set; }
        public SpeechRequest(string id, string text, string supersessionKey, FeedbackPriority priority, int generation)
        { EventId = ContractGuard.Id(id); Text = ContractGuard.Id(text); SupersessionKey = supersessionKey; Priority = priority; RecoveryGeneration = generation; }
    }
    public sealed class AudioReceipt
    {
        public string EventId { get; private set; }
        public AudioStatus Status { get; private set; }
        public double SimulationTime { get; private set; }
        public int RecoveryGeneration { get; private set; }
        public string Error { get; private set; }
        public AudioReceipt(string id, AudioStatus status, double time, int generation, string error = null)
        { EventId = ContractGuard.Id(id); Status = status; SimulationTime = ContractGuard.Time(time); RecoveryGeneration = generation; Error = error; }
    }
}
