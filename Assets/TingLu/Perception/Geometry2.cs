using System;
using TingLu.Contracts;

namespace TingLu.Perception
{
    internal static class Geometry2
    {
        internal static Point2 Transform(Point2 p, Point2 origin, float degrees)
        {
            double a = degrees * Math.PI / 180.0, c = Math.Cos(a), s = Math.Sin(a);
            return new Point2(origin.X + (float)(p.X * c - p.Y * s), origin.Y + (float)(p.X * s + p.Y * c));
        }

        internal static float Distance(Point2 a, Point2 b)
        { float x = a.X - b.X, y = a.Y - b.Y; return (float)Math.Sqrt(x * x + y * y); }

        internal static Point2 Lerp(Point2 a, Point2 b, float t)
        { return new Point2(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t); }
    }
}
