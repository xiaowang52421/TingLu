#if UNITY_INCLUDE_TESTS
using System;
using System.Collections.Generic;
using NUnit.Framework;
using TingLu.Contracts;
using TingLu.Config;
using TingLu.Memory;
using TingLu.Perception;

namespace TingLu.Tests.PerceptionMemory
{
    public sealed class PerceptionMemoryRulesTests
    {
        [Test]
        public void PassiveSoundKeepsLocationButConfirmsNoGeometry()
        {
            var f=new Fixture();f.Acoustics.Values.Add(new AcousticQueryResult("bell","bell",true,
                new Point2(0,2),new Point2(0,1),2,1,0,"clear",0));
            f.Service.Recompute();
            Assert.AreEqual(1,f.Service.Current.Observations.Count);
            Assert.AreEqual(InformationKind.SoundLocation,f.Service.Current.Observations[0].Kind);
            Assert.AreEqual(0,f.Service.Current.Observations[0].ConfirmedContour.Count);
            Assert.AreEqual(0,f.Memory.Fragments.Count);
        }

        [Test]
        public void FanUsesHalfOfConfiguredAngle()
        {
            var f=new Fixture();
            f.Acoustics.Values.Add(Sound("edge",37.5));
            f.Acoustics.Values.Add(Sound("outside",38));
            f.Service.Recompute();
            Assert.AreEqual(1,f.Service.Current.Observations.Count);
            Assert.AreEqual("edge",f.Service.Current.Observations[0].ObjectId);
        }

        [Test]
        public void ProbeClipsGeometryBehindOccluderAndNeverStoresDynamic()
        {
            var f=new Fixture();
            f.World.Register(Definition("static",ObjectCategory.Static,MemoryPolicy.ConfirmedStaticGeometry,
                new Segment2(new Point2(-0.2f,0),new Point2(0.2f,0))),new Point2(2,0),0,false);
            f.World.Register(Definition("walker",ObjectCategory.Dynamic,MemoryPolicy.Never,
                new Segment2(new Point2(-0.2f,0),new Point2(0.2f,0))),new Point2(0,2),0,false);
            f.World.Walls.Add(new Segment2(new Point2(1,-3),new Point2(1,3)));
            f.Service.Probe(Tap());
            Assert.AreEqual(1,f.Service.Current.Observations.Count,
                "the visible dynamic walker should have a short-lived echo");
            Assert.AreEqual("walker",f.Service.Current.Observations[0].ObjectId,
                "the static contour behind the wall must remain hidden");
            Assert.AreEqual(ObjectCategory.Dynamic,f.Service.Current.Observations[0].Category);
            Assert.AreEqual(ObservationSource.CaneEcho,f.Service.Current.Observations[0].Source);
            Assert.AreEqual(InformationKind.LocalGeometry,f.Service.Current.Observations[0].Kind);
            Assert.AreEqual(0,f.Memory.Fragments.Count);
            Assert.Greater(f.Service.LastProbeCoverage.Cells.Count,0);
        }

        [Test]
        public void CoveredOldPartIsClearedAndUncoveredPartRemains()
        {
            var store=new StaticMemoryStore();
            var observation=new Observation("fence",ObjectCategory.Static,MemoryPolicy.ConfirmedStaticGeometry,
                ObservationSource.CaneEcho,InformationKind.LocalGeometry,new Point2(0,0),
                new[]{new Segment2(new Point2(0,0),new Point2(3,0))},"metal",0,0.8);
            store.Observe(observation);
            var coverage=new ProbeCoverage("probe",1,new[]{new ProbeCell(new[]{new Point2(-1,-1),new Point2(1,-1),new Point2(1,1),new Point2(-1,1)})},new Segment2[0]);
            store.Clear(coverage);
            Assert.AreEqual(1,store.Fragments.Count);
            Assert.GreaterOrEqual(store.Fragments[0].WorldContour[0].A.X,0.99f);
            Assert.GreaterOrEqual(store.Fragments[0].WorldContour[0].B.X,0.99f);
        }

        private static AcousticQueryResult Sound(string id,double degrees)
        {double a=degrees*Math.PI/180;return new AcousticQueryResult(id,id,true,new Point2((float)Math.Sin(a)*2,(float)Math.Cos(a)*2),new Point2(0,1),2,1,0,"clear",0);}
        private static GameplayEvent Tap(){return new GameplayEvent("tap",0,"l1-1",null,new Point2(0,0),SemanticType.CaneTap,FeedbackPriority.Navigation,new CanePayload(7));}
        private static ObjectDefinition Definition(string id,ObjectCategory category,MemoryPolicy policy,Segment2 contour)
        {return new ObjectDefinition(id,category,policy,new[]{contour},"metal",null);}

        private sealed class Fixture
        {
            public readonly FakeWorld World=new FakeWorld();public readonly FakeAcoustics Acoustics=new FakeAcoustics();
            public readonly StaticMemoryStore Memory=new StaticMemoryStore();public readonly FakeClock Clock=new FakeClock();public readonly FakePlayer Player=new FakePlayer();
            public readonly PerceptionService Service;
            public Fixture(){var config=new GameplayConfig();Service=new PerceptionService(World,Acoustics,Player,Clock,config,Memory,
                Memory.Observe,Memory.Clear,"l1-1");}
        }
        private sealed class FakeClock:IGameClock{public double Now{get;set;}public bool Paused{get;set;}}
        private sealed class FakePlayer:IPlayerStateReader{public PlayerState Current{get{return new PlayerState(PlayerMainState.Active,ActionMode.Walking,new Point2(0,0),new Point2(0,1),100,100,0,0,0);}}}
        private sealed class FakeAcoustics:IAcousticQuery{public readonly List<AcousticQueryResult> Values=new List<AcousticQueryResult>();public IReadOnlyList<AcousticQueryResult> Sample(Point2 p,Point2 f,double t){return Values.AsReadOnly();}}
        private sealed class FakeWorld:IWorldQuery,IWorldOcclusionQuery
        {
            private readonly List<WorldObjectState> items=new List<WorldObjectState>();public readonly List<Segment2> Walls=new List<Segment2>();
            public void Register(ObjectDefinition d,Point2 p,float r,bool sound){items.Add(new WorldObjectState(d,p,r,sound));}
            public IReadOnlyList<WorldObjectState> QueryNearby(Point2 c,float r){var found=new List<WorldObjectState>();foreach(var x in items){float dx=x.Position.X-c.X,dy=x.Position.Y-c.Y;if(dx*dx+dy*dy<=r*r)found.Add(x);}return found.AsReadOnly();}
            public string MaterialAt(Point2 p){return "sidewalk";}
            public bool IsOccluded(Point2 a,Point2 b){foreach(var w in Walls)if(Intersects(a,b,w.A,w.B))return true;return false;}
            public IReadOnlyList<Segment2> QueryOcclusionBoundaries(Point2 center,float radius)
            {
                var result=new List<Segment2>(Walls);
                foreach(WorldObjectState item in items)
                    foreach(Segment2 local in item.Definition.LocalContour)
                        result.Add(new Segment2(new Point2(item.Position.X+local.A.X,item.Position.Y+local.A.Y),
                            new Point2(item.Position.X+local.B.X,item.Position.Y+local.B.Y)));
                return result.AsReadOnly();
            }
            private static bool Intersects(Point2 a,Point2 b,Point2 c,Point2 d){float d1=Cross(a,b,c),d2=Cross(a,b,d),d3=Cross(c,d,a),d4=Cross(c,d,b);return d1*d2<=0&&d3*d4<=0;}
            private static float Cross(Point2 a,Point2 b,Point2 c){return(b.X-a.X)*(c.Y-a.Y)-(b.Y-a.Y)*(c.X-a.X);}
        }
    }
}
#endif
