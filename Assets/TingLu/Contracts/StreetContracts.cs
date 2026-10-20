using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace TingLu.Contracts
{
    public static class StreetContract { public const string Version = "1.0.0"; public const int SaveSchema = 1; }
    public enum StreetKind { Permanent, Temporary, Active }
    // None also represents legacy documents; category inference is only a compatibility step,
    // never evidence that an actor moved during a contact interval.
    public enum StreetActorKind { None, Pedestrian, Car, Bicycle, ElectricBike }
    public enum StreetFailure { Vehicle, Courage }
    public struct StreetInputFrame
    {
        public readonly Point2 Move;
        public readonly float TurnDegrees;
        public readonly bool ProbePressed, PausePressed;
        public readonly MenuAction Menu;
        public StreetInputFrame(Point2 move, float turn = 0, bool probe = false, bool pause = false, MenuAction menu = MenuAction.None)
        { Move = move; TurnDegrees = turn; ProbePressed = probe; PausePressed = pause; Menu = menu; }
    }
    public struct StreetVector3
    { public readonly float X,Y,Z; public StreetVector3(float x,float y,float z){X=x;Y=y;Z=z;} }
    public static class StreetCoordinates
    {
        // World +Y maps to Unity +Z. Positive yaw turns clockwise from north toward east.
        public const float CameraYaw = 0;
        public static StreetVector3 ToScene(Point2 p,float height=0){return new StreetVector3(p.X,height,p.Y);}
        public static Point2 FromScene(float x,float z){return new Point2(x,z);}
        public static Point2 ScreenMove(Point2 axes){return StreetGeometry.NormalizeClamped(axes);}
        public static Point2 Turn(Point2 facing,float degrees)
        { double a=degrees*Math.PI/180; return StreetGeometry.Unit(new Point2((float)(facing.X*Math.Cos(a)+facing.Y*Math.Sin(a)),(float)(facing.Y*Math.Cos(a)-facing.X*Math.Sin(a)))); }
    }
    public static class StreetCatalog
    {
        public static readonly string[] Materials={"sidewalk","tactile","tactile.dots","asphalt","crosswalk","ramp","curb","brick","metal","cardboard"};
        public static readonly string[] Sounds={"street.car","street.electric","street.cycle","street.walker","street.bird","street.signal.ns","street.signal.ew","cane.tap","collision.bump","feedback.checkpoint","feedback.breath","footstep.sidewalk","footstep.tactile","footstep.tactile.dots","footstep.asphalt","footstep.crosswalk","footstep.ramp","footstep.curb"};
    }
    public sealed class StreetSurface
    {
        public readonly string Id,MaterialId;
        public readonly ReadOnlyCollection<Point2> Polygon;
        public readonly Point2 TextureAxis;
        public readonly float HeightBase,HeightSlopeX,HeightSlopeY;
        public readonly int Priority;
        public StreetSurface(string id,string material,IEnumerable<Point2> polygon,int priority=0,Point2? textureAxis=null,
            float heightBase=0f,float heightSlopeX=0f,float heightSlopeY=0f)
        {Id=ContractGuard.Id(id);MaterialId=ContractGuard.Id(material);Polygon=StreetGeometry.CopyPolygon(polygon);Priority=priority;
         TextureAxis=StreetTextureOrientation.Canonical(textureAxis??StreetTextureOrientation.ForSurface(material,Polygon));
         HeightBase=FiniteHeight(heightBase,"heightBase");HeightSlopeX=FiniteHeight(heightSlopeX,"heightSlopeX");HeightSlopeY=FiniteHeight(heightSlopeY,"heightSlopeY");}
        private static float FiniteHeight(float value,string name)
        {if(float.IsNaN(value)||float.IsInfinity(value))throw new ArgumentOutOfRangeException(name);return value;}
        public float HeightAt(Point2 point){return HeightBase+HeightSlopeX*point.X+HeightSlopeY*point.Y;}
    }
    public static class StreetTextureOrientation
    {
        // Max-component normalization is idempotent when fragments are copied repeatedly.
        // Opposite directions describe the same unoriented material axis.
        public static Point2 Canonical(Point2 axis)
        {
            if(float.IsNaN(axis.X)||float.IsNaN(axis.Y)||float.IsInfinity(axis.X)||float.IsInfinity(axis.Y))
                throw new ArgumentException("Invalid texture axis");
            float scale=Math.Max(Math.Abs(axis.X),Math.Abs(axis.Y));
            if(scale==0)throw new ArgumentException("Zero texture axis");
            float x=axis.X/scale,y=axis.Y/scale;
            if(x<0||(x==0&&y<0)){x=-x;y=-y;}
            return new Point2(x==0?0:x,y==0?0:y);
        }
        public static Point2 ForSurface(string material,IReadOnlyList<Point2> polygon)
        {
            if(material!="tactile"&&material!="crosswalk"&&material!="ramp")return new Point2(1,0);
            double longest=-1;
            Point2 axis=new Point2(1,0);
            for(int i=0;i<polygon.Count;i++)
            {
                Point2 a=polygon[i],b=polygon[(i+1)%polygon.Count];
                var edge=new Point2(b.X-a.X,b.Y-a.Y);
                double length=(double)edge.X*edge.X+(double)edge.Y*edge.Y;
                if(length<=0)continue;
                Point2 candidate=Canonical(edge);
                if(length>longest||(length==longest&&(candidate.X>axis.X||(candidate.X==axis.X&&candidate.Y>axis.Y))))
                {longest=length;axis=candidate;}
            }
            // Crosswalk bars run across the walking direction (the long surface extent).
            return material=="crosswalk"?new Point2(-axis.Y,axis.X):axis;
        }
    }
    public sealed class StreetObject
    {
        public readonly string Id,MaterialId,SoundId;
        public readonly StreetKind Kind;
        public readonly StreetActorKind ActorKind;
        public bool IsVehicle { get { return ActorKind == StreetActorKind.Car || ActorKind == StreetActorKind.Bicycle || ActorKind == StreetActorKind.ElectricBike; } }
        public readonly Point2 Position;
        public readonly ReadOnlyCollection<Point2> Polygon;
        public readonly bool Solid,VehicleHazard,BlocksProbe,SoundActive;
        public readonly float AcousticLoss,Height;
        public readonly float? ContactDamage,ContactStun;
        public readonly PerceptionSemantic Semantic;
        public StreetObject(string id,StreetKind kind,Point2 position,IEnumerable<Point2> polygon,string material,
            bool solid=false,bool vehicleHazard=false,bool blocksProbe=false,float acousticLoss=0,float height=0,
            string soundId=null,bool soundActive=false,PerceptionSemantic semantic=PerceptionSemantic.Ordinary,float? contactDamage=null,float? contactStun=null,StreetActorKind actorKind=StreetActorKind.None)
        {Id=ContractGuard.Id(id);Kind=kind;Position=position;Polygon=ContractGuard.Copy(polygon??new Point2[0]);MaterialId=ContractGuard.Id(material);
         ActorKind=actorKind!=StreetActorKind.None?actorKind:semantic==PerceptionSemantic.Person||soundId=="street.walker"?StreetActorKind.Pedestrian:soundId=="street.electric"?StreetActorKind.ElectricBike:soundId=="street.cycle"?StreetActorKind.Bicycle:soundId=="street.car"||vehicleHazard?StreetActorKind.Car:StreetActorKind.None;
         Solid=solid||(ActorKind!=StreetActorKind.None&&Polygon.Count>=3);VehicleHazard=vehicleHazard;BlocksProbe=blocksProbe;AcousticLoss=acousticLoss;Height=height;SoundId=soundId;SoundActive=soundActive;Semantic=semantic;ContactDamage=contactDamage;ContactStun=contactStun;}
    }
    public sealed class StreetWorldFrame
    {
        public readonly double Time;
        public readonly ReadOnlyCollection<StreetSurface> Surfaces;
        public readonly ReadOnlyCollection<StreetObject> Objects;
        public readonly double NextSignalChangeAt;
        public StreetWorldFrame(double time,IEnumerable<StreetSurface> surfaces,IEnumerable<StreetObject> objects,double? nextSignalChangeAt=null)
        {Time=ContractGuard.Time(time);Surfaces=ContractGuard.Copy(surfaces);Objects=ContractGuard.Copy(objects);
         NextSignalChangeAt=ContractGuard.Time(nextSignalChangeAt.HasValue?nextSignalChangeAt.Value:time);}
    }
    public sealed class StreetRoute
    {
        public readonly string ObjectId,SignalAxis;
        public readonly ReadOnlyCollection<Point2> Points;
        public readonly float Speed;
        public readonly double Offset;
        public readonly bool Loop;
        public readonly float? StopLineDistance;
        public StreetRoute(string id,IEnumerable<Point2> points,float speed,string signalAxis=null,double offset=0,bool loop=true,float? stopLineDistance=null)
        {ObjectId=ContractGuard.Id(id);Points=ContractGuard.Copy(points);if(Points.Count<2||!(speed>0)||float.IsInfinity(speed)||double.IsNaN(offset)||double.IsInfinity(offset)||(signalAxis!=null&&signalAxis!="ns"&&signalAxis!="ew")||(stopLineDistance.HasValue&&(!(stopLineDistance.Value>=0)||float.IsInfinity(stopLineDistance.Value))))throw new ArgumentException("Invalid route");Speed=speed;SignalAxis=signalAxis;Offset=offset;Loop=loop;StopLineDistance=stopLineDistance;}
    }
    public sealed class StreetTrafficState
    {
        public readonly double Time;
        public readonly ReadOnlyCollection<string> ObjectIds;
        public readonly ReadOnlyCollection<double> Distances;
        public StreetTrafficState(double time,IEnumerable<string> ids,IEnumerable<double> distances)
        {Time=ContractGuard.Time(time);ObjectIds=ContractGuard.Copy(ids);Distances=ContractGuard.Copy(distances);if(ObjectIds.Count!=Distances.Count)throw new ArgumentException("Traffic size mismatch");}
    }
    public sealed class StreetContact
    {
        public readonly string ObjectId;
        public readonly Point2 Point;
        public readonly bool Vehicle;
        public readonly float? Damage,StunSeconds;
        public StreetContact(string id,Point2 point,bool vehicle,float? damage=null,float? stunSeconds=null){ObjectId=ContractGuard.Id(id);Point=point;Vehicle=vehicle;Damage=damage;StunSeconds=stunSeconds;}
    }
    public sealed class StreetMoveResult
    {
        public readonly Point2 Position;
        public readonly ReadOnlyCollection<StreetContact> Contacts;
        public StreetMoveResult(Point2 p,IEnumerable<StreetContact> contacts){Position=p;Contacts=ContractGuard.Copy(contacts);}
    }
    public interface IStreetWorld
    {
        StreetWorldFrame Capture();
        void Tick(double delta,bool paused);
        StreetMoveResult Move(Point2 from,Point2 requested,float radius);
        // Historical name retained for implementers. Returns physical player contacts;
        // StreetContact.Vehicle means a moving vehicle intersected during this interval.
        IReadOnlyList<StreetContact> VehicleContacts(Point2 from,Point2 to,float radius);
        string MaterialAt(Point2 position);
        StreetTrafficState ExportTraffic();
        void ImportTraffic(StreetTrafficState state);
        string SignalPhase {get;}
    }
    public sealed class StreetProbeRequest
    {
        public readonly string Id;
        public readonly double Time;
        public readonly Point2 Origin,Facing;
        public readonly float Angle,Radius;
        public readonly ObservationSource Source;
        public StreetProbeRequest(string id,double time,Point2 origin,Point2 facing,float angle,float radius,ObservationSource source=ObservationSource.CaneEcho)
        {Id=ContractGuard.Id(id);Time=ContractGuard.Time(time);Origin=origin;Facing=StreetGeometry.Unit(facing);if(!(angle>0&&angle<360)||!(radius>0)||float.IsInfinity(radius))throw new ArgumentException("Invalid sector");Angle=angle;Radius=radius;Source=source;}
    }
    public sealed class StreetKnownSurface
    {
        public readonly string MaterialId;
        public readonly ReadOnlyCollection<Point2> Polygon;
        public readonly Point2 TextureAxis;
        public readonly float HeightBase,HeightSlopeX,HeightSlopeY;
        public readonly double ObservedAt,CurrentUntil;
        public StreetKnownSurface(string material,IEnumerable<Point2> polygon,double at,double until,Point2? textureAxis=null,
            float heightBase=0f,float heightSlopeX=0f,float heightSlopeY=0f)
        {MaterialId=ContractGuard.Id(material);Polygon=StreetGeometry.CopyPolygon(polygon);ObservedAt=ContractGuard.Time(at);CurrentUntil=ContractGuard.Time(until);
         TextureAxis=StreetTextureOrientation.Canonical(textureAxis??new Point2(1,0));
         HeightBase=FiniteHeight(heightBase,"heightBase");HeightSlopeX=FiniteHeight(heightSlopeX,"heightSlopeX");HeightSlopeY=FiniteHeight(heightSlopeY,"heightSlopeY");}
        private static float FiniteHeight(float value,string name){if(float.IsNaN(value)||float.IsInfinity(value))throw new ArgumentOutOfRangeException(name);return value;}
        public float HeightAt(Point2 point){return HeightBase+HeightSlopeX*point.X+HeightSlopeY*point.Y;}
    }
    public sealed class StreetKnownObject
    {
        public readonly string ObjectId,MaterialId;
        public readonly StreetKind Kind;
        public readonly ReadOnlyCollection<Segment2> Contour;
        public readonly float Height;
        public readonly double ObservedAt,CurrentUntil;
        public StreetKnownObject(string id,string material,StreetKind kind,IEnumerable<Segment2> contour,float height,double at,double until)
        {if(kind!=StreetKind.Permanent&&kind!=StreetKind.Temporary)throw new ArgumentException("Active sources cannot be geometry memories");if(!(height>=0)||float.IsInfinity(height))throw new ArgumentOutOfRangeException("height");ObjectId=ContractGuard.Id(id);MaterialId=ContractGuard.Id(material);Kind=kind;Contour=ContractGuard.Copy(contour);Height=height;ObservedAt=ContractGuard.Time(at);CurrentUntil=ContractGuard.Time(until);}
    }
    public sealed class StreetProbeResult
    {
        public readonly ProbeCoverage Coverage;
        public readonly ReadOnlyCollection<StreetKnownSurface> Surfaces;
        public readonly ReadOnlyCollection<StreetKnownObject> Objects;
        public readonly ReadOnlyCollection<StreetSignalObservation> Signals;
        public StreetProbeResult(ProbeCoverage coverage,IEnumerable<StreetKnownSurface> surfaces,IEnumerable<StreetKnownObject> objects,IEnumerable<StreetSignalObservation> signals=null)
        {Coverage=coverage??throw new ArgumentNullException("coverage");Surfaces=ContractGuard.Copy(surfaces);Objects=ContractGuard.Copy(objects);Signals=ContractGuard.Copy(signals??new StreetSignalObservation[0]);}
    }

    public sealed class StreetSignalObservation
    {
        public readonly string ObjectId;
        public readonly Point2 Position;
        public readonly bool IsGreen;
        public readonly double ObservedAt,ExpiresAt;
        public StreetSignalObservation(string objectId,Point2 position,bool isGreen,double observedAt,double expiresAt)
        {ObjectId=ContractGuard.Id(objectId);Position=position;IsGreen=isGreen;ObservedAt=ContractGuard.Time(observedAt);ExpiresAt=ContractGuard.Time(expiresAt);
         if(ExpiresAt<ObservedAt)throw new ArgumentException("Signal observation expires before it is observed", "expiresAt");}
    }
    public sealed class StreetMemoryFrame
    {
        public readonly ReadOnlyCollection<StreetKnownSurface> Surfaces;
        public readonly ReadOnlyCollection<StreetKnownObject> Objects;
        public StreetMemoryFrame(IEnumerable<StreetKnownSurface> surfaces,IEnumerable<StreetKnownObject> objects)
        {Surfaces=ContractGuard.Copy(surfaces);Objects=ContractGuard.Copy(objects);}
        public static StreetMemoryFrame Empty { get { return new StreetMemoryFrame(new StreetKnownSurface[0],new StreetKnownObject[0]); } }
    }
    public interface IStreetMemory
    {
        StreetMemoryFrame Current {get;}
        void Apply(StreetProbeResult result);
        StreetMemoryFrame Export();
        void Import(StreetMemoryFrame frame,bool stale);
        void MarkStale();
    }
    public sealed class StreetCheckpoint
    {
        public readonly string Id;
        public readonly Point2 Position,Facing;
        public readonly float Radius;
        public StreetCheckpoint(string id,Point2 p,Point2 f,float radius=0.7f){if(!(radius>0)||float.IsInfinity(radius))throw new ArgumentOutOfRangeException("radius");Id=ContractGuard.Id(id);Position=p;Facing=StreetGeometry.Unit(f);Radius=radius;}
    }
    public sealed class StreetSaveData
    {
        public readonly string LevelId,CheckpointId;
        public readonly PlayerState Player,CheckpointPlayer;
        public readonly StreetTrafficState Traffic,CheckpointTraffic;
        public readonly StreetMemoryFrame Memory;
        public readonly int Collisions,Generation;
        public readonly ReadOnlyCollection<string> ActivatedCheckpoints;
        public readonly bool Finished;
        public StreetSaveData(string level,string checkpoint,PlayerState player,PlayerState checkpointPlayer,StreetTrafficState traffic,StreetTrafficState checkpointTraffic,
            StreetMemoryFrame memory,int collisions,int generation,IEnumerable<string> activated,bool finished=false)
        {LevelId=ContractGuard.Id(level);CheckpointId=ContractGuard.Id(checkpoint);Player=player??throw new ArgumentNullException("player");CheckpointPlayer=checkpointPlayer??throw new ArgumentNullException("checkpointPlayer");Traffic=traffic??throw new ArgumentNullException("traffic");CheckpointTraffic=checkpointTraffic??throw new ArgumentNullException("checkpointTraffic");Memory=memory??throw new ArgumentNullException("memory");if(collisions<0||generation<0)throw new ArgumentOutOfRangeException();Collisions=collisions;Generation=generation;ActivatedCheckpoints=ContractGuard.Copy(activated);Finished=finished;}
    }
    public interface IStreetSaveService
    { SaveStatus Save(StreetSaveData value); SaveResult<StreetSaveData> Load(string levelId); }
    public interface IStreetAudioUpdater
    { void UpdateSource(string eventOrSourceId,Point2 position,float gain,float occlusion); }
    // Unity adapters consume these pure lifecycle interfaces; Bootstrap owns the single wiring point.
    public interface IStreetInput { StreetInputFrame Read(); void SetGameplayActive(bool active); void Clear(); }
    public interface IStreetCamera { void Follow(Point2 position,bool snap); }
    public interface IStreetHud { void Present(PlayerState player,UserSettings settings,string subtitle,bool playing); }
}
