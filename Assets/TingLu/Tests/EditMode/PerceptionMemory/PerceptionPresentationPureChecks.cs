using System;
using System.Collections.Generic;
using TingLu.Contracts;
using TingLu.Config;
using TingLu.Memory;
using TingLu.Perception;
using TingLu.Presentation;

// Run through PowerShell Add-Type when Unity is unavailable.
public static class PerceptionPresentationPureChecks
{
    public static string[] Run()
    {
        PassiveSoundDoesNotReadWorldIdentity();
        UnknownSoundHasNeutralLocator();
        SemanticStyleAndPaletteAreExplicit();
        MissingBoundaryQueryDoesNotClearOrConfirm();
        NarrowOccluderClipsContourAndCoverage();
        SameIdPositionsRemainSeparateAndClearLocally();
        TactileGuideRequiresCurrentGroundContact();
        DynamicObservationsNeverEnterStaticMemory();
        MemoryIntensityHonorsConfiguredRange();
        AudioOnlyFrameContainsNoVisualInformation();
        return new[] { "PassiveSoundDoesNotReadWorldIdentity", "UnknownSoundHasNeutralLocator",
            "SemanticStyleAndPaletteAreExplicit", "MissingBoundaryQueryDoesNotClearOrConfirm",
            "NarrowOccluderClipsContourAndCoverage", "SameIdPositionsRemainSeparateAndClearLocally",
            "TactileGuideRequiresCurrentGroundContact", "DynamicObservationsNeverEnterStaticMemory",
            "MemoryIntensityHonorsConfiguredRange", "AudioOnlyFrameContainsNoVisualInformation" };
    }

    private static void PassiveSoundDoesNotReadWorldIdentity()
    {
        var world = new FakeWorld();
        var hidden = new ObjectDefinition("walker", ObjectCategory.Dynamic, MemoryPolicy.Never,
            new[] { new Segment2(new Point2(-0.2f, 0), new Point2(0.2f, 0)) }, "person", null);
        world.Items.Add(new WorldObjectState(hidden, new Point2(0, 2), 0, false));
        var acoustics = new FakeAcoustics();
        acoustics.Values.Add(new AcousticQueryResult("source", "walker", true, new Point2(0, 2),
            new Point2(0, 1), 2, 1, 0, "clear", 0, PerceptionSemantic.Unknown, ObjectCategory.Event, null));
        var memory = new StaticMemoryStore();
        var service = new PerceptionService(world, acoustics, new FakePlayer(), new FakeClock(),
            new GameplayConfig(), memory, memory.Observe, memory.Clear, "l1-1");

        service.Recompute();

        Check(service.Current.Observations.Count == 1, "sound observation missing");
        Observation sound = service.Current.Observations[0];
        Check(sound.Kind == InformationKind.SoundLocation && sound.ConfirmedContour.Count == 0,
            "passive sound confirmed geometry");
        Check(sound.Semantic == PerceptionSemantic.Unknown && sound.Category == ObjectCategory.Event && sound.MaterialId == null,
            "sound classification leaked from the world definition");
        Check(!world.Queries.Exists(q => q.Radius == 0.05f),
            "perception looked up an acoustic object by ID");
        Check(memory.Fragments.Count == 0, "passive sound entered static memory");
        service.Dispose();
    }

    private static void SemanticStyleAndPaletteAreExplicit()
    {
        var observation = new Observation("warning", ObjectCategory.Event, MemoryPolicy.Never,
            ObservationSource.CaneEcho, InformationKind.LocalGeometry, new Point2(0, 2),
            new[] { new Segment2(new Point2(-0.2f, 2), new Point2(0.2f, 2)) }, "metal", 0, 1,
            PerceptionSemantic.Threat);
        var composer = new PresentationFrameComposer();
        var perception = new FakePerception(observation);
        var normal = composer.Compose(perception, new FakeMemory(), new FakePlayer(),
            new UserSettings(DisplayMode.Standard, OutputMode.Headphones, 1, true, false), 0.2);
        var assist = composer.Compose(perception, new FakeMemory(), new FakePlayer(),
            new UserSettings(DisplayMode.Standard, OutputMode.Headphones, 1, true, false,
                PaletteMode.ColorBlind, 1.5f, 0.7f), 0.2);
        Check(normal.Marks[0].Shape == VoiceShape.ThreatPulse && assist.Marks[0].Shape == VoiceShape.ThreatPulse,
            "threat shape depended on a guessed category");
        Check(Math.Abs(normal.Marks[0].Color.R - assist.Marks[0].Color.R) > 0.05f,
            "color-blind palette did not change threat color");
        Check(assist.Marks[0].Opacity < normal.Marks[0].Opacity,
            "voiceprint intensity was not applied");
    }

    private static void MissingBoundaryQueryDoesNotClearOrConfirm()
    {
        var world = new FakeWorldWithoutBoundaryQuery();
        var def = Definition("fence", ObjectCategory.Static, MemoryPolicy.ConfirmedStaticGeometry,
            new Segment2(new Point2(-0.5f, 0), new Point2(0.5f, 0)));
        world.Items.Add(new WorldObjectState(def, new Point2(0, 2), 0, false));
        var memory = new StaticMemoryStore();
        memory.Observe(new Observation("old", ObjectCategory.Static, MemoryPolicy.ConfirmedStaticGeometry,
            ObservationSource.CaneEcho, InformationKind.LocalGeometry, new Point2(0, 1),
            new[] { new Segment2(new Point2(0, 1), new Point2(2, 1)) }, "metal", 0, 1));
        var service = new PerceptionService(world, new FakeAcoustics(), new FakePlayer(), new FakeClock(),
            new GameplayConfig(), memory, memory.Observe, memory.Clear, "l1-1");
        string oldFragment = memory.Fragments[0].FragmentId;

        service.Probe(Tap());

        Check(service.LastProbeCoverage != null && service.LastProbeCoverage.Cells.Count == 0,
            "missing exact boundary query fabricated coverage");
        Check(memory.Fragments.Count == 1 && memory.Fragments[0].FragmentId == oldFragment,
            "missing coverage erased old memory");
        Check(service.Current.Observations.Count == 0,
            "missing exact boundary query confirmed contour geometry");
        service.Dispose();
    }

    private static void NarrowOccluderClipsContourAndCoverage()
    {
        var world = new FakeWorld();
        world.Walls.Add(new Segment2(new Point2(0.001f, 1), new Point2(0.002f, 1)));
        world.Items.Add(new WorldObjectState(Definition("fence", ObjectCategory.Static,
            MemoryPolicy.ConfirmedStaticGeometry,
            new Segment2(new Point2(-0.5f, 0), new Point2(0.5f, 0))), new Point2(0, 3), 0, false));
        var memory = new StaticMemoryStore();
        var service = new PerceptionService(world, new FakeAcoustics(), new FakePlayer(), new FakeClock(),
            new GameplayConfig(), memory, memory.Observe, memory.Clear, "l1-1");
        service.Probe(Tap());

        Check(service.LastProbeCoverage.Cells.Count > 0, "clear inner coverage was discarded");
        Check(service.LastProbeCoverage.OcclusionBoundary.Count >= 1, "narrow occluder boundary was omitted");
        float farthestCovered = 0;
        foreach (ProbeCell cell in service.LastProbeCoverage.Cells)
            foreach (Point2 p in cell.Polygon)
            {
                farthestCovered = Math.Max(farthestCovered, Distance(new Point2(0, 0), p));
                Check(!world.IsOccluded(new Point2(0, 0), p), "coverage vertex crosses a narrow occluder");
            }
        Check(farthestCovered > 6.9f,
            "a narrow occluder incorrectly shrank coverage in every open bearing");
        Check(service.Current.Observations.Count == 1, "visible contour pieces were not returned");
        Observation seen = service.Current.Observations[0];
        Check(seen.ConfirmedContour.Count >= 2, "narrow occluder did not split the contour");
        foreach (Segment2 segment in seen.ConfirmedContour)
            Check(!world.IsOccluded(new Point2(0, 0), new Point2((segment.A.X + segment.B.X) * 0.5f,
                (segment.A.Y + segment.B.Y) * 0.5f)), "confirmed contour bridges a narrow occluder");
        service.Dispose();
    }

    private static void SameIdPositionsRemainSeparateAndClearLocally()
    {
        var store = new StaticMemoryStore();
        store.Observe(MemoryObservation("fence", 0, 0));
        store.Observe(MemoryObservation("fence", 10, 1));
        Check(store.Fragments.Count == 2, "same-ID positions were merged into a guessed full object");
        string oldId = store.Fragments[0].FragmentId;
        string newId = store.Fragments[1].FragmentId;
        var empty = new ProbeCoverage("empty", 2, new ProbeCell[0], new Segment2[0]);
        store.Clear(empty);
        Check(store.Fragments.Count == 2 && store.Fragments[0].FragmentId == oldId && store.Fragments[1].FragmentId == newId,
            "empty local update erased remembered geometry");
        var local = new ProbeCoverage("local", 3, new[] { new ProbeCell(new[] {
            new Point2(-1,-1), new Point2(2,-1), new Point2(2,2), new Point2(-1,2) }) }, new Segment2[0]);
        store.Clear(local);
        Check(store.Fragments.Count == 1 && store.Fragments[0].ObjectId == "fence",
            "local verification did not preserve the out-of-area fragment");
        Check(store.Fragments[0].WorldContour[0].A.X >= 9.99f,
            "local verification changed the same-ID fragment at its old position");
    }

    private static Observation MemoryObservation(string id, float x, double at)
    {
        return new Observation(id, ObjectCategory.Static, MemoryPolicy.ConfirmedStaticGeometry,
            ObservationSource.CaneEcho, InformationKind.LocalGeometry, new Point2(x, 0),
            new[] { new Segment2(new Point2(x, 0), new Point2(x + 1, 0)) }, "metal", at, at + 1,
            PerceptionSemantic.Ordinary);
    }

    private static void TactileGuideRequiresCurrentGroundContact()
    {
        var world = new FakeWorld { GroundMaterial = "tactile_path" };
        world.Items.Add(new WorldObjectState(new ObjectDefinition("guide", ObjectCategory.Static,
            MemoryPolicy.ConfirmedStaticGeometry, new[] { new Segment2(new Point2(-5, 0), new Point2(5, 0)) },
            "tactile_path", null, PerceptionSemantic.Guide), new Point2(0, 0), 0, false));
        var player = new FakePlayer();
        var memory = new StaticMemoryStore();
        memory.Observe(new Observation("remembered-guide", ObjectCategory.Static,
            MemoryPolicy.ConfirmedStaticGeometry, ObservationSource.CaneEcho, InformationKind.LocalGeometry,
            new Point2(0, 1), new[] { new Segment2(new Point2(-10, 1), new Point2(10, 1)) },
            "tactile_path", 0, 1, PerceptionSemantic.Guide));
        var service = new PerceptionService(world, new FakeAcoustics(), player, new FakeClock(),
            new GameplayConfig(), memory, memory.Observe, memory.Clear, "l1-1");
        service.Recompute();
        Observation live = FindPath(service.Current);
        Check(live != null && live.ConfirmedContour.Count == 1,
            "current tactile segment was not confirmed under the player's foot");
        Segment2 current = live.ConfirmedContour[0];
        Check(Math.Abs(current.B.X - current.A.X) <= 0.801f,
            "live tactile line exposed more than the current segment");

        var composer = new PresentationFrameComposer();
        var settings = new UserSettings(DisplayMode.Standard, OutputMode.Headphones, 1, true, false);
        PresentationFrame frame = composer.Compose(service, memory, player, settings, 0.01);
        VoiceMark guideMemory = null;
        foreach (VoiceMark mark in frame.Marks) if (mark.Id.StartsWith("memory:")) guideMemory = mark;
        Check(guideMemory != null, "confirmed guide memory was not extended while nearby");
        foreach (Segment2 segment in guideMemory.Segments)
            Check(Distance(player.Current.Position, segment.A) <= 6.001f && Distance(player.Current.Position, segment.B) <= 6.001f,
                "confirmed guide memory extended beyond six metres");

        player.Position = new Point2(0, 0.5f);
        service.Recompute();
        Check(FindPath(service.Current) == null,
            "the tactile path appeared when the footpoint was over 0.4 metres from the segment");
        world.GroundMaterial = "sidewalk";
        player.Position = new Point2(0, 0);
        service.Recompute();
        Check(FindPath(service.Current) == null,
            "the tactile path remained after the player left the tactile surface");
        service.Dispose();
    }

    private static void DynamicObservationsNeverEnterStaticMemory()
    {
        var store = new StaticMemoryStore();
        store.Observe(new Observation("walker", ObjectCategory.Dynamic, MemoryPolicy.Never,
            ObservationSource.CaneEcho, InformationKind.LocalGeometry, new Point2(0, 2),
            new[] { new Segment2(new Point2(-0.2f, 2), new Point2(0.2f, 2)) }, "person", 0, 1,
            PerceptionSemantic.Person));
        Check(store.Fragments.Count == 0, "dynamic contour entered persistent static memory");
    }

    private static void MemoryIntensityHonorsConfiguredRange()
    {
        var store = new StaticMemoryStore();
        store.Observe(MemoryObservation("wall", 0, 0));
        var composer = new PresentationFrameComposer();
        var perception = new FakePerception();
        PresentationFrame normal = composer.Compose(perception, store, new FakePlayer(),
            new UserSettings(DisplayMode.Standard, OutputMode.Headphones, 1, true, false,
                PaletteMode.Default, 1, 1), 0.5);
        PresentationFrame boosted = composer.Compose(perception, store, new FakePlayer(),
            new UserSettings(DisplayMode.Standard, OutputMode.Headphones, 1, true, false,
                PaletteMode.Default, 1, 2), 0.5);
        Check(normal.Marks.Count == 1 && boosted.Marks.Count == 1, "remembered contour was not rendered");
        Check(Math.Abs(boosted.Marks[0].Opacity - normal.Marks[0].Opacity * 2) < 0.001f,
            "memory voiceprint intensity did not honor the configured 0–2 range");
    }

    private static void AudioOnlyFrameContainsNoVisualInformation()
    {
        var composer = new PresentationFrameComposer();
        composer.Accept(new GameplayEvent("hit", 0, "l1-1", "pole", new Point2(0, 0),
            SemanticType.Collision, FeedbackPriority.CriticalThreat,
            new CollisionPayload(10, 0, new Point2(0, 0))));
        var store = new StaticMemoryStore();
        store.Observe(MemoryObservation("wall", 0, 0));
        var perception = new FakePerception(new Observation("bell", ObjectCategory.Event, MemoryPolicy.Never,
            ObservationSource.PassiveSound, InformationKind.SoundLocation, new Point2(0, 2),
            new Segment2[0], null, 0, 1));
        PresentationFrame frame = composer.Compose(perception, store, new FakePlayer(),
            new UserSettings(DisplayMode.AudioOnly, OutputMode.Speakers, 1, false, false), 0.5);
        Check(!frame.Visible && frame.Marks.Count == 0 && frame.ViewSector.Count == 0 && !frame.PainFlash,
            "audio-only mode exposed game state through a visual frame");
    }

    private static Observation FindPath(PerceptionFrame frame)
    {
        foreach (Observation observation in frame.Observations)
            if (observation.Source == ObservationSource.TactilePath) return observation;
        return null;
    }
    private static float Distance(Point2 a,Point2 b)
    { float x=a.X-b.X,y=a.Y-b.Y;return(float)Math.Sqrt(x*x+y*y); }

    private static void UnknownSoundHasNeutralLocator()
    {
        var sound = new Observation("source", ObjectCategory.Event, MemoryPolicy.Never,
            ObservationSource.PassiveSound, InformationKind.SoundLocation, new Point2(0, 2),
            new Segment2[0], null, 0, 1);
        var perception = new FakePerception(sound);
        var composer = new PresentationFrameComposer();
        var settings = new UserSettings(DisplayMode.Standard, OutputMode.Headphones, 1, true, false);
        PresentationFrame frame = composer.Compose(perception, new FakeMemory(), new FakePlayer(), settings, 0.5);

        Check(frame.Marks.Count == 1 && frame.Marks[0].Shape == VoiceShape.LocatorArc,
            "sound was rendered as geometry");
        Tint tint = frame.Marks[0].Color;
        Check(Math.Abs(tint.R - 0.72f) < 0.001f && Math.Abs(tint.G - 0.76f) < 0.001f &&
            Math.Abs(tint.B - 0.80f) < 0.001f,
            "unclassified sound was presented as a threat or object class");
    }

    private static void Check(bool value, string message)
    { if (!value) throw new InvalidOperationException(message); }
    private static GameplayEvent Tap()
    { return new GameplayEvent("tap", 0, "l1-1", null, new Point2(0, 0), SemanticType.CaneTap, FeedbackPriority.Navigation, new CanePayload(7)); }
    private static ObjectDefinition Definition(string id, ObjectCategory category, MemoryPolicy policy, Segment2 contour)
    { return new ObjectDefinition(id, category, policy, new[] { contour }, "metal", null); }

    private sealed class FakeClock : IGameClock { public double Now { get { return 0; } } public bool Paused { get { return false; } } }
    private sealed class FakePlayer : IPlayerStateReader
    {
        public Point2 Position = new Point2(0, 0);
        public PlayerState Current { get { return new PlayerState(PlayerMainState.Active, ActionMode.Walking,
            Position, new Point2(0, 1), 100, 100, 0, 0, 0); } }
    }
    private sealed class FakeAcoustics : IAcousticQuery
    {
        public readonly List<AcousticQueryResult> Values = new List<AcousticQueryResult>();
        public IReadOnlyList<AcousticQueryResult> Sample(Point2 p, Point2 f, double t) { return Values.AsReadOnly(); }
    }
    private class FakeWorldWithoutBoundaryQuery : IWorldQuery
    {
        public readonly List<WorldObjectState> Items = new List<WorldObjectState>();
        public readonly List<Query> Queries = new List<Query>();
        public readonly List<Segment2> Walls = new List<Segment2>();
        public IReadOnlyList<WorldObjectState> QueryNearby(Point2 center, float radius)
        {
            Queries.Add(new Query(center, radius));
            var result = new List<WorldObjectState>();
            foreach (WorldObjectState item in Items)
            {
                float x = item.Position.X - center.X, y = item.Position.Y - center.Y;
                if (x * x + y * y <= radius * radius) result.Add(item);
            }
            return result.AsReadOnly();
        }
        public string GroundMaterial = "sidewalk";
        public string MaterialAt(Point2 p) { return GroundMaterial; }
        public bool IsOccluded(Point2 a, Point2 b)
        { foreach (Segment2 wall in Walls) if (Intersects(a, b, wall.A, wall.B)) return true; return false; }
        protected static bool Intersects(Point2 a, Point2 b, Point2 c, Point2 d)
        {
            float d1 = Cross(a, b, c), d2 = Cross(a, b, d), d3 = Cross(c, d, a), d4 = Cross(c, d, b);
            return d1 * d2 <= 0 && d3 * d4 <= 0;
        }
        private static float Cross(Point2 a, Point2 b, Point2 c)
        { return (b.X - a.X) * (c.Y - a.Y) - (b.Y - a.Y) * (c.X - a.X); }
    }
    private sealed class FakeWorld : FakeWorldWithoutBoundaryQuery, IWorldOcclusionQuery
    {
        public IReadOnlyList<Segment2> QueryOcclusionBoundaries(Point2 center, float radius)
        {
            var result = new List<Segment2>();
            foreach (Segment2 wall in Walls) result.Add(wall);
            foreach (WorldObjectState item in Items)
                foreach (Segment2 local in item.Definition.LocalContour)
                    result.Add(new Segment2(new Point2(item.Position.X + local.A.X, item.Position.Y + local.A.Y),
                        new Point2(item.Position.X + local.B.X, item.Position.Y + local.B.Y)));
            return result.AsReadOnly();
        }
    }
    private sealed class Query
    {
        public readonly Point2 Center; public readonly float Radius;
        public Query(Point2 center, float radius) { Center = center; Radius = radius; }
    }
    private sealed class FakePerception : IPerceptionReader
    {
        public PerceptionFrame Current { get; private set; }
        public FakePerception(params Observation[] values) { Current = new PerceptionFrame(1, 0, values); }
    }
    private sealed class FakeMemory : IMemoryReader
    { public IReadOnlyList<StaticMemorySnapshot> Fragments { get { return new StaticMemorySnapshot[0]; } } }
}
