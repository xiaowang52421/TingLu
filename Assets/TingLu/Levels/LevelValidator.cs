using System;
using System.Collections.Generic;
using System.IO;
using TingLu.Contracts;

namespace TingLu.Levels
{
    public sealed class LevelIssue
    {
        public readonly bool Error;
        public readonly string ItemId, Message;
        public LevelIssue(bool error,string item,string message){Error=error;ItemId=item;Message=message;}
    }
    public static class LevelValidator
    {
        public static List<LevelIssue> Check(LevelDocument d)
        {
            var issues=new List<LevelIssue>();Action<bool,string,string> add=(e,id,m)=>issues.Add(new LevelIssue(e,id,m));
            if(d==null){add(true,null,"关卡为空，请从模板新建。");return issues;}
            if(d.version!=1)add(true,null,"不支持此版本；请使用兼容版本打开，禁止覆盖。");
            if(string.IsNullOrWhiteSpace(d.id)||string.IsNullOrWhiteSpace(d.name))add(true,null,"填写关卡名称；关卡标识不可为空。");
            if(d.items==null||d.guide==null||d.movementRoutes==null){add(true,null,"对象、盲道或动态路线数据缺失，请重新打开有效文件。");return issues;}
            if(d.items.Count>2000||d.guide.Count>2000||d.movementRoutes.Count>500)add(true,null,"关卡超过首版对象、节点或动态路线数量限制。");
            if(!Range(d.guideWidth,.2f,.8f))add(true,null,"盲道宽度须为 0.2–0.8 米，与当前脚下感知范围相匹配。");
            var ids=new HashSet<string>{d.id};var items=new Dictionary<string,LevelItem>();int spawn=0,goal=0,fragments=0;
            foreach(var i in d.items)
            {
                if(i==null){add(true,null,"存在空对象，请重新导入有效关卡。");continue;}
                if(string.IsNullOrWhiteSpace(i.id)||!ids.Add(i.id))add(true,i.id,"对象标识重复或缺失；请重新复制有效对象。");else items.Add(i.id,i);
                if(!string.IsNullOrWhiteSpace(i.beatId)&&i.beatId!=i.id&&!ids.Add(i.beatId))add(true,i.id,"剧情触发标识重复；请重新复制或编辑该项目。");
                if(!string.IsNullOrWhiteSpace(i.checkpointId)&&!ids.Add(i.checkpointId))add(true,i.id,"检查点存档标识重复；请重新复制或编辑该项目。");
                if(Array.IndexOf(LevelCatalog.Types,i.type)<0)add(true,i.id,"未知素材类型，请删除并从素材面板重新添加。");
                if(i.type=="spawn")spawn++;if(i.type=="goal")goal++;if(i.type=="fragment")fragments++;
                if(!Range(i.x,-10000,10000)||!Range(i.y,-10000,10000)||!Range(i.rotation,-36000,36000))add(true,i.id,"位置或朝向无效；位置限制 ±10000 米，朝向须有限。");
                if(!Range(i.width,.05f,1000)||!Range(i.height,.05f,1000)||!Range(i.radius,.1f,100))add(true,i.id,"尺寸须为 0.05–1000 米，触发半径须为 0.1–100 米。");
                if(Array.IndexOf(LevelCatalog.Materials,i.material)<0)add(true,i.id,"未知材质，请从中文材质列表重新选择。");
                if(i.sound==null||Array.IndexOf(LevelCatalog.Sounds,i.sound)<0)add(true,i.id,"未知声音，请从占位音效列表重新选择。");
                if(i.trigger!="enter"&&i.trigger!="start"&&i.trigger!="perceived")add(true,i.id,"未知触发条件，请重新选择进入区域、开始或合法感知。");
                if(i.trigger=="start"&&!string.IsNullOrEmpty(i.requires))add(true,i.id,"关卡开始提示不能有前置剧情；请解除前置或选择进入区域。");
                if(i.action!="message"&&i.action!="checkpoint"&&i.action!="collect"&&i.action!="finish")add(true,i.id,"未知动作。");
                string expected=i.type=="checkpoint"?"checkpoint":i.type=="fragment"?"collect":i.type=="goal"?"finish":"message";
                if(i.action!=expected)add(true,i.id,"动作与素材不匹配；从素材面板重新添加该项目。");
                if(i.type=="fragment"&&(i.trigger!="perceived"||i.target!=i.id))add(true,i.id,"碎片必须经自身合法感知后收集。");
                if(i.type=="fragment"&&string.IsNullOrEmpty(i.sound))add(true,i.id,"碎片必须选择可听声音；无轮廓且无声音的碎片无法合法感知或收集。");
                if(LevelCatalog.IsWorld(i.type)&&LevelCompiler.HasBeat(i)&&(i.trigger!="perceived"||i.target!=i.id))add(true,i.id,"对象对白必须经自身合法感知触发；请重新选择关联对象。");
                if((i.type=="goal"||i.type=="checkpoint")&&i.trigger!="enter")add(true,i.id,"终点及检查点须通过进入区域触发。");
                if(i.type=="spawn"&&i.relative)add(true,i.id,"出生点须使用独立位置。");
                if(i.relative&&i.type!="story")add(true,i.id,"只有剧情提示可使用相对绑定位置。");
            }
            if(spawn!=1)add(true,null,"需要恰好一个出生点；请添加或删除多余出生点。");
            if(goal!=1)add(true,null,"需要恰好一个终点；请添加或删除多余终点。");
            if(fragments==0)add(false,null,"没有声景碎片；此关最多获得两星。");
            foreach(var i in d.items)
            {
                if(i==null)continue;LevelItem target;
                if(i.relative||i.trigger=="perceived")
                {
                    if(string.IsNullOrEmpty(i.target)||!items.TryGetValue(i.target,out target))add(true,i.id,"绑定对象丢失；选择已有对象或解除绑定。");
                    else if(target.relative||!LevelCatalog.IsWorld(target.type))add(true,i.id,"绑定须指向可合法感知的世界对象，不能指向提示或出生点。");
                    else if(i.trigger=="perceived"&&(target.type=="bird"||target.type=="fragment")&&string.IsNullOrEmpty(target.sound))add(true,i.id,"关联声源没有声音且无轮廓，无法合法感知；请给声源选择声音。");
                }
                else if(!string.IsNullOrEmpty(i.target)&&!items.ContainsKey(i.target))add(true,i.id,"关联对象不存在，请重新选择。");
                if(!string.IsNullOrEmpty(i.requires))
                {
                    if(!items.TryGetValue(i.requires,out target)||target.id==i.id||!LevelCompiler.HasBeat(target))add(true,i.id,"前置剧情不存在；重新选择或设为无。");
                    else if(target.requires==i.id)add(true,i.id,"剧情前置互相依赖；解除其中一个前置条件。");
                }
                var seen=new HashSet<string>();var cursor=i;
                while(cursor!=null&&!string.IsNullOrEmpty(cursor.requires)&&items.TryGetValue(cursor.requires,out target))
                {if(!seen.Add(cursor.id)){add(true,i.id,"剧情前置形成循环；解除前置条件。");break;}cursor=target;}
            }
            foreach(var n in d.guide)
            {if(n==null){add(true,null,"盲道包含空节点。");continue;}if(string.IsNullOrWhiteSpace(n.id)||!ids.Add(n.id))add(true,n.id,"盲道节点标识重复或缺失。");if(!Range(n.x,-10000,10000)||!Range(n.y,-10000,10000))add(true,n.id,"盲道节点位置无效。");}
            var routedActors=new HashSet<string>();
            foreach(var route in d.movementRoutes)
            {
                if(route==null){add(true,null,"存在空动态路线。");continue;}
                if(string.IsNullOrWhiteSpace(route.id)||!ids.Add(route.id))add(true,route.id,"动态路线标识重复或缺失。");
                if(route.waypoints==null){add(true,route.id,"动态路线缺少关键点。");continue;}
                if(route.waypoints.Count<2)add(true,route.id,"动态路线至少需要两个关键点。");
                if(route.waypoints.Count>2000)add(true,route.id,"动态路线关键点超过 2000 个。");
                LevelItem actor;
                if(string.IsNullOrWhiteSpace(route.actorId)||!items.TryGetValue(route.actorId,out actor)||
                    (actor.type!="walker"&&actor.type!="guard"))add(true,route.id,"动态路线必须绑定一个行人或门卫。");
                else if(!routedActors.Add(route.actorId))add(true,route.id,"每个人物目前只能绑定一条动态路线。");
                double previous=-1;
                for(int index=0;index<route.waypoints.Count;index++)
                {
                    LevelMovementWaypoint point=route.waypoints[index];
                    if(point==null){add(true,route.id,"动态路线包含空关键点。");continue;}
                    if(string.IsNullOrWhiteSpace(point.id)||!ids.Add(point.id))add(true,point.id,"动态路线关键点标识重复或缺失。");
                    if(!Range(point.x,-10000,10000)||!Range(point.y,-10000,10000)||!Range(point.rotationDegrees,-36000,36000)||
                        double.IsNaN(point.timeSeconds)||double.IsInfinity(point.timeSeconds)||point.timeSeconds<0||point.timeSeconds>86400)
                        add(true,route.id,"动态路线位置、朝向或时间超出范围。");
                    if(index==0&&point.timeSeconds!=0)add(true,route.id,"动态路线首个关键点时间必须为 0 秒。");
                    if(index>0&&point.timeSeconds<=previous)add(true,route.id,"动态路线关键点时间必须严格递增。");
                    previous=point.timeSeconds;
                }
                if(route.waypoints.Count>=2&&route.loop)
                {
                    LevelMovementWaypoint first=route.waypoints[0],last=route.waypoints[route.waypoints.Count-1];
                    if(first!=null&&last!=null&&(Distance(first.x,first.y,last.x,last.y)>.001f||AngleDistance(first.rotationDegrees,last.rotationDegrees)>.01f))
                        add(true,route.id,"循环路线末点必须回到起点并保持相同朝向；请用关键点画出完整回程。");
                }
            }
            if(d.guide.Count<2)add(true,null,"至少放置两个盲道节点；点选地图连续绘制。");
            for(int j=1;j<d.guide.Count;j++)if(d.guide[j]!=null&&d.guide[j-1]!=null&&Distance(d.guide[j].x,d.guide[j].y,d.guide[j-1].x,d.guide[j-1].y)<.02f)add(true,d.guide[j].id,"盲道段长度接近零；移动或删除重合节点。");
            double generated=0;
            for(int j=1;j<d.guide.Count;j++)if(d.guide[j]!=null&&d.guide[j-1]!=null)
            {float length=Distance(d.guide[j].x,d.guide[j].y,d.guide[j-1].x,d.guide[j-1].y);if(Range(length,0,30000))generated+=Math.Max(1,Math.Ceiling(length/3));}
            if(generated>5000)add(true,null,"盲道预计生成超过 5000 导向段；请缩短路线或减少远距离往返节点。");
            foreach(var i in d.items)
            {
                if(i==null||!Range(i.x,-10000,10000)||!Range(i.y,-10000,10000))continue;
                if(i.type=="spawn"||i.type=="checkpoint"||i.type=="goal")
                {
                    if(d.guide.Count>=2&&d.guide[0]!=null&&d.guide[d.guide.Count-1]!=null){
                    if(i.type=="spawn"&&Distance(i.x,i.y,d.guide[0].x,d.guide[0].y)>2)add(false,i.id,"出生点距离盲道起点超过 2 米；请检查连接。");
                    if(i.type=="goal"&&Distance(i.x,i.y,d.guide[d.guide.Count-1].x,d.guide[d.guide.Count-1].y)>3)add(false,i.id,"终点距离盲道末点超过 3 米；请检查连接。");}
                    foreach(var obstacle in d.items)
                        if(obstacle!=null&&LevelCatalog.IsSolid(obstacle.type)&&InBox(i.x,i.y,obstacle,.18f))add(true,i.id,"位置与障碍「"+obstacle.name+"」重叠；移动到至少留有身体空隙的安全位置。");
                }
            }
            add(false,null,"检查采用几何启发式，不能证明全图可达或真人纯音频可通关；请实际试玩。");return issues;
        }
        public static void RequireValid(LevelDocument d)
        {foreach(var issue in Check(d))if(issue.Error)throw new InvalidDataException(issue.Message);}
        public static bool Range(float x,float min,float max){return !float.IsNaN(x)&&!float.IsInfinity(x)&&x>=min&&x<=max;}
        public static float Distance(float x,float y,float a,float b){return (float)Math.Sqrt((x-a)*(x-a)+(y-b)*(y-b));}
        private static float AngleDistance(float a,float b){float d=Math.Abs((a-b)%360);return Math.Min(d,360-d);}
        public static bool InBox(float x,float y,LevelItem i,float margin)
        {double r=-i.rotation*Math.PI/180;float dx=x-i.x,dy=y-i.y;float lx=dx*(float)Math.Cos(r)-dy*(float)Math.Sin(r),ly=dx*(float)Math.Sin(r)+dy*(float)Math.Cos(r);return Math.Abs(lx)<=i.width*.5f+margin&&Math.Abs(ly)<=i.height*.5f+margin;}
    }
}
