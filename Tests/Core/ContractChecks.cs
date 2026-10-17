using System;
using System.Collections.Generic;
using System.Linq;
using TingLu.Contracts;
using TingLu.Bootstrap;
using TingLu.Config;

public static class ContractChecks
{
    private static void Assert(bool value, string detail) { if (!value) throw new Exception(detail); }
    private static void Reject(Action action) { bool rejected = false; try { action(); } catch (ArgumentException) { rejected = true; } Assert(rejected, "Expected rejection"); }
    private static GameplayEvent Event(string id)
    { return new GameplayEvent(id, 0, "l1-1", null, null, SemanticType.Goal, FeedbackPriority.Interface, new SemanticPayload("goal")); }
    public static string[] Run()
    {
        var passed = new List<string>();
        var clock = new SimulationClock();
        clock.Advance(0.5); clock.SetPaused(true); clock.Advance(99); Assert(clock.Now == 0.5, "Paused clock advanced");
        clock.SetPaused(false); clock.Advance(0.5); Assert(clock.Now == 1, "Resume time wrong"); Reject(() => clock.Advance(double.NaN));
        passed.Add("C01 Pause freezes simulation and invalid delta rejected");
        bool resetRejected=false;try{clock.Reset(0);}catch(InvalidOperationException){resetRejected=true;}
        Assert(resetRejected,"Timeline reset must require frozen simulation");
        clock.SetPaused(true);clock.Reset(12.5);clock.Advance(4);Assert(clock.Now==12.5,"Restored checkpoint timeline advanced while frozen");
        Reject(()=>clock.Reset(double.PositiveInfinity));clock.SetPaused(false);clock.Advance(.5);Assert(clock.Now==13,"Restored timeline resumes from saved checkpoint");
        passed.Add("C11 Timeline restore requires pause and preserves checkpoint phase");

        var contour = new List<Segment2> { new Segment2(new Point2(0, 0), new Point2(1, 0)) };
        var observation = new Observation("pole", ObjectCategory.Static, MemoryPolicy.ConfirmedStaticGeometry, ObservationSource.CaneEcho, InformationKind.LocalGeometry, new Point2(0, 0), contour, "metal", 1, 1.8);
        var memory = new StaticMemorySnapshot("fragment", observation);
        contour[0] = new Segment2(new Point2(9, 9), new Point2(10, 9));
        Assert(memory.WorldContour[0].A.X == 0 && observation.ConfirmedContour[0].A.X == 0, "Memory aliases world geometry");
        bool immutable = false; try { ((IList<Segment2>)memory.WorldContour)[0] = contour[0]; } catch (NotSupportedException) { immutable = true; }
        Assert(immutable, "Reader can mutate memory");
        passed.Add("C02 Geometry and memory copies do not alias mutable input");

        Reject(() => new Observation("pole", ObjectCategory.Static, MemoryPolicy.ConfirmedStaticGeometry, ObservationSource.PassiveSound, InformationKind.LocalGeometry, new Point2(), contour, "metal", 0, 1));
        Reject(() => new Observation("pole", ObjectCategory.Static, MemoryPolicy.ConfirmedStaticGeometry, ObservationSource.PassiveSound, InformationKind.SoundLocation, new Point2(), contour, "metal", 0, 1));
        var soundOnly = new Observation("pole", ObjectCategory.Static, MemoryPolicy.ConfirmedStaticGeometry, ObservationSource.PassiveSound, InformationKind.SoundLocation, new Point2(), new Segment2[0], null, 0, 1);
        Reject(() => new StaticMemorySnapshot("fragment", soundOnly));
        passed.Add("C03 Passive location cannot upgrade to remembered geometry");

        Reject(() => new ObjectDefinition("bike", ObjectCategory.Dynamic, MemoryPolicy.ConfirmedStaticGeometry, contour, "metal", null));
        var dynamicObservation = new Observation("bike", ObjectCategory.Dynamic, MemoryPolicy.Never, ObservationSource.BodyContact, InformationKind.LocalGeometry, new Point2(), contour, "metal", 0, 1);
        Reject(() => new StaticMemorySnapshot("bike-fragment", dynamicObservation));
        passed.Add("C04 Dynamic contact cannot create permanent memory");

        var trace = new List<string>(); var bus = new EventBus();
        var first = bus.Subscribe(e => { trace.Add("A" + e.EventId); if (e.EventId == "1") bus.Publish(Event("2")); });
        var second = bus.Subscribe(e => trace.Add("B" + e.EventId));
        bus.Publish(Event("1")); Assert(string.Join(",", trace) == "A1,B1,A2,B2", "Reentrant event changed FIFO");
        first.Dispose(); first.Dispose(); trace.Clear(); bus.Publish(Event("3")); Assert(string.Join(",", trace) == "B3", "Subscription not released");
        second.Dispose(); bus.Dispose();
        passed.Add("C05 Reentrant FIFO and idempotent subscription cleanup");

        var errorBus = new EventBus(); bool downstream = false;
        errorBus.Subscribe(e => { throw new InvalidOperationException("test consumer"); }); errorBus.Subscribe(e => downstream = true);
        bool reported = false; try { errorBus.Publish(Event("error")); } catch (AggregateException) { reported = true; }
        Assert(downstream && reported, "Consumer exception swallowed or prevented downstream delivery"); errorBus.Dispose();
        passed.Add("C06 Consumer failure delivered to remaining subscribers and reported");

        Reject(() => new GameplayEvent("bad", 0, "l1-1", null, null, SemanticType.Collision, FeedbackPriority.Interface, new SemanticPayload("wrong")));
        Assert(Event("global").WorldPosition == null, "Global event fabricated position");
        passed.Add("C07 Semantic payload mismatch rejected and global position nullable");

        Point2[] facings = { new Point2(0, 1), new Point2(1, 0), new Point2(0, -1), new Point2(-1, 0) };
        foreach (var facing in facings)
        {
            var left = new Point2(-facing.Y, facing.X); var right = new Point2(facing.Y, -facing.X);
            Assert(Math.Abs(Coordinates.Bearing(facing, left) - 90) < 0.001, "Left incorrect");
            Assert(Math.Abs(Coordinates.Bearing(facing, right) + 90) < 0.001, "Right incorrect");
            var audio = Coordinates.ToAudio(facing); Assert(audio.X == facing.X && audio.Z == facing.Y && audio.Y == 0, "Audio axis incorrect");
        }
        passed.Add("C08 Four-facing direction and XY audio plane mapping");

        var completed = new[] { "l1-1" }; var stars = new Dictionary<string, int> { { "l1-1", 3 } };
        var progress = new PlayerProgress(completed, stars, new[] { "bird", "bird" }, new string[0]); stars["l1-1"] = 1;
        Assert(progress.BestStars["l1-1"] == 3 && progress.CollectedIds.Count == 1, "Progress aliases caller or collection duplicates");
        passed.Add("C09 Progress ownership and collection deduplication");

        var config = new GameplayConfig(); config.Validate(); config.confusionMultiplier = float.NaN; Reject(() => config.Validate());
        Reject(() => new PlayerState(PlayerMainState.Active, ActionMode.Walking, new Point2(), new Point2(0, 0), 100, 100, 0, 0, 0));
        passed.Add("C10 Invalid configuration and non-normalized facing rejected");
        return passed.ToArray();
    }
}
