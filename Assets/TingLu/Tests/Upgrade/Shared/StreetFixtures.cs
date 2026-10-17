using System;
using System.Collections.Generic;
using TingLu.Contracts;
using TingLu.Config;
using TingLu.World;

namespace TingLu.Tests.Upgrade
{
    public sealed class FixtureClock : IGameClock
    {public double Now{get;set;} public bool Paused{get;set;} }
    public sealed class FixtureAcoustics : IAcousticQuery
    {
        public IReadOnlyList<AcousticQueryResult> Values=new AcousticQueryResult[0];
        public IReadOnlyList<AcousticQueryResult> Sample(Point2 p,Point2 f,double t){return Values;}
    }
    public sealed class FixtureMemory : IStreetMemory
    {
        public StreetMemoryFrame Current{get;private set;}=StreetMemoryFrame.Empty;
        public void Apply(StreetProbeResult p){Current=new StreetMemoryFrame(p.Surfaces,p.Objects);}
        public StreetMemoryFrame Export(){return Current;}
        public void Import(StreetMemoryFrame m,bool stale){Current=m;}
        public void MarkStale(){}
    }
    public static class StreetFixtures
    {
        public static IStreetWorld World()
        {var f=Road();return new StreetWorld(f.Surfaces,f.Objects,new StreetRoute[0],new StreetConfig());}
        public static StreetWorldFrame Road(double time=0)
        {return new StreetWorldFrame(time,new[]{new StreetSurface("road","sidewalk",StreetGeometry.Rect(-10,-10,10,10))},new[]{new StreetObject("wall",StreetKind.Permanent,new Point2(0,3),StreetGeometry.Rect(-2,3,2,3.3f),"brick",true,false,true,0.7f,2),new StreetObject("box",StreetKind.Temporary,new Point2(1,1),StreetGeometry.Rect(0.7f,0.7f,1.3f,1.3f),"cardboard",true,false,false,0.1f,0.5f),new StreetObject("bird",StreetKind.Active,new Point2(-2,0),new Point2[0],"brick",false,false,false,0,0,"street.bird",true,PerceptionSemantic.Ordinary)});}
        public static PlayerState Player(Point2 p=default(Point2))
        {return new PlayerState(PlayerMainState.Active,ActionMode.Walking,p,new Point2(0,1),100,100,0,0,0);}
        public static StreetTrafficState Traffic(double time=0){return new StreetTrafficState(time,new string[0],new double[0]);}
    }
}
