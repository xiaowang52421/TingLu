using System;
using NUnit.Framework;
using TingLu.Contracts;
using TingLu.Config;
namespace TingLu.Tests.Upgrade.Role00
{
    public sealed class ContractTests
    {
        [TestCase(-5f,-16f,0.0005f)]
        [TestCase(1000f,-1000f,0.001f)]
        public void ThinTranslatedPolygonKeepsAreaAcrossWindingAndCopies(float x,float y,float thickness)
        {
            var polygon=StreetGeometry.Rect(x,y,x+thickness,y+thickness);
            double expected=((double)polygon[1].X-polygon[0].X)*((double)polygon[2].Y-polygon[1].Y);
            Assert.That(expected,Is.GreaterThan(1e-8));
            Assert.That(StreetGeometry.Area(polygon),Is.EqualTo(expected).Within(1e-12));
            Array.Reverse(polygon);
            Assert.That(StreetGeometry.Area(polygon),Is.EqualTo(-expected).Within(1e-12));
            var copied=StreetGeometry.CopyPolygon(polygon);
            Assert.That(StreetGeometry.Area(copied),Is.EqualTo(expected).Within(1e-12));
            Assert.DoesNotThrow(()=>StreetGeometry.CopyPolygon(copied));
        }
        [Test] public void TranslatedCollinearPolygonRemainsDegenerate()
        {
            var polygon=new[]{new Point2(-5,-16),new Point2(-4,-15),new Point2(-3,-14)};
            Assert.That(StreetGeometry.Area(polygon),Is.Zero);
            Assert.Throws<ArgumentException>(()=>StreetGeometry.CopyPolygon(polygon));
        }
        [Test] public void ThinTriangleCopiesRemainValidWhenAbsoluteProductsCancel()
        {
            // The old float shoelace returned a negative area, then zero after reversal.
            var polygon=new[]{new Point2(-5.895838737487793f,-22.613975524902344f),
                new Point2(-18.802270889282227f,-2.259305953979492f),
                new Point2(-12.349055290222168f,-12.436640739440918f)};
            const double expected=4.852931397e-6;
            Assert.That(StreetGeometry.Area(polygon),Is.EqualTo(expected).Within(1e-11));
            Array.Reverse(polygon);
            Assert.That(StreetGeometry.Area(polygon),Is.EqualTo(-expected).Within(1e-11));
            var surface=new StreetKnownSurface("asphalt",polygon,0,1);
            Assert.DoesNotThrow(()=>new StreetKnownSurface(surface.MaterialId,surface.Polygon,0,1));
        }
        [TestCase(360f)] [TestCase(361f)]
        public void StreetRejectsFullCircleLowFocusProbeBeforeGameplay(float angle)
        {var config=new StreetConfig();config.Gameplay.lowFocusAngle=angle;Assert.Throws<ArgumentException>(()=>config.Validate());}
        [Test] public void StreetAcceptsSubFullCircleLowFocusProbe()
        {var config=new StreetConfig();config.Gameplay.lowFocusAngle=359f;Assert.DoesNotThrow(()=>config.Validate());}
        [Test] public void ConvexDifferenceConservesArea()
        {var a=StreetGeometry.Rect(0,0,4,4);var b=StreetGeometry.Rect(1,1,3,3);float sum=0;foreach(var p in StreetGeometry.Subtract(a,b))sum+=StreetGeometry.Area(p);Assert.That(sum,Is.EqualTo(12).Within(1e-4));Assert.That(StreetGeometry.Area(StreetGeometry.Intersect(a,b)),Is.EqualTo(4).Within(1e-4));}
        [Test] public void CoverageAndSnapshotsCopyMutableInputs()
        {var poly=StreetGeometry.Rect(0,0,2,2);var p=new StreetKnownSurface("sidewalk",poly,0,1);poly[0]=new Point2(100,100);Assert.That(p.Polygon[0].X,Is.Zero);}
        [Test] public void CoordinatesAndInputShareFixedBasis()
        {var f=StreetCoordinates.Turn(new Point2(0,1),90);Assert.That(f.X,Is.EqualTo(1).Within(1e-5));Assert.That(f.Y,Is.EqualTo(0).Within(1e-5));var p=StreetCoordinates.ToScene(new Point2(2,3));Assert.That(p.Z,Is.EqualTo(3));Assert.That(StreetCoordinates.ScreenMove(new Point2(1,1)).X,Is.EqualTo(0.7071068f).Within(1e-5));}
        [Test] public void InvalidSectorsAndActiveMemoriesAreRejected()
        {Assert.Throws<ArgumentException>(()=>new StreetProbeRequest("p",0,new Point2(),new Point2(0,1),360,7));Assert.Throws<ArgumentException>(()=>new StreetKnownObject("p","metal",StreetKind.Active,new Segment2[0],0,0,1));new StreetConfig().Validate();}
        [Test] public void ConvexDifferenceConservesAreaForRotatedClips()
        {
            var random=new Random(30215);
            for(int sample=0;sample<150;sample++)
            {
                var subject=StreetGeometry.Rect(-3,-2,3,2);float angle=(float)(random.NextDouble()*360),x=(float)(random.NextDouble()*8-4),y=(float)(random.NextDouble()*6-3);
                var clip=StreetGeometry.Rect(-1,-0.7f,1,0.7f);
                double radians=angle*Math.PI/180;
                for(int i=0;i<clip.Length;i++){var p=clip[i];clip[i]=new Point2(x+(float)(p.X*Math.Cos(radians)-p.Y*Math.Sin(radians)),y+(float)(p.X*Math.Sin(radians)+p.Y*Math.Cos(radians)));}
                float sum=0;foreach(var fragment in StreetGeometry.Subtract(subject,clip))sum+=Math.Abs(StreetGeometry.Area(fragment));
                var overlap=StreetGeometry.Intersect(subject,clip);sum+=Math.Abs(StreetGeometry.Area(overlap));
                Assert.That(sum,Is.EqualTo(24).Within(0.0003),"sample "+sample);
            }
        }
        [Test] public void SegmentDifferencePreservesBothUnobservedEnds()
        {
            var pieces=StreetGeometry.SubtractSegment(new Segment2(new Point2(-2,0),new Point2(2,0)),StreetGeometry.Rect(-1,-1,1,1));
            Assert.That(pieces.Count,Is.EqualTo(2));Assert.That(StreetGeometry.Distance(pieces[0].A,pieces[0].B)+StreetGeometry.Distance(pieces[1].A,pieces[1].B),Is.EqualTo(2).Within(1e-5));
        }
    }
}
