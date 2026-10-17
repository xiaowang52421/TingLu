using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace TingLu.Contracts
{
    public static class ContractVersion { public const string Current = "0.2.0"; }
    public enum PerceptionSemantic { Unknown, Ordinary, Person, Threat, Interaction, Guide }
    public enum PaletteMode { Default, ColorBlind }
    public enum ObjectCategory { Static, Dynamic, Event }
    public enum MemoryPolicy { Never, ConfirmedStaticGeometry }
    public enum InformationKind { SoundLocation, LocalGeometry, Tactile }
    public enum ObservationSource { PassiveSound, CaneEcho, CaneContact, BodyContact, TactilePath }
    public enum PlayerMainState { Active, Stunned, Failing, Finished }
    public enum ActionMode { Walking, Listening }
    public enum DisplayMode { Standard, NoUI, AudioOnly }
    public enum OutputMode { Headphones, Speakers }
    public enum FeedbackPriority { Ambience = 0, Interface = 10, Navigation = 20, CriticalThreat = 30 }
    public enum AudioStatus { Requested, Ready, Started, Failed, Stopped }
    public enum SemanticType { Footstep, CaneTap, Collision, Warning, Checkpoint, Collection, Goal, Failure, CooldownReady, MenuFocus, SaveError }
    public enum MenuAction { None, Next, Previous, Confirm, Back, QueryGoal, Decrease, Increase }

    public struct Point2
    {
        public readonly float X, Y;
        public Point2(float x, float y)
        {
            if (float.IsNaN(x) || float.IsInfinity(x) || float.IsNaN(y) || float.IsInfinity(y)) throw new ArgumentException("Non-finite point");
            X = x; Y = y;
        }
    }
    public struct Segment2
    {
        public readonly Point2 A, B;
        public Segment2(Point2 a, Point2 b) { A = a; B = b; }
    }
    public static class ContractGuard
    {
        public static string Id(string id)
        {
            if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("Stable ID required");
            return id;
        }
        public static double Time(double value)
        {
            if (value < 0 || double.IsNaN(value) || double.IsInfinity(value)) throw new ArgumentOutOfRangeException("time");
            return value;
        }
        public static ReadOnlyCollection<T> Copy<T>(IEnumerable<T> values)
        {
            if (values == null) throw new ArgumentNullException("values");
            return new List<T>(values).AsReadOnly();
        }
    }
    // Definition contours are local; observed and remembered contours are world-space value copies.
    public sealed class ObjectDefinition
    {
        public string ObjectId { get; private set; }
        public ObjectCategory Category { get; private set; }
        public MemoryPolicy MemoryPolicy { get; private set; }
        public ReadOnlyCollection<Segment2> LocalContour { get; private set; }
        public string MaterialId { get; private set; }
        public string SoundProfileId { get; private set; } // null means silent
        public PerceptionSemantic Semantic { get; private set; }
        public ObjectDefinition(string id, ObjectCategory category, MemoryPolicy memory, IEnumerable<Segment2> contour, string material, string sound, PerceptionSemantic semantic = PerceptionSemantic.Ordinary)
        {
            if (category != ObjectCategory.Static && memory != MemoryPolicy.Never) throw new ArgumentException("Only static definitions may be remembered");
            ObjectId = ContractGuard.Id(id); Category = category; MemoryPolicy = memory;
            LocalContour = ContractGuard.Copy(contour); MaterialId = ContractGuard.Id(material); SoundProfileId = sound;
            Semantic = semantic;
        }
    }
    public sealed class WorldObjectState
    {
        public ObjectDefinition Definition { get; private set; }
        public Point2 Position { get; private set; }
        public float RotationDegrees { get; private set; }
        public bool SoundActive { get; private set; }
        public WorldObjectState(ObjectDefinition definition, Point2 position, float rotation, bool soundActive)
        { Definition = definition ?? throw new ArgumentNullException("definition"); Position = position; RotationDegrees = rotation; SoundActive = soundActive; }
    }
    public sealed class AcousticQueryResult
    {
        public string SourceId { get; private set; }
        public string ObjectId { get; private set; }
        public bool Active { get; private set; }
        public Point2 ObservedSourcePosition { get; private set; }
        public Point2 Direction { get; private set; }
        public float Distance { get; private set; }
        public float Discernibility { get; private set; }
        public float Occlusion { get; private set; }
        public string PropagationId { get; private set; }
        public double SampledAt { get; private set; }
        public PerceptionSemantic Semantic { get; private set; }
        public ObjectCategory Category { get; private set; }
        public string MaterialId { get; private set; }
        public AcousticQueryResult(string source, string objectId, bool active, Point2 position, Point2 direction, float distance, float discernibility, float occlusion, string propagation, double time, PerceptionSemantic semantic = PerceptionSemantic.Unknown, ObjectCategory category = ObjectCategory.Event, string materialId = null)
        {
            if (distance < 0 || float.IsNaN(distance) || float.IsInfinity(distance) || !(discernibility >= 0 && discernibility <= 1) || !(occlusion >= 0 && occlusion <= 1)) throw new ArgumentOutOfRangeException("acoustics");
            SourceId = ContractGuard.Id(source); ObjectId = objectId; Active = active; ObservedSourcePosition = position; Direction = direction;
            Distance = distance; Discernibility = discernibility; Occlusion = occlusion; PropagationId = propagation; SampledAt = ContractGuard.Time(time);
            Semantic = semantic; Category = category; MaterialId = materialId;
        }
    }
    public sealed class Observation
    {
        public string ObjectId { get; private set; }
        public ObjectCategory Category { get; private set; }
        public MemoryPolicy MemoryPolicy { get; private set; }
        public ObservationSource Source { get; private set; }
        public InformationKind Kind { get; private set; }
        public Point2 ObservedPosition { get; private set; }
        public ReadOnlyCollection<Segment2> ConfirmedContour { get; private set; }
        public string MaterialId { get; private set; }
        public double ObservedAt { get; private set; }
        public double ExpiresAt { get; private set; }
        public PerceptionSemantic Semantic { get; private set; }
        public Observation(string id, ObjectCategory category, MemoryPolicy memory, ObservationSource source, InformationKind kind, Point2 position, IEnumerable<Segment2> contour, string material, double at, double expires, PerceptionSemantic semantic = PerceptionSemantic.Unknown)
        {
            ObjectId = ContractGuard.Id(id); Category = category; MemoryPolicy = memory; Source = source; Kind = kind; ObservedPosition = position;
            ConfirmedContour = ContractGuard.Copy(contour); MaterialId = material; ObservedAt = ContractGuard.Time(at); ExpiresAt = ContractGuard.Time(expires);
            Semantic = semantic;
            if (expires < at) throw new ArgumentException("Expiry before observation");
            if (kind == InformationKind.SoundLocation && ConfirmedContour.Count != 0) throw new ArgumentException("Sound location cannot confirm geometry");
            if (source == ObservationSource.PassiveSound && kind != InformationKind.SoundLocation) throw new ArgumentException("Passive sound is location only");
            if (category != ObjectCategory.Static && memory != MemoryPolicy.Never) throw new ArgumentException("Dynamic/event memory prohibited");
        }
    }
    public sealed class PerceptionFrame
    {
        public long FrameId { get; private set; }
        public double SampledAt { get; private set; }
        public ReadOnlyCollection<Observation> Observations { get; private set; }
        public PerceptionFrame(long id, double time, IEnumerable<Observation> observations)
        { FrameId = id; SampledAt = ContractGuard.Time(time); Observations = ContractGuard.Copy(observations); }
    }
    // Coverage is a union of actually probed convex cells, clipped against occluders by 02.
    public sealed class ProbeCell
    {
        public ReadOnlyCollection<Point2> Polygon { get; private set; }
        public ProbeCell(IEnumerable<Point2> polygon)
        { Polygon = ContractGuard.Copy(polygon); if (Polygon.Count < 3) throw new ArgumentException("Polygon needs 3 vertices"); }
    }
    public sealed class ProbeCoverage
    {
        public string ProbeId { get; private set; }
        public double SampledAt { get; private set; }
        public ReadOnlyCollection<ProbeCell> Cells { get; private set; }
        public ReadOnlyCollection<Segment2> OcclusionBoundary { get; private set; }
        public ProbeCoverage(string id, double time, IEnumerable<ProbeCell> cells, IEnumerable<Segment2> boundary)
        { ProbeId = ContractGuard.Id(id); SampledAt = ContractGuard.Time(time); Cells = ContractGuard.Copy(cells); OcclusionBoundary = ContractGuard.Copy(boundary); }
    }
    public sealed class StaticMemorySnapshot
    {
        public string ObjectId { get; private set; }
        public string FragmentId { get; private set; }
        public ReadOnlyCollection<Segment2> WorldContour { get; private set; }
        public double ObservedAt { get; private set; }
        public ObservationSource Provenance { get; private set; }
        public PerceptionSemantic Semantic { get; private set; }
        public StaticMemorySnapshot(string fragmentId, Observation observation)
        {
            if (observation == null || observation.Category != ObjectCategory.Static || observation.MemoryPolicy != MemoryPolicy.ConfirmedStaticGeometry || observation.Kind == InformationKind.SoundLocation || observation.ConfirmedContour.Count == 0)
                throw new ArgumentException("Memory requires confirmed static geometry");
            FragmentId = ContractGuard.Id(fragmentId); ObjectId = observation.ObjectId; WorldContour = ContractGuard.Copy(observation.ConfirmedContour);
            ObservedAt = observation.ObservedAt; Provenance = observation.Source;
            Semantic = observation.Semantic;
        }
    }
    public sealed class PlayerState
    {
        public PlayerMainState MainState { get; private set; }
        public ActionMode Mode { get; private set; }
        public Point2 Position { get; private set; }
        public Point2 Facing { get; private set; }
        public float Courage { get; private set; }
        public float Focus { get; private set; }
        public double CaneReadyAt { get; private set; }
        public double StunnedUntil { get; private set; }
        public double ConfusedUntil { get; private set; }
        public PlayerState(PlayerMainState state, ActionMode mode, Point2 position, Point2 facing, float courage, float focus, double ready, double stunned, double confused)
        {
            if (!(courage >= 0 && courage <= 100) || !(focus >= 0 && focus <= 100)) throw new ArgumentOutOfRangeException("stats");
            float length = facing.X * facing.X + facing.Y * facing.Y;
            if (Math.Abs(length - 1) > 0.001f) throw new ArgumentException("Facing must be normalized");
            MainState = state; Mode = mode; Position = position; Facing = facing; Courage = courage; Focus = focus;
            CaneReadyAt = ContractGuard.Time(ready); StunnedUntil = ContractGuard.Time(stunned); ConfusedUntil = ContractGuard.Time(confused);
        }
    }
    public struct InputFrame
    {
        public readonly Point2 Move;
        public readonly bool ListenHeld, TapPressed, PausePressed;
        public readonly MenuAction Menu;
        public InputFrame(Point2 move, bool listen, bool tap, bool pause, MenuAction menu)
        { Move = move; ListenHeld = listen; TapPressed = tap; PausePressed = pause; Menu = menu; }
    }
    public sealed class AttemptStats
    {
        public string AttemptId { get; private set; }
        public int Collisions { get; private set; }
        public ReadOnlyCollection<string> CollectedIds { get; private set; }
        public AttemptStats(string id, int collisions, IEnumerable<string> collected)
        { if (collisions < 0) throw new ArgumentOutOfRangeException("collisions"); AttemptId = ContractGuard.Id(id); Collisions = collisions; CollectedIds = ContractGuard.Copy(new HashSet<string>(collected)); }
    }
    public sealed class WorldEventSnapshot
    {
        public string ObjectId { get; private set; }
        public string StateId { get; private set; }
        public double RemainingSeconds { get; private set; }
        public WorldEventSnapshot(string id, string state, double remaining)
        { ObjectId = ContractGuard.Id(id); StateId = ContractGuard.Id(state); RemainingSeconds = ContractGuard.Time(remaining); }
    }
    public sealed class CheckpointSnapshot
    {
        public const int CurrentSchemaVersion = 1;
        public int SchemaVersion { get { return CurrentSchemaVersion; } }
        public string LevelId { get; private set; }
        public string CheckpointId { get; private set; }
        public double CapturedAt { get; private set; }
        public PlayerState Player { get; private set; }
        public ReadOnlyCollection<StaticMemorySnapshot> Memory { get; private set; }
        public ReadOnlyCollection<WorldEventSnapshot> Events { get; private set; }
        public int RandomSeed { get; private set; }
        public CheckpointSnapshot(string level, string checkpoint, double time, PlayerState player, IEnumerable<StaticMemorySnapshot> memory, IEnumerable<WorldEventSnapshot> events, int seed)
        { LevelId = ContractGuard.Id(level); CheckpointId = ContractGuard.Id(checkpoint); CapturedAt = ContractGuard.Time(time); Player = player ?? throw new ArgumentNullException("player"); Memory = ContractGuard.Copy(memory); Events = ContractGuard.Copy(events); RandomSeed = seed; }
    }
    public sealed class PlayerProgress
    {
        public ReadOnlyCollection<string> CompletedLevels { get; private set; }
        public ReadOnlyDictionary<string, int> BestStars { get; private set; }
        public ReadOnlyCollection<string> CollectedIds { get; private set; }
        public ReadOnlyCollection<string> Unlocks { get; private set; }
        public PlayerProgress(IEnumerable<string> levels, IDictionary<string, int> stars, IEnumerable<string> collected, IEnumerable<string> unlocks)
        {
            CompletedLevels = ContractGuard.Copy(new HashSet<string>(levels)); CollectedIds = ContractGuard.Copy(new HashSet<string>(collected)); Unlocks = ContractGuard.Copy(new HashSet<string>(unlocks));
            var copiedStars = new Dictionary<string, int>(stars);
            foreach (var value in copiedStars.Values) if (value < 1 || value > 3) throw new ArgumentOutOfRangeException("stars");
            BestStars = new ReadOnlyDictionary<string, int>(copiedStars);
        }
    }
    public sealed class UserSettings
    {
        public DisplayMode Display { get; private set; }
        public OutputMode Output { get; private set; }
        public float MasterVolume { get; private set; }
        public bool Subtitles { get; private set; }
        public bool Haptics { get; private set; }
        public PaletteMode Palette { get; private set; }
        public float FontScale { get; private set; }
        public float VoiceprintIntensity { get; private set; }
        public UserSettings(DisplayMode display, OutputMode output, float volume, bool subtitles, bool haptics, PaletteMode palette = PaletteMode.Default, float fontScale = 1, float voiceprintIntensity = 1)
        {
            if (!(volume >= 0 && volume <= 1) || !(fontScale >= 0.75f && fontScale <= 2) || !(voiceprintIntensity >= 0 && voiceprintIntensity <= 2)) throw new ArgumentOutOfRangeException("settings");
            Display = display; Output = output; MasterVolume = volume; Subtitles = subtitles; Haptics = haptics; Palette = palette; FontScale = fontScale; VoiceprintIntensity = voiceprintIntensity;
        }
    }
}
