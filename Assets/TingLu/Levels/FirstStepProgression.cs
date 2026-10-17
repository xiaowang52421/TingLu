using System;
using System.Collections.Generic;
using TingLu.Contracts;

namespace TingLu.Levels
{
    /// <summary>Checkpoint snapshots and attempt counters deliberately have separate lifetimes.</summary>
    public sealed class CheckpointService : ICheckpointService
    {
        private readonly string level;
        private readonly IGameClock clock;
        private readonly IPlayerController player;
        private readonly IMemoryCheckpoint memory;
        private readonly IWorldLifecycle world;
        private readonly Func<int> seed;
        private readonly Action<int> restoreSeed;
        private CheckpointSnapshot latest;
        public CheckpointSnapshot Latest { get { return latest; } }
        public CheckpointService(string levelId, IGameClock gameClock, IPlayerController playerController,
            IMemoryCheckpoint memoryCheckpoint, IWorldLifecycle worldLifecycle, Func<int> getSeed, Action<int> setSeed)
        {
            level=ContractGuard.Id(levelId); clock=gameClock??throw new ArgumentNullException("gameClock");
            player=playerController??throw new ArgumentNullException("playerController");
            memory=memoryCheckpoint??throw new ArgumentNullException("memoryCheckpoint"); world=worldLifecycle??throw new ArgumentNullException("worldLifecycle");
            seed=getSeed??throw new ArgumentNullException("getSeed"); restoreSeed=setSeed??throw new ArgumentNullException("setSeed");
        }
        public void Activate(string checkpointId)
        {
            PlayerState state=player.Current;
            var healed=new PlayerState(PlayerMainState.Active,ActionMode.Walking,state.Position,state.Facing,100,
                Math.Min(100,state.Focus+50),state.CaneReadyAt,state.StunnedUntil,state.ConfusedUntil);
            player.Restore(healed,clock.Now,clock.Now);
            latest=new CheckpointSnapshot(level,checkpointId,clock.Now,player.Current,memory.Export(),world.CaptureEvents(),seed());
        }
        public void RestoreLatest()
        {
            if(latest==null) throw new InvalidOperationException("No safe checkpoint has been captured");
            world.Restore(latest.Events,latest.RandomSeed);restoreSeed(latest.RandomSeed); player.Restore(latest.Player,latest.CapturedAt,clock.Now);
            memory.Restore(latest.Memory);
        }
        public void Restore(CheckpointSnapshot snapshot)
        {
            if(snapshot==null || snapshot.SchemaVersion!=CheckpointSnapshot.CurrentSchemaVersion || snapshot.LevelId!=level)
                throw new ArgumentException("Checkpoint schema or level does not match");
            world.Restore(snapshot.Events,snapshot.RandomSeed); restoreSeed(snapshot.RandomSeed);
            player.Restore(snapshot.Player,snapshot.CapturedAt,clock.Now); memory.Restore(snapshot.Memory); latest=snapshot;
        }
    }

    public sealed class AttemptTracker
    {
        private readonly HashSet<string> collected=new HashSet<string>();
        public string AttemptId { get; private set; }
        public int Collisions { get; private set; }
        public IReadOnlyCollection<string> Collected { get { return new List<string>(collected).AsReadOnly(); } }
        public AttemptTracker(string attemptId) { AttemptId=ContractGuard.Id(attemptId); }
        public void RecordCollision() { checked { Collisions++; } }
        public bool Collect(string id) { return collected.Add(ContractGuard.Id(id)); }
        public AttemptStats Snapshot() { return new AttemptStats(AttemptId,Collisions,collected); }
        public static AttemptTracker Resume(AttemptStats snapshot)
        { if(snapshot==null) throw new ArgumentNullException("snapshot"); var tracker=new AttemptTracker(snapshot.AttemptId); tracker.Collisions=snapshot.Collisions; foreach(string id in snapshot.CollectedIds) tracker.collected.Add(id); return tracker; }
        public void NewAttempt(string id) { AttemptId=ContractGuard.Id(id); Collisions=0; collected.Clear(); }
    }

    public sealed class LevelResult
    {
        public int Stars { get; private set; }
        public int Collisions { get; private set; }
        public int Courage { get; private set; }
        public bool HasFragment { get; private set; }
        public LevelResult(int stars,int collisions,int courage,bool hasFragment) { Stars=stars;Collisions=collisions;Courage=courage;HasFragment=hasFragment; }
    }

    public static class FirstStepScoring
    {
        public static LevelResult Calculate(AttemptStats attempt,float courage,string fragmentId)
        {
            if(attempt==null) throw new ArgumentNullException("attempt");
            bool fragment=attempt.CollectedIds.Contains(fragmentId);
            int stars=attempt.Collisions==0 && fragment ? 3 : courage>=70 ? 2 : 1;
            return new LevelResult(stars,attempt.Collisions,(int)Math.Floor(courage),fragment);
        }
        public static PlayerProgress Complete(PlayerProgress previous,LevelResult result,string levelId)
        {
            if(previous==null || result==null) throw new ArgumentNullException();
            var levels=new HashSet<string>(previous.CompletedLevels){levelId};
            var stars=new Dictionary<string,int>(previous.BestStars);
            int old; if(!stars.TryGetValue(levelId,out old)||old<result.Stars) stars[levelId]=result.Stars;
            return new PlayerProgress(levels,stars,previous.CollectedIds,previous.Unlocks);
        }
    }

    /// <summary>Owns the two-second zero-courage recovery beat; presentation is supplied by the caller.</summary>
    public sealed class FailureRecovery
    {
        private readonly IPlayerStateReader player;
        private readonly ICheckpointService checkpoints;
        private readonly Func<double> now;
        private readonly Action<bool> showBreathingBlackout;
        private readonly Action persist;
        private readonly double duration;
        private double beganAt;
        private bool active;
        public FailureRecovery(IPlayerStateReader playerState,ICheckpointService checkpointService,Func<double> simulationTime,
            Action<bool> blackout,Action save,double failureSeconds)
        { player=playerState??throw new ArgumentNullException("playerState");checkpoints=checkpointService??throw new ArgumentNullException("checkpointService");now=simulationTime??throw new ArgumentNullException("simulationTime");showBreathingBlackout=blackout??throw new ArgumentNullException("blackout");persist=save??throw new ArgumentNullException("save");if(failureSeconds<0)throw new ArgumentOutOfRangeException("failureSeconds");duration=failureSeconds; }
        public void Tick()
        {
            if(player.Current.MainState==PlayerMainState.Failing)
            {
                if(!active){active=true;beganAt=now();showBreathingBlackout(true);}
                if(now()-beganAt>=duration){checkpoints.RestoreLatest();active=false;showBreathingBlackout(false);persist();}
            }
        }
    }
}
