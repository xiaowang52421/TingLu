using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using TingLu.Contracts;

namespace TingLu.Levels
{
    /// <summary>
    /// Editable, serializable schema for the street upgrade.  It is intentionally
    /// separate from the M0 LevelDocument JSON contract.
    /// </summary>
    [Serializable, DataContract]
    public sealed class StreetLevelDocument
    {
        public const int CurrentSchemaVersion = 2;
        public const int CurrentVersion = CurrentSchemaVersion;

        [DataMember(Order = 0, IsRequired = true)] public int schemaVersion = CurrentSchemaVersion;
        [DataMember(Order = 1, IsRequired = true)] public string id = "street.level";
        [DataMember(Order = 2, IsRequired = true)] public string title = "新街区关卡";
        [DataMember(Order = 3, IsRequired = true)] public string goalText = "沿着盲道前进。";
        [DataMember(Order = 4, IsRequired = true)] public float spawnX, spawnY, facingX = 0, facingY = 1;
        [DataMember(Order = 5, IsRequired = true)] public float goalX, goalY, goalRadius = 1;
        [DataMember(Order = 6, IsRequired = true)] public List<StreetSurfaceData> surfaces = new List<StreetSurfaceData>();
        [DataMember(Order = 7, IsRequired = true)] public List<StreetObjectData> objects = new List<StreetObjectData>();
        [DataMember(Order = 8, IsRequired = true)] public List<StreetRouteData> routes = new List<StreetRouteData>();
        [DataMember(Order = 9, IsRequired = true)] public List<StreetCheckpointData> checkpoints = new List<StreetCheckpointData>();
    }

    [Serializable, DataContract]
    public sealed class StreetPointData
    {
        [DataMember(Order = 0)] public float x, y;
        public StreetPointData() { }
        public StreetPointData(float px, float py) { x = px; y = py; }
        public Point2 ToPoint() { return new Point2(x, y); }
        public static StreetPointData From(Point2 p) { return new StreetPointData(p.X, p.Y); }
    }

    [Serializable, DataContract]
    public sealed class StreetSurfaceData
    {
        [DataMember(Order = 0)] public string id = "surface";
        [DataMember(Order = 1)] public string materialId = "sidewalk";
        [DataMember(Order = 2)] public List<StreetPointData> polygon = new List<StreetPointData>();
        [DataMember(Order = 3)] public StreetPointData textureAxis = new StreetPointData(1, 0);
        [DataMember(Order = 4)] public float heightBase, heightSlopeX, heightSlopeY;
        [DataMember(Order = 5)] public int priority;
    }

    [Serializable, DataContract]
    public sealed class StreetObjectData
    {
        [DataMember(Order = 0)] public string id = "object";
        [DataMember(Order = 1)] public StreetKind kind = StreetKind.Permanent;
        [DataMember(Order = 2)] public StreetPointData position = new StreetPointData();
        [DataMember(Order = 3)] public List<StreetPointData> polygon = new List<StreetPointData>();
        [DataMember(Order = 4)] public string materialId = "brick";
        [DataMember(Order = 5)] public bool solid, vehicleHazard, blocksProbe;
        [DataMember(Order = 6)] public float acousticLoss, height;
        [DataMember(Order = 7)] public string soundId;
        [DataMember(Order = 8)] public bool soundActive;
        [DataMember(Order = 9)] public PerceptionSemantic semantic = PerceptionSemantic.Ordinary;
        [DataMember(Order = 10)] public float? contactDamage, contactStun;
    }

    [Serializable, DataContract]
    public sealed class StreetRouteData
    {
        [DataMember(Order = 0)] public string objectId;
        [DataMember(Order = 1)] public List<StreetPointData> points = new List<StreetPointData>();
        [DataMember(Order = 2)] public float speed = 1;
        [DataMember(Order = 3)] public string signalAxis;
        [DataMember(Order = 4)] public double offset;
        [DataMember(Order = 5)] public bool loop = true;
        [DataMember(Order = 6)] public float? stopLineDistance;
    }

    [Serializable, DataContract]
    public sealed class StreetCheckpointData
    {
        [DataMember(Order = 0)] public string id = "checkpoint";
        [DataMember(Order = 1)] public StreetPointData position = new StreetPointData();
        [DataMember(Order = 2)] public StreetPointData facing = new StreetPointData(0, 1);
        [DataMember(Order = 3)] public float radius = .7f;
    }

    public static class StreetLevelCodec
    {
        public static string Encode(StreetLevelDocument value)
        {
            if (value == null) throw new ArgumentNullException("value");
            using (var stream = new MemoryStream())
            {
                new DataContractJsonSerializer(typeof(StreetLevelDocument)).WriteObject(stream, value);
                return Encoding.UTF8.GetString(stream.ToArray());
            }
        }

        public static StreetLevelDocument Decode(string json)
        {
            if (String.IsNullOrWhiteSpace(json)) throw new InvalidDataException("街区关卡文件为空。");
            StreetLevelDocument value;
            try
            {
                using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(json)))
                    value = (StreetLevelDocument)new DataContractJsonSerializer(typeof(StreetLevelDocument)).ReadObject(stream);
            }
            catch (Exception error) { throw new InvalidDataException("街区关卡文件无法读取；原文件保留。", error); }
            if (value == null || value.schemaVersion != StreetLevelDocument.CurrentSchemaVersion)
                throw new InvalidDataException("不支持的街区关卡版本；不会覆盖原文件。需要 schemaVersion=2。");
            RequireEditable(value);
            return value;
        }

        // Drafts may contain unresolved gameplay errors. The editor must still be able
        // to draw, undo, save, and reopen them without losing the author's work.
        private static void RequireEditable(StreetLevelDocument value)
        {
            if (value.surfaces == null || value.objects == null || value.routes == null || value.checkpoints == null)
                throw new InvalidDataException("地面、对象、路线和检查点列表不可缺失。");
            if (value.surfaces.Count > 2000 || value.objects.Count > 2000 || value.routes.Count > 500 || value.checkpoints.Count > 500)
                throw new InvalidDataException("关卡数量超出编辑器限制。");
            EditableNumber(value.spawnX); EditableNumber(value.spawnY); EditableNumber(value.facingX); EditableNumber(value.facingY);
            EditableNumber(value.goalX); EditableNumber(value.goalY); EditableNumber(value.goalRadius);
            foreach (var surface in value.surfaces)
            {
                if (surface == null) throw new InvalidDataException("地面数据不可为空。");
                EditablePoints(surface.polygon); EditablePoint(surface.textureAxis);
                EditableNumber(surface.heightBase); EditableNumber(surface.heightSlopeX); EditableNumber(surface.heightSlopeY);
            }
            foreach (var item in value.objects)
            {
                if (item == null) throw new InvalidDataException("对象数据不可为空。");
                EditablePoint(item.position); EditablePoints(item.polygon); EditableNumber(item.height); EditableNumber(item.acousticLoss);
                if (item.contactDamage.HasValue) EditableNumber(item.contactDamage.Value);
                if (item.contactStun.HasValue) EditableNumber(item.contactStun.Value);
            }
            foreach (var route in value.routes)
            {
                if (route == null) throw new InvalidDataException("路线数据不可为空。");
                EditablePoints(route.points); EditableNumber(route.speed);
                if (double.IsNaN(route.offset) || double.IsInfinity(route.offset)) throw new InvalidDataException("路线起始偏移距离须为有限数。");
                if (route.stopLineDistance.HasValue) EditableNumber(route.stopLineDistance.Value);
            }
            foreach (var checkpoint in value.checkpoints)
            {
                if (checkpoint == null) throw new InvalidDataException("检查点数据不可为空。");
                EditablePoint(checkpoint.position); EditablePoint(checkpoint.facing); EditableNumber(checkpoint.radius);
            }
        }
        private static void EditableNumber(float value)
        { if (float.IsNaN(value) || float.IsInfinity(value) || Math.Abs(value) > 10000) throw new InvalidDataException("数值须有限且在正负 10000 之间。"); }
        private static void EditablePoint(StreetPointData point)
        { if (point == null) throw new InvalidDataException("坐标数据不可为空。"); EditableNumber(point.x); EditableNumber(point.y); }
        private static void EditablePoints(List<StreetPointData> points)
        {
            if (points == null || points.Count > 512) throw new InvalidDataException("顶点列表不可缺失且最多 512 个点。");
            foreach (var point in points) EditablePoint(point);
        }

        public static StreetLevelDocument Clone(StreetLevelDocument value) { return Decode(Encode(value)); }

        public static StreetLevelDocument FromLevel(StreetLevel level)
        {
            if (level == null) throw new ArgumentNullException("level");
            var result = new StreetLevelDocument {
                schemaVersion = StreetLevelDocument.CurrentSchemaVersion, id = level.Id, title = level.Title, goalText = level.GoalText,
                spawnX = level.Spawn.Position.X, spawnY = level.Spawn.Position.Y,
                facingX = level.Spawn.Facing.X, facingY = level.Spawn.Facing.Y,
                goalX = level.Goal.X, goalY = level.Goal.Y, goalRadius = level.GoalRadius
            };
            foreach (StreetSurface surface in level.Surfaces)
            {
                var data = new StreetSurfaceData {
                    id = surface.Id, materialId = surface.MaterialId, textureAxis = StreetPointData.From(surface.TextureAxis),
                    heightBase = surface.HeightBase, heightSlopeX = surface.HeightSlopeX, heightSlopeY = surface.HeightSlopeY,
                    priority = surface.Priority
                };
                foreach (Point2 point in surface.Polygon) data.polygon.Add(StreetPointData.From(point));
                result.surfaces.Add(data);
            }
            foreach (StreetObject item in level.Objects)
            {
                var data = new StreetObjectData {
                    id = item.Id, kind = item.Kind, position = StreetPointData.From(item.Position), materialId = item.MaterialId,
                    solid = item.Solid, vehicleHazard = item.VehicleHazard, blocksProbe = item.BlocksProbe,
                    acousticLoss = item.AcousticLoss, height = item.Height, soundId = item.SoundId, soundActive = item.SoundActive,
                    semantic = item.Semantic, contactDamage = item.ContactDamage, contactStun = item.ContactStun
                };
                foreach (Point2 point in item.Polygon) data.polygon.Add(StreetPointData.From(point));
                result.objects.Add(data);
            }
            foreach (StreetRoute route in level.Routes)
            {
                var data = new StreetRouteData { objectId = route.ObjectId, speed = route.Speed, signalAxis = route.SignalAxis,
                    offset = route.Offset, loop = route.Loop, stopLineDistance = route.StopLineDistance };
                foreach (Point2 point in route.Points) data.points.Add(StreetPointData.From(point));
                result.routes.Add(data);
            }
            foreach (StreetCheckpoint checkpoint in level.Checkpoints)
                result.checkpoints.Add(new StreetCheckpointData { id = checkpoint.Id, position = StreetPointData.From(checkpoint.Position), facing = StreetPointData.From(checkpoint.Facing), radius = checkpoint.Radius });
            return result;
        }

        public static StreetLevelDocument Template()
        {
            StreetLevelDocument value = FromLevel(StreetLevelFactory.Create());
            value.id = Guid.NewGuid().ToString("N");
            value.title = String.IsNullOrEmpty(value.title) ? "新街区关卡" : value.title + "（副本）";
            return value;
        }

        public static List<string> Validate(StreetLevelDocument value)
        {
            var errors = new List<string>();
            if (value == null) { errors.Add("街区关卡为空。"); return errors; }
            if (value.schemaVersion != StreetLevelDocument.CurrentSchemaVersion) errors.Add("schemaVersion 必须为 2；旧版或未来版本禁止覆盖。");
            RequireId(errors, value.id, "关卡");
            if (String.IsNullOrWhiteSpace(value.title)) errors.Add("关卡标题不能为空。");
            if (String.IsNullOrWhiteSpace(value.goalText)) errors.Add("请填写玩家听到的目标提示。");
            Finite(errors, value.spawnX, "spawnX"); Finite(errors, value.spawnY, "spawnY");
            Finite(errors, value.facingX, "facingX"); Finite(errors, value.facingY, "facingY");
            Finite(errors, value.goalX, "goalX"); Finite(errors, value.goalY, "goalY"); PositiveFinite(errors, value.goalRadius, "goalRadius");
            if (Math.Abs(value.facingX) + Math.Abs(value.facingY) < 0.00001f) errors.Add("出生朝向不能为零向量。");
            if (value.surfaces == null || value.objects == null || value.routes == null || value.checkpoints == null) { errors.Add("surface/object/route/checkpoint 列表不能为 null。"); return errors; }
            if (value.surfaces.Count == 0) errors.Add("至少需要一块地面；请从素材栏添加人行道。");
            try { RequireEditable(value); } catch (InvalidDataException error) { errors.Add(error.Message); return errors; }
            var ids = new HashSet<string>(StringComparer.Ordinal); ids.Add(value.id);
            foreach (StreetSurfaceData surface in value.surfaces) ValidateSurface(surface, errors, ids);
            foreach (StreetObjectData item in value.objects) ValidateObject(item, errors, ids);
            foreach (StreetCheckpointData checkpoint in value.checkpoints) ValidateCheckpoint(checkpoint, errors, ids);
            var objectIds = new HashSet<string>(StringComparer.Ordinal); foreach (StreetObjectData item in value.objects) if (item != null && !String.IsNullOrWhiteSpace(item.id)) objectIds.Add(item.id);
            var routeIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (StreetRouteData route in value.routes) ValidateRoute(route, errors, ids, objectIds, routeIds);
            foreach (var route in value.routes)
            {
                var actor = value.objects.Find(item => item.id == route.objectId);
                if (actor == null) continue;
                if (actor.kind != StreetKind.Active) errors.Add(actor.id + "：只有活动对象可以设置移动路线。");
                if (route.points.Count >= 2)
                {
                    if (StreetGeometry.Distance(route.points[0].ToPoint(), actor.position.ToPoint()) > .01f)
                        errors.Add(actor.id + "：路线首点需与对象位置一致，避免试玩时跳到别处。");
                    double length = 0;
                    for (int i = 1; i < route.points.Count; i++) length += StreetGeometry.Distance(route.points[i-1].ToPoint(), route.points[i].ToPoint());
                    if (length < .01) errors.Add(actor.id + "：路线不能全部重合。");
                    if (route.stopLineDistance.HasValue && route.stopLineDistance.Value > length) errors.Add(actor.id + "：停车线距离超过整条路线长度。");
                }
            }
            CheckSafePosition(value, new Point2(value.spawnX, value.spawnY), "出生点", errors);
            CheckSafePosition(value, new Point2(value.goalX, value.goalY), "终点", errors);
            foreach (var checkpoint in value.checkpoints) CheckSafePosition(value, checkpoint.position.ToPoint(), "检查点 " + checkpoint.id, errors);
            return errors;
        }

        private static void CheckSafePosition(StreetLevelDocument value, Point2 position, string label, List<string> errors)
        {
            bool onGround = false;
            foreach (var surface in value.surfaces)
                if (surface.polygon.Count >= 3 && StreetGeometry.Contains(Points(surface.polygon), position)) onGround = true;
            if (!onGround) errors.Add(label + "须放在可行走地面内。");
            foreach (var item in value.objects)
            {
                if (!item.solid || item.polygon.Count < 3) continue;
                var polygon = Points(item.polygon);
                bool blocked = StreetGeometry.Contains(polygon, position);
                for (int i = 0; i < polygon.Length && !blocked; i++)
                {
                    Point2 a = polygon[i], b = polygon[(i + 1) % polygon.Length];
                    float dx = b.X - a.X, dy = b.Y - a.Y, length = dx * dx + dy * dy;
                    float t = length <= 0 ? 0 : Math.Max(0, Math.Min(1, ((position.X-a.X)*dx+(position.Y-a.Y)*dy)/length));
                    blocked = StreetGeometry.Distance(position, new Point2(a.X+t*dx,a.Y+t*dy)) < .18f;
                }
                if (blocked) errors.Add(label + "与障碍「" + item.id + "」重叠，请移到有身体空隙的位置。");
            }
        }

        public static StreetLevel Compile(StreetLevelDocument value)
        {
            List<string> issues = Validate(value); if (issues.Count > 0) throw new InvalidDataException(issues[0]);
            var surfaces = new List<StreetSurface>(); foreach (StreetSurfaceData source in value.surfaces) surfaces.Add(ToSurface(source));
            var objects = new List<StreetObject>(); foreach (StreetObjectData source in value.objects) objects.Add(ToObject(source));
            var routes = new List<StreetRoute>(); foreach (StreetRouteData source in value.routes) routes.Add(ToRoute(source));
            var checkpoints = new List<StreetCheckpoint>(); foreach (StreetCheckpointData source in value.checkpoints) checkpoints.Add(new StreetCheckpoint(source.id, source.position.ToPoint(), source.facing.ToPoint(), source.radius));
            Point2 facing = StreetGeometry.Unit(new Point2(value.facingX, value.facingY));
            var spawn = new PlayerState(PlayerMainState.Active, ActionMode.Walking, new Point2(value.spawnX, value.spawnY), facing, 100, 100, 0, 0, 0);
            return new StreetLevel(value.id, value.title, value.goalText, spawn, surfaces, objects, routes, checkpoints, new Point2(value.goalX, value.goalY), value.goalRadius);
        }

        private static StreetSurface ToSurface(StreetSurfaceData source) { var p = Points(source.polygon); return new StreetSurface(source.id, source.materialId, p, source.priority, source.textureAxis.ToPoint(), source.heightBase, source.heightSlopeX, source.heightSlopeY); }
        private static StreetObject ToObject(StreetObjectData source) { return new StreetObject(source.id, source.kind, source.position.ToPoint(), Points(source.polygon), source.materialId, source.solid, source.vehicleHazard, source.blocksProbe, source.acousticLoss, source.height, source.soundId, source.soundActive, source.semantic, source.contactDamage, source.contactStun); }
        private static StreetRoute ToRoute(StreetRouteData source) { var points = Points(source.points); return new StreetRoute(source.objectId, points, source.speed, source.signalAxis, source.offset, source.loop, source.stopLineDistance); }
        private static Point2[] Points(List<StreetPointData> source) { var result = new Point2[source.Count]; for (int i = 0; i < result.Length; i++) result[i] = source[i].ToPoint(); return result; }

        private static void ValidateSurface(StreetSurfaceData value, List<string> errors, HashSet<string> ids)
        {
            if (value == null) { errors.Add("存在空 surface。"); return; } RequireId(errors, value.id, "surface"); AddId(errors, ids, value.id, "surface"); RequireId(errors, value.materialId, "surface materialId"); ValidatePolygon(value.polygon, errors, value.id);
            if (Array.IndexOf(StreetCatalog.Materials, value.materialId) < 0) errors.Add(value.id + "：请选择已支持的地面材质。");
            ValidatePoint(value.textureAxis, errors, value.id + ".textureAxis"); if (value.textureAxis == null || (Math.Abs(value.textureAxis.x) + Math.Abs(value.textureAxis.y) < 0.00001f)) errors.Add(value.id + " textureAxis 不能为零。");
            Finite(errors, value.heightBase, value.id + ".heightBase"); Finite(errors, value.heightSlopeX, value.id + ".heightSlopeX"); Finite(errors, value.heightSlopeY, value.id + ".heightSlopeY");
        }
        private static void ValidateObject(StreetObjectData value, List<string> errors, HashSet<string> ids)
        {
            if (value == null) { errors.Add("存在空 object。"); return; } RequireId(errors, value.id, "object"); AddId(errors, ids, value.id, "object"); RequireId(errors, value.materialId, "object materialId"); ValidatePoint(value.position, errors, value.id + ".position");
            if (!Enum.IsDefined(typeof(StreetKind), value.kind) || !Enum.IsDefined(typeof(PerceptionSemantic), value.semantic)) errors.Add(value.id + "：对象类别无效。");
            if (Array.IndexOf(StreetCatalog.Materials, value.materialId) < 0) errors.Add(value.id + "：对象材质无效。");
            if (!string.IsNullOrEmpty(value.soundId) && Array.IndexOf(StreetCatalog.Sounds, value.soundId) < 0) errors.Add(value.id + "：声音无效，请从列表选择。");
            if (value.soundId == "street.signal.ns" || value.soundId == "street.signal.ew")
            {
                if (value.id != value.soundId || value.kind != StreetKind.Permanent || value.soundActive)
                    errors.Add(value.id + "：信号灯请使用素材栏的专用类型，由交通系统控制声音。");
            }
            if (value.soundActive && string.IsNullOrEmpty(value.soundId)) errors.Add(value.id + "：启用声音前请先选择声音。");
            if ((value.solid || value.vehicleHazard || value.blocksProbe) && value.polygon != null && value.polygon.Count < 3) errors.Add(value.id + "：碰撞或遮挡对象需要实体轮廓。");
            // Sound-only activity (for example a bird) intentionally has no contour or height.
            if (value.polygon == null || value.polygon.Count > 0) ValidatePolygon(value.polygon, errors, value.id);
            Finite(errors, value.acousticLoss, value.id + ".acousticLoss"); if (value.acousticLoss < 0 || value.acousticLoss > 1) errors.Add(value.id + " acousticLoss 必须在 0 到 1 之间。");
            Finite(errors, value.height, value.id + ".height"); if (value.height < 0) errors.Add(value.id + "：高度不能为负。");
            OptionalPositiveFinite(errors, value.contactDamage, value.id + ".contactDamage"); OptionalPositiveFinite(errors, value.contactStun, value.id + ".contactStun");
        }
        private static void ValidateCheckpoint(StreetCheckpointData value, List<string> errors, HashSet<string> ids) { if (value == null) { errors.Add("存在空 checkpoint。"); return; } RequireId(errors, value.id, "checkpoint"); AddId(errors, ids, value.id, "checkpoint"); ValidatePoint(value.position, errors, value.id + ".position"); ValidatePoint(value.facing, errors, value.id + ".facing"); if (value.facing == null || Math.Abs(value.facing.x) + Math.Abs(value.facing.y) < 0.00001f) errors.Add(value.id + " facing 不能为零。"); PositiveFinite(errors, value.radius, value.id + ".radius"); }
        private static void ValidateRoute(StreetRouteData value, List<string> errors, HashSet<string> ids, HashSet<string> objects, HashSet<string> routeIds)
        {
            if (value == null) { errors.Add("存在空 route。"); return; } RequireId(errors, value.objectId, "route objectId"); if (!objects.Contains(value.objectId)) errors.Add("route 引用的 object 不存在：" + value.objectId); if (!routeIds.Add(value.objectId)) errors.Add("object 只能绑定一条 route：" + value.objectId); if (value.points == null || value.points.Count < 2) errors.Add("route 至少需要两个点：" + value.objectId); else foreach (StreetPointData point in value.points) ValidatePoint(point, errors, value.objectId + ".point"); PositiveFinite(errors, value.speed, value.objectId + ".speed"); Finite(errors, value.offset, value.objectId + ".offset"); if (value.offset < 0) errors.Add("route offset 不能为负：" + value.objectId); if (value.signalAxis != null && value.signalAxis != "ns" && value.signalAxis != "ew") errors.Add("route signalAxis 只能为 ns、ew 或 null：" + value.objectId);
            if (value.stopLineDistance.HasValue) { Finite(errors, value.stopLineDistance.Value, value.objectId + ".stopLineDistance"); if (value.stopLineDistance.Value < 0) errors.Add(value.objectId + "：停车线距离不能为负。"); }
            if (value.signalAxis != null && !value.stopLineDistance.HasValue) errors.Add("有 signalAxis 的 route 必须设置 stopLineDistance：" + value.objectId);
        }
        private static void ValidatePolygon(List<StreetPointData> value, List<string> errors, string owner)
        {
            if (value == null || value.Count < 3) { errors.Add(owner + " polygon 至少需要三个点。"); return; }
            var points = new List<Point2>(); foreach (StreetPointData point in value) { ValidatePoint(point, errors, owner + ".polygon"); if (point != null) points.Add(point.ToPoint()); }
            if (points.Count < 3 || Math.Abs(StreetGeometry.Area(points)) < 0.000001f) { errors.Add(owner + " polygon 不能退化。"); return; }
            for (int i = 0; i < points.Count; i++) { Point2 a = points[i], b = points[(i + 1) % points.Count]; if (StreetGeometry.Distance(a, b) < StreetGeometry.Epsilon) errors.Add(owner + " polygon 不能有重合相邻点。"); for (int j = i + 1; j < points.Count; j++) { if (j == i || (j + 1) % points.Count == i || (i + 1) % points.Count == j) continue; if (SegmentsCross(a, b, points[j], points[(j + 1) % points.Count])) errors.Add(owner + " polygon 不能自相交。"); } }
            float sign = Math.Sign(StreetGeometry.Area(points));
            for (int i = 0; i < points.Count; i++)
                if (sign * StreetGeometry.Cross(points[i], points[(i+1)%points.Count], points[(i+2)%points.Count]) < -StreetGeometry.Epsilon)
                { errors.Add(owner + "：轮廓须为凸多边形；凹形区域请拆成多块。"); break; }
        }
        private static bool SegmentsCross(Point2 a, Point2 b, Point2 c, Point2 d) { float ab1 = StreetGeometry.Cross(a, b, c), ab2 = StreetGeometry.Cross(a, b, d), cd1 = StreetGeometry.Cross(c, d, a), cd2 = StreetGeometry.Cross(c, d, b); return ((ab1 > 0 && ab2 < 0) || (ab1 < 0 && ab2 > 0)) && ((cd1 > 0 && cd2 < 0) || (cd1 < 0 && cd2 > 0)); }
        private static void ValidatePoint(StreetPointData point, List<string> errors, string name) { if (point == null) { errors.Add(name + " 不能为 null。"); return; } Finite(errors, point.x, name + ".x"); Finite(errors, point.y, name + ".y"); }
        private static void RequireId(List<string> errors, string id, string name) { if (String.IsNullOrWhiteSpace(id)) errors.Add(name + " id 不能为空。"); }
        private static void AddId(List<string> errors, HashSet<string> ids, string id, string name) { if (!String.IsNullOrWhiteSpace(id) && !ids.Add(id)) errors.Add(name + " id 重复：" + id); }
        private static void Finite(List<string> errors, float value, string name) { if (float.IsNaN(value) || float.IsInfinity(value)) errors.Add(name + " 必须是有限数。"); }
        private static void Finite(List<string> errors, double value, string name) { if (double.IsNaN(value) || double.IsInfinity(value)) errors.Add(name + " 必须是有限数。"); }
        private static void PositiveFinite(List<string> errors, float value, string name) { Finite(errors, value, name); if (!(value > 0)) errors.Add(name + " 必须大于 0。"); }
        private static void OptionalPositiveFinite(List<string> errors, float? value, string name) { if (value.HasValue) { Finite(errors, value.Value, name); if (value.Value < 0) errors.Add(name + " 不能为负。"); } }
    }
}

