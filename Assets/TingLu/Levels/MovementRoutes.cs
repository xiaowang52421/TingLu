using System;
using System.Collections.Generic;
using TingLu.Contracts;

namespace TingLu.Levels
{
    public readonly struct MovementRoutePoint
    {
        public readonly double TimeSeconds;
        public readonly Point2 Position;
        public readonly float RotationDegrees;
        public MovementRoutePoint(double timeSeconds,Point2 position,float rotationDegrees)
        {TimeSeconds=timeSeconds;Position=position;RotationDegrees=rotationDegrees;}
    }

    public readonly struct MovementPose
    {
        public readonly Point2 Position;
        public readonly float RotationDegrees;
        public MovementPose(Point2 position,float rotationDegrees)
        {Position=position;RotationDegrees=rotationDegrees;}
    }

    /// <summary>Immutable route compiled from authoring data; times are simulation seconds.</summary>
    public sealed class MovementRouteDefinition
    {
        private readonly IReadOnlyList<MovementRoutePoint> points;
        public string Id { get; private set; }
        public string ActorId { get; private set; }
        public bool Loop { get; private set; }
        public IReadOnlyList<MovementRoutePoint> Points { get { return points; } }
        public double DurationSeconds { get { return points[points.Count-1].TimeSeconds; } }
        public MovementRouteDefinition(string id,string actorId,bool loop,IEnumerable<MovementRoutePoint> waypoints)
        {Id=ContractGuard.Id(id);ActorId=ContractGuard.Id(actorId);Loop=loop;points=ContractGuard.Copy(waypoints);}
    }

    public static class MovementRouteSampler
    {
        public static MovementPose Sample(MovementRouteDefinition route,double simulationTime)
        {
            if(route==null)throw new ArgumentNullException("route");
            ContractGuard.Time(simulationTime);
            IReadOnlyList<MovementRoutePoint> points=route.Points;
            double time=simulationTime;
            if(route.Loop)time%=route.DurationSeconds;
            if(time<=points[0].TimeSeconds)return Pose(points[0]);
            for(int i=1;i<points.Count;i++)
            {
                MovementRoutePoint end=points[i];
                if(time>end.TimeSeconds)continue;
                MovementRoutePoint start=points[i-1];
                double span=end.TimeSeconds-start.TimeSeconds;
                float t=(float)((time-start.TimeSeconds)/span);
                float rotation=start.RotationDegrees+DeltaAngle(start.RotationDegrees,end.RotationDegrees)*t;
                return new MovementPose(new Point2(start.Position.X+(end.Position.X-start.Position.X)*t,
                    start.Position.Y+(end.Position.Y-start.Position.Y)*t),rotation);
            }
            return Pose(points[points.Count-1]);
        }
        private static MovementPose Pose(MovementRoutePoint point)
        {return new MovementPose(point.Position,point.RotationDegrees);}
        private static float DeltaAngle(float from,float to)
        {float delta=(to-from)%360f;if(delta>180)delta-=360;if(delta< -180)delta+=360;return delta;}
    }
}
