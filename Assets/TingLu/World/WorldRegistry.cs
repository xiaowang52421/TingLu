using System;
using System.Collections.Generic;
using TingLu.Contracts;

namespace TingLu.World
{
    /// <summary>Per-level world registry. Position changes are explicit and definitions stay immutable.</summary>
    public sealed class WorldRegistry : IWorldQuery, IWorldLifecycle, IWorldOcclusionQuery, IWorldMovement
    {
        private sealed class Entry { public ObjectDefinition Definition; public Point2 Position; public float Rotation; public bool Sound; public string State; public double Remaining; }
        private readonly Dictionary<string, Entry> entries = new Dictionary<string, Entry>();
        private readonly List<Segment2> occluders = new List<Segment2>();
        private readonly List<GroundSurface> ground = new List<GroundSurface>();
        private bool disposed;
        private int seed;
        private sealed class GroundSurface { public string Material; public Point2[] Polygon; public float Area; }

        public int RandomSeed { get { return seed; } }
        public void Register(ObjectDefinition definition, Point2 position, float rotation, bool soundActive)
        {
            EnsureLive(); if (definition == null) throw new ArgumentNullException("definition");
            if (entries.ContainsKey(definition.ObjectId)) throw new ArgumentException("Duplicate world object " + definition.ObjectId);
            entries.Add(definition.ObjectId, new Entry { Definition=definition, Position=position, Rotation=rotation, Sound=soundActive, State="idle" });
        }
        public void AddOccluder(Segment2 segment) { EnsureLive(); occluders.Add(segment); }
        public void AddGroundSurface(string materialId, IEnumerable<Point2> polygon)
        {
            EnsureLive(); var points=new List<Point2>(polygon??throw new ArgumentNullException("polygon"));
            if(points.Count<3)throw new ArgumentException("Ground surface needs at least three points", "polygon");
            float area=0;for(int i=0;i<points.Count;i++){Point2 a=points[i],b=points[(i+1)%points.Count];area+=a.X*b.Y-b.X*a.Y;}
            ground.Add(new GroundSurface{Material=ContractGuard.Id(materialId),Polygon=points.ToArray(),Area=Math.Abs(area)});
        }
        public void SetEventState(string objectId, string state, double remainingSeconds)
        { EnsureLive(); ContractGuard.Time(remainingSeconds); Entry e=Get(objectId); e.State=ContractGuard.Id(state); e.Remaining=remainingSeconds; }
        public void Move(string objectId, Point2 position, float rotation, bool soundActive)
        { Entry e=Get(objectId); e.Position=position; e.Rotation=rotation; e.Sound=soundActive; }
        public WorldObjectState GetState(string objectId) { Entry e=Get(objectId); return new WorldObjectState(e.Definition,e.Position,e.Rotation,e.Sound); }
        public IReadOnlyList<WorldObjectState> QueryNearby(Point2 center, float radius)
        {
            EnsureLive(); if (radius < 0 || float.IsNaN(radius) || float.IsInfinity(radius)) throw new ArgumentOutOfRangeException("radius");
            var result=new List<WorldObjectState>(); float sq=radius*radius;
            foreach (Entry e in entries.Values) { float x=e.Position.X-center.X,y=e.Position.Y-center.Y; if(x*x+y*y<=sq) result.Add(new WorldObjectState(e.Definition,e.Position,e.Rotation,e.Sound)); }
            return result.AsReadOnly();
        }
        public string MaterialAt(Point2 position)
        {
            EnsureLive(); GroundSurface best=null;
            foreach(GroundSurface surface in ground) if(Contains(surface.Polygon,position)&&(best==null||surface.Area<best.Area))best=surface;
            return best==null?"sidewalk":best.Material;
        }
        public bool IsOccluded(Point2 from, Point2 to)
        { EnsureLive(); foreach(Segment2 wall in occluders) if(Intersects(from,to,wall.A,wall.B)) return true; return false; }
        public IReadOnlyList<Segment2> QueryOcclusionBoundaries(Point2 center,float radius)
        { EnsureLive();if(radius<0||float.IsNaN(radius)||float.IsInfinity(radius))throw new ArgumentOutOfRangeException("radius");var result=new List<Segment2>();foreach(Segment2 s in occluders)if(DistanceToSegment(center,s)<=radius)result.Add(s);foreach(Entry e in entries.Values){if(e.Definition.Category==ObjectCategory.Event||e.Definition.Semantic==PerceptionSemantic.Guide)continue;foreach(Segment2 local in e.Definition.LocalContour){Segment2 s=new Segment2(Transform(local.A,e.Position,e.Rotation),Transform(local.B,e.Position,e.Rotation));if(DistanceToSegment(center,s)<=radius)result.Add(s);}}return result.AsReadOnly(); }
        public MovementResult ResolveMovement(Point2 from,Point2 requested,float bodyRadius)
        {
            EnsureLive();if(bodyRadius<0||float.IsNaN(bodyRadius)||float.IsInfinity(bodyRadius))throw new ArgumentOutOfRangeException("bodyRadius");
            string hitId=null;Point2 hitPoint=from;if(!Blocked(from,bodyRadius,out hitId,out hitPoint)){
                float distance=Distance(from,requested);int steps=Math.Max(1,(int)Math.Ceiling(distance/0.05f));float last=0,firstBlocked=1;bool hit=false;
                for(int i=1;i<=steps;i++){float t=(float)i/steps;Point2 p=Lerp(from,requested,t);string id;Point2 cp;if(Blocked(p,bodyRadius,out id,out cp)){firstBlocked=t;hitId=id;hitPoint=cp;hit=true;break;}last=t;}
                if(!hit)return new MovementResult(requested);
                float low=last,high=firstBlocked;for(int i=0;i<12;i++){float mid=(low+high)*.5f;string id;Point2 cp;if(Blocked(Lerp(from,requested,mid),bodyRadius,out id,out cp)){high=mid;hitId=id;hitPoint=cp;}else low=mid;}
                return new MovementResult(Lerp(from,requested,Math.Max(0,low-0.002f)),hitId,hitPoint);
            }
            return new MovementResult(from,hitId,hitPoint);
        }
        public IReadOnlyList<WorldEventSnapshot> CaptureEvents()
        {
            EnsureLive(); var list=new List<WorldEventSnapshot>();
            foreach(var pair in entries) if(pair.Value.Definition.Category==ObjectCategory.Event) list.Add(new WorldEventSnapshot(pair.Key,pair.Value.State,pair.Value.Remaining));
            return list.AsReadOnly();
        }
        public void Restore(IReadOnlyList<WorldEventSnapshot> snapshots, int randomSeed)
        {
            EnsureLive(); if(snapshots==null) throw new ArgumentNullException("snapshots"); seed=randomSeed;
            foreach(WorldEventSnapshot snapshot in snapshots) { Entry e=Get(snapshot.ObjectId); if(e.Definition.Category!=ObjectCategory.Event) throw new ArgumentException("Snapshot references non-event object"); e.State=snapshot.StateId; e.Remaining=snapshot.RemainingSeconds; }
        }
        public void Dispose() { disposed=true; entries.Clear(); occluders.Clear(); ground.Clear(); }
        private Entry Get(string id) { EnsureLive(); Entry e; if(!entries.TryGetValue(ContractGuard.Id(id),out e)) throw new KeyNotFoundException(id); return e; }
        private void EnsureLive() { if(disposed) throw new ObjectDisposedException("WorldRegistry"); }
        private static bool Intersects(Point2 a,Point2 b,Point2 c,Point2 d)
        { float d1=Cross(a,b,c),d2=Cross(a,b,d),d3=Cross(c,d,a),d4=Cross(c,d,b); const float e=0.00001f; if(d1*d2<0&&d3*d4<0)return true;return Math.Abs(d1)<=e&&On(a,b,c)||Math.Abs(d2)<=e&&On(a,b,d)||Math.Abs(d3)<=e&&On(c,d,a)||Math.Abs(d4)<=e&&On(c,d,b); }
        private static bool On(Point2 a,Point2 b,Point2 p){return p.X>=Math.Min(a.X,b.X)-0.00001f&&p.X<=Math.Max(a.X,b.X)+0.00001f&&p.Y>=Math.Min(a.Y,b.Y)-0.00001f&&p.Y<=Math.Max(a.Y,b.Y)+0.00001f;}
        private static bool Contains(Point2[] polygon,Point2 p){bool inside=false;for(int i=0,j=polygon.Length-1;i<polygon.Length;j=i++){Point2 a=polygon[i],b=polygon[j];if(((a.Y>p.Y)!=(b.Y>p.Y))&&(p.X<(b.X-a.X)*(p.Y-a.Y)/(b.Y-a.Y)+a.X))inside=!inside;}return inside;}
        private bool Blocked(Point2 p,float radius,out string id,out Point2 contact){id=null;contact=p;float best=float.PositiveInfinity;foreach(Entry e in entries.Values){if(e.Definition.Category!=ObjectCategory.Static||e.Definition.Semantic==PerceptionSemantic.Guide)continue;int n=e.Definition.LocalContour.Count;var points=new Point2[n];for(int i=0;i<n;i++)points[i]=Transform(e.Definition.LocalContour[i].A,e.Position,e.Rotation);if(n>=3&&Inside(points,p)){id=e.Definition.ObjectId;return true;}for(int i=0;i<n;i++){Segment2 s=new Segment2(Transform(e.Definition.LocalContour[i].A,e.Position,e.Rotation),Transform(e.Definition.LocalContour[i].B,e.Position,e.Rotation));Point2 q=Closest(p,s);float d=Distance(p,q);if(d<=radius&&d<best){best=d;id=e.Definition.ObjectId;contact=q;}}}return id!=null;}
        private static Point2 Transform(Point2 p,Point2 origin,float degrees){double r=degrees*Math.PI/180;return new Point2(origin.X+p.X*(float)Math.Cos(r)-p.Y*(float)Math.Sin(r),origin.Y+p.X*(float)Math.Sin(r)+p.Y*(float)Math.Cos(r));}
        private static Point2 Lerp(Point2 a,Point2 b,float t){return new Point2(a.X+(b.X-a.X)*t,a.Y+(b.Y-a.Y)*t);}
        private static float Distance(Point2 a,Point2 b){float x=a.X-b.X,y=a.Y-b.Y;return (float)Math.Sqrt(x*x+y*y);}
        private static float DistanceToSegment(Point2 p,Segment2 s){return Distance(p,Closest(p,s));}
        private static Point2 Closest(Point2 p,Segment2 s){float x=s.B.X-s.A.X,y=s.B.Y-s.A.Y,l=x*x+y*y;if(l<1e-10f)return s.A;float t=((p.X-s.A.X)*x+(p.Y-s.A.Y)*y)/l;t=Math.Max(0,Math.Min(1,t));return new Point2(s.A.X+x*t,s.A.Y+y*t);}
        private static bool Inside(Point2[] polygon,Point2 p){bool inside=false;for(int i=0,j=polygon.Length-1;i<polygon.Length;j=i++){Point2 a=polygon[i],b=polygon[j];if(((a.Y>p.Y)!=(b.Y>p.Y))&&(p.X<(b.X-a.X)*(p.Y-a.Y)/(b.Y-a.Y)+a.X))inside=!inside;}return inside;}
        private static float Cross(Point2 a,Point2 b,Point2 c) { return (b.X-a.X)*(c.Y-a.Y)-(b.Y-a.Y)*(c.X-a.X); }
    }
}
