using System;
using System.Collections.Generic;
using TingLu.Contracts;

namespace TingLu.Levels
{
    public static class LevelCompiler
    {
        public static bool HasBeat(LevelItem i){return i.type=="goal"||i.type=="checkpoint"||i.type=="fragment"||i.type=="story"||!string.IsNullOrEmpty(i.text);}
        public static Point2 Position(LevelDocument d,LevelItem i)
        {if(i.relative){foreach(var parent in d.items)if(parent.id==i.target)return new Point2(parent.x+i.x,parent.y+i.y);}return new Point2(i.x,i.y);}
        public static FirstStepLevel Compile(LevelDocument d)
        {
            LevelValidator.RequireValid(d);LevelItem spawn=d.items.Find(i=>i.type=="spawn");
            var objects=new List<FirstStepWorldObject>();var ground=new List<FirstStepGround>();var beats=new List<FirstStepBeat>();var path=new List<Segment2>();var fragments=new List<string>();
            var movementRoutes=new List<MovementRouteDefinition>();
            foreach(var i in d.items)
            {
                var p=Position(d,i);
                if(LevelCatalog.IsWorld(i.type))
                {
                    bool actor=i.type=="walker"||i.type=="guard",ambient=i.type=="bird"||i.type=="fragment";
                    var category=actor?ObjectCategory.Dynamic:ambient?ObjectCategory.Event:ObjectCategory.Static;
                    var semantic=actor?PerceptionSemantic.Person:i.type=="fragment"?PerceptionSemantic.Interaction:PerceptionSemantic.Ordinary;
                    var def=new ObjectDefinition(i.id,category,category==ObjectCategory.Static?MemoryPolicy.ConfirmedStaticGeometry:MemoryPolicy.Never,
                        ambient?new Segment2[0]:Box(i.width,i.height),i.material,string.IsNullOrEmpty(i.sound)?null:i.sound,semantic);
                    objects.Add(new FirstStepWorldObject(def,p,i.rotation,!string.IsNullOrEmpty(i.sound)));
                }
                if(i.type=="ground")ground.Add(new FirstStepGround(i.material,Rectangle(p,i.width,i.height,i.rotation)));
                if(i.type=="fragment")fragments.Add(i.id);
                if(HasBeat(i))
                {
                    var trigger=i.trigger=="start"?FirstStepTrigger.AtStart:i.trigger=="perceived"?FirstStepTrigger.PerceivedObject:FirstStepTrigger.EnterArea;
                    var kind=i.type=="checkpoint"?FirstStepBeatKind.Checkpoint:i.type=="fragment"?FirstStepBeatKind.Collectible:i.type=="goal"?FirstStepBeatKind.Goal:FirstStepBeatKind.SelfTalk;
                    beats.Add(new FirstStepBeat(string.IsNullOrEmpty(i.beatId)?i.id:i.beatId,trigger,kind,p,i.radius,i.text,
                        i.type=="story"||i.type=="goal"||i.type=="checkpoint"?i.sound:null,i.target,i.requires,
                        i.type=="checkpoint"&&!string.IsNullOrEmpty(i.checkpointId)?i.checkpointId:null));
                }
            }
            foreach(LevelMovementRoute route in d.movementRoutes)
            {
                var points=new List<MovementRoutePoint>();
                foreach(LevelMovementWaypoint point in route.waypoints)
                    points.Add(new MovementRoutePoint(point.timeSeconds,new Point2(point.x,point.y),point.rotationDegrees));
                movementRoutes.Add(new MovementRouteDefinition(route.id,route.actorId,route.loop,points));
            }
            for(int k=1;k<d.guide.Count;k++)
            {
                var a=new Point2(d.guide[k-1].x,d.guide[k-1].y);var b=new Point2(d.guide[k].x,d.guide[k].y);path.Add(new Segment2(a,b));
                float distance=LevelValidator.Distance(a.X,a.Y,b.X,b.Y);int pieces=Math.Max(1,(int)Math.Ceiling(distance/3));
                for(int j=0;j<pieces;j++)
                {
                    var start=Lerp(a,b,(float)j/pieces);var end=Lerp(a,b,(float)(j+1)/pieces);var center=Lerp(start,end,.5f);
                    objects.Add(new FirstStepWorldObject(new ObjectDefinition(d.id+"/guide/"+d.guide[k].id+"/"+j,ObjectCategory.Static,MemoryPolicy.Never,
                        new[]{new Segment2(new Point2(start.X-center.X,start.Y-center.Y),new Point2(end.X-center.X,end.Y-center.Y))},"tactile_path",null,PerceptionSemantic.Guide),center,0,false));
                    float dx=end.X-start.X,dy=end.Y-start.Y,len=LevelValidator.Distance(start.X,start.Y,end.X,end.Y),half=d.guideWidth*.5f;
                    float ux=dx/len,uy=dy/len,extension=Math.Min(half,len*.25f),nx=-uy*half,ny=ux*half;
                    var aa=new Point2(start.X-ux*extension,start.Y-uy*extension);var bb=new Point2(end.X+ux*extension,end.Y+uy*extension);
                    ground.Add(new FirstStepGround("tactile_path",new[]{new Point2(aa.X+nx,aa.Y+ny),new Point2(aa.X-nx,aa.Y-ny),new Point2(bb.X-nx,bb.Y-ny),new Point2(bb.X+nx,bb.Y+ny)}));
                }
            }
            foreach(var node in d.guide)ground.Add(new FirstStepGround("tactile_path",Rectangle(new Point2(node.x,node.y),d.guideWidth,d.guideWidth,0)));
            // Goal resolves after ordinary events at the same location (including collections).
            var ordered=new List<FirstStepBeat>();foreach(var beat in beats)if(beat.Kind!=FirstStepBeatKind.Goal)ordered.Add(beat);foreach(var beat in beats)if(beat.Kind==FirstStepBeatKind.Goal)ordered.Add(beat);
            double r=spawn.rotation*Math.PI/180;
            return new FirstStepLevel(d.id,d.goalText,new Point2(spawn.x,spawn.y),new Point2(-(float)Math.Sin(r),(float)Math.Cos(r)),ordered,path,objects,ground,fragments,movementRoutes);
        }
        private static Point2 Lerp(Point2 a,Point2 b,float t){return new Point2(a.X+(b.X-a.X)*t,a.Y+(b.Y-a.Y)*t);}
        private static Segment2[] Box(float width,float height)
        {var p=Rectangle(new Point2(0,0),width,height,0);return new[]{new Segment2(p[0],p[1]),new Segment2(p[1],p[2]),new Segment2(p[2],p[3]),new Segment2(p[3],p[0])};}
        private static Point2[] Rectangle(Point2 center,float width,float height,float rotation)
        {double r=rotation*Math.PI/180;var result=new Point2[4];int k=0;foreach(var p in new[]{new Point2(-width/2,-height/2),new Point2(width/2,-height/2),new Point2(width/2,height/2),new Point2(-width/2,height/2)})result[k++]=new Point2(center.X+p.X*(float)Math.Cos(r)-p.Y*(float)Math.Sin(r),center.Y+p.X*(float)Math.Sin(r)+p.Y*(float)Math.Cos(r));return result;}
    }
}
