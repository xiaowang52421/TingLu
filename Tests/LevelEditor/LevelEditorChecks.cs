using System;
using System.Collections.Generic;
using System.IO;
using TingLu.Contracts;
using TingLu.Levels;
using TingLu.Bootstrap;
using TingLu.Persistence;
using TingLu.UI;

public static class LevelEditorChecks
{
    private sealed class Audio:IAudioPlayback,IAudioSpeech
    {
        public event Action<AudioReceipt> Receipt;
        public readonly List<AudioRequest> Sounds=new List<AudioRequest>();
        public readonly List<SpeechRequest> Lines=new List<SpeechRequest>();
        public void Play(AudioRequest r){Sounds.Add(r);if(Receipt!=null)Receipt(new AudioReceipt(r.EventId,AudioStatus.Started,0,r.RecoveryGeneration));}
        public void Speak(SpeechRequest r){Lines.Add(r);}public void StopSpeech(string id){}public void Stop(string id){}public void Reset(int n){}public void UpdateListener(Point2 p,Point2 f){}public void SetPaused(bool p){}public void Dispose(){}
    }
    private static void Assert(bool b,string m){if(!b)throw new Exception(m);}
    private static void Rejected(Action action,string m){bool rejected=false;try{action();}catch(InvalidDataException){rejected=true;}Assert(rejected,m);}
    private static InputFrame Input(float x=0,float y=0,bool tap=false){return new InputFrame(new Point2(x,y),false,tap,false,MenuAction.None);}
    private static void Move(DemoSession game,float x,float y)
    {for(int i=0;i<1000;i++){Point2 p=game.Player.Current.Position;float dx=x-p.X,dy=y-p.Y;if(Math.Sqrt(dx*dx+dy*dy)<.07)return;if(game.Menu.Screen!=MenuScreen.Playing)return;game.Tick(Input(dx,dy),.05);}throw new Exception("路线未到达目标");}
    public static string[] Run(string root)
    {
        Directory.CreateDirectory(root);var passed=new List<string>();
        var template=FirstStepTemplate.Create();LevelValidator.RequireValid(template);
        Assert(template.guide.Count==8&&template.guide[2].x<-.6f,"Template must retain safe pole bypass");
        var copy=LevelCodec.Duplicate(template);Assert(copy.id!=template.id,"Independent level ID");
        var old=new HashSet<string>();foreach(var i in template.items)old.Add(i.id);
        foreach(var i in copy.items){Assert(!old.Contains(i.id),"Copied object ID");Assert(string.IsNullOrEmpty(i.target)||!old.Contains(i.target),"target remap");Assert(string.IsNullOrEmpty(i.requires)||!old.Contains(i.requires),"requires remap");}
        LevelValidator.RequireValid(copy);string path=Path.Combine(root,"第二关.json");LevelCodec.Save(path,copy);
        var loaded=LevelCodec.Load(path);string stable=loaded.items[0].id;loaded.items[0].x=1;LevelCodec.Save(path,loaded);Assert(LevelCodec.Load(path).items[0].id==stable,"Normal save must preserve IDs");Assert(File.Exists(path+".bak"),"Safe save backup");
        passed.Add("L01 First-step safe template, independent copy reference remap, safe JSON save/reload and stable IDs");
        var route=new MovementRouteDefinition("route","walker",true,new[]{
            new MovementRoutePoint(0,new Point2(0,0),350),new MovementRoutePoint(10,new Point2(10,0),10),
            new MovementRoutePoint(20,new Point2(0,0),350)});
        var halfway=MovementRouteSampler.Sample(route,5);var returnHalf=MovementRouteSampler.Sample(route,15);
        Assert(Math.Abs(halfway.Position.X-5)<.001f&&Math.Abs(returnHalf.Position.X-5)<.001f,"Route must interpolate both outbound and return legs");
        Assert(Math.Abs(MovementRouteSampler.Sample(route,20).Position.X)<.001f&&Math.Abs(MovementRouteSampler.Sample(route,25).Position.X-5)<.001f,"Closed loop must wrap continuously by simulation time");
        Assert(MovementRouteSampler.Sample(route,5).RotationDegrees>350||MovementRouteSampler.Sample(route,5).RotationDegrees<10,"Rotation interpolation must take the short angular path");
        var once=new MovementRouteDefinition("once","walker",false,new[]{new MovementRoutePoint(0,new Point2(1,2),0),new MovementRoutePoint(3,new Point2(4,5),90)});
        Assert(MovementRouteSampler.Sample(once,9).Position.X==4&&MovementRouteSampler.Sample(once,9).Position.Y==5,"Non-loop route must hold its final pose");
        var oldFormat=System.Text.RegularExpressions.Regex.Replace(LevelCodec.Encode(FirstStepTemplate.Blank()),",\"movementRoutes\":\\[\\]","");
        Assert(LevelCodec.Decode(oldFormat).movementRoutes.Count==0,"Older JSON without routes must remain route-free and readable");
        var badRoute=FirstStepTemplate.Blank();var moving=LevelCatalog.Create("walker",2,2);badRoute.items.Add(moving);
        badRoute.movementRoutes.Add(new LevelMovementRoute{actorId=moving.id,loop=true,waypoints=new List<LevelMovementWaypoint>{
            new LevelMovementWaypoint(0,2,2),new LevelMovementWaypoint(5,3,2)}});
        Rejected(()=>LevelCompiler.Compile(badRoute),"Loop route with a position seam must reject");
        passed.Add("L08 Movement route interpolation, loop continuity, stable simulation-time sampling, old JSON compatibility and invalid seam rejection");
        var invalid=LevelCodec.Clone(copy);invalid.items.Add(invalid.items[0]);Rejected(()=>LevelCodec.Save(Path.Combine(root,"invalid.json"),invalid),"Duplicate IDs must reject");
        invalid=LevelCodec.Clone(copy);invalid.items.Find(i=>i.type=="goal").requires="missing";Rejected(()=>LevelCompiler.Compile(invalid),"Dangling references must reject");
        invalid=FirstStepTemplate.Blank();invalid.items[0].rotation=float.NaN;Rejected(()=>LevelCompiler.Compile(invalid),"Nonfinite facing must reject");
        invalid=FirstStepTemplate.Blank();invalid.items.Add(LevelCatalog.Create("pole",0,0));Rejected(()=>LevelCompiler.Compile(invalid),"Unsafe spawn must reject");
        invalid=FirstStepTemplate.Blank();invalid.guide[1].x=invalid.guide[0].x;invalid.guide[1].y=invalid.guide[0].y;Rejected(()=>LevelCompiler.Compile(invalid),"Zero segment must reject");
        invalid=FirstStepTemplate.Blank();for(int i=0;i<5;i++)invalid.guide.Add(new LevelNode(i%2==0?10000:-10000,10000));Rejected(()=>LevelCompiler.Compile(invalid),"Path expansion must be bounded");
        invalid=FirstStepTemplate.Blank();var actor=LevelCatalog.Create("walker",2,2);actor.text="Hidden info";actor.trigger="enter";invalid.items.Add(actor);Rejected(()=>LevelCompiler.Compile(invalid),"Actor dialogue must retain legal perception gate");
        invalid=FirstStepTemplate.Blank();var inaudible=LevelCatalog.Create("fragment",0,3);inaudible.sound="";invalid.items.Add(inaudible);Rejected(()=>LevelCompiler.Compile(invalid),"Silent event fragment cannot be legally collected");
        invalid=FirstStepTemplate.Blank();var story=LevelCatalog.Create("story",0,0);story.trigger="start";story.requires=invalid.items[1].id;invalid.items.Add(story);Rejected(()=>LevelCompiler.Compile(invalid),"Start prerequisites reject");
        passed.Add("L02 Invalid IDs, references, facing, unsafe spawn, zero guides, expansion overflow and illegal disclosure reject");
        string future=Path.Combine(root,"future.json");string original=LevelCodec.Encode(copy).Replace("\"version\":1","\"version\":99");File.WriteAllText(future,original);
        Rejected(()=>LevelCodec.Load(future),"Future version rejects load");Rejected(()=>LevelCodec.Save(future,copy,false),"Future version protects overwrite including draft");Assert(File.ReadAllText(future)==original,"Future preserved");
        var draft=FirstStepTemplate.Blank();draft.guide.Clear();string draftPath=Path.Combine(root,"draft.json");LevelCodec.Save(draftPath,draft,false);Assert(LevelCodec.Load(draftPath).guide.Count==0,"Incomplete draft roundtrip");Rejected(()=>LevelCompiler.Compile(draft),"Draft cannot play");
        passed.Add("L03 Future version preserved; incomplete draft safely saves but cannot play");
        var corrupt=FirstStepTemplate.Blank();corrupt.items.Add(null);Rejected(()=>LevelCodec.Decode(LevelCodec.Encode(corrupt)),"Null object must be rejected before UI draw");
        corrupt=FirstStepTemplate.Blank();corrupt.guide.Add(null);Rejected(()=>LevelCodec.Decode(LevelCodec.Encode(corrupt)),"Null node must be rejected before UI draw");
        corrupt=FirstStepTemplate.Blank();corrupt.items[0].type="future-template";Rejected(()=>LevelCodec.Decode(LevelCodec.Encode(corrupt)),"Unknown template must be rejected before UI draw");
        var silent=FirstStepTemplate.Blank();silent.items[1].y=10;var quietCheckpoint=LevelCatalog.Create("checkpoint",0,0);quietCheckpoint.radius=.5f;quietCheckpoint.sound="";silent.items.Add(quietCheckpoint);
        var quietAudio=new Audio();using(var game=new DemoSession(quietAudio,quietAudio,new LocalSaveService(Path.Combine(root,"quiet")),null,LevelCompiler.Compile(silent)))
        {game.Start(false);game.Tick(Input(),.05);Assert(!quietAudio.Sounds.Exists(s=>s.SoundId=="feedback.checkpoint"),"No sound choice must remain silent");}
        passed.Add("L07 Corrupt GUI inputs reject before drawing; custom no-sound checkpoint stays silent");
        var level=FirstStepTemplate.Blank();level.name="独立注入验收";level.goalText="走到新终点。";var spawn=level.items[0];spawn.x=50;spawn.y=60;spawn.rotation=90;
        var goal=level.items[1];goal.x=50;goal.y=70;goal.radius=.5f;
        level.guide.Clear();level.guide.Add(new LevelNode(50,60));level.guide.Add(new LevelNode(50,70));
        var pole=LevelCatalog.Create("pole",53,65);pole.width=.8f;pole.rotation=30;level.items.Add(pole);
        var guard=LevelCatalog.Create("guard",54,62);level.items.Add(guard);
        var checkpoint=LevelCatalog.Create("checkpoint",50,65);checkpoint.radius=.4f;level.items.Add(checkpoint);
        var fragment=LevelCatalog.Create("fragment",50,70);fragment.radius=.7f;level.items.Add(fragment); // intentionally AFTER goal
        var message=LevelCatalog.Create("story",0,0);message.text="编辑后的关联对白";message.trigger="perceived";message.target=guard.id;message.relative=true;message.radius=10;message.sound="feedback.checkpoint";level.items.Add(message);
        string configured=Path.Combine(root,"configured.json");LevelCodec.Save(configured,level);var runtime=LevelCompiler.Compile(LevelCodec.Load(configured));
        var sourceDto=level.items.Find(i=>i.id==pole.id);sourceDto.x=999;Assert(runtime.Objects[0].Position.X==53,"Runtime definitions copied from DTO");
        Assert(runtime.Spawn.X==50&&runtime.SpawnFacing.X<-.99&&runtime.RuntimeId==level.id,"Configured spawn/facing/ID");
        var record=new Audio();string preview=Path.Combine(root,"preview");string production=Path.Combine(root,"production");var prod=new LocalSaveService(production);
        var settings=new UserSettings(DisplayMode.AudioOnly,OutputMode.Speakers,.27f,false,false);Assert(prod.SaveSettings(settings)==SaveStatus.Success,"Production settings seed");
        string productionSettings=File.ReadAllText(Path.Combine(production,"TingLu","settings.json"));
        using(var game=new DemoSession(record,record,new LocalSaveService(preview),null,runtime))
        {
            game.Start(false);Assert(game.Running&&game.Player.Current.Position.X==50,"Modified spawn reaches production assembly");
            Assert(game.Settings.MasterVolume==1&&game.Settings.Display==DisplayMode.Standard,"Preview cannot load formal settings");
            Assert(game.World.GetState(pole.id).Position.X==53,"Configured obstacle reached world");Assert(game.World.MaterialAt(new Point2(50,63))=="tactile_path","Edited tactile route enters world");
            Assert(game.World.GetState(guard.id).Definition.Category==ObjectCategory.Dynamic&&game.World.GetState(guard.id).Definition.MemoryPolicy==MemoryPolicy.Never,"Dynamic memory invariant");
            game.Tick(Input(tap:true),.05);
            Assert(record.Sounds.Exists(s=>s.SoundId=="ambience.guard"&&s.SourceId==guard.id),"Copied guard sound must retain selected resource");
            Assert(record.Lines.Exists(s=>s.Text==message.text),"Edited dialogue reached legal flow");Assert(record.Sounds.Exists(s=>s.SoundId=="feedback.checkpoint"),"Story-selected sound must actually play");
            Move(game,50,65);var resume=new LocalSaveService(preview).LoadResume();Assert(resume.Status==SaveStatus.Success&&new HashSet<string>(resume.Value.ActivatedCheckpointIds).Contains(checkpoint.id),"New checkpoint stable ID activated");
            Move(game,50,70);Assert(game.Menu.Screen==MenuScreen.Results,"No fixed gatekeeper requirement for independent goal");
            Assert(game.Progress.BestStars[level.id]==3,"Collection after goal in data still happens before scoring");Assert(new HashSet<string>(game.Progress.CollectedIds).Contains(fragment.id),"New fragment ID rewarded");
            game.ReturnToMenu();game.Start(true); // Finished resume must not start another result accidentally.
        }
        Assert(File.ReadAllText(Path.Combine(production,"TingLu","settings.json"))==productionSettings,"Preview must preserve formal settings");Assert(!File.Exists(Path.Combine(production,"TingLu","progress.json"))&&!File.Exists(Path.Combine(production,"TingLu","resume.json")),"Preview cannot touch formal progress/resume");
        passed.Add("L04 Reloaded independent data enters real DemoSession: spawn, heading, obstacles, tactile path, lawful dialogue, selected sounds, checkpoint, fragment and goal");
        passed.Add("L05 Preview settings/progress/resume IO including return/dispose stays in isolated directory");
        // Rebind relative stories by moving their parent before compiling; coordinates must follow.
        level.items.Find(i=>i.id==guard.id).x=58;var moved=LevelCompiler.Compile(level);foreach(var b in moved.Beats)if(b.Id==message.id)Assert(b.Position.X==58,"Relative bound area follows parent");
        var self=LevelCodec.DuplicateItem(fragment);Assert(self.target==self.id&&self.id!=fragment.id,"Single copied fragment self reference remapped");
        var builtin=FirstStepTemplate.CreateBuiltin();var originalCheckpoint=builtin.items.Find(i=>i.type=="checkpoint");
        var checkpointCopy=LevelCodec.DuplicateItem(originalCheckpoint);builtin.items.Add(checkpointCopy);LevelValidator.RequireValid(builtin);
        Assert(checkpointCopy.beatId!=originalCheckpoint.beatId&&checkpointCopy.checkpointId!=originalCheckpoint.checkpointId,"Copied checkpoint must own independent progression and save IDs");
        Assert(checkpointCopy.beatId==checkpointCopy.id,"Self progression identity must follow copied item identity");
        passed.Add("L06 Relative triggers follow moved parent and single-object copy remaps self references");
        string defaultPath=Path.Combine(Directory.GetCurrentDirectory(),"Assets/TingLu/Levels/Data/FirstStep.json");
        if(File.Exists(defaultPath))
        {
            var defaultDoc=LevelCodec.Load(defaultPath);var defaultLevel=LevelCompiler.Compile(defaultDoc);
            Assert(defaultDoc.id==FirstStepLevel.LevelId&&defaultLevel.RuntimeId==FirstStepLevel.LevelId&&!defaultLevel.Legacy,"FirstStep.json must be the configured non-Legacy default");
            Assert(defaultDoc.items.Exists(i=>i.id==FirstStepLevel.WalkerObjectId)&&defaultDoc.items.Exists(i=>i.id==FirstStepLevel.FragmentObjectId),"Default world IDs must preserve legacy save/object identity");
            Assert(defaultDoc.items.Exists(i=>i.checkpointId=="turn")&&defaultDoc.items.Exists(i=>i.checkpointId=="end"),"Default checkpoint IDs must preserve saved activation IDs");
            Assert(defaultLevel.MovementRoutes.Count==1&&defaultLevel.MovementRoutes[0].ActorId==FirstStepLevel.WalkerObjectId,"Default walker route must bind by its stable world ID");
            Assert(defaultDoc.guide[0].id=="l1-1/guide-node/01"&&defaultDoc.items.Exists(i=>i.beatId=="sound-fragment-01"),"Default node and progression IDs must be stable");
            passed.Add("L09 FirstStep.json compiles as non-Legacy default with save-compatible actor/checkpoint/beat IDs and configured walker route");
        }
        return passed.ToArray();
    }
}
