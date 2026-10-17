using System;
using System.Collections.Generic;
using TingLu.Contracts;
using TingLu.Config;
using TingLu.World;
using TingLu.Player;
using TingLu.Memory;
using TingLu.Perception;
using TingLu.Audio;
using TingLu.Levels;
using TingLu.UI;

namespace TingLu.Bootstrap
{
    public sealed class DemoSession : IDisposable
    {
        private readonly GameplayConfig config;
        private readonly IAudioPlayback audio;
        private readonly IAudioSpeech speech;
        private readonly ISaveService saves;
        private readonly FirstStepLevel level;
        private readonly Dictionary<string,string> levelSounds = new Dictionary<string,string>();
        private readonly Dictionary<string, string> sourceEvents = new Dictionary<string, string>();
        private readonly HashSet<string> contacts = new HashSet<string>();
        private IDisposable subscription;
        private FirstStepFlow flow;
        private CheckpointService checkpoints;
        private AcousticModel acoustic;
        private double nextSense, failedAt = -1, nextHeartbeat;
        private bool disposed, running, finished, restoring, recoveryFault, onTactile;
        private Point2 knownGuideDirection;
        private bool guideDirectionKnown;
        public readonly SimulationClock Clock = new SimulationClock();
        public readonly EventBus Events = new EventBus();
        public readonly AccessibleMenu Menu = new AccessibleMenu();
        public WorldRegistry World { get; private set; }
        public PlayerController Player { get; private set; }
        public StaticMemoryStore Memory { get; private set; }
        public PerceptionService Perception { get; private set; }
        public UserSettings Settings { get; private set; }
        public PlayerProgress Progress { get; private set; }
        public IReadOnlyList<AcousticQueryResult> Sources { get; private set; }
        public int Generation { get; private set; }
        public bool Blackout { get { return recoveryFault || failedAt >= 0 || restoring || (Player != null && Player.Current.MainState == PlayerMainState.Failing); } }
        public bool Running { get { return running; } }
        public event Action<GameplayEvent> Feedback;
        public event Action<string> Error;
        public DemoSession(IAudioPlayback playback, IAudioSpeech voice, ISaveService storage, GameplayConfig tuning = null, FirstStepLevel levelDefinition = null)
        {
            audio = playback ?? throw new ArgumentNullException("playback"); speech = voice ?? throw new ArgumentNullException("voice");
            saves = storage ?? throw new ArgumentNullException("storage"); config = tuning ?? new GameplayConfig(); config.Validate();
            level=levelDefinition??new FirstStepLevel();
            foreach(var obj in level.Objects)
            {
                string sound=obj.Definition.SoundProfileId;
                if(level.Legacy) sound=obj.Definition.ObjectId==FirstStepLevel.GatekeeperObjectId?"ambience.guard":obj.Definition.Semantic==PerceptionSemantic.Person?"ambience.pedestrian":"ambience.bird";
                if(!string.IsNullOrEmpty(sound))levelSounds[obj.Definition.ObjectId]=sound;
            }
            var loadedSettings = saves.LoadSettings(); Settings = loadedSettings.Status == SaveStatus.Success ? loadedSettings.Value : new UserSettings(DisplayMode.Standard, OutputMode.Headphones, 1, true, true);
            var loadedProgress = saves.LoadProgress(); Progress = loadedProgress.Status == SaveStatus.Success ? loadedProgress.Value : new PlayerProgress(new string[0], new Dictionary<string,int>(), new string[0], new string[0]);
            Sources = new AcousticQueryResult[0];
            InitializeMenu(); Clock.SetPaused(true);
        }
        private void InitializeMenu()
        {
            var resume = saves.LoadResume();
            Menu.Initialize(Settings, resume.Status == SaveStatus.Success && !resume.Value.Finished && resume.Value.Checkpoint.LevelId==level.RuntimeId, level.GoalText, new MenuCallbacks
            {
                Start = () => Start(false), Continue = () => Start(true), Pause = Pause, Resume = Resume,
                Retry = () => { if (finished) Start(false); else RestoreCheckpoint(); }, ReturnToMenu = ReturnToMenu,
                SaveOptions = ChangeSettings
            }, speech, Generation);
        }
        public void Start(bool continueSaved)
        {
            EnsureLive();
            try { StartLevel(continueSaved); }
            catch (Exception error)
            {
                TearDownLevel(); Clock.SetPaused(true); audio.Reset(++Generation); audio.SetPaused(false);
                InitializeMenu(); ReportError("暂时没能载入这段路，请从头开始。");
                if(Error!=null)Error(error.Message);
            }
        }
        private void StartLevel(bool continueSaved)
        {
            EnsureLive(); TearDownLevel();
            Clock.SetPaused(true); Clock.Reset();
            Generation++; audio.Reset(Generation); audio.SetPaused(false);
            var definition = level; World = new WorldRegistry();
            foreach (var obj in definition.Objects) World.Register(obj.Definition, obj.Position, obj.Rotation, obj.SoundActive);
            foreach (var ground in definition.Ground) World.AddGroundSurface(ground.Material, ground.Polygon);
            var initial = new PlayerState(PlayerMainState.Active, ActionMode.Walking, definition.Spawn, definition.SpawnFacing, config.spawnCourage, config.spawnFocus, Clock.Now, Clock.Now, Clock.Now);
            Player = new PlayerController(config, Clock, Events, World, definition.RuntimeId, initial);
            Memory = new StaticMemoryStore();
            acoustic = new AcousticModel(World, new[]
            {
                new AcousticProfile("bird-morning", 4, 22, 0.6f, 1, "air"),
                new AcousticProfile("walker-steps", 3, 14, 0.65f, 1, "air"),
                new AcousticProfile("gatekeeper-voice", 4, 18, 0.6f, 1, "air"),
                new AcousticProfile("sparrow-fragment", 3, 14, 0.6f, 1, "air"),
                new AcousticProfile("ambience.bird", 4, 22, .6f, 1, "air"),
                new AcousticProfile("ambience.pedestrian", 3, 14, .65f, 1, "air"),
                new AcousticProfile("ambience.guard", 4, 18, .6f, 1, "air"),
                new AcousticProfile("feedback.checkpoint", 3, 14, .6f, 1, "air"),
                new AcousticProfile("cane.tap", 3, 14, .6f, 1, "air"),
                new AcousticProfile("echo.ping", 3, 14, .6f, 1, "air")
            }, 24);
            Perception = new PerceptionService(World, acoustic, Player, Clock, config, Memory, Memory.Observe, Memory.Clear, definition.RuntimeId);
            checkpoints = new CheckpointService(definition.RuntimeId, Clock, Player, Memory, World, () => World.RandomSeed, seed => {});
            ResumeData saved = null;
            if (continueSaved)
            {
                var result = saves.LoadResume();
                if (result.Status != SaveStatus.Success || result.Value.Finished) { TearDownLevel(); Clock.SetPaused(true); ReportError("上次的进度暂时读不到，请从头开始。"); return; }
                saved = result.Value; Clock.Reset(saved.Checkpoint.CapturedAt); checkpoints.Restore(saved.Checkpoint);
            }
            var attempt = saved == null ? new AttemptTracker(Guid.NewGuid().ToString("N")) : AttemptTracker.Resume(saved.Attempt);
            // Flow is created after the host event handler so perception updates precede semantic feedback.
            subscription = Events.Subscribe(OnGameplayEvent);
            flow = new FirstStepFlow(definition, Player, Events, checkpoints, saves, attempt, () => Progress, value => Progress = value, PresentBeat, () => Clock.Now, IsPerceived);
            if (saved != null) flow.RestoreProgress(saved);
            failedAt = -1; recoveryFault = false; finished = false; onTactile = false; running = true; nextSense = Clock.Now; nextHeartbeat = Clock.Now; contacts.Clear();
            SyncListenerAndPerception(); InitializeMenu(); Menu.SetPlaying(); flow.Start();
            Speak("方向键或 W A S D 移动，按住 Shift 听一听，空格敲杖。G 重听目标，Esc 暂停。", "controls");
            Clock.SetPaused(false);
        }
        public void Tick(InputFrame input, double delta)
        {
            EnsureLive(); ContractGuard.Time(delta);
            if (Menu.Screen != MenuScreen.Playing || input.PausePressed || input.Menu == MenuAction.QueryGoal)
            {
                // A held stick does not repeatedly change menu focus: adapters supply menu edges.
                Menu.HandleInput(new InputFrame(new Point2(0, 0), false, false, input.PausePressed, input.Menu));
                if(!running && Menu.Screen==MenuScreen.Playing)InitializeMenu();
                if(recoveryFault && Menu.Screen==MenuScreen.Playing)Menu.OpenPause();
                return;
            }
            if (!running || finished || Clock.Paused || restoring) return;
            Clock.Advance(delta);
            if (Player.Current.MainState == PlayerMainState.Failing)
            {
                if (failedAt < 0) { failedAt = Clock.Now; Play("feedback.breath", null, FeedbackPriority.Navigation); Speak("没关系，再来一次。", "failure"); }
                if (Clock.Now - failedAt >= config.failureSeconds) RestoreCheckpoint();
                return;
            }
            UpdateMovementRoutes();
            Point2 previous = Player.Current.Position;
            var desired = DesiredMovement(input, delta);
            MovementResult hit = World.ResolveMovement(previous, desired, 0.18f);
            Player.Tick(input, delta);
            if (hit.ContactObjectId != null) Player.NotifyCollision(hit.ContactObjectId, config.damageStatic, 0, hit.ContactPoint);
            var nextContacts = new HashSet<string>();
            if (hit.ContactObjectId != null) nextContacts.Add(hit.ContactObjectId);
            foreach (string old in contacts) if (!nextContacts.Contains(old)) Player.SetContact(old, false);
            contacts.Clear(); foreach (string current in nextContacts) contacts.Add(current);
            audio.UpdateListener(Player.Current.Position, Player.Current.Facing);
            if (Clock.Now >= nextSense) { SyncListenerAndPerception(); nextSense = Clock.Now + 1 / config.perceptionHz; }
            if (Player.Current.Courage < 30 && Clock.Now >= nextHeartbeat) { Play("feedback.heartbeat", null, FeedbackPriority.Navigation); nextHeartbeat = Clock.Now + 1; }
            flow.Tick();
            if (flow.Result != null && !finished)
            {
                finished = true; Player.SetMainState(PlayerMainState.Finished); Clock.SetPaused(true); audio.SetPaused(false);
                foreach (string id in sourceEvents.Values) audio.Stop(id); sourceEvents.Clear();
                Menu.OpenResults("到了。" + flow.Result.Stars + " 星，这一路碰撞 " + flow.Result.Collisions + " 次。");
            }
        }
        private Point2 DesiredMovement(InputFrame input, double delta)
        {
            if (Player.Current.MainState != PlayerMainState.Active) return Player.Current.Position;
            float x = input.Move.X, y = input.Move.Y, length = (float)Math.Sqrt(x*x+y*y);
            if (length < 0.001f) return Player.Current.Position;
            float speed = input.ListenHeld ? config.listenSpeed : config.walkSpeed;
            return new Point2(Player.Current.Position.X + x/length*speed*(float)delta, Player.Current.Position.Y+y/length*speed*(float)delta);
        }
        private void SyncListenerAndPerception()
        {
            UpdateMovementRoutes();
            audio.UpdateListener(Player.Current.Position, Player.Current.Facing);
            Perception.Recompute(); Sources = acoustic.Sample(Player.Current.Position, Player.Current.Facing, Clock.Now);
            bool tactile=false;
            foreach(var observation in Perception.Current.Observations)
                if(observation.Source==ObservationSource.TactilePath){tactile=true;break;}
            if(tactile!=onTactile)
            {
                onTactile=tactile;
                guideDirectionKnown=false;
                Speak(tactile?"脚下是盲道。":"离开盲道了。","tactile",FeedbackPriority.Navigation);
            }
            if(tactile)PresentTactileDirection();
            var active = new HashSet<string>();
            foreach (var source in Sources)
            {
                if (!source.Active || source.Discernibility <= 0) continue;
                active.Add(source.SourceId);
                if (!sourceEvents.ContainsKey(source.SourceId))
                {
                    string id = Guid.NewGuid().ToString("N"); sourceEvents[source.SourceId] = id;
                    string sound;
                    if(!levelSounds.TryGetValue(source.SourceId,out sound))continue;
                    audio.Play(new AudioRequest(id, sound, source.ObservedSourcePosition, FeedbackPriority.Ambience, Generation, source.SourceId, source.Discernibility, source.Occlusion, true));
                }
            }
            var removed = new List<string>();
            foreach (var pair in sourceEvents) if (!active.Contains(pair.Key)) { audio.Stop(pair.Value); removed.Add(pair.Key); }
            foreach (string key in removed) sourceEvents.Remove(key);
        }
        private void UpdateMovementRoutes()
        {
            foreach(var route in level.MovementRoutes)
            {
                var pose=MovementRouteSampler.Sample(route,Clock.Now);
                var actor=World.GetState(route.ActorId);
                World.Move(route.ActorId,pose.Position,pose.RotationDegrees,actor.SoundActive);
            }
        }
        private bool IsPerceived(string id)
        { foreach (var observation in Perception.Current.Observations) if (observation.ObjectId == id && observation.ExpiresAt > Clock.Now) return true; return false; }
        private void PresentTactileDirection()
        {
            Point2 p=Player.Current.Position,facing=Player.Current.Facing,best=default(Point2);float score=-1;bool found=false;
            foreach(var observation in Perception.Current.Observations)
            {
                if(observation.Source!=ObservationSource.TactilePath)continue;
                foreach(var segment in observation.ConfirmedContour)
                    foreach(var end in new[]{segment.A,segment.B})
                    {
                        float dx=end.X-p.X,dy=end.Y-p.Y,length=(float)Math.Sqrt(dx*dx+dy*dy);
                        if(length<0.15f)continue;
                        float forward=(dx*facing.X+dy*facing.Y)/length;
                        if(forward<-.05f||forward<score)continue;
                        best=new Point2(dx/length,dy/length);score=forward;found=true;
                    }
            }
            if(!found)return;
            if(!guideDirectionKnown||knownGuideDirection.X*best.X+knownGuideDirection.Y*best.Y<0.75f)
            {
                Speak("盲道向"+RelativeDirection(best.X,best.Y)+"延伸。","tactile-direction",FeedbackPriority.Navigation);
                knownGuideDirection=best;guideDirectionKnown=true;
            }
        }
        private void OnGameplayEvent(GameplayEvent value)
        {
            if (value.Type == SemanticType.CaneTap)
            {
                Perception.Probe(value); Play("cane.tap", Player.Current.Position, FeedbackPriority.Navigation);
                foreach (var observation in Perception.Current.Observations)
                {
                    if (observation.Source != ObservationSource.CaneEcho || observation.Semantic == PerceptionSemantic.Guide) continue;
                    var p = observation.ObservedPosition; float dx=p.X-Player.Current.Position.X,dy=p.Y-Player.Current.Position.Y;
                    float distance=(float)Math.Sqrt(dx*dx+dy*dy); Play("echo.ping", p, FeedbackPriority.Navigation, 1/(1+distance));
                    Speak("回声，" + RelativeDirection(dx,dy) + "，约" + Math.Max(1, (int)Math.Round(distance)) + "米，" + MaterialName(observation.MaterialId) + "。", "echo:" + observation.ObjectId, FeedbackPriority.Navigation);
                }
            }
            else if (value.Type == SemanticType.Collision) { Perception.Contact(value); Play("collision.bump", null, FeedbackPriority.Navigation); }
            else if (value.Type == SemanticType.Footstep)
            {
                string material = ((FootstepPayload)value.Payload).MaterialId;
                Play(material == "tactile_path" ? "footstep.tactile" : "footstep.asphalt", Player.Current.Position, FeedbackPriority.Navigation);
            }
            else if (value.Type == SemanticType.SaveError) ReportError("进度没能保存，请稍后再试。");
            if (Feedback != null) Feedback(value);
        }
        private string RelativeDirection(float x,float y)
        {
            var facing=Player.Current.Facing; double angle=Coordinates.Bearing(facing,new Point2(x,y));
            if(Math.Abs(angle)<30)return "前方"; if(Math.Abs(angle)>150)return "身后";return angle>0?"左侧":"右侧";
        }
        private static string MaterialName(string id) { return id == "metal" ? "金属" : id == "concrete" ? "硬质表面" : "物体"; }
        private void PresentBeat(FirstStepBeat beat)
        {
            if (beat.Text != null) Speak(beat.Text, "beat:" + beat.Id);
            if(!level.Legacy){if(!string.IsNullOrEmpty(beat.SoundId))Play(beat.SoundId,beat.Position,FeedbackPriority.Interface);}
            else if (beat.Kind == FirstStepBeatKind.Checkpoint) Play("feedback.checkpoint", null, FeedbackPriority.Interface);
            if (beat.Kind == FirstStepBeatKind.Collectible) Speak("收下一段城市记忆。", "collection");
        }
        private void Speak(string text,string key,FeedbackPriority priority=FeedbackPriority.Interface)
        { Menu.PresentKnownText(text,key,priority); }
        private void Play(string sound,Point2? position,FeedbackPriority priority,float gain=1)
        { audio.Play(new AudioRequest(Guid.NewGuid().ToString("N"),sound,position,priority,Generation,null,gain)); }
        public void Pause() { if (!running) return; Clock.SetPaused(true); audio.SetPaused(true); flow.SaveResume(); }
        public void Resume() { if (!running || restoring || recoveryFault) return; SyncListenerAndPerception(); audio.SetPaused(false); Clock.SetPaused(false); }
        public void RestoreCheckpoint()
        {
            if (!running) return;
            restoring = true; Clock.SetPaused(true); Generation++; audio.Reset(Generation); sourceEvents.Clear();
            try
            {
                checkpoints.RestoreLatest(); Perception.ClearTransient(); contacts.Clear(); failedAt=-1; onTactile=false;
                audio.UpdateListener(Player.Current.Position,Player.Current.Facing); audio.SetPaused(false);
                SyncListenerAndPerception(); InitializeMenu(); Menu.SetPlaying(); flow.SaveResume();
                recoveryFault=false;
                Clock.SetPaused(false);
            }
            catch (Exception error) { recoveryFault=true; Clock.SetPaused(true); Menu.OpenPause(); ReportError("暂时没能恢复进度，请返回菜单再试。"); if(Error!=null)Error(error.Message); }
            finally { restoring=false; }
        }
        public void ReturnToMenu() { if(flow!=null)flow.SaveResume(); running=false; Clock.SetPaused(true); audio.Reset(++Generation); sourceEvents.Clear(); InitializeMenu(); }
        private void ChangeSettings(UserSettings settings) { Settings=settings; if(saves.SaveSettings(settings)!=SaveStatus.Success)ReportError("设置没能保存，请稍后再试。"); }
        private void ReportError(string text) { if(Error!=null)Error(text); Speak(text,"error"); }
        private void TearDownLevel()
        {
            if(subscription!=null){subscription.Dispose();subscription=null;} if(flow!=null){flow.Dispose();flow=null;}
            if(Perception!=null){Perception.Dispose();Perception=null;} if(World!=null){World.Dispose();World=null;}
            Player=null;Memory=null;sourceEvents.Clear();contacts.Clear();running=false;Sources=new AcousticQueryResult[0];
        }
        public void Dispose(){if(disposed)return;if(flow!=null)flow.SaveResume();TearDownLevel();Events.Dispose();audio.Dispose();disposed=true;}
        private void EnsureLive(){if(disposed)throw new ObjectDisposedException("DemoSession");}
    }
}
