using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using TingLu.Config;
using TingLu.Contracts;
using UnityEngine;
using UnityEngine.Rendering;

[assembly: InternalsVisibleTo("TingLu.Tests.Upgrade.Role08")]

namespace TingLu.Presentation
{
    public sealed class StreetRenderer : MonoBehaviour
    {
        private const string MaterialResourcePath = "TingLu/StreetSurface";
        private const float SurfaceHeight = 0.015f;
        private const float StrokeHeight = 0.045f;
        private const float PlayerMarkerHeight = 0.06f;

        private readonly Dictionary<SurfaceBatchKey, SurfaceBatch> surfaceBatches = new Dictionary<SurfaceBatchKey, SurfaceBatch>();
        private readonly List<LineRenderer> linePool = new List<LineRenderer>();
        private readonly List<DashSegment> dashSegments = new List<DashSegment>();
        private Camera playerCamera;
        private StreetConfig config;
        private Material displayMaterial;
        private bool configured;
        private int displayLayer;
        private bool hasSurfaceCache;
        private StreetMemoryFrame cachedSurfaceMemory;
        private PaletteMode cachedSurfacePalette;
        private DisplayMode cachedSurfaceDisplay;
        private Matrix4x4 cachedSurfaceWorldToLocal;
        private double cachedSurfaceNow;

        internal int ActiveSurfaceBatchCount { get; private set; }
        internal int ActiveStrokeCount { get; private set; }
        internal int SurfaceBatchRebuildCount { get; private set; }

        public void Configure(Camera camera, int layer, StreetConfig streetConfig)
        {
            if (configured) throw new InvalidOperationException("StreetRenderer is already configured.");
            if (camera == null) throw new ArgumentNullException("camera");
            if (streetConfig == null) throw new ArgumentNullException("streetConfig");
            if (layer < 0 || layer > 31) throw new ArgumentOutOfRangeException("layer");
            streetConfig.Validate();

            displayMaterial = Resources.Load<Material>(MaterialResourcePath);
            if (displayMaterial == null)
                throw new InvalidOperationException("Street presentation material is missing from Resources: " + MaterialResourcePath);

            playerCamera = camera;
            displayLayer = layer;
            config = streetConfig;
            playerCamera.clearFlags = CameraClearFlags.SolidColor;
            playerCamera.backgroundColor = Color.black;
            playerCamera.cullingMask = 1 << displayLayer;
            configured = true;
        }

        public void Render(StreetMemoryFrame memory, PerceptionFrame perception, PlayerState player,
            UserSettings settings, double now, IEnumerable<StreetSignalObservation> signals = null)
        {
            EnsureConfigured();
            StreetRenderPlan plan = StreetRenderPlan.Create(memory, perception, player, settings, now, signals);
            RenderSurfaces(plan, memory, settings, now);
            int cursor = 0;
            foreach (StreetStroke stroke in plan.Strokes)
            {
                ExpandStroke(stroke, dashSegments);
                foreach (DashSegment dash in dashSegments)
                {
                    float baseHeight = stroke.Style == StreetStrokeStyle.Player ? PlayerMarkerHeight : StrokeHeight;
                    if (StreetGeometry.Distance(dash.A, dash.B) <= StreetGeometry.Epsilon && stroke.Elevation > 0f)
                        DrawHeightEdge(dash.A, stroke.Width, stroke.Color, baseHeight, baseHeight + stroke.Elevation, ref cursor);
                    else
                        DrawLine(dash.A, dash.B, stroke.Width, stroke.Color, baseHeight + stroke.Elevation, ref cursor);
                }
            }
            ActiveStrokeCount = cursor;
            for (int i = cursor; i < linePool.Count; i++) linePool[i].positionCount = 0;
        }

        // Kept as a source/reflection-compatible entry point for the production
        // screenshot probe and older integration callers; signal state is optional.
        public void Render(StreetMemoryFrame memory, PerceptionFrame perception, PlayerState player,
            UserSettings settings, double now)
        { Render(memory, perception, player, settings, now, null); }

        public void Clear()
        {
            hasSurfaceCache = false;
            cachedSurfaceMemory = null;
            ActiveSurfaceBatchCount = 0;
            ActiveStrokeCount = 0;
            ClearSurfaceBatches();
            for (int i = 0; i < linePool.Count; i++) linePool[i].positionCount = 0;
        }

        private void RenderSurfaces(StreetRenderPlan plan, StreetMemoryFrame memory, UserSettings settings, double now)
        {
            Matrix4x4 worldToLocal = transform.worldToLocalMatrix;
            if (CanReuseSurfaceBatch(memory, settings, worldToLocal, now))
            {
                cachedSurfaceNow = now;
                return;
            }

            // A failed rebuild must not leave an apparently reusable partial surface batch.
            hasSurfaceCache = false;
            cachedSurfaceMemory = null;
            ActiveSurfaceBatchCount = 0;
            bool succeeded = false;
            try
            {
                ClearSurfaceBatches();
                var used = new HashSet<SurfaceBatchKey>();
                foreach (StreetSurfacePatch patch in plan.Surfaces)
                {
                    SurfaceBatchKey key = new SurfaceBatchKey(patch.Source.MaterialId, patch.Current, settings.Palette);
                    SurfaceBatch batch = GetOrCreateBatch(key);
                    used.Add(key);
                    AppendPolygon(batch, patch.Source);
                }

                int activeBatchCount = 0;
                foreach (SurfaceBatchKey key in used)
                {
                    SurfaceBatch batch = surfaceBatches[key];
                    if (batch.Vertices.Count == 0) continue;
                    batch.Mesh.indexFormat = batch.Vertices.Count > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16;
                    batch.Mesh.SetVertices(batch.Vertices);
                    batch.Mesh.SetUVs(0, batch.Uvs);
                    batch.Mesh.SetColors(batch.Colors);
                    batch.Mesh.SetTriangles(batch.Triangles, 0, true);
                    batch.Mesh.RecalculateBounds();
                    ConfigureSurfaceMaterial(batch, key);
                    batch.Renderer.enabled = true;
                    activeBatchCount++;
                }

                ActiveSurfaceBatchCount = activeBatchCount;
                succeeded = true;
            }
            finally
            {
                ClearSurfaceBatchScratch();
                if (!succeeded)
                {
                    ClearSurfaceBatches();
                    ActiveSurfaceBatchCount = 0;
                    hasSurfaceCache = false;
                    cachedSurfaceMemory = null;
                }
            }

            cachedSurfaceMemory = memory;
            cachedSurfacePalette = settings.Palette;
            cachedSurfaceDisplay = settings.Display;
            cachedSurfaceWorldToLocal = worldToLocal;
            cachedSurfaceNow = now;
            hasSurfaceCache = true;
            SurfaceBatchRebuildCount++;
        }

        private void ClearSurfaceBatches()
        {
            foreach (SurfaceBatch batch in surfaceBatches.Values)
            {
                batch.Renderer.enabled = false;
                ClearSurfaceBatchScratch(batch);
            }
            foreach (SurfaceBatch batch in surfaceBatches.Values) batch.Mesh.Clear();
        }

        private void ClearSurfaceBatchScratch()
        {
            foreach (SurfaceBatch batch in surfaceBatches.Values) ClearSurfaceBatchScratch(batch);
        }

        private static void ClearSurfaceBatchScratch(SurfaceBatch batch)
        {
            batch.Vertices.Clear();
            batch.Uvs.Clear();
            batch.Colors.Clear();
            batch.Triangles.Clear();
        }

        private bool CanReuseSurfaceBatch(StreetMemoryFrame memory, UserSettings settings,
            Matrix4x4 worldToLocal, double now)
        {
            if (!hasSurfaceCache || !ReferenceEquals(cachedSurfaceMemory, memory) ||
                cachedSurfacePalette != settings.Palette || cachedSurfaceDisplay != settings.Display ||
                !cachedSurfaceWorldToLocal.Equals(worldToLocal) || now < cachedSurfaceNow)
                return false;

            if (now > cachedSurfaceNow)
            {
                foreach (StreetKnownSurface surface in memory.Surfaces)
                {
                    if (surface == null) continue;
                    if (CrossedBoundary(cachedSurfaceNow, now, surface.ObservedAt) ||
                        CrossedBoundary(cachedSurfaceNow, now, surface.CurrentUntil))
                        return false;
                }
            }
            return true;
        }

        private static bool CrossedBoundary(double previous, double current, double boundary)
        { return boundary > previous && boundary <= current; }

        private SurfaceBatch GetOrCreateBatch(SurfaceBatchKey key)
        {
            SurfaceBatch batch;
            if (surfaceBatches.TryGetValue(key, out batch)) return batch;

            var gameObject = new GameObject("Known ground " + key.MaterialId + (key.Current ? " current" : " memory"));
            gameObject.layer = displayLayer;
            gameObject.transform.SetParent(transform, false);
            var filter = gameObject.AddComponent<MeshFilter>();
            var renderer = gameObject.AddComponent<MeshRenderer>();
            var mesh = new Mesh { name = gameObject.name + " mesh" };
            mesh.MarkDynamic();
            filter.sharedMesh = mesh;
            renderer.sharedMaterial = displayMaterial;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            batch = new SurfaceBatch(mesh, renderer);
            surfaceBatches.Add(key, batch);
            return batch;
        }

        private void AppendPolygon(SurfaceBatch batch, StreetKnownSurface source)
        {
            IReadOnlyList<Point2> polygon = source.Polygon;
            if (polygon == null || polygon.Count < 3) return;
            PolygonTriangulation triangulation = Triangulate(polygon);
            if (triangulation.Triangles.Count == 0) return;

            // Texture orientation belongs to the source surface and survives clipping.
            // Project the world-space vertices directly so adjacent fragments share
            // both the same direction and the same texture phase.
            Point2 axis = StreetGeometry.Unit(source.TextureAxis);
            Point2 normal = new Point2(-axis.Y, axis.X);
            int first = batch.Vertices.Count;
            for (int i = 0; i < triangulation.Vertices.Count; i++)
            {
                Point2 point = triangulation.Vertices[i];
                float height = Mathf.Max(SurfaceHeight, SurfaceHeight + source.HeightAt(point));
                batch.Vertices.Add(transform.InverseTransformPoint(new Vector3(point.X, height, point.Y)));
                batch.Uvs.Add(new Vector2(Dot(point, axis), Dot(point, normal)));
                batch.Colors.Add(Color.white);
            }
            for (int i = 0; i < triangulation.Triangles.Count; i++) batch.Triangles.Add(first + triangulation.Triangles[i]);
        }

        private void ConfigureSurfaceMaterial(SurfaceBatch batch, SurfaceBatchKey key)
        {
            Color baseColor, accentColor;
            GetSurfaceColors(key.MaterialId, key.Palette, out baseColor, out accentColor);
            MaterialPropertyBlock block = batch.PropertyBlock;
            batch.Renderer.GetPropertyBlock(block);
            block.SetColor("_BaseColor", baseColor);
            block.SetColor("_AccentColor", accentColor);
            block.SetFloat("_Pattern", (float)StreetRenderPlan.PatternForMaterial(key.MaterialId));
            block.SetFloat("_UvScale", 2.4f);
            block.SetFloat("_Brightness", key.Current ? 1f : 0.40f);
            batch.Renderer.SetPropertyBlock(block);
        }

        private void DrawLine(Point2 a, Point2 b, float width, Color color, float height, ref int cursor)
        {
            LineRenderer line = GetLine(cursor++);
            line.startWidth = line.endWidth = width;
            line.startColor = line.endColor = color;
            line.positionCount = 2;
            line.SetPosition(0, transform.InverseTransformPoint(new Vector3(a.X, height, a.Y)));
            line.SetPosition(1, transform.InverseTransformPoint(new Vector3(b.X, height, b.Y)));
        }

        private void DrawHeightEdge(Point2 point, float width, Color color, float lowerHeight, float upperHeight, ref int cursor)
        {
            LineRenderer line = GetLine(cursor++);
            line.startWidth = line.endWidth = width;
            line.startColor = line.endColor = color;
            line.positionCount = 2;
            line.SetPosition(0, transform.InverseTransformPoint(new Vector3(point.X, lowerHeight, point.Y)));
            line.SetPosition(1, transform.InverseTransformPoint(new Vector3(point.X, upperHeight, point.Y)));
        }

        private LineRenderer GetLine(int index)
        {
            while (linePool.Count <= index)
            {
                var gameObject = new GameObject("Known contour " + linePool.Count);
                gameObject.layer = displayLayer;
                gameObject.transform.SetParent(transform, false);
                var line = gameObject.AddComponent<LineRenderer>();
                line.sharedMaterial = displayMaterial;
                line.useWorldSpace = false;
                line.alignment = LineAlignment.View;
                line.textureMode = LineTextureMode.Stretch;
                line.numCapVertices = 0;
                line.numCornerVertices = 0;
                line.shadowCastingMode = ShadowCastingMode.Off;
                line.receiveShadows = false;
                line.positionCount = 0;
                linePool.Add(line);
            }
            return linePool[index];
        }

        private static void ExpandStroke(StreetStroke stroke, List<DashSegment> output)
        {
            output.Clear();
            float length = StreetGeometry.Distance(stroke.Segment.A, stroke.Segment.B);
            if (length <= StreetGeometry.Epsilon)
            {
                if (stroke.Elevation > 0f)
                    output.Add(new DashSegment(stroke.Segment.A, stroke.Segment.B));
                return;
            }
            if (stroke.DashLength <= 0f || stroke.GapLength <= 0f)
            {
                output.Add(new DashSegment(stroke.Segment.A, stroke.Segment.B));
                return;
            }

            float cursor = 0f;
            while (cursor < length)
            {
                float end = Mathf.Min(length, cursor + stroke.DashLength);
                output.Add(new DashSegment(StreetGeometry.Lerp(stroke.Segment.A, stroke.Segment.B, cursor / length),
                    StreetGeometry.Lerp(stroke.Segment.A, stroke.Segment.B, end / length)));
                cursor = end + stroke.GapLength;
            }
        }

        private static float Dot(Point2 a, Point2 b) { return a.X * b.X + a.Y * b.Y; }

        private static PolygonTriangulation Triangulate(IReadOnlyList<Point2> polygon)
        {
            var vertices = new List<Point2>();
            var triangles = new List<int>();
            if (polygon == null || polygon.Count < 3)
                return new PolygonTriangulation(vertices, triangles);

            var xCuts = new List<double>(polygon.Count * 2);
            for (int i = 0; i < polygon.Count; i++) xCuts.Add(polygon[i].X);
            AddIntersectionCuts(polygon, xCuts);
            xCuts.Sort();
            for (int i = xCuts.Count - 1; i > 0; i--)
            {
                double scale = Math.Max(1.0, Math.Max(Math.Abs(xCuts[i]), Math.Abs(xCuts[i - 1])));
                if (Math.Abs(xCuts[i] - xCuts[i - 1]) <= scale * 1e-12) xCuts.RemoveAt(i);
            }

            var crossings = new List<ScanlineCrossing>(polygon.Count);
            for (int slab = 0; slab + 1 < xCuts.Count; slab++)
            {
                double left = xCuts[slab], right = xCuts[slab + 1];
                if (right <= left) continue;
                double middle = (left + right) * 0.5;
                crossings.Clear();
                for (int edge = 0; edge < polygon.Count; edge++)
                {
                    Point2 a = polygon[edge], b = polygon[(edge + 1) % polygon.Count];
                    if ((a.X < middle && b.X < middle) || (a.X > middle && b.X > middle) || a.X == b.X)
                        continue;
                    crossings.Add(new ScanlineCrossing(edge, YAt(polygon, edge, middle)));
                }
                crossings.Sort(delegate(ScanlineCrossing a, ScanlineCrossing b)
                {
                    int compare = a.MiddleY.CompareTo(b.MiddleY);
                    return compare != 0 ? compare : a.Edge.CompareTo(b.Edge);
                });

                for (int i = 0; i + 1 < crossings.Count; i += 2)
                {
                    // The walk may self-touch after clipped-cell unions. Even-odd
                    // scanline pairing keeps these fragments and concave notches
                    // inside their exact boundary instead of bridging across them.
                    int lowerEdge = crossings[i].Edge;
                    int upperEdge = crossings[i + 1].Edge;
                    Point2 lowerLeft = new Point2((float)left, (float)YAt(polygon, lowerEdge, left));
                    Point2 lowerRight = new Point2((float)right, (float)YAt(polygon, lowerEdge, right));
                    Point2 upperRight = new Point2((float)right, (float)YAt(polygon, upperEdge, right));
                    Point2 upperLeft = new Point2((float)left, (float)YAt(polygon, upperEdge, left));
                    AddTriangle(vertices, triangles, lowerLeft, lowerRight, upperRight);
                    AddTriangle(vertices, triangles, lowerLeft, upperRight, upperLeft);
                }
            }
            return new PolygonTriangulation(vertices, triangles);
        }

        private static void AddIntersectionCuts(IReadOnlyList<Point2> polygon, List<double> xCuts)
        {
            for (int first = 0; first < polygon.Count; first++)
            {
                Point2 a = polygon[first], b = polygon[(first + 1) % polygon.Count];
                double rx = (double)b.X - a.X, ry = (double)b.Y - a.Y;
                for (int second = first + 1; second < polygon.Count; second++)
                {
                    if (second == (first + 1) % polygon.Count || first == (second + 1) % polygon.Count) continue;
                    Point2 c = polygon[second], d = polygon[(second + 1) % polygon.Count];
                    if (Math.Max(a.X, b.X) < Math.Min(c.X, d.X) || Math.Max(c.X, d.X) < Math.Min(a.X, b.X) ||
                        Math.Max(a.Y, b.Y) < Math.Min(c.Y, d.Y) || Math.Max(c.Y, d.Y) < Math.Min(a.Y, b.Y)) continue;
                    double sx = (double)d.X - c.X, sy = (double)d.Y - c.Y;
                    double denominator = rx * sy - ry * sx;
                    if (denominator == 0.0) continue;
                    double qx = (double)c.X - a.X, qy = (double)c.Y - a.Y;
                    double t = (qx * sy - qy * sx) / denominator;
                    double u = (qx * ry - qy * rx) / denominator;
                    if (t <= 0.0 || t >= 1.0 || u <= 0.0 || u >= 1.0) continue;
                    xCuts.Add(a.X + t * rx);
                }
            }
        }

        private static double YAt(IReadOnlyList<Point2> polygon, int edge, double x)
        {
            Point2 a = polygon[edge], b = polygon[(edge + 1) % polygon.Count];
            return a.Y + ((double)b.Y - a.Y) * (x - a.X) / ((double)b.X - a.X);
        }

        private static void AddTriangle(List<Point2> vertices, List<int> triangles,
            Point2 a, Point2 b, Point2 c)
        {
            double cross = ((double)b.X - a.X) * ((double)c.Y - a.Y) -
                ((double)b.Y - a.Y) * ((double)c.X - a.X);
            if (cross <= 0.0) return;
            int first = vertices.Count;
            vertices.Add(a);
            vertices.Add(b);
            vertices.Add(c);
            triangles.Add(first);
            triangles.Add(first + 1);
            triangles.Add(first + 2);
        }

        private static void GetSurfaceColors(string materialId, PaletteMode palette, out Color baseColor, out Color accentColor)
        {
            bool colorBlind = palette == PaletteMode.ColorBlind;
            switch (materialId)
            {
                case "sidewalk":
                    baseColor = colorBlind ? new Color(0.36f, 0.42f, 0.47f) : new Color(0.42f, 0.45f, 0.44f);
                    accentColor = colorBlind ? new Color(0.56f, 0.62f, 0.65f) : new Color(0.56f, 0.58f, 0.54f); return;
                case "tactile":
                    baseColor = colorBlind ? new Color(0.66f, 0.46f, 0.12f) : new Color(0.63f, 0.48f, 0.20f);
                    accentColor = colorBlind ? new Color(0.94f, 0.72f, 0.25f) : new Color(0.88f, 0.69f, 0.30f); return;
                case "tactile.dots":
                    baseColor = colorBlind ? new Color(0.75f, 0.57f, 0.18f) : new Color(0.77f, 0.62f, 0.30f);
                    accentColor = colorBlind ? new Color(1.0f, 0.83f, 0.37f) : new Color(0.98f, 0.82f, 0.46f); return;
                case "asphalt":
                    baseColor = colorBlind ? new Color(0.17f, 0.21f, 0.24f) : new Color(0.19f, 0.21f, 0.21f);
                    accentColor = colorBlind ? new Color(0.31f, 0.36f, 0.38f) : new Color(0.32f, 0.33f, 0.31f); return;
                case "crosswalk":
                    baseColor = colorBlind ? new Color(0.21f, 0.25f, 0.28f) : new Color(0.23f, 0.25f, 0.24f);
                    accentColor = colorBlind ? new Color(0.88f, 0.79f, 0.56f) : new Color(0.82f, 0.83f, 0.77f); return;
                case "ramp":
                    baseColor = colorBlind ? new Color(0.27f, 0.48f, 0.52f) : new Color(0.29f, 0.47f, 0.43f);
                    accentColor = colorBlind ? new Color(0.49f, 0.71f, 0.72f) : new Color(0.48f, 0.65f, 0.58f); return;
                default:
                    baseColor = colorBlind ? new Color(0.34f, 0.38f, 0.43f) : new Color(0.39f, 0.41f, 0.39f);
                    accentColor = colorBlind ? new Color(0.53f, 0.57f, 0.62f) : new Color(0.57f, 0.58f, 0.53f); return;
            }
        }

        private void EnsureConfigured()
        {
            if (!configured || playerCamera == null || config == null || displayMaterial == null)
                throw new InvalidOperationException("Configure must be called before rendering.");
        }

        private void OnDestroy()
        {
            foreach (SurfaceBatch batch in surfaceBatches.Values)
            {
                if (batch.Mesh == null) continue;
                if (Application.isPlaying) Destroy(batch.Mesh); else DestroyImmediate(batch.Mesh);
            }
        }

        private sealed class SurfaceBatch
        {
            internal readonly Mesh Mesh;
            internal readonly MeshRenderer Renderer;
            internal readonly List<Vector3> Vertices = new List<Vector3>();
            internal readonly List<Vector2> Uvs = new List<Vector2>();
            internal readonly List<Color> Colors = new List<Color>();
            internal readonly List<int> Triangles = new List<int>();
            internal readonly MaterialPropertyBlock PropertyBlock = new MaterialPropertyBlock();
            internal SurfaceBatch(Mesh mesh, MeshRenderer renderer) { Mesh = mesh; Renderer = renderer; }
        }

        private sealed class PolygonTriangulation
        {
            internal readonly List<Point2> Vertices;
            internal readonly List<int> Triangles;
            internal PolygonTriangulation(List<Point2> vertices, List<int> triangles)
            { Vertices = vertices; Triangles = triangles; }
        }

        private struct ScanlineCrossing
        {
            internal readonly int Edge;
            internal readonly double MiddleY;
            internal ScanlineCrossing(int edge, double middleY) { Edge = edge; MiddleY = middleY; }
        }

        private struct SurfaceBatchKey : IEquatable<SurfaceBatchKey>
        {
            internal readonly string MaterialId;
            internal readonly bool Current;
            internal readonly PaletteMode Palette;
            internal SurfaceBatchKey(string materialId, bool current, PaletteMode palette)
            { MaterialId = materialId; Current = current; Palette = palette; }
            public bool Equals(SurfaceBatchKey other)
            { return MaterialId == other.MaterialId && Current == other.Current && Palette == other.Palette; }
            public override bool Equals(object obj) { return obj is SurfaceBatchKey && Equals((SurfaceBatchKey)obj); }
            public override int GetHashCode()
            { unchecked { return ((MaterialId.GetHashCode() * 397) ^ Current.GetHashCode()) * 397 ^ (int)Palette; } }
        }

        private struct DashSegment
        {
            internal readonly Point2 A, B;
            internal DashSegment(Point2 a, Point2 b) { A = a; B = b; }
        }
    }
}
