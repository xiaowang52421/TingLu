using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using TingLu.Bootstrap;
using TingLu.Config;
using TingLu.Contracts;
using TingLu.Levels;
using TingLu.Persistence;
using TingLu.UI;
using TingLu.World;

namespace TingLu.Tests.Upgrade.Integration
{
    public sealed class StreetCollisionRulesTests
    {
        private string root;
        [SetUp] public void Setup(){root=Path.Combine(Path.GetTempPath(),"tinglu-contact-"+Guid.NewGuid().ToString("N"));}
        [TearDown] public void Cleanup(){if(Directory.Exists(root))Directory.Delete(root,true);}
        private sealed class Audio : IAudioPlayback
        {
            public event Action<AudioReceipt> Receipt {add{} remove{}}
            public void Play(AudioRequest r){} public void Stop(string id){} public void Reset(int generation){}
            public void SetPaused(bool paused){} public void UpdateListener(Point2 p,Point2 f){} public void Dispose(){}
        }
        private static StreetObject Actor(StreetActorKind kind, Point2 p, bool legacyHazard=true)
        {return new StreetObject("actor",StreetKind.Active,p,StreetGeometry.Rect(p.X-.25f,p.Y-.25f,p.X+.25f,p.Y+.25f),"metal",true,legacyHazard,false,0,1,
            kind==StreetActorKind.None?null:kind==StreetActorKind.Pedestrian?"street.walker":"street.car",true,kind==StreetActorKind.Pedestrian?PerceptionSemantic.Person:PerceptionSemantic.Threat,100,10,kind);}
        private StreetSession Session(StreetObject actor, StreetRoute[] routes, bool checkpoint=false)
        {
            var spawn=new PlayerState(PlayerMainState.Active,ActionMode.Walking,new Point2(-2,0),new Point2(1,0),100,100,0,0,0);
            var doc=new StreetLevelDocument{id="contact.fixture",title="碰撞验证",goalText="向前走",spawnX=-2,spawnY=0,facingX=1,facingY=0,goalX=9,goalY=9,goalRadius=.4f};
            var surface=new StreetSurfaceData{id="ground",materialId="sidewalk"};
            foreach(var v in StreetGeometry.Rect(-10,-10,10,10))surface.polygon.Add(StreetPointData.From(v));doc.surfaces.Add(surface);
            var data=new StreetObjectData{id=actor.Id,kind=actor.Kind,actorKind=actor.ActorKind,position=StreetPointData.From(actor.Position),materialId=actor.MaterialId,
                solid=actor.Solid,vehicleHazard=actor.VehicleHazard,soundId=actor.SoundId,soundActive=actor.SoundId!=null&&actor.SoundActive,semantic=actor.Semantic,contactDamage=actor.ContactDamage,contactStun=actor.ContactStun};
            foreach(var v in actor.Polygon)data.polygon.Add(StreetPointData.From(v));doc.objects.Add(data);
            foreach(var route in routes){var r=new StreetRouteData{objectId=route.ObjectId,speed=route.Speed,signalAxis=route.SignalAxis,offset=route.Offset,loop=route.Loop,stopLineDistance=route.StopLineDistance};foreach(var v in route.Points)r.points.Add(StreetPointData.From(v));doc.routes.Add(r);}
            if(checkpoint)doc.checkpoints.Add(new StreetCheckpointData{id="safe",position=StreetPointData.From(spawn.Position),facing=StreetPointData.From(spawn.Facing),radius=.5f});
            var level=StreetLevelCodec.Compile(StreetLevelCodec.Decode(StreetLevelCodec.Encode(doc)));
            var s=new StreetSession(level,new Audio(),null,new StreetSaveService(root,"isolated"));s.Start(false);return s;
        }

        [TestCase(StreetActorKind.Pedestrian,false)] [TestCase(StreetActorKind.Pedestrian,true)]
        [TestCase(StreetActorKind.Car,false)] [TestCase(StreetActorKind.Car,true)]
        [TestCase(StreetActorKind.Bicycle,false)] [TestCase(StreetActorKind.Bicycle,true)]
        [TestCase(StreetActorKind.ElectricBike,false)] [TestCase(StreetActorKind.ElectricBike,true)]
        [TestCase(StreetActorKind.None,false)]
        public void ContactMatrixUsesPhysicalMotionAndFeedbackWithoutLegacyDamage(StreetActorKind kind,bool moving)
        {
            var p=moving?new Point2(-4,0):new Point2(0,0);
            var actor=Actor(kind,p,kind!=StreetActorKind.None);
            var routes=moving?new[]{new StreetRoute(actor.Id,new[]{p,new Point2(4,0)},4,null,0,false)}:new StreetRoute[0];
            using(var s=Session(actor,routes))
            {
                for(int i=0;i<180&&s.Collisions==0;i++)s.Tick(new StreetInputFrame(moving?new Point2():new Point2(1,0)),.02);
                bool danger=moving&&kind!=StreetActorKind.Pedestrian;
                Assert.AreEqual(1,s.Collisions);Assert.AreEqual(danger?1:0,s.Recoveries);
                Assert.AreEqual(1,s.CollisionFeedback.TriggerCount);Assert.Greater(s.CollisionFeedback.Alpha,0);
                Assert.AreEqual(100,s.Player.Current.Courage);Assert.AreEqual(PlayerMainState.Active,s.Player.Current.MainState);
                if(danger)Assert.AreEqual(-2,s.Player.Current.Position.X,.0001,"no checkpoint restores safe spawn immediately");
                else if(!moving)Assert.Less(s.Player.Current.Position.X,-.42f,"parked actors remain blocking even with a legacy hazard flag");
            }
        }
        [Test] public void ContinuousWallContactReentryPauseSaveAndMenuHaveConsistentLifecycle()
        {
            using(var s=Session(Actor(StreetActorKind.None,new Point2(),false),new StreetRoute[0]))
            {
                for(int i=0;i<180;i++)s.Tick(new StreetInputFrame(new Point2(1,0)),.02);
                Assert.AreEqual(1,s.Collisions);Assert.AreEqual(1,s.CollisionFeedback.TriggerCount);Assert.AreEqual(0,s.CollisionFeedback.Alpha);
                for(int i=0;i<40;i++)s.Tick(new StreetInputFrame(new Point2(-1,0)),.02);
                for(int i=0;i<60&&s.Collisions<2;i++)s.Tick(new StreetInputFrame(new Point2(1,0)),.02);
                Assert.AreEqual(2,s.Collisions);Assert.Greater(s.CollisionFeedback.Alpha,0);
                float alpha=s.CollisionFeedback.Alpha;var position=s.Player.Current.Position;
                s.Pause();s.Tick(new StreetInputFrame(new Point2(1,0)),4);
                Assert.AreEqual(alpha,s.CollisionFeedback.Alpha);Assert.AreEqual(position.X,s.Player.Current.Position.X);
                s.Resume();s.Tick(new StreetInputFrame(new Point2()),.1);Assert.AreEqual(2,s.Collisions);
                s.Save();s.Start(true);Assert.AreEqual(0,s.CollisionFeedback.Alpha);Assert.AreEqual(0,s.CollisionFeedback.TriggerCount);
                s.Tick(new StreetInputFrame(new Point2(1,0)),.02);Assert.AreEqual(2,s.Collisions,"loaded touching pose is not a new collision");
                s.ReturnToMenu();Assert.AreEqual(0,s.CollisionFeedback.Alpha);
            }
        }
        [Test] public void MovingHazardRestoresLatestCheckpointOnceAndKeepsItsGlow()
        {
            var actor=Actor(StreetActorKind.Car,new Point2(-4,0));
            using(var s=Session(actor,new[]{new StreetRoute(actor.Id,new[]{new Point2(-4,0),new Point2(4,0)},4,null,0,false)},true))
            {
                s.Tick(new StreetInputFrame(new Point2()),.02);Assert.AreEqual("safe",s.CheckpointId);
                for(int i=0;i<90&&s.Recoveries==0;i++)s.Tick(new StreetInputFrame(new Point2()),.02);
                Assert.AreEqual(1,s.Recoveries);Assert.Greater(s.CollisionFeedback.Alpha,0);Assert.AreEqual(-2,s.Player.Current.Position.X);
                for(int i=0;i<5;i++)s.Tick(new StreetInputFrame(new Point2()),.02);
                Assert.AreEqual(1,s.Recoveries,"restoring traffic must not replay the previous sweep");
                s.Pause();float a=s.CollisionFeedback.Alpha;s.Tick(new StreetInputFrame(new Point2()),1);Assert.AreEqual(a,s.CollisionFeedback.Alpha);
            }
        }
        [Test] public void RouteEndAndSignalWaitAreOrdinaryButMovingSubintervalAndRestartAreDangerous()
        {
            var config=new StreetConfig();var car=Actor(StreetActorKind.Car,new Point2(-2,0));
            var ended=new StreetWorld(new StreetSurface[0],new[]{car},new[]{new StreetRoute("actor",new[]{new Point2(-2,0),new Point2(0,0)},10,null,0,false)},config);
            ended.Tick(1,false);Assert.IsTrue(ended.VehicleContacts(new Point2(),new Point2(),.18f).Single().Vehicle,"movement before end-of-frame stop remains dangerous");
            ended.VehicleContacts(new Point2(3,0),new Point2(3,0),.18f);ended.Tick(.02,false);
            Assert.IsFalse(ended.VehicleContacts(new Point2(1,0),new Point2(),.18f).Single().Vehicle);
            var waiting=new StreetWorld(new StreetSurface[0],new[]{car},new[]{new StreetRoute("actor",new[]{new Point2(-2,0),new Point2(4,0)},1,"ew",1,false,1)},config);
            waiting.Tick(1,false);Assert.IsFalse(waiting.VehicleContacts(new Point2(0,0),new Point2(-1,0),.18f).Single().Vehicle);
            Assert.IsFalse(waiting.Capture().Objects.Single().SoundActive);
            waiting.VehicleContacts(new Point2(5,0),new Point2(5,0),.18f);
            waiting.Tick(17,false);Assert.IsTrue(waiting.VehicleContacts(new Point2(-.5f,0),new Point2(-.5f,0),.18f).Single().Vehicle);
            Assert.IsTrue(waiting.Capture().Objects.Single().SoundActive);
        }
        [TestCase(false)] [TestCase(true)]
        public void SessionStoppedVehicleBlocksAndFlashesWithoutRecoveryDespiteLegacyEffects(bool signalWait)
        {
            var actor=Actor(StreetActorKind.ElectricBike,new Point2());
            var route=new StreetRoute(actor.Id,new[]{new Point2(),new Point2(1,0)},2,
                signalWait?"ew":null,1,false,signalWait?(float?)1:null);
            using(var s=Session(actor,new[]{route}))
            {
                for(int i=0;i<200&&s.Collisions==0;i++)s.Tick(new StreetInputFrame(new Point2(1,0)),.02);
                Assert.AreEqual(1,s.Collisions);Assert.AreEqual(0,s.Recoveries);Assert.AreEqual(1,s.CollisionFeedback.TriggerCount);
                Assert.Greater(s.CollisionFeedback.Alpha,0);Assert.Less(s.Player.Current.Position.X,.58f);
                Assert.AreEqual(100,s.Player.Current.Courage);Assert.AreEqual(PlayerMainState.Active,s.Player.Current.MainState);
                Assert.IsFalse(s.World.Capture().Objects.Single().SoundActive);
            }
        }
        [Test] public void RestoredOverlapSuppressesTeleportContactUntilSeparation()
        {
            var car=Actor(StreetActorKind.Car,new Point2());
            var world=new StreetWorld(new StreetSurface[0],new[]{car},new[]{new StreetRoute("actor",new[]{new Point2(),new Point2(5,0)},1)},new StreetConfig());
            world.ImportTraffic(world.ExportTraffic());world.Tick(.02,false);
            Assert.IsEmpty(world.VehicleContacts(new Point2(),new Point2(),.18f));
            world.Tick(.02,false);Assert.IsEmpty(world.VehicleContacts(new Point2(),new Point2(),.18f));
            world.VehicleContacts(new Point2(3,0),new Point2(3,0),.18f);world.Tick(.02,false);
            Assert.IsTrue(world.VehicleContacts(new Point2(3,0),new Point2(),.18f).Single().Vehicle);
        }
        [Test] public void LegacySchemaTwoWithoutActorKindKeepsPeopleOrdinaryAndUnroutedVehiclesParked()
        {
            var doc=StreetLevelCodec.FromLevel(StreetLevelFactory.Create());
            string current=StreetLevelCodec.Encode(doc);
            string legacy=System.Text.RegularExpressions.Regex.Replace(current,"\\\"actorKind\\\":\\d+,?","");
            legacy=legacy.Replace(",}","}");
            Assert.IsFalse(legacy.Contains("actorKind"));
            var loaded=StreetLevelCodec.Compile(StreetLevelCodec.Decode(legacy));
            var car=loaded.Objects.Single(o=>o.Id=="vehicle.parked.se");
            Assert.AreEqual(StreetActorKind.Car,car.ActorKind);Assert.IsTrue(car.Solid);
            var person=loaded.Objects.Single(o=>o.Id=="walker.north.west");
            Assert.AreEqual(StreetActorKind.Pedestrian,person.ActorKind);Assert.IsFalse(person.IsVehicle);
            var world=new StreetWorld(loaded.Surfaces,loaded.Objects,loaded.Routes,new StreetConfig());
            world.Tick(.02,false);
            var hits=world.VehicleContacts(car.Position,car.Position,.18f);
            Assert.IsTrue(hits.Any(h=>h.ObjectId==car.Id&&!h.Vehicle));
            Assert.IsFalse(world.Capture().Objects.Single(o=>o.Id==car.Id).SoundActive);
        }
    }
}
