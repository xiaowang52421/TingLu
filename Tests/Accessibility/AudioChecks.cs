using System;
using System.Collections.Generic;
using TingLu.Audio;
using TingLu.Contracts;

public static class AudioChecks
{
    private sealed class World : IWorldQuery
    {
        public IReadOnlyList<WorldObjectState> States = new WorldObjectState[0];
        public bool Blocked;
        public IReadOnlyList<WorldObjectState> QueryNearby(Point2 center, float radius) { return States; }
        public string MaterialAt(Point2 position) { return "road"; }
        public bool IsOccluded(Point2 from, Point2 to) { return Blocked; }
    }

    public static string[] Run()
    {
        var checks = new List<string>();
        var world = new World();
        var definition = new ObjectDefinition("ambient/bird", ObjectCategory.Event, MemoryPolicy.Never,
            new Segment2[0], "leaves", "ambience.bird", PerceptionSemantic.Person);
        world.States = new[] { new WorldObjectState(definition, new Point2(2, 0), 0, true) };
        var model = new AcousticModel(world, new[] { new AcousticProfile("ambience.bird", 1, 12, 0.5f, 0.9f, "clear") }, 20);
        AcousticQueryResult clear = model.Sample(new Point2(0, 0), new Point2(0, 1), 1)[0];
        Require(clear.Semantic == PerceptionSemantic.Person && clear.Category == ObjectCategory.Event && clear.MaterialId == "leaves",
            "acoustic sample carries known metadata");
        Require(clear.SourceId == "ambient/bird" && clear.Discernibility > 0 && AcousticModel.PlaybackGain(clear) == clear.Discernibility,
            "source identity and shared playback gain");
        RequireThrows(() => new AcousticProfile("bad/nan", float.NaN, 10, 0, 1, "clear"),
            "acoustic profile rejects NaN distances");
        RequireThrows(() => new AcousticProfile("bad/infinity", 1, float.PositiveInfinity, 0, 1, "clear"),
            "acoustic profile rejects infinite distances");
        world.Blocked = true;
        AcousticQueryResult blocked = model.Sample(new Point2(0, 0), new Point2(0, 1), 2)[0];
        Require(blocked.Occlusion == 0.5f && blocked.Discernibility < clear.Discernibility,
            "occlusion changes the shared discernibility result");
        checks.Add("acoustic_gain_and_metadata");

        var queue = new SpeechPriorityQueue();
        queue.Enqueue(new SpeechRequest("menu-1", "首页", "focus", FeedbackPriority.Interface, 0));
        queue.Enqueue(new SpeechRequest("threat", "车辆靠近", null, FeedbackPriority.CriticalThreat, 0));
        queue.Enqueue(new SpeechRequest("menu-2", "设置", "focus", FeedbackPriority.Interface, 0));
        Require(queue.Count == 2, "focus supersession collapses pending speech");
        Require(queue.Dequeue().EventId == "threat", "higher priority speech goes first");
        Require(queue.Dequeue().EventId == "menu-2", "latest same-key speech remains");
        queue.Enqueue(new SpeechRequest("paused-threat", "路口有车", null, FeedbackPriority.CriticalThreat, 0));
        queue.Enqueue(new SpeechRequest("paused-menu", "菜单已暂停", null, FeedbackPriority.Interface, 0));
        Require(queue.Peek(FeedbackPriority.Interface).EventId == "paused-menu",
            "interface speech can be selected while higher priority world speech waits during pause");
        SpeechRequest[] droppedWorldSpeech = queue.RemoveWhere(request => request.Priority != FeedbackPriority.Interface);
        Require(droppedWorldSpeech.Length == 1 && droppedWorldSpeech[0].EventId == "paused-threat" &&
            queue.Count == 1 && queue.Peek().EventId == "paused-menu",
            "pause can discard stale world speech while keeping menu speech");
        checks.Add("speech_priority_and_supersession");
        return checks.ToArray();
    }

    private static void Require(bool condition, string message)
    { if (!condition) throw new InvalidOperationException(message); }

    private static void RequireThrows(Action action, string message)
    {
        try { action(); }
        catch (ArgumentOutOfRangeException) { return; }
        throw new InvalidOperationException(message);
    }
}

