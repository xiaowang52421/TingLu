using System;
using System.Collections.Generic;
using TingLu.Contracts;

namespace TingLu.Levels
{
    public enum FirstStepTrigger { AtStart, NearBird, BeforeFirstPole, TurnCheckpoint, MeetWalker, CollectSoundFragment, NearBicycle, EndCheckpoint, MeetGatekeeper, Goal, EnterArea, PerceivedObject }
    public enum FirstStepBeatKind { SelfTalk, SoundLandmark, CaneLesson, Checkpoint, Walker, Collectible, StaticObstacle, Gatekeeper, Goal }
    public sealed class FirstStepBeat
    {
        public string Id { get; private set; }
        public FirstStepTrigger Trigger { get; private set; }
        public FirstStepBeatKind Kind { get; private set; }
        public Point2 Position { get; private set; }
        public float Radius { get; private set; }
        public string Text { get; private set; }
        public string SoundId { get; private set; }
        public string TargetId { get; private set; }
        public string RequiresId { get; private set; }
        public string CheckpointId { get; private set; }
        public FirstStepBeat(string id, FirstStepTrigger trigger, FirstStepBeatKind kind, Point2 position, float radius, string text, string soundId = null, string targetId = null, string requiresId = null,string checkpointId=null)
        { Id=ContractGuard.Id(id); Trigger=trigger; Kind=kind; Position=position; Radius=radius; Text=text; SoundId=soundId; TargetId=targetId; RequiresId=requiresId; CheckpointId=checkpointId; }
    }
    public sealed class FirstStepLevel
    {
        public const string LevelId="1-1";
        public const string BirdObjectId="l1-1/bird/01";
        public const string WalkerObjectId="l1-1/walker/01";
        public const string FragmentObjectId="l1-1/fragment/01";
        public const string BicycleObjectId="l1-1/bicycle/01";
        public const string GatekeeperObjectId="l1-1/gatekeeper/01";
        public string RuntimeId { get; private set; }
        public string GoalText { get; private set; }
        public bool Legacy { get; private set; }
        public Point2 Spawn { get; private set; }
        public Point2 SpawnFacing { get; private set; }
        public IReadOnlyList<FirstStepWorldObject> Objects { get; private set; }
        public IReadOnlyList<FirstStepGround> Ground { get; private set; }
        public IReadOnlyList<string> FragmentIds { get; private set; }
        public IReadOnlyList<FirstStepBeat> Beats { get; private set; }
        public IReadOnlyList<Segment2> GuidancePath { get; private set; }
        public IReadOnlyList<MovementRouteDefinition> MovementRoutes { get; private set; }
        public FirstStepLevel()
        {
            RuntimeId=LevelId;GoalText="先去老周那儿拿快递。";Legacy=true;Spawn=new Point2(0,0);SpawnFacing=new Point2(0,1);
            Objects=FirstStepWorldFactory.Objects();Ground=FirstStepWorldFactory.Ground();FragmentIds=new List<string>{FragmentObjectId}.AsReadOnly();
            Beats=new List<FirstStepBeat> {
                new FirstStepBeat("start-thought",FirstStepTrigger.AtStart,FirstStepBeatKind.SelfTalk,new Point2(0,1),4,"先去老周那儿拿快递。"),
                new FirstStepBeat("bird-landmark",FirstStepTrigger.NearBird,FirstStepBeatKind.SoundLandmark,new Point2(2,9),8,null,"bird-morning"),
                new FirstStepBeat("cane-lesson",FirstStepTrigger.BeforeFirstPole,FirstStepBeatKind.CaneLesson,new Point2(0,8),8,"想确认前面的路况，可以轻敲一下杖。"),
                new FirstStepBeat("turn-checkpoint",FirstStepTrigger.TurnCheckpoint,FirstStepBeatKind.Checkpoint,new Point2(0,40),2,"转过弯，歇口气再走。",checkpointId:"turn"),
                new FirstStepBeat("walker-demo",FirstStepTrigger.MeetWalker,FirstStepBeatKind.Walker,new Point2(0,36),3,"早啊。", "walker-greeting"),
                new FirstStepBeat("sound-fragment-01",FirstStepTrigger.CollectSoundFragment,FirstStepBeatKind.Collectible,new Point2(5,40.8f),1.4f,"花坛里有只麻雀。","sparrow-fragment"),
                new FirstStepBeat("end-checkpoint",FirstStepTrigger.EndCheckpoint,FirstStepBeatKind.Checkpoint,new Point2(22,40),2,"快到了。",checkpointId:"end"),
                new FirstStepBeat("gatekeeper",FirstStepTrigger.MeetGatekeeper,FirstStepBeatKind.Gatekeeper,new Point2(25,40),4,"哟，出门啊？","gatekeeper-greeting"),
                new FirstStepBeat("goal",FirstStepTrigger.Goal,FirstStepBeatKind.Goal,new Point2(28,40),1.8f,"到了。","level-complete")
            }.AsReadOnly();
            GuidancePath=new List<Segment2> {
                new Segment2(new Point2(0,0),new Point2(0,40)),
                new Segment2(new Point2(0,40),new Point2(25,40)),
                new Segment2(new Point2(25,40),new Point2(28,40))
            }.AsReadOnly();
            MovementRoutes=new List<MovementRouteDefinition>().AsReadOnly();
        }
        public FirstStepLevel(string id,string goal,Point2 spawn,Point2 facing,IEnumerable<FirstStepBeat> beats,IEnumerable<Segment2> path,
            IEnumerable<FirstStepWorldObject> objects,IEnumerable<FirstStepGround> ground,IEnumerable<string> fragments)
            : this(id,goal,spawn,facing,beats,path,objects,ground,fragments,new MovementRouteDefinition[0])
        {
        }
        public FirstStepLevel(string id,string goal,Point2 spawn,Point2 facing,IEnumerable<FirstStepBeat> beats,IEnumerable<Segment2> path,
            IEnumerable<FirstStepWorldObject> objects,IEnumerable<FirstStepGround> ground,IEnumerable<string> fragments,
            IEnumerable<MovementRouteDefinition> movementRoutes)
        {
            RuntimeId=ContractGuard.Id(id);GoalText=goal;Spawn=spawn;SpawnFacing=facing;Legacy=false;
            Beats=ContractGuard.Copy(beats);GuidancePath=ContractGuard.Copy(path);Objects=ContractGuard.Copy(objects);Ground=ContractGuard.Copy(ground);FragmentIds=ContractGuard.Copy(fragments);MovementRoutes=ContractGuard.Copy(movementRoutes);
        }
    }
}
