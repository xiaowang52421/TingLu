using System;
using System.Collections.Generic;
using TingLu.Contracts;

namespace TingLu.Levels
{
    /// <summary>Position-triggered first level progression. Presentation/audio are supplied callbacks.</summary>
    public sealed class FirstStepFlow : IDisposable
    {
        private readonly FirstStepLevel level;
        private readonly IPlayerStateReader player;
        private readonly IEventBus bus;
        private readonly ICheckpointService checkpoints;
        private readonly ISaveService saves;
        private AttemptTracker attempt;
        private readonly Func<PlayerProgress> readProgress;
        private readonly Action<PlayerProgress> writeProgress;
        private readonly Action<FirstStepBeat> present;
        private readonly Func<double> now;
        private readonly Func<string,bool> isPerceived;
        private readonly HashSet<string> fired=new HashSet<string>();
        private readonly HashSet<string> activated=new HashSet<string>();
        private readonly IDisposable subscription;
        private FailureRecovery failureRecovery;
        private bool finished, disposed;
        public LevelResult Result { get; private set; }
        public AttemptTracker Attempt { get { return attempt; } }

        public FirstStepFlow(FirstStepLevel definition,IPlayerStateReader playerState,IEventBus eventBus,
            ICheckpointService checkpointService,ISaveService saveService,AttemptTracker attemptState,
            Func<PlayerProgress> getProgress,Action<PlayerProgress> setProgress,Action<FirstStepBeat> presentBeat,Func<double> simulationTime)
            : this(definition, playerState, eventBus, checkpointService, saveService, attemptState,
                getProgress, setProgress, presentBeat, simulationTime, null)
        {
        }

        public FirstStepFlow(FirstStepLevel definition,IPlayerStateReader playerState,IEventBus eventBus,
            ICheckpointService checkpointService,ISaveService saveService,AttemptTracker attemptState,
            Func<PlayerProgress> getProgress,Action<PlayerProgress> setProgress,Action<FirstStepBeat> presentBeat,
            Func<double> simulationTime,Func<string,bool> perceptionGate)
        {
            level=definition??throw new ArgumentNullException("definition");player=playerState??throw new ArgumentNullException("playerState");
            bus=eventBus??throw new ArgumentNullException("eventBus");checkpoints=checkpointService??throw new ArgumentNullException("checkpointService");
            saves=saveService??throw new ArgumentNullException("saveService");attempt=attemptState??throw new ArgumentNullException("attemptState");
            readProgress=getProgress??throw new ArgumentNullException("getProgress");writeProgress=setProgress??throw new ArgumentNullException("setProgress");
            present=presentBeat??throw new ArgumentNullException("presentBeat");now=simulationTime??throw new ArgumentNullException("simulationTime");
            // The ten-argument overload preserves module-only callers. Production hosts pass
            // the current legal-observation query so sound-led beats cannot reveal hidden actors.
            isPerceived=perceptionGate??(id=>true);
            subscription=bus.Subscribe(OnEvent);
        }
        public void Start()
        {
            foreach(var beat in level.Beats)if(beat.Trigger==FirstStepTrigger.AtStart)Fire(beat);
            if(checkpoints.Latest==null){checkpoints.Activate("start");activated.Add("start");}
            SaveResume();
        }
        public void Tick()
        {
            if(disposed||finished)return;
            if(failureRecovery!=null)failureRecovery.Tick();
            if(player.Current.MainState==PlayerMainState.Failing)return;
            Point2 p=player.Current.Position;
            foreach(FirstStepBeat beat in level.Beats)
            {
                if(fired.Contains(beat.Id)||Distance(p,beat.Position)>beat.Radius)continue;
                if(beat.Trigger==FirstStepTrigger.AtStart)continue;
                if(!string.IsNullOrEmpty(beat.RequiresId)&&!fired.Contains(beat.RequiresId))continue;
                if(beat.Trigger==FirstStepTrigger.EnterArea||beat.Trigger==FirstStepTrigger.PerceivedObject)
                {
                    if(beat.Trigger==FirstStepTrigger.PerceivedObject&&!Perceived(beat.TargetId))continue;
                    if(beat.Kind==FirstStepBeatKind.Checkpoint)ActivateCheckpoint(string.IsNullOrEmpty(beat.CheckpointId)?beat.Id:beat.CheckpointId);
                    if(beat.Kind==FirstStepBeatKind.Collectible&&!attempt.Collect(beat.TargetId))continue;
                    Fire(beat);
                    if(beat.Kind==FirstStepBeatKind.Goal)Complete();else if(beat.Kind==FirstStepBeatKind.Checkpoint||beat.Kind==FirstStepBeatKind.Collectible)SaveResume();
                    continue;
                }
                if(beat.Trigger==FirstStepTrigger.NearBird) { if(Perceived(FirstStepLevel.BirdObjectId))Fire(beat); continue; }
                if(beat.Trigger==FirstStepTrigger.BeforeFirstPole) { Fire(beat); continue; }
                if(beat.Trigger==FirstStepTrigger.MeetWalker) { if(Perceived(FirstStepLevel.WalkerObjectId))Fire(beat); continue; }
                if(beat.Trigger==FirstStepTrigger.TurnCheckpoint) { ActivateCheckpoint("turn");Fire(beat);SaveResume();continue; }
                if(beat.Trigger==FirstStepTrigger.CollectSoundFragment) { if(Perceived(FirstStepLevel.FragmentObjectId)&&attempt.Collect(FirstStepLevel.FragmentObjectId)){Fire(beat);SaveResume();}continue; }
                if(beat.Trigger==FirstStepTrigger.NearBicycle) { if(Perceived(FirstStepLevel.BicycleObjectId))Fire(beat);continue; }
                if(beat.Trigger==FirstStepTrigger.EndCheckpoint) { ActivateCheckpoint("end");Fire(beat);SaveResume();continue; }
                if(beat.Trigger==FirstStepTrigger.MeetGatekeeper) { if(Perceived(FirstStepLevel.GatekeeperObjectId))Fire(beat);continue; }
                if(beat.Trigger==FirstStepTrigger.Goal)
                {
                    if(!fired.Contains("gatekeeper"))continue;
                    Fire(beat);Complete();continue;
                }
            }
        }
        public void AttachFailureRecovery(FailureRecovery recovery)
        { if(disposed)throw new ObjectDisposedException("FirstStepFlow");failureRecovery=recovery??throw new ArgumentNullException("recovery"); }
        public void RetryCheckpoint()
        { if(disposed)throw new ObjectDisposedException("FirstStepFlow");checkpoints.RestoreLatest();SaveResume(); }
        // Call after the host restores the ResumeData checkpoint and recreates AttemptTracker.
        public void RestoreProgress(ResumeData resume)
        {if(resume==null)throw new ArgumentNullException("resume");attempt=AttemptTracker.Resume(resume.Attempt);fired.Clear();activated.Clear();foreach(string id in resume.FiredBeatIds)fired.Add(id);foreach(string id in resume.ActivatedCheckpointIds)activated.Add(id);finished=resume.Finished;if(finished)Result=Calculate();}
        public void NewAttempt(string newAttemptId)
        { attempt.NewAttempt(newAttemptId);fired.Clear();activated.Clear();activated.Add("start");finished=false;Result=null;checkpoints.Activate("start");Start(); }
        public void SaveResume()
        { SaveStatus status=saves.SaveResume(new ResumeData(checkpoints.Latest,attempt.Snapshot(),fired,activated,finished)); if(status!=SaveStatus.Success)PublishSaveError(status); }
        private void ActivateCheckpoint(string id){if(activated.Add(id))checkpoints.Activate(id);}
        private PlayerProgress MergeAttemptRewards(PlayerProgress previous)
        {var collected=new HashSet<string>(previous.CollectedIds);foreach(string id in attempt.Snapshot().CollectedIds)collected.Add(id);return new PlayerProgress(previous.CompletedLevels,new Dictionary<string,int>(previous.BestStars),collected,previous.Unlocks);}
        private void Fire(FirstStepBeat beat) { if(fired.Add(beat.Id))present(beat); }
        private LevelResult Calculate()
        {
            string collected=null;var owned=new HashSet<string>(attempt.Collected);foreach(var id in level.FragmentIds)if(owned.Contains(id)){collected=id;break;}
            return FirstStepScoring.Calculate(attempt.Snapshot(),player.Current.Courage,collected);
        }
        private void Complete()
        {
            finished=true;Result=Calculate();
            PlayerProgress updated=MergeAttemptRewards(FirstStepScoring.Complete(readProgress(),Result,level.RuntimeId));writeProgress(updated);
            SaveStatus status=saves.SaveProgress(updated);if(status!=SaveStatus.Success)PublishSaveError(status);SaveResume();
        }
        private bool Perceived(string objectId) { return isPerceived(objectId); }
        private FirstStepBeat Find(string id) { foreach(var beat in level.Beats)if(beat.Id==id)return beat;throw new KeyNotFoundException(id); }
        private void OnEvent(GameplayEvent value)
        { if(disposed)return;if(value.Type==SemanticType.Collision){attempt.RecordCollision();SaveResume();} }
        private void PublishSaveError(SaveStatus status)
        { bus.Publish(new GameplayEvent(Guid.NewGuid().ToString("N"),now(),level.RuntimeId,null,null,SemanticType.SaveError,FeedbackPriority.Interface,new SemanticPayload("save-error",status.ToString()))); }
        private static float Distance(Point2 a,Point2 b) { float x=a.X-b.X,y=a.Y-b.Y;return (float)Math.Sqrt(x*x+y*y); }
        public void Dispose() { if(disposed)return;disposed=true;subscription.Dispose(); }
    }
}
