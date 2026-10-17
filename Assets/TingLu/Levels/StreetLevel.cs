using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using TingLu.Contracts;

namespace TingLu.Levels
{
    /// <summary>Runtime-only content for the street upgrade; deliberately separate from LevelDocument.</summary>
    public sealed class StreetLevel
    {
        public readonly string Id;
        public readonly string Title;
        public readonly string GoalText;
        public readonly PlayerState Spawn;
        public readonly ReadOnlyCollection<StreetSurface> Surfaces;
        public readonly ReadOnlyCollection<StreetObject> Objects;
        public readonly ReadOnlyCollection<StreetRoute> Routes;
        public readonly ReadOnlyCollection<StreetCheckpoint> Checkpoints;
        public readonly Point2 Goal;
        public readonly float GoalRadius;

        internal StreetLevel(string id, string title, string goalText, PlayerState spawn,
            IList<StreetSurface> surfaces, IList<StreetObject> objects, IList<StreetRoute> routes,
            IList<StreetCheckpoint> checkpoints, Point2 goal, float goalRadius)
        {
            Id = id;
            Title = title;
            GoalText = goalText;
            Spawn = spawn;
            Surfaces = new List<StreetSurface>(surfaces).AsReadOnly();
            Objects = new List<StreetObject>(objects).AsReadOnly();
            Routes = new List<StreetRoute>(routes).AsReadOnly();
            Checkpoints = new List<StreetCheckpoint>(checkpoints).AsReadOnly();
            Goal = goal;
            GoalRadius = goalRadius;
        }
    }

    public static class StreetLevelFactory
    {
        public const string LevelId = "street.crossroads.v1";
        private const float RoadHalfWidth = 4f;
        private const float CrosswalkOuterEdge = 5.9f;
        private const float VehicleHalfLength = 0.8f;
        private const float StopMargin = 0.5f;
        private const float RoadExtent = 24f;
        private const float VehicleRouteExtent = 27f;
        public const float StopLineDistance = VehicleRouteExtent - CrosswalkOuterEdge - VehicleHalfLength - StopMargin;

        public static StreetLevel Create()
        {
            var surfaces = CreateSurfaces();
            var objects = new List<StreetObject>();
            var routes = new List<StreetRoute>();
            var checkpoints = new List<StreetCheckpoint>
            {
                new StreetCheckpoint("checkpoint.west.approach", new Point2(-5.5f, 5.3f), new Point2(1, 0), 0.75f),
                new StreetCheckpoint("checkpoint.east.landing", new Point2(5.5f, 5.3f), new Point2(1, 0), 0.75f)
            };

            AddBuilding(objects, "building.nw", -24, 7, -7, 24);
            AddBuilding(objects, "building.sw", -24, -24, -7, -7);
            AddBuilding(objects, "building.se", 7, -24, 24, -7);
            AddBuilding(objects, "building.ne.west", 7, 10.5f, 8.2f, 24);
            AddBuilding(objects, "building.ne.east", 10.7f, 7, 24, 24);

            AddBox(objects, "obstacle.bicycle.west", StreetKind.Temporary, -5.5f, -10f,
                0.58f, 0.68f, "metal", true, false, true, null, 0, 0.65f, 15);
            AddBox(objects, "obstacle.cardboard.east", StreetKind.Temporary, 5.5f, 9.3f,
                0.58f, 0.58f, "cardboard", true, false, true, null, 0, 0.7f);
            AddBox(objects, "obstacle.barrier.se.edge", StreetKind.Temporary, 5.9f, -13f,
                0.45f, 0.4f, "metal", true, false, true, null, 0.15f, 1.1f);
            AddBox(objects, "vehicle.parked.se", StreetKind.Temporary, 3.1f, -14f,
                0.52f, 1.05f, "metal", true, false, false, "street.car", 0, 0.8f, 15);

            AddSignal(objects, "street.signal.ns", -6.35f, 5.3f);
            AddSignal(objects, "street.signal.ew", 5.3f, -6.35f);

            AddVehicle(objects, routes, "vehicle.ns.southbound", "street.car",
                new[]
                {
                    new Point2(-1.35f, VehicleRouteExtent), new Point2(-1.35f, -VehicleRouteExtent),
                    new Point2(-32, -VehicleRouteExtent), new Point2(-32, 32),
                    new Point2(-1.35f, 32), new Point2(-1.35f, VehicleRouteExtent)
                }, true, "ns");
            AddVehicle(objects, routes, "vehicle.ns.northbound", "street.car",
                new[]
                {
                    new Point2(1.35f, -VehicleRouteExtent), new Point2(1.35f, VehicleRouteExtent),
                    new Point2(32, VehicleRouteExtent), new Point2(32, -32),
                    new Point2(1.35f, -32), new Point2(1.35f, -VehicleRouteExtent)
                }, true, "ns");
            AddVehicle(objects, routes, "vehicle.ew.eastbound", "street.car",
                new[]
                {
                    new Point2(-VehicleRouteExtent, -1.35f), new Point2(VehicleRouteExtent, -1.35f),
                    new Point2(VehicleRouteExtent, 36), new Point2(-36, 36),
                    new Point2(-36, -1.35f), new Point2(-VehicleRouteExtent, -1.35f)
                }, false, "ew");
            AddVehicle(objects, routes, "vehicle.ew.westbound", "street.car",
                new[]
                {
                    new Point2(VehicleRouteExtent, 1.35f), new Point2(-VehicleRouteExtent, 1.35f),
                    new Point2(-VehicleRouteExtent, -36), new Point2(36, -36),
                    new Point2(36, 1.35f), new Point2(VehicleRouteExtent, 1.35f)
                }, false, "ew");

            Point2 cycleStart = new Point2(12, -5.55f);
            var cyclePolygon = StreetGeometry.Rect(cycleStart.X - 0.72f, cycleStart.Y - 0.3f,
                cycleStart.X + 0.72f, cycleStart.Y + 0.3f);
            objects.Add(new StreetObject("rider.east.sidewalk", StreetKind.Active, cycleStart, cyclePolygon,
                "metal", false, true, false, 0, 0.8f, "street.electric", true));
            routes.Add(new StreetRoute("rider.east.sidewalk",
                new[] { cycleStart, new Point2(22, -5.55f) }, 2.4f, null, 0, false));

            Point2 walkerStart = new Point2(-5.5f, 14);
            objects.Add(new StreetObject("walker.north.west", StreetKind.Active, walkerStart,
                StreetGeometry.Rect(walkerStart.X - 0.25f, walkerStart.Y - 0.25f,
                    walkerStart.X + 0.25f, walkerStart.Y + 0.25f),
                "brick", false, false, false, 0, 1.7f, "street.walker", true));
            routes.Add(new StreetRoute("walker.north.west",
                new[] { walkerStart, new Point2(-5.5f, 21) }, 0.8f, null, 0, false));

            Point2 bird = new Point2(12.8f, 18.2f);
            objects.Add(new StreetObject("animal.bird.ne", StreetKind.Active, bird, new Point2[0],
                "brick", false, false, false, 0, 0, "street.bird", true));

            return new StreetLevel(LevelId, "十字路口", "先沿西侧盲道走到斑马线前。听着提示音过街，再往东北小巷走，到巷口就到了。",
                new PlayerState(PlayerMainState.Active, ActionMode.Walking,
                    new Point2(-5.5f, -16), new Point2(0, 1), 100, 100, 0, 0, 0),
                surfaces, objects, routes, checkpoints, new Point2(9.45f, 18), 0.85f);
        }

        private static List<StreetSurface> CreateSurfaces()
        {
            var surfaces = new List<StreetSurface>();
            AddSurface(surfaces, "road.ns", "asphalt", StreetGeometry.Rect(-RoadHalfWidth, -RoadExtent, RoadHalfWidth, RoadExtent), 20);
            AddSurface(surfaces, "road.ew", "asphalt", StreetGeometry.Rect(-RoadExtent, -RoadHalfWidth, RoadExtent, RoadHalfWidth), 20);

            AddSurface(surfaces, "sidewalk.west", "sidewalk", StreetGeometry.Rect(-7, -RoadExtent, -4, RoadExtent), 10);
            AddSurface(surfaces, "sidewalk.east", "sidewalk", StreetGeometry.Rect(4, -RoadExtent, 7, RoadExtent), 10);
            AddSurface(surfaces, "sidewalk.south", "sidewalk", StreetGeometry.Rect(-RoadExtent, -7, RoadExtent, -4), 10);
            AddSurface(surfaces, "sidewalk.north", "sidewalk", StreetGeometry.Rect(-RoadExtent, 4, RoadExtent, 7), 10);

            AddSurface(surfaces, "crosswalk.ns.north", "crosswalk", StreetGeometry.Rect(-4, 4.6f, 4, 5.9f), 30);
            AddSurface(surfaces, "crosswalk.ns.south", "crosswalk", StreetGeometry.Rect(-4, -5.9f, 4, -4.6f), 30);
            AddSurface(surfaces, "crosswalk.ew.west", "crosswalk", StreetGeometry.Rect(-5.9f, -4, -4.6f, 4), 30);
            AddSurface(surfaces, "crosswalk.ew.east", "crosswalk", StreetGeometry.Rect(4.6f, -4, 5.9f, 4), 30);

            AddSurface(surfaces, "ramp.ns.north.west", "ramp", StreetGeometry.Rect(-4.5f, 4.2f, -3.8f, 6.2f), 40);
            AddSurface(surfaces, "ramp.ns.north.east", "ramp", StreetGeometry.Rect(3.8f, 4.2f, 4.5f, 6.2f), 40);
            AddSurface(surfaces, "ramp.ns.south.west", "ramp", StreetGeometry.Rect(-4.5f, -6.2f, -3.8f, -4.2f), 40);
            AddSurface(surfaces, "ramp.ns.south.east", "ramp", StreetGeometry.Rect(3.8f, -6.2f, 4.5f, -4.2f), 40);
            AddSurface(surfaces, "ramp.ew.west.south", "ramp", StreetGeometry.Rect(-6.2f, -4.5f, -4.2f, -3.8f), 40);
            AddSurface(surfaces, "ramp.ew.west.north", "ramp", StreetGeometry.Rect(-6.2f, 3.8f, -4.2f, 4.5f), 40);
            AddSurface(surfaces, "ramp.ew.east.south", "ramp", StreetGeometry.Rect(4.2f, -4.5f, 6.2f, -3.8f), 40);
            AddSurface(surfaces, "ramp.ew.east.north", "ramp", StreetGeometry.Rect(4.2f, 3.8f, 6.2f, 4.5f), 40);

            AddSurface(surfaces, "tactile.west.south", "tactile", StreetGeometry.Rect(-5.8f, -23.8f, -5.2f, -6.2f), 50);
            AddSurface(surfaces, "tactile.west.north", "tactile", StreetGeometry.Rect(-5.8f, 6.2f, -5.2f, 23.8f), 50);
            AddSurface(surfaces, "tactile.east.south", "tactile", StreetGeometry.Rect(5.2f, -23.8f, 5.8f, -6.2f), 50);
            AddSurface(surfaces, "tactile.east.north", "tactile", StreetGeometry.Rect(5.2f, 6.2f, 5.8f, 23.8f), 50);
            AddSurface(surfaces, "tactile.north.west", "tactile", StreetGeometry.Rect(-23.8f, 5.2f, -6.2f, 5.8f), 50);
            AddSurface(surfaces, "tactile.north.east", "tactile", StreetGeometry.Rect(6.2f, 5.2f, 23.8f, 5.8f), 50);
            AddSurface(surfaces, "tactile.south.west", "tactile", StreetGeometry.Rect(-23.8f, -5.8f, -6.2f, -5.2f), 50);
            AddSurface(surfaces, "tactile.south.east", "tactile", StreetGeometry.Rect(6.2f, -5.8f, 23.8f, -5.2f), 50);

            AddSurface(surfaces, "tactile.dots.north.west", "tactile.dots", StreetGeometry.Rect(-5.95f, 4.55f, -5.05f, 5.95f), 60);
            AddSurface(surfaces, "tactile.dots.north.east", "tactile.dots", StreetGeometry.Rect(5.05f, 4.55f, 5.95f, 5.95f), 60);
            AddSurface(surfaces, "tactile.dots.south.west", "tactile.dots", StreetGeometry.Rect(-5.95f, -5.95f, -5.05f, -4.55f), 60);
            AddSurface(surfaces, "tactile.dots.south.east", "tactile.dots", StreetGeometry.Rect(5.05f, -5.95f, 5.95f, -4.55f), 60);
            AddSurface(surfaces, "tactile.dots.west.south", "tactile.dots", StreetGeometry.Rect(-5.95f, -5.95f, -4.55f, -5.05f), 60);
            AddSurface(surfaces, "tactile.dots.west.north", "tactile.dots", StreetGeometry.Rect(-5.95f, 5.05f, -4.55f, 5.95f), 60);
            AddSurface(surfaces, "tactile.dots.east.south", "tactile.dots", StreetGeometry.Rect(4.55f, -5.95f, 5.95f, -5.05f), 60);
            AddSurface(surfaces, "tactile.dots.east.north", "tactile.dots", StreetGeometry.Rect(4.55f, 5.05f, 5.95f, 5.95f), 60);

            AddSurface(surfaces, "alley.entry", "brick", StreetGeometry.Rect(7, 7, 8.2f, 10.5f), 10);
            AddSurface(surfaces, "alley.ne", "brick", StreetGeometry.Rect(8.2f, 7, 10.7f, RoadExtent), 10);

            AddSurface(surfaces, "curb.ns.west.south", "curb", StreetGeometry.Rect(-4.15f, -RoadExtent, -4, -6.2f), 25);
            AddSurface(surfaces, "curb.ns.west.north", "curb", StreetGeometry.Rect(-4.15f, 6.2f, -4, RoadExtent), 25);
            AddSurface(surfaces, "curb.ns.east.south", "curb", StreetGeometry.Rect(4, -RoadExtent, 4.15f, -6.2f), 25);
            AddSurface(surfaces, "curb.ns.east.north", "curb", StreetGeometry.Rect(4, 6.2f, 4.15f, RoadExtent), 25);
            AddSurface(surfaces, "curb.ew.south.west", "curb", StreetGeometry.Rect(-RoadExtent, -4.15f, -6.2f, -4), 25);
            AddSurface(surfaces, "curb.ew.south.east", "curb", StreetGeometry.Rect(6.2f, -4.15f, RoadExtent, -4), 25);
            AddSurface(surfaces, "curb.ew.north.west", "curb", StreetGeometry.Rect(-RoadExtent, 4, -6.2f, 4.15f), 25);
            AddSurface(surfaces, "curb.ew.north.east", "curb", StreetGeometry.Rect(6.2f, 4, RoadExtent, 4.15f), 25);
            return surfaces;
        }

        private static void AddVehicle(List<StreetObject> objects, List<StreetRoute> routes, string id,
            string sound, Point2[] points, bool vertical, string signalAxis)
        {
            Point2 start = points[0];
            float halfWidth = vertical ? 0.48f : VehicleHalfLength;
            float halfHeight = vertical ? VehicleHalfLength : 0.48f;
            objects.Add(new StreetObject(id, StreetKind.Active, start,
                StreetGeometry.Rect(start.X - halfWidth, start.Y - halfHeight,
                    start.X + halfWidth, start.Y + halfHeight),
                "metal", false, true, false, 0, 1.45f, sound, true));
            routes.Add(new StreetRoute(id, points, 5.5f, signalAxis,
                StopLineDistance, true, StopLineDistance));
        }

        private static void AddSignal(List<StreetObject> objects, string id, float x, float y)
        {
            // A small permanent, non-solid housing is the visible geometry.  The
            // phase-dependent audio source keeps the stable signal id and is
            // synchronized by StreetWorld; lamp colour is a short-lived observation.
            objects.Add(new StreetObject(id, StreetKind.Permanent, new Point2(x, y),
                StreetGeometry.Rect(x - 0.18f, y - 0.18f, x + 0.18f, y + 0.18f),
                "metal", false, false, false, 0, 2f, id, false));
        }

        private static void AddBuilding(List<StreetObject> objects, string id, float x0, float y0, float x1, float y1)
        {
            Point2 center = new Point2((x0 + x1) * 0.5f, (y0 + y1) * 0.5f);
            objects.Add(new StreetObject(id, StreetKind.Permanent, center, StreetGeometry.Rect(x0, y0, x1, y1),
                "brick", true, false, true, 0.7f, 4.5f));
        }

        private static void AddBox(List<StreetObject> objects, string id, StreetKind kind, float x, float y,
            float halfWidth, float halfHeight, string material, bool solid, bool vehicleHazard,
            bool blocksProbe, string sound, float acousticLoss, float height, float? damage = null)
        {
            var center = new Point2(x, y);
            objects.Add(new StreetObject(id, kind, center,
                StreetGeometry.Rect(x - halfWidth, y - halfHeight, x + halfWidth, y + halfHeight),
                material, solid, vehicleHazard, blocksProbe, acousticLoss, height, sound, false,
                PerceptionSemantic.Ordinary, damage));
        }

        private static void AddSurface(List<StreetSurface> surfaces, string id, string material,
            Point2[] polygon, int priority)
        {
            const float curbHeight = 0.15f;
            float baseHeight = 0f, slopeX = 0f, slopeY = 0f;
            if (material == "sidewalk" || material == "tactile" || material == "tactile.dots" || material == "curb" || material == "brick")
                baseHeight = curbHeight;
            if (material == "ramp")
            {
                Point2 center = new Point2((polygon[0].X + polygon[2].X) * 0.5f, (polygon[0].Y + polygon[2].Y) * 0.5f);
                // NS crossings run across the north/south road, from the west
                // sidewalk to the east sidewalk. Their ramps therefore descend
                // along X; EW crossings descend along Y. Position relative to
                // the junction does not determine the direction of the slope.
                if (id.StartsWith("ramp.ns.", StringComparison.Ordinal))
                {
                    slopeX = (center.X >= 0 ? curbHeight : -curbHeight) / (polygon[2].X - polygon[0].X);
                    float roadEnd = center.X >= 0 ? polygon[0].X : polygon[2].X;
                    baseHeight = -slopeX * roadEnd;
                }
                else
                {
                    slopeY = (center.Y >= 0 ? curbHeight : -curbHeight) / (polygon[2].Y - polygon[0].Y);
                    float roadEnd = center.Y >= 0 ? polygon[0].Y : polygon[2].Y;
                    baseHeight = -slopeY * roadEnd;
                }
            }
            surfaces.Add(new StreetSurface(id, material, polygon, priority, null, baseHeight, slopeX, slopeY));
        }
    }
}
