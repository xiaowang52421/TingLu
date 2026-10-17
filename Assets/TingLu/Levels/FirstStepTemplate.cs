using System;
using TingLu.Contracts;

namespace TingLu.Levels
{
    public static class FirstStepTemplate
    {
        public static LevelDocument Create()
        {
            var d=LevelCodec.Duplicate(CreateBuiltin());d.name="第一步（可编辑模板）";return d;
        }
        public static LevelDocument CreateBuiltin()
        {
            var d=new LevelDocument{id=FirstStepLevel.LevelId,name="第一步",goalText="先去老周那儿拿快递。"};
            var spawn=LevelCatalog.Create("spawn",0,0);spawn.id="l1-1/spawn/01";d.items.Add(spawn);
            foreach(var o in FirstStepWorldFactory.Objects())
            {
                if(o.Definition.Semantic==PerceptionSemantic.Guide)continue;
                string id=o.Definition.ObjectId;string type=id.Contains("pole")?"pole":id.Contains("bicycle")?"bicycle":id.Contains("gatekeeper")?"guard":id.Contains("walker")?"walker":id.Contains("fragment")?"fragment":"bird";
                var item=LevelCatalog.Create(type,o.Position.X,o.Position.Y);item.id=id;item.rotation=o.Rotation;item.target=id;d.items.Add(item);
            }
            // The real world path bypasses the pole; do not import the old straight guidance skeleton.
            foreach(var p in new[]{new Point2(0,0),new Point2(0,13.8f),new Point2(-.65f,14.4f),new Point2(-.65f,15.6f),new Point2(0,16.4f),new Point2(0,40),new Point2(25,40),new Point2(28,40)})d.guide.Add(new LevelNode(p.X,p.Y));
            int groundIndex=0;
            foreach(var g in FirstStepWorldFactory.Ground())
            {
                if(g.Material=="tactile_path")continue;float l=float.MaxValue,r=float.MinValue,b=float.MaxValue,t=float.MinValue;
                foreach(var p in g.Polygon){l=Math.Min(l,p.X);r=Math.Max(r,p.X);b=Math.Min(b,p.Y);t=Math.Max(t,p.Y);}
                var item=LevelCatalog.Create("ground",(l+r)/2,(b+t)/2);item.id="l1-1/ground/"+(++groundIndex).ToString("D2");item.width=r-l;item.height=t-b;item.material=g.Material;d.items.Add(item);
            }
            foreach(var beat in new FirstStepLevel().Beats)
            {
                if(beat.Kind==FirstStepBeatKind.Collectible){var fragment=d.items.Find(i=>i.type=="fragment");fragment.text=beat.Text;fragment.radius=beat.Radius;fragment.beatId=beat.Id;continue;}
                string type=beat.Kind==FirstStepBeatKind.Checkpoint?"checkpoint":beat.Kind==FirstStepBeatKind.Goal?"goal":"story";
                var i=LevelCatalog.Create(type,beat.Position.X,beat.Position.Y);i.id=beat.Id;i.beatId=beat.Id;i.name=beat.Id=="cane-lesson"?"敲杖教学":type=="story"?"对白提示":i.name;i.text=beat.Text??"";i.radius=beat.Radius;
                string target=beat.Trigger==FirstStepTrigger.NearBird?FirstStepLevel.BirdObjectId:beat.Trigger==FirstStepTrigger.MeetWalker?FirstStepLevel.WalkerObjectId:beat.Trigger==FirstStepTrigger.MeetGatekeeper?FirstStepLevel.GatekeeperObjectId:null;
                if(target!=null){i.trigger="perceived";i.target=target;i.relative=true;var parent=d.items.Find(p=>p.id==target);i.x-=parent.x;i.y-=parent.y;}
                if(beat.Trigger==FirstStepTrigger.AtStart)i.trigger="start";
                if(type=="goal")i.requires="gatekeeper";
                if(beat.Kind==FirstStepBeatKind.Checkpoint){i.sound="feedback.checkpoint";i.checkpointId=beat.CheckpointId;}
                d.items.Add(i);
            }
            LevelItem walker=d.items.Find(i=>i.id==FirstStepLevel.WalkerObjectId);
            var route=new LevelMovementRoute{id="l1-1/route/walker/01",actorId=walker.id,loop=true};
            route.waypoints.Add(new LevelMovementWaypoint(0,.7f,43){id="l1-1/route/walker/waypoint/01"});
            route.waypoints.Add(new LevelMovementWaypoint(18,.7f,28.6f){id="l1-1/route/walker/waypoint/02"});
            route.waypoints.Add(new LevelMovementWaypoint(36,.7f,43){id="l1-1/route/walker/waypoint/03"});
            d.movementRoutes.Add(route);
            for(int n=0;n<d.guide.Count;n++)d.guide[n].id="l1-1/guide-node/"+(n+1).ToString("D2");
            // Template is independent from built-in IDs, including references and node IDs.
            return d;
        }
        public static LevelDocument Blank()
        {var d=new LevelDocument();d.items.Add(LevelCatalog.Create("spawn",0,0));d.items.Add(LevelCatalog.Create("goal",0,10));d.guide.Add(new LevelNode(0,0));d.guide.Add(new LevelNode(0,10));return d;}
    }
}
