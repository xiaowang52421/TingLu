using System;
using System.Collections.Generic;
using TingLu.Contracts;

namespace TingLu.Levels
{
    public sealed class FirstStepWorldObject
    {
        public ObjectDefinition Definition { get; private set; }
        public Point2 Position { get; private set; }
        public float Rotation { get; private set; }
        public bool SoundActive { get; private set; }
        public FirstStepWorldObject(ObjectDefinition definition,Point2 position,float rotation,bool soundActive){Definition=definition;Position=position;Rotation=rotation;SoundActive=soundActive;}
    }
    public sealed class FirstStepGround
    {
        public string Material { get; private set; }
        public IReadOnlyList<Point2> Polygon { get; private set; }
        public FirstStepGround(string material,Point2[] polygon){Material=material;Polygon=System.Array.AsReadOnly(polygon);}
    }
    /// <summary>Deterministic first-level data. The host registers it in the shared WorldRegistry.</summary>
    public static class FirstStepWorldFactory
    {
        private const float GuideHalfWidth = 0.3f;
        private const float MaximumGuideSegment = 3f;

        public static IReadOnlyList<FirstStepWorldObject> Objects()
        {
            var objects = new List<FirstStepWorldObject>{
                new FirstStepWorldObject(Static("l1-1/pole/01","concrete",Box(-.18f,-.18f,.18f,.18f)),new Point2(0,15),0,false),
                new FirstStepWorldObject(Static("l1-1/bicycle/01","metal",Box(-.85f,-.32f,.85f,.32f)),new Point2(18,41.1f),-22,false),
                // These walkers are visible world actors to the host. Their category keeps them out
                // of permanent memory; animation can call Move without changing their definitions.
                new FirstStepWorldObject(new ObjectDefinition("l1-1/walker/01",ObjectCategory.Dynamic,MemoryPolicy.Never,Box(-.25f,-.25f,.25f,.25f),"cloth","walker-steps",PerceptionSemantic.Person),new Point2(0,36),0,true),
                new FirstStepWorldObject(new ObjectDefinition("l1-1/gatekeeper/01",ObjectCategory.Dynamic,MemoryPolicy.Never,Box(-.25f,-.25f,.25f,.25f),"cloth","gatekeeper-voice",PerceptionSemantic.Person),new Point2(25,40),0,true),
                new FirstStepWorldObject(new ObjectDefinition("l1-1/bird/01",ObjectCategory.Event,MemoryPolicy.Never,new Segment2[0],"air","bird-morning"),new Point2(2,9),0,true),
                new FirstStepWorldObject(new ObjectDefinition("l1-1/fragment/01",ObjectCategory.Event,MemoryPolicy.Never,new Segment2[0],"air","sparrow-fragment",PerceptionSemantic.Interaction),new Point2(5,40.8f),0,true)};

            int index = 0;
            foreach (Segment2 segment in GuideSegments())
            {
                Point2 center = Midpoint(segment.A, segment.B);
                var local = new Segment2(new Point2(segment.A.X-center.X, segment.A.Y-center.Y),
                    new Point2(segment.B.X-center.X, segment.B.Y-center.Y));
                var guide = new ObjectDefinition("l1-1/guide/" + (++index).ToString("D3"),
                    ObjectCategory.Static, MemoryPolicy.Never, new[] { local }, "tactile_path", null, PerceptionSemantic.Guide);
                objects.Add(new FirstStepWorldObject(guide, center, 0, false));
            }
            return objects.AsReadOnly();
        }

        public static IReadOnlyList<FirstStepGround> Ground()
        {
            var surfaces = new List<FirstStepGround>{
                new FirstStepGround("sidewalk",Polygon(-1.5f,-1,1.5f,-1,1.5f,41.5f,-1.5f,41.5f)),
                new FirstStepGround("sidewalk",Polygon(0,38.5f,26,38.5f,26,41.5f,0,41.5f)),
                new FirstStepGround("garden",Polygon(2,38.5f,9,38.5f,9,43,2,43)),
                new FirstStepGround("asphalt",Polygon(12,38.5f,24,38.5f,24,43,12,43))};
            foreach (Segment2 segment in GuideSegments())
                surfaces.Add(new FirstStepGround("tactile_path", GuidePolygon(segment, GuideHalfWidth)));
            foreach (Point2 corner in GuideWaypoints())
                surfaces.Add(new FirstStepGround("tactile_path", Square(corner, GuideHalfWidth)));
            return surfaces.AsReadOnly();
        }

        private static IReadOnlyList<Segment2> GuideSegments()
        {
            Point2[] waypoints = GuideWaypoints();
            var segments = new List<Segment2>();
            for (int leg = 0; leg + 1 < waypoints.Length; leg++)
            {
                Point2 start = waypoints[leg], end = waypoints[leg + 1];
                float length = Distance(start, end);
                int pieces = Math.Max(1, (int)Math.Ceiling(length / MaximumGuideSegment));
                for (int piece = 0; piece < pieces; piece++)
                {
                    float a = (float)piece / pieces, b = (float)(piece + 1) / pieces;
                    segments.Add(new Segment2(Lerp(start, end, a), Lerp(start, end, b)));
                }
            }
            return segments.AsReadOnly();
        }

        private static Point2[] GuideWaypoints()
        {
            // The pole occupies x=[-.18,.18], y=[14.82,15.18]. Keep at least
            // 0.18m body clearance while the tactile line passes to its left.
            return new[]{new Point2(0,0),new Point2(0,13.8f),new Point2(-.65f,14.4f),
                new Point2(-.65f,15.6f),new Point2(0,16.4f),new Point2(0,40),
                new Point2(25,40),new Point2(28,40)};
        }

        private static Point2[] GuidePolygon(Segment2 segment, float halfWidth)
        {
            float dx = segment.B.X-segment.A.X, dy = segment.B.Y-segment.A.Y;
            float length = (float)Math.Sqrt(dx*dx+dy*dy);
            float extension = Math.Min(halfWidth, length*.25f);
            float ux=dx/length, uy=dy/length;
            float nx=-uy*halfWidth, ny=ux*halfWidth;
            Point2 a=new Point2(segment.A.X-ux*extension,segment.A.Y-uy*extension);
            Point2 b=new Point2(segment.B.X+ux*extension,segment.B.Y+uy*extension);
            return new[]{new Point2(a.X+nx,a.Y+ny),new Point2(a.X-nx,a.Y-ny),
                new Point2(b.X-nx,b.Y-ny),new Point2(b.X+nx,b.Y+ny)};
        }

        private static Point2[] Square(Point2 center,float halfWidth)
        { return new[]{new Point2(center.X-halfWidth,center.Y-halfWidth),new Point2(center.X+halfWidth,center.Y-halfWidth),new Point2(center.X+halfWidth,center.Y+halfWidth),new Point2(center.X-halfWidth,center.Y+halfWidth)}; }
        private static Point2 Midpoint(Point2 a,Point2 b){return new Point2((a.X+b.X)*.5f,(a.Y+b.Y)*.5f);}
        private static Point2 Lerp(Point2 a,Point2 b,float t){return new Point2(a.X+(b.X-a.X)*t,a.Y+(b.Y-a.Y)*t);}
        private static float Distance(Point2 a,Point2 b){float x=a.X-b.X,y=a.Y-b.Y;return (float)Math.Sqrt(x*x+y*y);}
        private static ObjectDefinition Static(string id,string material,IReadOnlyList<Segment2> contour)
        {return new ObjectDefinition(id,ObjectCategory.Static,MemoryPolicy.ConfirmedStaticGeometry,contour,material,null);}
        private static Segment2[] Box(float l,float b,float r,float t)
        {return new[]{new Segment2(new Point2(l,b),new Point2(r,b)),new Segment2(new Point2(r,b),new Point2(r,t)),new Segment2(new Point2(r,t),new Point2(l,t)),new Segment2(new Point2(l,t),new Point2(l,b))};}
        private static Point2[] Polygon(float x1,float y1,float x2,float y2,float x3,float y3,float x4,float y4)
        {return new[]{new Point2(x1,y1),new Point2(x2,y2),new Point2(x3,y3),new Point2(x4,y4)};}
        private static Point2[] Polygon(float x1,float y1,float x2,float y2,float x3,float y3,float x4,float y4,float x5,float y5)
        {return new[]{new Point2(x1,y1),new Point2(x2,y2),new Point2(x3,y3),new Point2(x4,y4),new Point2(x5,y5)};}
    }
}
