using System;
using System.Collections.Generic;
using TingLu.Contracts;
using UnityEngine;

namespace TingLu.Levels.Editor
{
    /// <summary>
    /// 中文素材目录和安全的街区对象默认值。
    ///
    /// StreetKind 是运行时的记忆分类，并不能单独表示“汽车”或“鸟鸣”。
    /// 因此编辑器显示的对象类型在本目录中定义，再映射到新版 DTO 的
    /// StreetObjectData 字段。这个类不保存任何关卡状态，画布和检查器可以
    /// 重复调用它来创建草稿对象。
    /// </summary>
    public static class StreetEditorCatalog
    {
        public const string SignalNorthSouthId = "street.signal.ns";
        public const string SignalEastWestId = "street.signal.ew";

        public static readonly string[] SurfaceMaterialIds =
        {
            "asphalt", "sidewalk", "tactile", "tactile.dots",
            "crosswalk", "ramp", "curb", "brick"
        };

        public static readonly string[] SurfaceMaterialNames =
        {
            "道路", "人行道", "盲道", "提示砖", "斑马线", "坡道", "路沿", "砖地"
        };

        public static readonly string[] ObjectTypeIds =
        {
            "building", "obstacle", "car", "parkedCar", "electricBike", "parkedElectricBike", "bicycle", "parkedBicycle", "pedestrian", "standingPedestrian", "bird",
            "signalNs", "signalEw"
        };

        public static readonly string[] ObjectTypeNames =
        {
            "建筑", "临时障碍", "行驶汽车", "停放汽车", "行驶电动车", "停放电动车", "行驶自行车", "停放自行车", "行走行人", "静止行人", "鸟鸣", "南北信号灯", "东西信号灯"
        };

        public static readonly string[] ObjectMaterialIds =
        { "sidewalk", "tactile", "tactile.dots", "asphalt", "crosswalk", "ramp", "curb", "brick", "metal", "cardboard" };
        public static readonly string[] ObjectMaterialNames =
        { "人行道", "盲道", "提示砖", "道路", "斑马线", "坡道", "路沿", "砖地", "金属", "纸板" };

        public static readonly string[] SoundIds =
        {
            "street.car", "street.electric", "street.cycle", "street.walker", "street.bird",
            SignalNorthSouthId, SignalEastWestId, "cane.tap", "collision.bump", "feedback.checkpoint",
            "feedback.breath", "footstep.sidewalk", "footstep.tactile", "footstep.tactile.dots",
            "footstep.asphalt", "footstep.crosswalk", "footstep.ramp", "footstep.curb"
        };

        public static readonly string[] SoundNames =
        {
            "汽车", "电动车", "自行车", "行人", "鸟鸣", "南北信号灯", "东西信号灯",
            "敲杖", "碰撞", "检查点", "呼吸", "人行道脚步", "盲道脚步", "提示砖脚步",
            "道路脚步", "斑马线脚步", "坡道脚步", "路沿脚步"
        };

        // Aliases make the catalog convenient for palette code which calls these
        // collections Materials / MaterialNames or Objects / ObjectNames.
        public static readonly string[] Materials = SurfaceMaterialIds;
        public static readonly string[] MaterialNames = SurfaceMaterialNames;
        public static readonly string[] Objects = ObjectTypeIds;
        public static readonly string[] ObjectNames = ObjectTypeNames;
        public static readonly string[] Sounds = SoundIds;
        public static readonly string[] SoundDisplayNames = SoundNames;

        /// <summary>描述一个可放入左侧素材栏的地面默认值。</summary>
        public sealed class SurfaceTemplate
        {
            public readonly string Id, Name;
            public readonly Color Color;
            public readonly int Priority;
            public readonly Point2 TextureAxis;
            public readonly float HeightBase, HeightSlopeX, HeightSlopeY;

            internal SurfaceTemplate(string id, string name, Color color, int priority,
                Point2 textureAxis, float heightBase, float heightSlopeX, float heightSlopeY)
            {
                Id = id; Name = name; Color = color; Priority = priority;
                TextureAxis = textureAxis; HeightBase = heightBase;
                HeightSlopeX = heightSlopeX; HeightSlopeY = heightSlopeY;
            }
        }

        /// <summary>描述一个可放入左侧素材栏的对象安全默认值。</summary>
        public sealed class ObjectTemplate
        {
            public readonly string Id, Name;
            public readonly Color Color;
            public readonly StreetKind Kind;
            public readonly string MaterialId, SoundId;
            public readonly bool Solid, VehicleHazard, BlocksProbe, SoundActive;
            public readonly float AcousticLoss, Height;
            public readonly PerceptionSemantic Semantic;
            public readonly float? ContactDamage, ContactStun;

            internal ObjectTemplate(string id, string name, Color color, StreetKind kind,
                string materialId, string soundId, bool solid, bool vehicleHazard,
                bool blocksProbe, float acousticLoss, float height, bool soundActive,
                PerceptionSemantic semantic, float? contactDamage, float? contactStun)
            {
                Id = id; Name = name; Color = color; Kind = kind; MaterialId = materialId;
                SoundId = soundId; Solid = solid; VehicleHazard = vehicleHazard;
                BlocksProbe = blocksProbe; AcousticLoss = acousticLoss; Height = height;
                SoundActive = soundActive; Semantic = semantic;
                ContactDamage = contactDamage; ContactStun = contactStun;
            }
        }

        private static readonly Dictionary<string, SurfaceTemplate> SurfaceMap = CreateSurfaceMap();
        private static readonly Dictionary<string, ObjectTemplate> ObjectMap = CreateObjectMap();

        public static bool IsSurfaceMaterial(string id)
        { return !string.IsNullOrEmpty(id) && SurfaceMap.ContainsKey(id); }

        public static bool IsObjectType(string type)
        { return !string.IsNullOrEmpty(type) && ObjectMap.ContainsKey(type); }

        public static SurfaceTemplate Surface(string id)
        {
            SurfaceTemplate value;
            return id != null && SurfaceMap.TryGetValue(id, out value) ? value : null;
        }

        public static ObjectTemplate Object(string type)
        {
            ObjectTemplate value;
            return type != null && ObjectMap.TryGetValue(type, out value) ? value : null;
        }

        public static Color GetSurfaceColor(string id)
        {
            SurfaceTemplate value = Surface(id);
            return value == null ? new Color(.39f, .41f, .39f) : value.Color;
        }

        public static Color GetObjectColor(string type)
        {
            ObjectTemplate value = Object(type);
            return value == null ? new Color(.62f, .62f, .62f) : value.Color;
        }

        public static string SoundName(string id)
        {
            if (string.IsNullOrEmpty(id)) return "无声音";
            int index = Array.IndexOf(SoundIds, id);
            return index < 0 ? "未注册声音" : SoundNames[index];
        }

        public static string CreateSurfaceId(string materialId, IEnumerable<string> existingIds = null)
        {
            if (!IsSurfaceMaterial(materialId)) throw new ArgumentException("未知地面材质：" + materialId, "materialId");
            string prefix = "surface." + materialId;
            string id;
            do { id = prefix + "." + Guid.NewGuid().ToString("N"); }
            while (Contains(existingIds, id));
            return id;
        }

        public static string SurfaceName(string id)
        {
            SurfaceTemplate value = Surface(id);
            return value == null ? "未知地面" : value.Name;
        }

        public static string ObjectName(string type)
        {
            int index = Array.IndexOf(ObjectTypeIds, type);
            if (index >= 0) return ObjectTypeNames[index];
            ObjectTemplate value = Object(type);
            return value == null ? "未知对象" : value.Name;
        }

        /// <summary>
        /// Returns a stable ID for the two signal fixtures and a unique ID for all
        /// other objects. Callers should still check the document for duplicate IDs.
        /// </summary>
        public static string CreateObjectId(string type, IEnumerable<string> existingIds = null)
        {
            if (!IsObjectType(type)) throw new ArgumentException("未知街区对象：" + type, "type");
            if (type == "signalNs") return UniqueSignalId(SignalNorthSouthId, existingIds);
            if (type == "signalEw") return UniqueSignalId(SignalEastWestId, existingIds);
            string prefix = type;
            string id;
            do { id = prefix + "." + Guid.NewGuid().ToString("N"); }
            while (Contains(existingIds, id));
            return id;
        }

        /// <summary>Infers the palette type from a document object without adding UI-only serialized fields.</summary>
        public static string GetObjectType(StreetObjectData item, StreetLevelDocument document = null)
        {
            if (item == null) return null;
            if (item.id == SignalNorthSouthId) return "signalNs";
            if (item.id == SignalEastWestId) return "signalEw";
            string actorType = ActorTypeFor(item.actorKind);
            if (actorType == null)
            {
                // Older documents may not have actorKind. Keep their sound and
                // semantic fields as a compatibility fallback, after the explicit
                // actor classification used by new documents.
                if (item.soundId == "street.cycle") actorType = "bicycle";
                else if (item.soundId == "street.electric") actorType = "electricBike";
                else if (item.soundId == "street.car") actorType = "car";
                else if (item.soundId == "street.walker" || item.semantic == PerceptionSemantic.Person) actorType = "pedestrian";
            }
            if (actorType != null)
            {
                bool moving = document == null || (document.routes != null &&
                    document.routes.Exists(route => route != null && route.objectId == item.id));
                return moving ? actorType : ParkedTypeFor(actorType);
            }
            if (item.soundId == "street.bird") return "bird";
            if (item.kind == StreetKind.Temporary) return "obstacle";
            if (item.kind == StreetKind.Permanent && item.solid && item.blocksProbe) return "building";
            return null;
        }

        private static string ActorTypeFor(StreetActorKind kind)
        {
            switch (kind)
            {
                case StreetActorKind.Pedestrian: return "pedestrian";
                case StreetActorKind.Car: return "car";
                case StreetActorKind.Bicycle: return "bicycle";
                case StreetActorKind.ElectricBike: return "electricBike";
                default: return null;
            }
        }

        private static string ParkedTypeFor(string movingType)
        {
            if (movingType == "car") return "parkedCar";
            if (movingType == "electricBike") return "parkedElectricBike";
            if (movingType == "bicycle") return "parkedBicycle";
            if (movingType == "pedestrian") return "standingPedestrian";
            return movingType;
        }

        /// <summary>Applies a palette type's behavior defaults while preserving its map shape and position.</summary>
        public static void ApplyObjectType(StreetObjectData item, string type,
            IEnumerable<string> existingIds = null)
        {
            if (item == null) throw new ArgumentNullException("item");
            ObjectTemplate defaults = Object(type);
            if (defaults == null) throw new ArgumentException("未知街区对象：" + type, "type");
            string desiredId = item.id;
            if (type == "signalNs") desiredId = SignalNorthSouthId;
            else if (type == "signalEw") desiredId = SignalEastWestId;
            else if (desiredId == SignalNorthSouthId || desiredId == SignalEastWestId)
                desiredId = CreateObjectId(type, existingIds);
            if (!StringComparer.Ordinal.Equals(desiredId, item.id) && Contains(existingIds, desiredId))
                throw new InvalidOperationException("关卡中已存在「" + defaults.Name + "」；南北、东西信号灯各只能放置一个。");
            item.id = desiredId;
            item.actorKind = ActorKindFor(type);
            item.kind = defaults.Kind; item.materialId = defaults.MaterialId; item.soundId = defaults.SoundId;
            item.solid = defaults.Solid; item.vehicleHazard = defaults.VehicleHazard;
            item.blocksProbe = defaults.BlocksProbe; item.acousticLoss = defaults.AcousticLoss;
            item.height = defaults.Height; item.soundActive = defaults.SoundActive;
            item.semantic = defaults.Semantic; item.contactDamage = defaults.ContactDamage;
            item.contactStun = defaults.ContactStun;
        }

        /// <summary>Creates a surface DTO with all runtime fields initialized.</summary>
        public static StreetSurfaceData CreateSurface(string materialId, string id,
            IList<StreetPointData> polygon, int? priority = null)
        {
            SurfaceTemplate defaults = Surface(materialId);
            if (defaults == null) throw new ArgumentException("未知地面材质：" + materialId, "materialId");
            if (string.IsNullOrEmpty(id)) throw new ArgumentException("地面需要唯一 ID。", "id");
            if (polygon == null || polygon.Count < 3) throw new ArgumentException("地面至少需要三个顶点。", "polygon");
            var data = new StreetSurfaceData();
            data.id = id; data.materialId = materialId; data.polygon = new List<StreetPointData>(polygon);
            data.textureAxis = Point(TextureAxis(materialId, polygon));
            float heightBase = defaults.HeightBase, slopeX = defaults.HeightSlopeX, slopeY = defaults.HeightSlopeY;
            if (materialId == "ramp") RampHeight(polygon, out heightBase, out slopeX, out slopeY);
            data.heightBase = heightBase; data.heightSlopeX = slopeX;
            data.heightSlopeY = slopeY; data.priority = priority.HasValue ? priority.Value : defaults.Priority;
            return data;
        }

        public static StreetSurfaceData CreateSurface(string materialId,
            IList<StreetPointData> polygon, IEnumerable<string> existingIds = null, int? priority = null)
        { return CreateSurface(materialId, CreateSurfaceId(materialId, existingIds), polygon, priority); }

        /// <summary>Creates an object DTO from a Chinese palette type and safe defaults.</summary>
        public static StreetObjectData CreateObject(string type, string id,
            StreetPointData position, IList<StreetPointData> polygon = null,
            IEnumerable<string> existingIds = null)
        {
            ObjectTemplate defaults = Object(type);
            if (defaults == null) throw new ArgumentException("未知街区对象：" + type, "type");
            if (string.IsNullOrEmpty(id)) id = CreateObjectId(type, existingIds);
            if (string.IsNullOrEmpty(id)) throw new InvalidOperationException("南北、东西信号灯各只能放置一个。");
            if ((type == "signalNs" || type == "signalEw") && id != (type == "signalNs" ? SignalNorthSouthId : SignalEastWestId))
                throw new ArgumentException("信号灯必须使用固定 ID：" + (type == "signalNs" ? SignalNorthSouthId : SignalEastWestId), "id");
            if (type != "signalNs" && type != "signalEw" && (id == SignalNorthSouthId || id == SignalEastWestId))
                throw new ArgumentException("南北、东西信号灯的固定 ID 不能用于其他对象。", "id");
            if (Contains(existingIds, id)) throw new InvalidOperationException("关卡中已经存在这个对象，无法重复创建。");
            if (position == null) position = Point(new Point2(0, 0));
            var data = new StreetObjectData();
            data.actorKind = ActorKindFor(type);
            data.id = id; data.kind = defaults.Kind; data.position = position;
            data.polygon = polygon == null ? DefaultFootprint(type, position) : new List<StreetPointData>(polygon);
            data.materialId = defaults.MaterialId; data.solid = defaults.Solid;
            data.vehicleHazard = defaults.VehicleHazard; data.blocksProbe = defaults.BlocksProbe;
            data.acousticLoss = defaults.AcousticLoss; data.height = defaults.Height;
            data.soundId = defaults.SoundId; data.soundActive = defaults.SoundActive;
            data.semantic = defaults.Semantic; data.contactDamage = defaults.ContactDamage;
            data.contactStun = defaults.ContactStun;
            return data;
        }

        public static StreetObjectData CreateObject(string type, StreetPointData position,
            IList<StreetPointData> polygon = null, IEnumerable<string> existingIds = null)
        {
            string id = CreateObjectId(type, existingIds);
            if (id == null) throw new InvalidOperationException("南北、东西信号灯各只能放置一个。");
            return CreateObject(type, id, position, polygon, existingIds);
        }

        public static StreetRouteData CreateRoute(string objectId, IList<StreetPointData> points,
            float speed = 1, string signalAxis = null, double offset = 0, bool loop = true,
            float? stopLineDistance = null)
        {
            if (string.IsNullOrEmpty(objectId)) throw new ArgumentException("路线需要关联一个对象。", "objectId");
            if (points == null || points.Count < 2) throw new ArgumentException("路线至少需要两个节点。", "points");
            if (!(speed > 0) || float.IsNaN(speed) || float.IsInfinity(speed)) throw new ArgumentOutOfRangeException("speed");
            if (signalAxis != null && signalAxis != "ns" && signalAxis != "ew")
                throw new ArgumentException("信号方向只能选南北或东西。", "signalAxis");
            if (signalAxis != null && !stopLineDistance.HasValue)
                throw new ArgumentException("受信号灯控制的路线必须设置停车线距离。", "stopLineDistance");
            if (double.IsNaN(offset) || double.IsInfinity(offset) || offset < 0)
                throw new ArgumentOutOfRangeException("offset", "路线起始偏移不能是负数或无穷值。");
            if (stopLineDistance.HasValue && (!(stopLineDistance.Value > 0) || float.IsNaN(stopLineDistance.Value) || float.IsInfinity(stopLineDistance.Value)))
                throw new ArgumentOutOfRangeException("stopLineDistance", "停车线距离必须是有限的正数。");
            return new StreetRouteData { objectId = objectId, points = new List<StreetPointData>(points),
                speed = speed, signalAxis = signalAxis, offset = offset, loop = loop,
                stopLineDistance = stopLineDistance };
        }

        public static StreetCheckpointData CreateCheckpoint(string id, StreetPointData position,
            StreetPointData facing = null, float radius = .75f, IEnumerable<string> existingIds = null)
        {
            if (string.IsNullOrEmpty(id))
            {
                do { id = "checkpoint." + Guid.NewGuid().ToString("N"); }
                while (Contains(existingIds, id));
            }
            else if (Contains(existingIds, id)) throw new InvalidOperationException("关卡中已存在这个检查点 ID。");
            if (position == null) position = Point(0, 0);
            if (facing == null) facing = Point(0, 1);
            if (!(radius > 0) || float.IsNaN(radius) || float.IsInfinity(radius)) throw new ArgumentOutOfRangeException("radius");
            return new StreetCheckpointData { id = id, position = position, facing = facing, radius = radius };
        }

        public static StreetPointData Point(Point2 point)
        { return new StreetPointData { x = point.X, y = point.Y }; }

        public static StreetPointData Point(float x, float y)
        { return new StreetPointData { x = x, y = y }; }

        /// <summary>Builds a rotated rectangle in world coordinates for a new object or surface.</summary>
        public static List<StreetPointData> Rectangle(float centerX, float centerY,
            float width, float length, float rotationDegrees = 0)
        {
            if (!(width > 0) || !(length > 0)) throw new ArgumentOutOfRangeException("width");
            float angle = rotationDegrees * Mathf.Deg2Rad;
            float c = Mathf.Cos(angle), s = Mathf.Sin(angle), hx = width * .5f, hy = length * .5f;
            var result = new List<StreetPointData>(4);
            AddCorner(result, centerX, centerY, -hx, -hy, c, s);
            AddCorner(result, centerX, centerY, hx, -hy, c, s);
            AddCorner(result, centerX, centerY, hx, hy, c, s);
            AddCorner(result, centerX, centerY, -hx, hy, c, s);
            return result;
        }

        private static void AddCorner(List<StreetPointData> result, float x, float y,
            float localX, float localY, float c, float s)
        { result.Add(Point(x + localX * c - localY * s, y + localX * s + localY * c)); }

        private static List<StreetPointData> DefaultFootprint(string type, StreetPointData position)
        {
            if (type == "bird") return new List<StreetPointData>();
            if (type == "signalNs" || type == "signalEw") return Rectangle(position.x, position.y, .36f, .36f);
            if (type == "building") return Rectangle(position.x, position.y, 8, 8);
            if (type == "obstacle") return Rectangle(position.x, position.y, .7f, .7f);
            if (type == "car" || type == "parkedCar") return Rectangle(position.x, position.y, .96f, 1.6f);
            if (type == "electricBike" || type == "parkedElectricBike" || type == "bicycle" || type == "parkedBicycle") return Rectangle(position.x, position.y, .6f, 1.44f);
            return Rectangle(position.x, position.y, .5f, .5f);
        }

        private static Point2 TextureAxis(string materialId, IList<StreetPointData> polygon)
        {
            if (materialId != "tactile" && materialId != "crosswalk" && materialId != "ramp")
                return new Point2(1, 0);
            double longest = -1;
            Point2 axis = new Point2(1, 0);
            for (int i = 0; i < polygon.Count; i++)
            {
                StreetPointData a = polygon[i], b = polygon[(i + 1) % polygon.Count];
                float dx = b.x - a.x, dy = b.y - a.y;
                double length = (double)dx * dx + (double)dy * dy;
                if (length <= 0) continue;
                float scale = Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dy));
                float x = dx / scale, y = dy / scale;
                if (x < 0 || (x == 0 && y < 0)) { x = -x; y = -y; }
                var candidate = new Point2(x == 0 ? 0 : x, y == 0 ? 0 : y);
                if (length > longest || (length == longest &&
                    (candidate.X > axis.X || (candidate.X == axis.X && candidate.Y > axis.Y))))
                { longest = length; axis = candidate; }
            }
            return materialId == "crosswalk" ? new Point2(-axis.Y, axis.X) : axis;
        }

        private static void RampHeight(IList<StreetPointData> polygon,
            out float heightBase, out float slopeX, out float slopeY)
        {
            float minX = float.PositiveInfinity, maxX = float.NegativeInfinity;
            float minY = float.PositiveInfinity, maxY = float.NegativeInfinity;
            for (int i = 0; i < polygon.Count; i++)
            {
                minX = Mathf.Min(minX, polygon[i].x); maxX = Mathf.Max(maxX, polygon[i].x);
                minY = Mathf.Min(minY, polygon[i].y); maxY = Mathf.Max(maxY, polygon[i].y);
            }
            float width = maxX - minX, length = maxY - minY;
            slopeX = slopeY = heightBase = 0;
            if (width <= 0 && length <= 0) return;
            const float curbHeight = .15f;
            if (width <= length)
            {
                float center = (minX + maxX) * .5f;
                slopeX = (center >= 0 ? curbHeight : -curbHeight) / Mathf.Max(width, .01f);
                float roadEdge = center >= 0 ? minX : maxX;
                heightBase = -slopeX * roadEdge;
            }
            else
            {
                float center = (minY + maxY) * .5f;
                slopeY = (center >= 0 ? curbHeight : -curbHeight) / Mathf.Max(length, .01f);
                float roadEdge = center >= 0 ? minY : maxY;
                heightBase = -slopeY * roadEdge;
            }
        }

        private static string UniqueSignalId(string id, IEnumerable<string> existingIds)
        { return Contains(existingIds, id) ? null : id; }

        private static bool Contains(IEnumerable<string> values, string value)
        {
            if (values == null) return false;
            foreach (string item in values) if (StringComparer.Ordinal.Equals(item, value)) return true;
            return false;
        }

        private static Dictionary<string, SurfaceTemplate> CreateSurfaceMap()
        {
            var map = new Dictionary<string, SurfaceTemplate>(StringComparer.Ordinal);
            AddSurface(map, "asphalt", "道路", new Color(.19f, .21f, .21f), 20, new Point2(1, 0));
            AddSurface(map, "sidewalk", "人行道", new Color(.42f, .45f, .44f), 10, new Point2(1, 0), .15f);
            AddSurface(map, "tactile", "盲道", new Color(.63f, .48f, .20f), 50, new Point2(1, 0), .15f);
            AddSurface(map, "tactile.dots", "提示砖", new Color(.77f, .62f, .30f), 60, new Point2(1, 0), .15f);
            AddSurface(map, "crosswalk", "斑马线", new Color(.23f, .25f, .24f), 30, new Point2(0, 1));
            AddSurface(map, "ramp", "坡道", new Color(.29f, .47f, .43f), 40, new Point2(1, 0));
            AddSurface(map, "curb", "路沿", new Color(.34f, .36f, .35f), 25, new Point2(1, 0), .15f);
            AddSurface(map, "brick", "砖地", new Color(.48f, .30f, .24f), 10, new Point2(1, 0), .15f);
            return map;
        }

        private static void AddSurface(Dictionary<string, SurfaceTemplate> map, string id,
            string name, Color color, int priority, Point2 textureAxis, float heightBase = 0)
        { map.Add(id, new SurfaceTemplate(id, name, color, priority, textureAxis, heightBase, 0, 0)); }

        private static Dictionary<string, ObjectTemplate> CreateObjectMap()
        {
            var map = new Dictionary<string, ObjectTemplate>(StringComparer.Ordinal);
            AddObject(map, "building", "建筑", new Color(.43f, .43f, .46f), StreetKind.Permanent,
                "brick", null, true, false, true, .7f, 4.5f, false, PerceptionSemantic.Ordinary, null, null);
            AddObject(map, "obstacle", "临时障碍", new Color(.90f, .54f, .16f), StreetKind.Temporary,
                "metal", null, true, false, true, 0, .7f, false, PerceptionSemantic.Ordinary, null, null);
            AddObject(map, "car", "汽车", new Color(.78f, .24f, .22f), StreetKind.Active,
                "metal", "street.car", true, true, false, 0, 1.45f, true, PerceptionSemantic.Threat, null, null);
            AddObject(map, "electricBike", "电动车", new Color(.18f, .64f, .70f), StreetKind.Active,
                "metal", "street.electric", true, true, false, 0, .8f, true, PerceptionSemantic.Threat, null, null);
            AddObject(map, "pedestrian", "行人", new Color(.27f, .48f, .82f), StreetKind.Active,
                "brick", "street.walker", true, false, false, 0, 1.7f, true, PerceptionSemantic.Person, null, null);
            AddObject(map, "bicycle", "行驶自行车", new Color(.2f,.7f,.5f), StreetKind.Active,
                "metal", "street.cycle", true, true, false, 0, .8f, true, PerceptionSemantic.Threat, null, null);
            foreach(string pair in new[]{"parkedCar:car:停放汽车", "parkedElectricBike:electricBike:停放电动车", "parkedBicycle:bicycle:停放自行车", "standingPedestrian:pedestrian:静止行人"})
            {
                var parts=pair.Split(':'); var source=map[parts[1]];
                AddObject(map,parts[0],parts[2],source.Color,StreetKind.Active,source.MaterialId,source.SoundId,true,false,false,0,source.Height,false,source.Semantic,null,null);
            }
            AddObject(map, "bird", "鸟鸣", new Color(.92f, .76f, .18f), StreetKind.Active,
                "brick", "street.bird", false, false, false, 0, 0, true, PerceptionSemantic.Ordinary, null, null);
            AddObject(map, "signalNs", "南北信号灯", new Color(.22f, .78f, .32f), StreetKind.Permanent,
                "metal", SignalNorthSouthId, false, false, false, 0, 2f, false, PerceptionSemantic.Ordinary, null, null);
            AddObject(map, "signalEw", "东西信号灯", new Color(.67f, .37f, .82f), StreetKind.Permanent,
                "metal", SignalEastWestId, false, false, false, 0, 2f, false, PerceptionSemantic.Ordinary, null, null);
            return map;
        }

        public static StreetActorKind ActorKindFor(string type)
        {
            if (type == "car" || type == "parkedCar") return StreetActorKind.Car;
            if (type == "electricBike" || type == "parkedElectricBike") return StreetActorKind.ElectricBike;
            if (type == "bicycle" || type == "parkedBicycle") return StreetActorKind.Bicycle;
            if (type == "pedestrian" || type == "standingPedestrian") return StreetActorKind.Pedestrian;
            return StreetActorKind.None;
        }
        public static bool StartsMoving(string type)
        { return type == "car" || type == "electricBike" || type == "bicycle" || type == "pedestrian"; }
        public static void ConfigureMovement(StreetLevelDocument document, StreetObjectData item, bool moving)
        {
            if (document == null) throw new ArgumentNullException("document");
            if (item == null) return;
            StreetActorKind kind = item.actorKind;
            if (kind == StreetActorKind.None) kind = ActorKindFor(GetObjectType(item));
            if (kind == StreetActorKind.None) return;

            item.actorKind = kind;
            item.solid = true;
            if (document.routes == null) document.routes = new List<StreetRouteData>();
            StreetRouteData route = document.routes.Find(value => value != null && value.objectId == item.id);
            if (!moving) document.routes.RemoveAll(value => value != null && value.objectId == item.id);
            else if (route == null)
            {
                float speed = kind == StreetActorKind.Pedestrian ? 1 : 3;
                document.routes.Add(CreateRoute(item.id, new[]
                {
                    new StreetPointData(item.position.x, item.position.y),
                    new StreetPointData(item.position.x + 3, item.position.y)
                }, speed));
            }
            item.soundActive = moving;
        }

        private static void AddObject(Dictionary<string, ObjectTemplate> map, string id, string name,
            Color color, StreetKind kind, string material, string sound, bool solid, bool hazard,
            bool blocksProbe, float acousticLoss, float height, bool soundActive,
            PerceptionSemantic semantic, float? contactDamage, float? contactStun)
        {
            map.Add(id, new ObjectTemplate(id, name, color, kind, material, sound, solid, hazard,
                blocksProbe, acousticLoss, height, soundActive, semantic, contactDamage, contactStun));
        }
    }
}
