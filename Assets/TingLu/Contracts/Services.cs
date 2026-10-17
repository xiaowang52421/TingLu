using System;
using System.Collections.Generic;
namespace TingLu.Contracts
{
    public interface IGameClock { double Now { get; } bool Paused { get; } }
    public interface IEventBus { IDisposable Subscribe(Action<GameplayEvent> consumer); void Publish(GameplayEvent value); }
    public interface IInputSource { InputFrame Read(); }
    public interface IWorldQuery
    {
        IReadOnlyList<WorldObjectState> QueryNearby(Point2 center, float radius);
        string MaterialAt(Point2 position);
        bool IsOccluded(Point2 from, Point2 to);
    }
    public interface IAcousticQuery { IReadOnlyList<AcousticQueryResult> Sample(Point2 listener, Point2 facing, double time); }
    public interface IWorldOcclusionQuery { IReadOnlyList<Segment2> QueryOcclusionBoundaries(Point2 center, float radius); }
    public sealed class MovementResult
    {
        public Point2 Position { get; private set; }
        public string ContactObjectId { get; private set; }
        public Point2 ContactPoint { get; private set; }
        public MovementResult(Point2 position, string contactId = null, Point2 contactPoint = default(Point2)) { Position = position; ContactObjectId = contactId; ContactPoint = contactPoint; }
    }
    public interface IWorldMovement { MovementResult ResolveMovement(Point2 from, Point2 requested, float bodyRadius); }
    public interface IPerceptionReader { PerceptionFrame Current { get; } }
    public interface IMemoryReader { IReadOnlyList<StaticMemorySnapshot> Fragments { get; } }
    public interface IMemoryCheckpoint : IMemoryReader
    {
        IReadOnlyList<StaticMemorySnapshot> Export();
        void Restore(IReadOnlyList<StaticMemorySnapshot> fragments);
    }
    public interface IPlayerStateReader { PlayerState Current { get; } }
    public interface IPlayerController : IPlayerStateReader { void Tick(InputFrame input, double delta); void Restore(PlayerState state, double capturedAt, double now); }
    public interface IWorldLifecycle : IDisposable
    {
        IReadOnlyList<WorldEventSnapshot> CaptureEvents();
        void Restore(IReadOnlyList<WorldEventSnapshot> events, int randomSeed);
    }
    public interface IPerceptionService : IPerceptionReader
    {
        void Recompute();
        void ClearTransient();
        void Probe(GameplayEvent successfulTap);
        void Contact(GameplayEvent collision);
    }
    public interface IAudioPlayback : IDisposable
    {
        event Action<AudioReceipt> Receipt;
        void Play(AudioRequest request);
        void Stop(string eventId);
        void UpdateListener(Point2 position, Point2 facing);
        void Reset(int recoveryGeneration);
        void SetPaused(bool paused);
    }
    public interface ISemanticFeedback { void Submit(GameplayEvent knownInformation); void ReplayGoal(); }
    public interface IAudioSpeech
    {
        void Speak(SpeechRequest request);
        void StopSpeech(string eventId);
    }
    public interface ICheckpointService
    {
        CheckpointSnapshot Latest { get; }
        void Activate(string checkpointId);
        void RestoreLatest();
    }
    public enum SaveStatus { Success, Missing, Corrupt, FutureVersion, IoFailure }
    public sealed class ResumeData
    {
        public CheckpointSnapshot Checkpoint { get; private set; }
        public AttemptStats Attempt { get; private set; }
        public IReadOnlyList<string> FiredBeatIds { get; private set; }
        public IReadOnlyList<string> ActivatedCheckpointIds { get; private set; }
        public bool Finished { get; private set; }
        public ResumeData(CheckpointSnapshot checkpoint, AttemptStats attempt, IEnumerable<string> firedBeatIds = null, IEnumerable<string> activatedCheckpointIds = null, bool finished = false)
        { Checkpoint = checkpoint; Attempt = attempt; FiredBeatIds = ContractGuard.Copy(firedBeatIds ?? new string[0]); ActivatedCheckpointIds = ContractGuard.Copy(activatedCheckpointIds ?? new string[0]); Finished = finished; }
    }
    public sealed class SaveResult<T>
    {
        public SaveStatus Status { get; private set; }
        public T Value { get; private set; }
        public bool FromBackup { get; private set; }
        public string Error { get; private set; }
        public SaveResult(SaveStatus status, T value, bool backup = false, string error = null) { Status = status; Value = value; FromBackup = backup; Error = error; }
    }
    public interface ISaveService
    {
        SaveResult<ResumeData> LoadResume();
        SaveStatus SaveResume(ResumeData data);
        SaveResult<PlayerProgress> LoadProgress();
        SaveStatus SaveProgress(PlayerProgress data);
        SaveResult<UserSettings> LoadSettings();
        SaveStatus SaveSettings(UserSettings data);
    }
}
