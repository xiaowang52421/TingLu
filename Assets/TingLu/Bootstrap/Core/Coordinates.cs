using System;
using TingLu.Contracts;
namespace TingLu.Bootstrap
{
    public struct AudioVector
    {
        public readonly float X, Y, Z;
        public AudioVector(float x, float y, float z) { X = x; Y = y; Z = z; }
    }
    public static class Coordinates
    {
        public static AudioVector ToAudio(Point2 p) { return new AudioVector(p.X, 0, p.Y); }
        // Zero is +Y, positive angles rotate counterclockwise toward -X.
        public static float Bearing(Point2 facing, Point2 direction)
        { return (float)(Math.Atan2(facing.X * direction.Y - facing.Y * direction.X, facing.X * direction.X + facing.Y * direction.Y) * 180 / Math.PI); }
    }
}
