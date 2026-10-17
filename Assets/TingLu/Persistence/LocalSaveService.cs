using System;
using System.IO;
using System.Security.Cryptography;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using TingLu.Contracts;

namespace TingLu.Persistence
{
    [DataContract] internal sealed class Envelope { [DataMember] public int version; [DataMember] public string checksum; [DataMember] public string payload; }
    [DataContract] internal sealed class SaveDto
    {
        [DataMember] public string kind; [DataMember] public string attemptId; [DataMember] public int collisions; [DataMember] public string[] collected;
        [DataMember] public CheckpointDto checkpoint; [DataMember] public string[] levels; [DataMember] public StarDto[] stars; [DataMember] public string[] unlocks;
        [DataMember] public int display; [DataMember] public int output; [DataMember] public float volume; [DataMember] public bool subtitles; [DataMember] public bool haptics;
        [DataMember] public string[] firedBeats; [DataMember] public string[] activatedCheckpoints; [DataMember] public bool finished;
        [DataMember] public int? palette; [DataMember] public float? fontScale; [DataMember] public float? voiceprintIntensity;
    }
    [DataContract] internal sealed class StarDto { [DataMember] public string id; [DataMember] public int value; }
    [DataContract] internal sealed class PointDto { [DataMember] public float x; [DataMember] public float y; }
    [DataContract] internal sealed class SegmentDto { [DataMember] public PointDto a; [DataMember] public PointDto b; }
    [DataContract] internal sealed class MemoryDto { [DataMember] public string objectId; [DataMember] public string fragmentId; [DataMember] public SegmentDto[] contour; [DataMember] public double observed; [DataMember] public int provenance; }
    [DataContract] internal sealed class EventDto { [DataMember] public string id; [DataMember] public string state; [DataMember] public double remaining; }
    [DataContract] internal sealed class PlayerDto { [DataMember] public int main; [DataMember] public int mode; [DataMember] public PointDto pos; [DataMember] public PointDto facing; [DataMember] public float courage; [DataMember] public float focus; [DataMember] public double ready; [DataMember] public double stunned; [DataMember] public double confused; }
    [DataContract] internal sealed class CheckpointDto { [DataMember] public int schema; [DataMember] public string level; [DataMember] public string id; [DataMember] public double captured; [DataMember] public PlayerDto player; [DataMember] public MemoryDto[] memory; [DataMember] public EventDto[] events; [DataMember] public int seed; }

    /// <summary>Local three-slot save with checksums, backup fallback and future-version protection.</summary>
    public sealed class LocalSaveService : ISaveService
    {
        private const int Version=1;
        private readonly string root;
        public LocalSaveService(string persistentDirectory)
        { root=Path.Combine(ContractGuard.Id(persistentDirectory),"TingLu"); Directory.CreateDirectory(root); }

        public SaveResult<ResumeData> LoadResume() { return Load("resume",DecodeResume); }
        public SaveStatus SaveResume(ResumeData value) { if(value==null)return SaveStatus.IoFailure; return Save("resume",EncodeResume(value)); }
        public SaveResult<PlayerProgress> LoadProgress() { return Load("progress",DecodeProgress); }
        public SaveStatus SaveProgress(PlayerProgress value) { if(value==null)return SaveStatus.IoFailure; return Save("progress",EncodeProgress(value)); }
        public SaveResult<UserSettings> LoadSettings() { return Load("settings",DecodeSettings); }
        public SaveStatus SaveSettings(UserSettings value) { if(value==null)return SaveStatus.IoFailure; return Save("settings",EncodeSettings(value)); }

        private SaveStatus Save(string name,SaveDto dto)
        {
            try {
                string target=Path.Combine(root,name+".json"), temp=Path.Combine(root,name+".json.tmp"), backup=Path.Combine(root,name+".json.bak");
                if(IsValidFutureSave(target)||IsValidFutureSave(backup))return SaveStatus.FutureVersion;
                string payload=ToJson(dto); var envelope=new Envelope { version=Version,payload=payload,checksum=Hash(payload) };
                byte[] bytes=Encoding.UTF8.GetBytes(ToJson(envelope));
                using(var stream=new FileStream(temp,FileMode.Create,FileAccess.Write,FileShare.None)) { stream.Write(bytes,0,bytes.Length); stream.Flush(true); }
                if(File.Exists(target)) {
                    try { File.Replace(temp,target,backup,true); }
                    catch(PlatformNotSupportedException) { File.Copy(target,backup,true); File.Delete(target); File.Move(temp,target); }
                    catch(IOException) { File.Copy(target,backup,true); File.Delete(target); File.Move(temp,target); }
                } else File.Move(temp,target);
                return SaveStatus.Success;
            } catch { return SaveStatus.IoFailure; }
        }
        private SaveResult<T> Load<T>(string name,Func<SaveDto,T> decode)
        {
            string path=Path.Combine(root,name+".json"), backup=path+".bak";
            if(!File.Exists(path)) return File.Exists(backup)?TryRead(backup,decode,true):new SaveResult<T>(SaveStatus.Missing,default(T));
            SaveResult<T> primary=TryRead(path,decode,false);
            if(primary.Status==SaveStatus.Success||primary.Status==SaveStatus.FutureVersion) return primary;
            SaveResult<T> old=File.Exists(backup)?TryRead(backup,decode,true):null;
            if(old!=null && old.Status==SaveStatus.Success)return old;
            if(old!=null && old.Status==SaveStatus.FutureVersion)return old;
            return primary;
        }
        private SaveResult<T> TryRead<T>(string path,Func<SaveDto,T> decode,bool fromBackup)
        {
            try {
                string json=File.ReadAllText(path,Encoding.UTF8); Envelope envelope=FromJson<Envelope>(json);
                if(envelope==null||string.IsNullOrEmpty(envelope.payload)||string.IsNullOrEmpty(envelope.checksum)||Hash(envelope.payload)!=envelope.checksum) return new SaveResult<T>(SaveStatus.Corrupt,default(T),fromBackup,"checksum or envelope invalid");
                if(envelope.version>Version)return new SaveResult<T>(SaveStatus.FutureVersion,default(T),fromBackup,"save version is newer than this build");
                if(envelope.version<Version)return new SaveResult<T>(SaveStatus.Corrupt,default(T),fromBackup,"unsupported old save version");
                T value=decode(FromJson<SaveDto>(envelope.payload));
                if(value==null)return new SaveResult<T>(SaveStatus.Corrupt,default(T),fromBackup,"save payload has the wrong shape");
                return new SaveResult<T>(SaveStatus.Success,value,fromBackup);
            } catch(Exception ex) { return new SaveResult<T>(SaveStatus.Corrupt,default(T),fromBackup,ex.Message); }
        }
        private static bool IsValidFutureSave(string path)
        {
            if(!File.Exists(path))return false;
            try{Envelope e=FromJson<Envelope>(File.ReadAllText(path,Encoding.UTF8));return e!=null&&e.version>Version&&!string.IsNullOrEmpty(e.payload)&&Hash(e.payload)==e.checksum;}
            catch{return false;}
        }
        private static string ToJson<T>(T item) { var serializer=new DataContractJsonSerializer(typeof(T)); using(var stream=new MemoryStream()){serializer.WriteObject(stream,item);return Encoding.UTF8.GetString(stream.ToArray());} }
        private static T FromJson<T>(string json) { var serializer=new DataContractJsonSerializer(typeof(T)); using(var stream=new MemoryStream(Encoding.UTF8.GetBytes(json))){return (T)serializer.ReadObject(stream);} }
        private static string Hash(string value) { using(var sha=SHA256.Create()){byte[] hash=sha.ComputeHash(Encoding.UTF8.GetBytes(value));var b=new StringBuilder(hash.Length*2);foreach(byte x in hash)b.Append(x.ToString("x2"));return b.ToString();} }

        private static SaveDto EncodeResume(ResumeData value)
        { return new SaveDto { kind="resume",attemptId=value.Attempt.AttemptId,collisions=value.Attempt.Collisions,collected=Copy(value.Attempt.CollectedIds),checkpoint=EncodeCheckpoint(value.Checkpoint),firedBeats=Copy(value.FiredBeatIds),activatedCheckpoints=Copy(value.ActivatedCheckpointIds),finished=value.Finished }; }
        private static ResumeData DecodeResume(SaveDto d)
        { if(d.kind!="resume"||d.checkpoint==null)return null;return new ResumeData(DecodeCheckpoint(d.checkpoint),new AttemptStats(d.attemptId,d.collisions,d.collected??new string[0]),d.firedBeats??new string[0],d.activatedCheckpoints??new string[0],d.finished); }
        private static SaveDto EncodeProgress(PlayerProgress p)
        { var stars=new StarDto[p.BestStars.Count];int i=0;foreach(var pair in p.BestStars)stars[i++]=new StarDto{id=pair.Key,value=pair.Value};return new SaveDto{kind="progress",levels=Copy(p.CompletedLevels),stars=stars,collected=Copy(p.CollectedIds),unlocks=Copy(p.Unlocks)}; }
        private static PlayerProgress DecodeProgress(SaveDto d)
        { if(d.kind!="progress")return null;var stars=new System.Collections.Generic.Dictionary<string,int>();foreach(var s in d.stars??new StarDto[0])stars.Add(s.id,s.value);return new PlayerProgress(d.levels??new string[0],stars,d.collected??new string[0],d.unlocks??new string[0]); }
        private static SaveDto EncodeSettings(UserSettings s) { return new SaveDto{kind="settings",display=(int)s.Display,output=(int)s.Output,volume=s.MasterVolume,subtitles=s.Subtitles,haptics=s.Haptics,palette=(int)s.Palette,fontScale=s.FontScale,voiceprintIntensity=s.VoiceprintIntensity}; }
        private static UserSettings DecodeSettings(SaveDto d) { if(d.kind!="settings")return null;return new UserSettings((DisplayMode)d.display,(OutputMode)d.output,d.volume,d.subtitles,d.haptics,d.palette.HasValue?(PaletteMode)d.palette.Value:PaletteMode.Default,d.fontScale.HasValue?d.fontScale.Value:1,d.voiceprintIntensity.HasValue?d.voiceprintIntensity.Value:1); }
        private static CheckpointDto EncodeCheckpoint(CheckpointSnapshot s)
        {
            if(s==null)throw new ArgumentNullException("checkpoint");var p=s.Player;
            var m=new MemoryDto[s.Memory.Count];for(int i=0;i<m.Length;i++){var v=s.Memory[i];var contour=new SegmentDto[v.WorldContour.Count];for(int j=0;j<contour.Length;j++)contour[j]=new SegmentDto{a=Point(v.WorldContour[j].A),b=Point(v.WorldContour[j].B)};m[i]=new MemoryDto{objectId=v.ObjectId,fragmentId=v.FragmentId,contour=contour,observed=v.ObservedAt,provenance=(int)v.Provenance};}
            var e=new EventDto[s.Events.Count];for(int i=0;i<e.Length;i++)e[i]=new EventDto{id=s.Events[i].ObjectId,state=s.Events[i].StateId,remaining=s.Events[i].RemainingSeconds};
            return new CheckpointDto{schema=s.SchemaVersion,level=s.LevelId,id=s.CheckpointId,captured=s.CapturedAt,seed=s.RandomSeed,player=new PlayerDto{main=(int)p.MainState,mode=(int)p.Mode,pos=Point(p.Position),facing=Point(p.Facing),courage=p.Courage,focus=p.Focus,ready=p.CaneReadyAt,stunned=p.StunnedUntil,confused=p.ConfusedUntil},memory=m,events=e};
        }
        private static CheckpointSnapshot DecodeCheckpoint(CheckpointDto s)
        {
            if(s.schema!=CheckpointSnapshot.CurrentSchemaVersion)throw new SerializationException("Unsupported checkpoint schema");var p=s.player;
            var memory=new System.Collections.Generic.List<StaticMemorySnapshot>();foreach(var v in s.memory??new MemoryDto[0]) { var segs=new System.Collections.Generic.List<Segment2>();foreach(var q in v.contour??new SegmentDto[0])segs.Add(new Segment2(Point(q.a),Point(q.b)));var obs=new Observation(v.objectId,ObjectCategory.Static,MemoryPolicy.ConfirmedStaticGeometry,(ObservationSource)v.provenance,InformationKind.LocalGeometry,new Point2(0,0),segs,"remembered",v.observed,v.observed);memory.Add(new StaticMemorySnapshot(v.fragmentId,obs)); }
            var events=new System.Collections.Generic.List<WorldEventSnapshot>();foreach(var v in s.events??new EventDto[0])events.Add(new WorldEventSnapshot(v.id,v.state,v.remaining));
            return new CheckpointSnapshot(s.level,s.id,s.captured,new PlayerState((PlayerMainState)p.main,(ActionMode)p.mode,Point(p.pos),Point(p.facing),p.courage,p.focus,p.ready,p.stunned,p.confused),memory,events,s.seed);
        }
        private static PointDto Point(Point2 p){return new PointDto{x=p.X,y=p.Y};}
        private static Point2 Point(PointDto p){if(p==null)throw new SerializationException("Point missing");return new Point2(p.x,p.y);}
        private static string[] Copy(System.Collections.Generic.IEnumerable<string> values){return new System.Collections.Generic.List<string>(values).ToArray();}
    }
}
