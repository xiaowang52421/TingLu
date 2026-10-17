using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace TingLu.Contracts
{
    // Continuous convex clipping primitives. Cells are polygon fragments, never gameplay grid cells.
    public static class StreetGeometry
    {
        public const float Epsilon=0.00001f;
        public static float Cross(Point2 a,Point2 b,Point2 c){return (b.X-a.X)*(c.Y-a.Y)-(b.Y-a.Y)*(c.X-a.X);}
        public static float Distance(Point2 a,Point2 b){float x=a.X-b.X,y=a.Y-b.Y;return (float)Math.Sqrt(x*x+y*y);}
        public static Point2 Lerp(Point2 a,Point2 b,float t){return new Point2(a.X+(b.X-a.X)*t,a.Y+(b.Y-a.Y)*t);}
        public static Point2 Unit(Point2 p){float l=Distance(p,new Point2());if(l<Epsilon)throw new ArgumentException("Zero facing");return new Point2(p.X/l,p.Y/l);}
        public static Point2 NormalizeClamped(Point2 p){float l=Distance(p,new Point2());return l>1?new Point2(p.X/l,p.Y/l):p;}
        public static float Area(IReadOnlyList<Point2> p)
        {
            if(p.Count<3)return 0;
            // Translate before multiplying so thin fragments far from the origin do not
            // lose their area to cancellation. Keep the public units and threshold unchanged.
            double x=p[0].X,y=p[0].Y,sum=0;
            for(int i=1;i<p.Count-1;i++)
            {
                var a=p[i];var b=p[i+1];
                sum+=((double)a.X-x)*((double)b.Y-y)-((double)b.X-x)*((double)a.Y-y);
            }
            return (float)(sum*0.5);
        }
        public static ReadOnlyCollection<Point2> CopyPolygon(IEnumerable<Point2> p)
        {var list=new List<Point2>(p??throw new ArgumentNullException("polygon"));if(list.Count<3||Math.Abs(Area(list))<1e-8f)throw new ArgumentException("Degenerate polygon");if(Area(list)<0)list.Reverse();return list.AsReadOnly();}
        public static Point2[] Rect(float x0,float y0,float x1,float y1)
        {return new[]{new Point2(x0,y0),new Point2(x1,y0),new Point2(x1,y1),new Point2(x0,y1)};}
        public static bool Contains(IReadOnlyList<Point2> polygon,Point2 p,float epsilon=Epsilon)
        {if(polygon.Count<3)return false;float sign=Area(polygon)>=0?1:-1;for(int i=0;i<polygon.Count;i++)if(sign*Cross(polygon[i],polygon[(i+1)%polygon.Count],p)<-epsilon)return false;return true;}
        public static List<Point2> HalfPlane(IReadOnlyList<Point2> p,Point2 a,Point2 b,bool inside)
        {
            var output=new List<Point2>();if(p.Count==0)return output;
            Point2 prev=p[p.Count-1];float pv=Cross(a,b,prev)*(inside?1:-1);bool pin=pv>=0;
            foreach(Point2 cur in p){float cv=Cross(a,b,cur)*(inside?1:-1);bool cin=cv>=0;if(pin!=cin){float t=pv/(pv-cv);output.Add(Lerp(prev,cur,t));}if(cin)output.Add(cur);prev=cur;pv=cv;pin=cin;}
            return Clean(output);
        }
        public static List<Point2> Intersect(IReadOnlyList<Point2> a,IReadOnlyList<Point2> b)
        {var r=new List<Point2>(a);var clip=new List<Point2>(b);if(Area(clip)<0)clip.Reverse();for(int i=0;i<clip.Count&&r.Count>=3;i++)r=HalfPlane(r,clip[i],clip[(i+1)%clip.Count],true);return Valid(r)?r:new List<Point2>();}
        public static List<List<Point2>> Subtract(IReadOnlyList<Point2> subject,IReadOnlyList<Point2> clip)
        {
            var output=new List<List<Point2>>();var remaining=new List<Point2>(subject);var c=new List<Point2>(clip);if(Area(c)<0)c.Reverse();
            for(int i=0;i<c.Count&&Valid(remaining);i++){var outside=HalfPlane(remaining,c[i],c[(i+1)%c.Count],false);if(Valid(outside))output.Add(outside);remaining=HalfPlane(remaining,c[i],c[(i+1)%c.Count],true);}return output;
        }
        public static bool ClipSegment(Segment2 s,IReadOnlyList<Point2> poly,out Segment2 clipped)
        {
            float low=0,high=1;float sign=Area(poly)>=0?1:-1;
            for(int i=0;i<poly.Count;i++){var a=poly[i];var b=poly[(i+1)%poly.Count];float d0=sign*Cross(a,b,s.A),d1=sign*Cross(a,b,s.B);if(d0<-Epsilon&&d1<-Epsilon){clipped=default(Segment2);return false;}if(Math.Abs(d1-d0)<1e-12f)continue;float t=-d0/(d1-d0);if(d0<0)low=Math.Max(low,t);if(d1<0)high=Math.Min(high,t);}
            clipped=new Segment2(Lerp(s.A,s.B,low),Lerp(s.A,s.B,high));return high-low>1e-7f;
        }
        public static List<Segment2> SubtractSegment(Segment2 s,IReadOnlyList<Point2> poly)
        {
            var r=new List<Segment2>();Segment2 cut;if(!ClipSegment(s,poly,out cut)){r.Add(s);return r;}
            if(Distance(s.A,cut.A)>Epsilon)r.Add(new Segment2(s.A,cut.A));if(Distance(cut.B,s.B)>Epsilon)r.Add(new Segment2(cut.B,s.B));return r;
        }
        public static Point2 Closest(Point2 p,Segment2 s){float x=s.B.X-s.A.X,y=s.B.Y-s.A.Y,l=x*x+y*y;return l<1e-12f?s.A:Lerp(s.A,s.B,Math.Max(0,Math.Min(1,((p.X-s.A.X)*x+(p.Y-s.A.Y)*y)/l)));}
        public static bool RaySegment(Point2 origin,Point2 direction,Segment2 s,out float distance)
        {float sx=s.B.X-s.A.X,sy=s.B.Y-s.A.Y,den=direction.X*sy-direction.Y*sx;distance=0;if(Math.Abs(den)<1e-9)return false;float qx=s.A.X-origin.X,qy=s.A.Y-origin.Y;float t=(qx*sy-qy*sx)/den,u=(qx*direction.Y-qy*direction.X)/den;if(t<0||u<0||u>1)return false;distance=t;return true;}
        public static bool Valid(IReadOnlyList<Point2> p){return p.Count>=3&&Math.Abs(Area(p))>1e-8f;}
        private static List<Point2> Clean(List<Point2> p)
        {for(int i=p.Count-1;i>=0&&p.Count>1;i--)if(Distance(p[i],p[(i+1)%p.Count])<1e-7)p.RemoveAt(i);return p;}
    }
}
