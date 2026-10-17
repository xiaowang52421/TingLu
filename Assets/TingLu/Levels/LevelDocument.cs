using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using TingLu.Contracts;

namespace TingLu.Levels
{
    // Only this public content DTO is edited/serialized. Gameplay definitions are compiled copies.
    [Serializable, DataContract] public sealed class LevelDocument
    {
        public const int CurrentVersion = 1;
        [DataMember] public int version = CurrentVersion;
        [DataMember] public string id = NewId(), name = "新关卡", goalText = "沿盲道前往终点。";
        [DataMember] public float guideWidth = .6f;
        [DataMember] public List<LevelItem> items = new List<LevelItem>();
        [DataMember] public List<LevelNode> guide = new List<LevelNode>();
        [DataMember] public List<LevelMovementRoute> movementRoutes = new List<LevelMovementRoute>();
        public static string NewId() { return Guid.NewGuid().ToString("N"); }
    }
    [Serializable, DataContract] public sealed class LevelNode
    {
        [DataMember] public string id = LevelDocument.NewId();
        [DataMember] public float x, y;
        public LevelNode() { }
        public LevelNode(float px,float py) { x=px;y=py; }
    }
    [Serializable, DataContract] public sealed class LevelMovementRoute
    {
        [DataMember] public string id = LevelDocument.NewId(), actorId = "";
        [DataMember] public bool loop = true;
        [DataMember] public List<LevelMovementWaypoint> waypoints = new List<LevelMovementWaypoint>();
    }
    [Serializable, DataContract] public sealed class LevelMovementWaypoint
    {
        [DataMember] public string id = LevelDocument.NewId();
        [DataMember] public double timeSeconds;
        [DataMember] public float x, y, rotationDegrees;
        public LevelMovementWaypoint() { }
        public LevelMovementWaypoint(double time,float px,float py,float rotation=0)
        {timeSeconds=time;x=px;y=py;rotationDegrees=rotation;}
    }
    [Serializable, DataContract] public sealed class LevelItem
    {
        [DataMember] public string id = LevelDocument.NewId(), name, type;
        [DataMember] public float x,y,rotation,width=1,height=1,radius=2;
        [DataMember] public string material="concrete",sound="",text="",trigger="enter",action="message",target="",requires="";
        // Optional stable progression identity when one item also owns a world object ID.
        [DataMember] public string beatId="";
        [DataMember] public string checkpointId="";
        // Binding is relative to target: x/y become offsets, resolved in both drawing and runtime.
        [DataMember] public bool relative;
    }
    public static class LevelCatalog
    {
        public static readonly string[] Types={"spawn","goal","pole","bicycle","walker","guard","bird","fragment","checkpoint","story","wall","ground"};
        public static readonly string[] Names={"↑ 出生点","★ 终点","● 电线杆","▰ 固定单车","人 行人","人 门卫","♪ 鸟鸣声源","◇ 声景碎片","✓ 检查点","说 剧情提示","▥ 墙 / 围挡","▱ 地面区域"};
        public static readonly string[] Materials={"concrete","metal","cloth","air","sidewalk","garden","asphalt"};
        public static readonly string[] MaterialNames={"混凝土","金属","布料","空气","人行道","花坛地面","沥青"};
        public static readonly string[] Sounds={"","ambience.bird","ambience.pedestrian","ambience.guard","feedback.checkpoint","cane.tap","echo.ping"};
        public static readonly string[] SoundNames={"无声音","鸟鸣（合成占位）","脚步（合成占位）","门卫（合成占位）","检查点（合成占位）","敲杖（合成占位）","回声（合成占位）"};
        public static LevelItem Create(string type,float x,float y)
        {
            int index=Array.IndexOf(Types,type);if(index<0)throw new ArgumentException("未知素材");
            var a=new LevelItem{type=type,name=Names[index].Substring(2),x=x,y=y};
            if(type=="pole"){a.width=a.height=.36f;}
            if(type=="bicycle"){a.width=1.7f;a.height=.64f;a.material="metal";}
            if(type=="walker"||type=="guard"){a.width=a.height=.5f;a.material="cloth";a.sound=type=="guard"?"ambience.guard":"ambience.pedestrian";}
            if(type=="bird"||type=="fragment"){a.material="air";a.sound="ambience.bird";}
            if(type=="ground"){a.width=3;a.height=8;a.material="sidewalk";}
            if(type=="wall"){a.width=4;a.height=.3f;}
            if(type=="story"){a.text="在这里填写玩家听到的对白。";}
            if(type=="goal"){a.action="finish";a.text="到了。";}
            if(type=="checkpoint"){a.action="checkpoint";a.text="歇口气再走。";}
            if(type=="fragment"){a.action="collect";a.trigger="perceived";a.target=a.id;a.text="收下一段城市记忆。";}
            if(LevelCatalog.IsWorld(type)){a.trigger="perceived";a.target=a.id;}
            return a;
        }
        public static bool IsWorld(string type) { return type=="pole"||type=="bicycle"||type=="walker"||type=="guard"||type=="bird"||type=="fragment"||type=="wall"; }
        public static bool IsSolid(string type) { return type=="pole"||type=="bicycle"||type=="wall"; }
    }
    public static class LevelCodec
    {
        public static string Encode(LevelDocument value)
        {using(var s=new MemoryStream()){new DataContractJsonSerializer(typeof(LevelDocument)).WriteObject(s,value);return Encoding.UTF8.GetString(s.ToArray());}}
        public static LevelDocument Decode(string json)
        {
            LevelDocument d;
            using(var s=new MemoryStream(Encoding.UTF8.GetBytes(json))) d=(LevelDocument)new DataContractJsonSerializer(typeof(LevelDocument)).ReadObject(s);
            if(d==null||d.version!=LevelDocument.CurrentVersion)throw new InvalidDataException("不支持的关卡版本；不会覆盖原文件。");
            if(d.items==null||d.guide==null)throw new InvalidDataException("关卡缺少对象或盲道数据。");
            if(d.movementRoutes==null)d.movementRoutes=new List<LevelMovementRoute>();
            foreach(var i in d.items)
            {
                if(i==null||Array.IndexOf(LevelCatalog.Types,i.type)<0)throw new InvalidDataException("关卡包含空对象或未知素材，无法安全打开；原文件保留。");
                if(!LevelValidator.Range(i.x,-10000,10000)||!LevelValidator.Range(i.y,-10000,10000)||!LevelValidator.Range(i.width,.05f,1000)||!LevelValidator.Range(i.height,.05f,1000)||!LevelValidator.Range(i.rotation,-36000,36000)||!LevelValidator.Range(i.radius,.1f,100))throw new InvalidDataException("对象包含不可绘制的位置或尺寸；原文件保留。");
            }
            foreach(var n in d.guide)if(n==null||!LevelValidator.Range(n.x,-10000,10000)||!LevelValidator.Range(n.y,-10000,10000))throw new InvalidDataException("关卡包含空节点或无效盲道坐标；原文件保留。");
            if(d.movementRoutes.Count>2000)throw new InvalidDataException("动态路线数量超过限制；原文件保留。");
            foreach(var route in d.movementRoutes)
            {
                if(route==null||route.waypoints==null)throw new InvalidDataException("动态路线数据不完整；原文件保留。");
                if(route.waypoints.Count>2000)throw new InvalidDataException("动态路线节点超过限制；原文件保留。");
                foreach(var point in route.waypoints)
                    if(point==null||!LevelValidator.Range(point.x,-10000,10000)||!LevelValidator.Range(point.y,-10000,10000)||
                        !LevelValidator.Range(point.rotationDegrees,-36000,36000)||double.IsNaN(point.timeSeconds)||double.IsInfinity(point.timeSeconds))
                        throw new InvalidDataException("动态路线包含无效坐标、朝向或时间；原文件保留。");
            }
            return d;
        }
        public static LevelDocument Clone(LevelDocument d) { return Decode(Encode(d)); }
        public static LevelDocument Duplicate(LevelDocument d)
        {
            var copy=Clone(d);copy.id=LevelDocument.NewId();copy.name+=" 副本";var map=new Dictionary<string,string>();
            foreach(var i in copy.items){map.Add(i.id,LevelDocument.NewId());}
            foreach(var i in copy.items){i.id=map[i.id];if(!string.IsNullOrEmpty(i.target)&&map.ContainsKey(i.target))i.target=map[i.target];if(!string.IsNullOrEmpty(i.requires)&&map.ContainsKey(i.requires))i.requires=map[i.requires];}
            foreach(var i in copy.items)if(!string.IsNullOrEmpty(i.beatId))i.beatId=LevelDocument.NewId();
            foreach(var i in copy.items)if(!string.IsNullOrEmpty(i.checkpointId))i.checkpointId=LevelDocument.NewId();
            foreach(var n in copy.guide)n.id=LevelDocument.NewId();
            foreach(var route in copy.movementRoutes)
            {
                route.id=LevelDocument.NewId();if(map.ContainsKey(route.actorId))route.actorId=map[route.actorId];
                foreach(var point in route.waypoints)point.id=LevelDocument.NewId();
            }
            return copy;
        }
        public static LevelItem DuplicateItem(LevelItem item)
        {
            var d=new LevelDocument();d.items.Add(item);var copy=Clone(d).items[0];string old=copy.id;
            copy.id=LevelDocument.NewId();copy.name+=" 副本";
            if(!string.IsNullOrEmpty(copy.beatId))copy.beatId=copy.beatId==old?copy.id:LevelDocument.NewId();
            if(!string.IsNullOrEmpty(copy.checkpointId))copy.checkpointId=LevelDocument.NewId();
            if(copy.target==old)copy.target=copy.id;if(copy.requires==old)copy.requires=copy.id;return copy;
        }
        public static LevelDocument Load(string path){return Decode(File.ReadAllText(path,Encoding.UTF8));}
        public static void Save(string path,LevelDocument d,bool requireValid=true)
        {
            if(d==null||d.version!=LevelDocument.CurrentVersion)throw new InvalidDataException("不支持的关卡版本。");
            if(requireValid)LevelValidator.RequireValid(d);path=Path.GetFullPath(path);
            Decode(Encode(d)); // Drafts may miss gameplay content, but must remain safely reopenable.
            if(File.Exists(path))Decode(File.ReadAllText(path,Encoding.UTF8)); // Future/corrupt assets are protected.
            Directory.CreateDirectory(Path.GetDirectoryName(path));string temporary=path+"."+Guid.NewGuid().ToString("N")+".tmp";
            try
            {
                byte[] bytes=Encoding.UTF8.GetBytes(Encode(d));
                using(var s=new FileStream(temporary,FileMode.CreateNew,FileAccess.Write,FileShare.None)){s.Write(bytes,0,bytes.Length);s.Flush(true);}
                if(File.Exists(path))File.Replace(temporary,path,path+".bak");else File.Move(temporary,path);
            }
            finally{if(File.Exists(temporary))File.Delete(temporary);}
        }
    }
}
