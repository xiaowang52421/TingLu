using System;
using System.Collections.Generic;
using TingLu.Contracts;
using TingLu.Config;
using TingLu.Player;
using TingLu.World;
using TingLu.Perception;
using TingLu.Memory;
using TingLu.Audio;
using TingLu.Levels;
using TingLu.UI;

namespace TingLu.Bootstrap
{
    // Composition root: all world knowledge reaches presentation only through perception and memory.
    public sealed class StreetSession : IDisposable
    {
        public readonly SimulationClock Clock=new SimulationClock();
        public readonly StreetConfig Config;
        public readonly StreetCollisionFeedback CollisionFeedback;
        public readonly StreetLevel Level;
        public readonly AccessibleMenu Menu=new AccessibleMenu();
        public StreetWorld World{get;private set;}
        public StreetPlayerController Player{get;private set;}
        public StreetMemory Memory{get;private set;}
        public PerceptionFrame Perception{get;private set;}=new PerceptionFrame(0,0,new Observation[0]);
        public IReadOnlyList<StreetSignalObservation> Signals{get;private set;}=new StreetSignalObservation[0];
        public IReadOnlyList<AcousticQueryResult> Sources{get;private set;}=new AcousticQueryResult[0];
        public UserSettings Settings{get;private set;}
        public int Generation{get;private set;}
        public int Collisions{get;private set;}
        public int Recoveries{get;private set;}
        public bool Running{get;private set;}
        public bool Finished{get;private set;}
        public string CheckpointId{get;private set;}
        public event Action<string> Error;
        public event Action<UserSettings> SettingsChanged;
        public event Action Recovered;
        public event Action QuitRequested;
        public event Action<string> CheckpointActivated;
        private readonly IAudioPlayback playback;
        private readonly IAudioSpeech speech;
        private readonly IStreetSaveService saves;
        private StreetPerception perceptionService;
        private StreetAcousticModel acoustics;
        private StreetAudioController audio;
        private PlayerState checkpointPlayer;
        private StreetTrafficState checkpointTraffic;
        private readonly HashSet<string> activated=new HashSet<string>();
        private readonly Dictionary<string,StreetSignalObservation> signalObservations=new Dictionary<string,StreetSignalObservation>(StringComparer.Ordinal);
        private bool recovering,disposed,sessionFault;
        private double lastStepAt;
        public StreetSession(StreetLevel level,IAudioPlayback backend,IAudioSpeech speechBackend,IStreetSaveService saveService,StreetConfig config=null,UserSettings settings=null)
        {
            Level=level??throw new ArgumentNullException("level");playback=backend??throw new ArgumentNullException("backend");speech=speechBackend;saves=saveService??throw new ArgumentNullException("saveService");Config=config??new StreetConfig();Config.Validate();Settings=settings??new UserSettings(DisplayMode.Standard,OutputMode.Headphones,1,true,false);
            CollisionFeedback=new StreetCollisionFeedback(Config.CollisionFeedback);
            Clock.SetPaused(true);playback.SetPaused(true);
            var saved=saves.Load(Level.Id);
            Menu.Initialize(Settings,saved.Status==SaveStatus.Success,Level.GoalText,new MenuCallbacks{Start=()=>Start(false),Continue=()=>Start(true),Pause=Pause,Resume=Resume,CanResume=()=>Running&&!Finished&&!recovering&&!sessionFault,Retry=Retry,ReturnToMenu=ReturnToMenu,ReplayGoal=ReplayGoal,SaveOptions=ApplySettings,Quit=RequestQuit},new CurrentSpeech(this),Generation);
        }
        public void Start(bool resume)
        {
            if(disposed)return;
            try
            {
                SaveResult<StreetSaveData> saved=resume?saves.Load(Level.Id):null;
                if(resume&&(saved==null||saved.Status!=SaveStatus.Success)){Report("暂时不能继续这份存档："+(saved==null?"读取失败":saved.Status.ToString()));return;}
                CollisionFeedback.Clear();
                Clock.SetPaused(true);playback.SetPaused(true);Generation++;if(audio!=null)audio.Reset(Generation);playback.Reset(Generation);
                World=new StreetWorld(Level.Surfaces,Level.Objects,Level.Routes,Config);
                Clock.Reset(0);Player=new StreetPlayerController(Config,Clock,World,Level.Spawn);
                Memory=new StreetMemory();perceptionService=new StreetPerception(Config,Clock);
                acoustics=new StreetAcousticModel(()=>World.Capture(),Player,Config);audio=new StreetAudioController(playback,acoustics);
                activated.Clear();Collisions=0;Recoveries=0;Finished=false;CheckpointId="spawn";checkpointPlayer=Level.Spawn;checkpointTraffic=World.ExportTraffic();lastStepAt=-100;
                signalObservations.Clear();Signals=new StreetSignalObservation[0];
                Player.Probed+=request=>{Play("cane.tap",request.Origin,FeedbackPriority.Navigation);StreetProbeResult result=perceptionService.Probe(request,World.Capture());Memory.Apply(result);UpdateSignalObservations(result.Signals);};
                Player.Contacted+=contact=>{CollisionFeedback.Trigger();if(!contact.Vehicle){Collisions++;Play("collision.bump",contact.Point,FeedbackPriority.Interface);}Memory.Apply(perceptionService.Contact(contact,World.Capture()));};
                Player.Failed+=OnFailure;
                Player.Stepped+=(material,position)=>
                {if(Clock.Now-lastStepAt<0.12)return;lastStepAt=Clock.Now;Play("footstep."+material,position,FeedbackPriority.Ambience);};
                if(resume)
                {
                    StreetSaveData data=saved.Value;
                    if(data.LevelId!=Level.Id)throw new InvalidOperationException("关卡存档不匹配");
                    World.ImportTraffic(data.Traffic);Clock.Reset(data.Traffic.Time);
                    Player.Restore(data.Player,data.Traffic.Time,data.Traffic.Time);Memory.Import(data.Memory,true);
                    checkpointPlayer=data.CheckpointPlayer;checkpointTraffic=data.CheckpointTraffic;CheckpointId=data.CheckpointId;
                    Collisions=data.Collisions;Generation=Math.Max(Generation,data.Generation+1);playback.Reset(Generation);
                    foreach(string id in data.ActivatedCheckpoints)activated.Add(id);
                    Finished=data.Finished;
                }
                Running=true;Clock.SetPaused(false);RefreshSenses();sessionFault=false;
                if(Finished){Clock.SetPaused(true);Player.SetFinished();Menu.OpenResults(Level.Id==StreetLevelFactory.LevelId?"已经走到街对面了。":"已经走到终点了。");}
                else{Menu.SetPlaying();Clock.SetPaused(false);playback.SetPaused(false);if(!resume)Menu.PresentKnownText(Level.GoalText+" WASD走路，移动鼠标转身，右键敲杖。路上的声音一直都能听见。","street-start",FeedbackPriority.Interface);}
                Save();if(Recovered!=null)Recovered();
            }
            catch(Exception e){sessionFault=true;Clock.SetPaused(true);playback.SetPaused(true);Running=false;Menu.Back();Report("街区启动失败："+e.Message);}
        }
        public void Tick(StreetInputFrame input,double delta)
        {
            if(disposed)return;ContractGuard.Time(delta);
            try
            {
            var menuInput=new InputFrame(new Point2(),false,false,input.PausePressed,input.Menu);
            if(Menu.HandleInput(menuInput)||!Running||Clock.Paused||Finished)return;
            // A fixed maximum integration step keeps collision, resources and traffic on one timeline.
            int n=Math.Max(1,(int)Math.Ceiling(delta/0.02));double step=delta/n;
            for(int i=0;i<n;i++)
            {
                int generation=Generation;
                CollisionFeedback.Advance(step);
                World.Tick(step,false);Clock.Advance(step);
                Player.Tick(new StreetInputFrame(input.Move,i==0?input.TurnDegrees:0,i==0&&input.ProbePressed),step);
                if(generation!=Generation||Clock.Paused||!Running)return;
                RefreshSenses();UpdateProgress();
                if(Finished)return;
            }
            }
            catch(Exception e){PauseAfterFault("街区运行中断："+e.Message);}
        }
        private void UpdateProgress()
        {
            foreach(StreetCheckpoint cp in Level.Checkpoints)
                if(!activated.Contains(cp.Id)&&StreetGeometry.Distance(Player.Current.Position,cp.Position)<=cp.Radius)
                {
                    activated.Add(cp.Id);CheckpointId=cp.Id;
                    var s=Player.Current;checkpointPlayer=new PlayerState(PlayerMainState.Active,ActionMode.Walking,cp.Position,cp.Facing,s.Courage,Math.Min(100,s.Focus+Config.Gameplay.checkpointFocusGain),0,0,0);
                    checkpointTraffic=World.ExportTraffic();Save();Play("feedback.checkpoint",cp.Position,FeedbackPriority.Interface);
                    Menu.PresentKnownText("这里站稳了。","checkpoint",FeedbackPriority.Interface);if(CheckpointActivated!=null)CheckpointActivated(cp.Id);
                }
            if(StreetGeometry.Distance(Player.Current.Position,Level.Goal)<=Level.GoalRadius)
            {Finished=true;Player.SetFinished();Clock.SetPaused(true);playback.SetPaused(true);Save();Menu.OpenResults(Level.Id==StreetLevelFactory.LevelId?"到巷口了。这段路走完了。":"到终点了。这段路走完了。");}
        }
        private void OnFailure(StreetFailure reason,string objectId)
        {
            if(recovering||!Running||Clock.Paused)return;
            Recover(reason,reason==StreetFailure.Vehicle);
        }
        private void Recover(StreetFailure reason,bool countCollision)
        {
            if(recovering||!Running)return;
            recovering=true;if(countCollision)Collisions++;
            try
            {
                Clock.SetPaused(true);playback.SetPaused(true);Generation++;audio.Reset(Generation);playback.Reset(Generation);
                World.ImportTraffic(checkpointTraffic);Clock.Reset(checkpointTraffic.Time);
                Player.Restore(checkpointPlayer,checkpointTraffic.Time,checkpointTraffic.Time);
                Memory.MarkStale();perceptionService.Clear();signalObservations.Clear();Signals=new StreetSignalObservation[0];lastStepAt=-100;
                Clock.SetPaused(false);RefreshSenses();Recoveries++;sessionFault=false;
                Save();Menu.PresentKnownText(reason==StreetFailure.Vehicle?"撞上车了，回到刚才站稳的地方。":"缓一缓，回到刚才站稳的地方。","recovery",FeedbackPriority.Interface);
                if(Recovered!=null)Recovered();
                Clock.SetPaused(false);playback.SetPaused(false);
            }
            catch(Exception e){PauseAfterFault("恢复没有完成："+e.Message);}
            finally{recovering=false;}
        }
        private void RefreshSenses()
        {
            playback.UpdateListener(Player.Current.Position,Player.Current.Facing);
            Sources=acoustics.Sample(Player.Current.Position,Player.Current.Facing,Clock.Now);
            Perception=perceptionService.ProjectAcoustics(Sources,Clock.Now);PruneSignalObservations(Clock.Now);audio.Sync(Sources,Generation);
        }
        public void Pause(){if(!Running)return;Clock.SetPaused(true);playback.SetPaused(true);Save();}
        public void Resume()
        {
            if(!Running||Finished||recovering||sessionFault)return;
            try
            {
                Clock.SetPaused(false);RefreshSenses();
                if(Recovered!=null)Recovered();
                playback.SetPaused(false);
            }
            catch(Exception e){PauseAfterFault("暂时不能接着走："+e.Message);}
        }
        public void Retry()
        {
            if(!Running||Finished){Start(false);return;}
            Recover(StreetFailure.Courage,false);
            if(!Clock.Paused)Menu.SetPlaying();
        }
        public void ReturnToMenu(){if(Running)Save();CollisionFeedback.Clear();Running=false;Clock.SetPaused(true);playback.SetPaused(true);Generation++;if(audio!=null)audio.Reset(Generation);playback.Reset(Generation);Sources=new AcousticQueryResult[0];Perception=new PerceptionFrame(0,Clock.Now,new Observation[0]);signalObservations.Clear();Signals=new StreetSignalObservation[0];}
        public SaveStatus Save()
        {
            // A partially restored session must never replace the last usable save.
            if(sessionFault)return SaveStatus.IoFailure;
            if(Player==null||World==null||Memory==null)return SaveStatus.Missing;
            SaveStatus status=saves.Save(new StreetSaveData(Level.Id,CheckpointId,Player.Current,checkpointPlayer,World.ExportTraffic(),checkpointTraffic,Memory.Export(),Collisions,Generation,activated,Finished));
            if(status==SaveStatus.Success)Menu.SetCanContinue(true);
            else Report("存档没有写入："+status);return status;
        }
        private void RequestQuit(){if(QuitRequested!=null)QuitRequested();}
        public void ApplySettings(UserSettings settings){Settings=settings;if(SettingsChanged!=null)SettingsChanged(settings);}
        public void ReplayGoal(){Menu.PresentKnownText(Level.GoalText,"goal",FeedbackPriority.Interface);}
        private void Play(string sound,Point2 p,FeedbackPriority priority){playback.Play(new AudioRequest(Guid.NewGuid().ToString("N"),sound,p,priority,Generation));}
        private void PauseAfterFault(string text)
        {sessionFault=true;Clock.SetPaused(true);playback.SetPaused(true);Menu.OpenPause();Report(text);}
        private void Report(string text)
        {Menu.PresentKnownText("出了点问题，请先回到首页，再试一次。","street-error",FeedbackPriority.Interface);if(Error!=null)Error(text);}
        public void Dispose(){if(disposed)return;ReturnToMenu();disposed=true;playback.Dispose();}
        private void UpdateSignalObservations(IReadOnlyList<StreetSignalObservation> values)
        {if(values==null)return;foreach(StreetSignalObservation value in values)if(value!=null)signalObservations[value.ObjectId]=value;PruneSignalObservations(Clock.Now);}
        private void PruneSignalObservations(double now)
        {var keep=new List<StreetSignalObservation>();foreach(StreetSignalObservation value in signalObservations.Values)if(now>=value.ObservedAt&&now<value.ExpiresAt)keep.Add(value);signalObservations.Clear();foreach(StreetSignalObservation value in keep)signalObservations[value.ObjectId]=value;Signals=keep.AsReadOnly();}
        private sealed class CurrentSpeech:IAudioSpeech
        {
            private readonly StreetSession owner;public CurrentSpeech(StreetSession s){owner=s;}
            public void Speak(SpeechRequest r){if(owner.speech!=null)owner.speech.Speak(new SpeechRequest(r.EventId,r.Text,r.SupersessionKey,r.Priority,owner.Generation));}
            public void StopSpeech(string id){if(owner.speech!=null)owner.speech.StopSpeech(id);}
        }
    }
}


