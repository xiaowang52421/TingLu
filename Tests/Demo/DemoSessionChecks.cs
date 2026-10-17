using System;
using System.Collections.Generic;
using System.IO;
using TingLu.Bootstrap;
using TingLu.Contracts;
using TingLu.Persistence;
using TingLu.Presentation;
using TingLu.UI;
using TingLu.Levels;

public static class DemoSessionChecks
{
    private sealed class Audio : IAudioPlayback, IAudioSpeech
    {
        public event Action<AudioReceipt> Receipt;
        public readonly List<AudioRequest> Sounds = new List<AudioRequest>();
        public readonly List<SpeechRequest> Lines = new List<SpeechRequest>();
        public bool Disposed;
        public void Play(AudioRequest request) { Sounds.Add(request); if(Receipt!=null)Receipt(new AudioReceipt(request.EventId,AudioStatus.Started,0,request.RecoveryGeneration)); }
        public void Speak(SpeechRequest request) { Lines.Add(request); }
        public void StopSpeech(string id) { }
        public void Stop(string id) { }
        public void UpdateListener(Point2 position,Point2 facing) { }
        public void Reset(int generation) { }
        public void SetPaused(bool paused) { }
        public void Dispose() { Disposed=true; }
    }
    private static InputFrame Input(float x=0,float y=0,bool listen=false,bool tap=false,bool pause=false,MenuAction menu=MenuAction.None)
    {return new InputFrame(new Point2(x,y),listen,tap,pause,menu);}
    private static void Check(bool value,string message){if(!value)throw new Exception(message);}
    private static void WalkTo(DemoSession game,float x,float y)
    {
        for(int i=0;i<1500;i++)
        {
            Point2 p=game.Player.Current.Position;float dx=x-p.X,dy=y-p.Y;
            if(Math.Sqrt(dx*dx+dy*dy)<0.08)return;
            Check(game.Menu.Screen==MenuScreen.Playing,"Route interrupted at "+p.X+","+p.Y);
            game.Tick(Input(dx,dy),0.05);
        }
        throw new Exception("Movement did not reach "+x+","+y);
    }
    public static string[] Run(string root,string configuredLevelJson=null)
    {
        Directory.CreateDirectory(root);var passed=new List<string>();
        string saveRoot=Path.Combine(root,"session");var store=new LocalSaveService(saveRoot);var audio=new Audio();
        using(var game=new DemoSession(audio,audio,store))
        {
            Check(game.Clock.Paused&&game.Menu.Screen==MenuScreen.Main,"Initial menu must freeze simulation");
            game.Tick(Input(menu:MenuAction.Confirm),0.05);
            Check(game.Running&&game.Player!=null&&!game.Clock.Paused,"Menu Start must assemble gameplay");
            Check(audio.Lines.Count>0&&audio.Sounds.Count>0,"Assembly must route speech and ambient requests");
            passed.Add("D01 Main menu starts assembled session and routes feedback");
            for(int i=0;i<15;i++)game.Tick(Input(0,1),0.05);
            Check(game.Player.Current.Position.Y>0.7,"Player input must move");
            Check(audio.Sounds.Exists(s=>s.SoundId.StartsWith("footstep.")),"Actual movement must route footsteps");
            game.Tick(Input(tap:true),0.05);
            Check(audio.Sounds.Exists(s=>s.SoundId=="cane.tap"),"Cane action must route sound");
            passed.Add("D02 Movement and cane actions pass through event/audio assembly");
            game.Tick(Input(pause:true),0.05);double pausedAt=game.Clock.Now;Point2 pausedPosition=game.Player.Current.Position;
            game.Tick(Input(0,1),5);
            Check(game.Clock.Now==pausedAt&&game.Player.Current.Position.Y==pausedPosition.Y,"Pause must freeze clock and player");
            game.Tick(Input(pause:true),0.05);game.Tick(Input(0,1),0.05);
            Check(game.Clock.Now>pausedAt&&game.Menu.Screen==MenuScreen.Playing,"Resume must re-enable gameplay");
            passed.Add("D03 Pause and resume freeze and restart the same simulation");
            game.Player.NotifyCollision("l1-1/pole/01",100,0,new Point2(0,15));
            Check(game.Blackout,"Zero courage must hide frame immediately");
            game.Tick(Input(),0.05);game.Tick(Input(),2.1);
            Check(game.Player.Current.Courage==100&&!game.Blackout&&!game.Clock.Paused,"Failure must restore safe checkpoint");
            game.ReturnToMenu();var saved=store.LoadResume();
            Check(saved.Status==SaveStatus.Success&&saved.Value.Attempt.Collisions==1,"Retry must preserve collision count");
            passed.Add("D04 Failure restores checkpoint and keeps attempt collision count on disk");
            game.Tick(Input(menu:MenuAction.Next),0.05);game.Tick(Input(menu:MenuAction.Confirm),0.05);
            Check(game.Running&&game.Menu.Screen==MenuScreen.Playing,"Continue must load saved session");
            Check(store.LoadResume().Value.Attempt.Collisions==1,"Continue must preserve attempt");
            passed.Add("D05 Return to menu and Continue restore persisted attempt");
        }
        Check(audio.Disposed,"Session disposal must release backend");
        passed.Add("D06 Session disposal releases its audio backend");
        string routeRoot=Path.Combine(root,"route");var routeStore=new LocalSaveService(routeRoot);var routeAudio=new Audio();
        using(var game=new DemoSession(routeAudio,routeAudio,routeStore))
        {
            game.Start(false);WalkTo(game,0.8f,0);WalkTo(game,0.8f,20);WalkTo(game,0,20);WalkTo(game,0,40);
            WalkTo(game,5,40);WalkTo(game,26.2f,40);
            for(int i=0;i<50&&game.Menu.Screen==MenuScreen.Playing;i++)game.Tick(Input(1,0),0.05);
            Check(game.Menu.Screen==MenuScreen.Results,"Traversing first-level route must open results");
            var result=routeStore.LoadProgress();Check(result.Status==SaveStatus.Success&&result.Value.BestStars["1-1"]==3,"Unhurt collected route must save 3 stars");
            Check(routeStore.LoadResume().Value.Finished,"Finished resume must not offer Continue");
            passed.Add("D07 Input-driven first-level route collects fragment, finishes and persists three stars");
        }
        foreach(DisplayMode mode in Enum.GetValues(typeof(DisplayMode)))
        {
            var modeStore=new LocalSaveService(Path.Combine(root,mode.ToString()));modeStore.SaveSettings(new UserSettings(mode,OutputMode.Headphones,1,false,false));
            var output=new Audio();using(var game=new DemoSession(output,output,modeStore))
            {
                game.Tick(Input(menu:MenuAction.Confirm),0.05);game.Tick(Input(menu:MenuAction.QueryGoal),0.05);
                Check(output.Lines.Exists(line=>line.Text.Contains("老周")),"Goal must remain spoken in "+mode);
                var frame=new PresentationFrameComposer().Compose(game.Perception,game.Memory,game.Player,game.Settings,game.Clock.Now);
                Check(frame.Visible==(mode!=DisplayMode.AudioOnly),"Display mode presentation mismatch");
            }
        }
        passed.Add("D08 Three modes share menu/goal flow; AudioOnly has no presentation frame");
        var invalidStore=new LocalSaveService(Path.Combine(root,"invalid-resume"));var initial=store.LoadResume().Value;
        var invalidCheckpoint=new CheckpointSnapshot(initial.Checkpoint.LevelId,initial.Checkpoint.CheckpointId,initial.Checkpoint.CapturedAt,
            initial.Checkpoint.Player,initial.Checkpoint.Memory,new[]{new WorldEventSnapshot("unknown-event","idle",0)},initial.Checkpoint.RandomSeed);
        invalidStore.SaveResume(new ResumeData(invalidCheckpoint,initial.Attempt));
        var invalidAudio=new Audio();using(var game=new DemoSession(invalidAudio,invalidAudio,invalidStore))
        {
            game.Tick(Input(menu:MenuAction.Next),0.05);game.Tick(Input(menu:MenuAction.Confirm),0.05);
            Check(!game.Running&&game.Clock.Paused&&game.Menu.Screen==MenuScreen.Main,"Invalid world references must leave a safe usable main menu");
            game.Tick(Input(menu:MenuAction.Confirm),0.05);
            Check(game.Running&&!game.Clock.Paused,"Failed Continue must still permit a new run");
        }
        passed.Add("D09 Invalid resume freezes safely and permits starting a fresh run");
        if(!string.IsNullOrWhiteSpace(configuredLevelJson))
        {
            var definition=LevelCompiler.Compile(LevelCodec.Decode(configuredLevelJson));
            Check(!definition.Legacy&&definition.RuntimeId=="1-1"&&definition.MovementRoutes.Count>0,"Published first level must use editable data with stable ID and routes");
            var configuredStore=new LocalSaveService(Path.Combine(root,"configured"));var configuredAudio=new Audio();
            using(var game=new DemoSession(configuredAudio,configuredAudio,configuredStore,null,definition))
            {
                game.Start(false);game.Tick(Input(),1);
                var route=definition.MovementRoutes[0];var expected=MovementRouteSampler.Sample(route,game.Clock.Now);
                var actor=game.World.GetState(route.ActorId);
                Check(Math.Abs(actor.Position.Y-expected.Position.Y)<.001f,"Runtime route must sample simulation time");
                game.Tick(Input(pause:true),.05);Point2 paused=actor.Position;double pausedTime=game.Clock.Now;
                game.Tick(Input(),5);
                Check(game.Clock.Now==pausedTime&&game.World.GetState(route.ActorId).Position.Y==paused.Y,"Paused route must stay frozen");
                game.Tick(Input(pause:true),.05);
                WalkTo(game,.8f,0);WalkTo(game,.8f,20);WalkTo(game,0,20);WalkTo(game,0,40);
                game.ReturnToMenu();var checkpoint=configuredStore.LoadResume().Value.Checkpoint;
                Check(checkpoint.CheckpointId=="turn","Default data must preserve checkpoint ID used by old saves");
                game.Start(true);
                Check(game.Clock.Now==checkpoint.CapturedAt,"Continue must restore the checkpoint simulation phase");
                expected=MovementRouteSampler.Sample(route,checkpoint.CapturedAt);actor=game.World.GetState(route.ActorId);
                Check(Math.Abs(actor.Position.Y-expected.Position.Y)<.001f,"Continue must reconstruct the moving sound source from configured route");
                WalkTo(game,0,40);WalkTo(game,5,40);WalkTo(game,25.8f,40);
                for(int i=0;i<80&&game.Menu.Screen==MenuScreen.Playing;i++)game.Tick(Input(1,0),.05);
                Check(game.Menu.Screen==MenuScreen.Results&&configuredStore.LoadProgress().Value.BestStars["1-1"]==3,"Configured published first level must finish and save three stars");
            }
            passed.Add("D10 Published JSON route freezes, resumes checkpoint phase and finishes with stable rewards");
            var compatibleStore=new LocalSaveService(Path.Combine(root,"legacy-compatibility"));compatibleStore.SaveResume(initial);
            var compatibleAudio=new Audio();using(var game=new DemoSession(compatibleAudio,compatibleAudio,compatibleStore,null,definition))
            {
                game.Start(true);Check(game.Running&&!game.Clock.Paused,"Legacy first-level resume must remain loadable with the published data");
                game.ReturnToMenu();Check(compatibleStore.LoadResume().Value.Attempt.Collisions==1,"Legacy attempt statistics must survive data migration");
            }
            passed.Add("D11 Legacy first-level checkpoint and collision statistics survive configured-level migration");
        }
        return passed.ToArray();
    }
}
