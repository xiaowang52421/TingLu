using NUnit.Framework;
using System.Collections.Generic;
using TingLu.Config;
using TingLu.Contracts;
using TingLu.Presentation;
using UnityEngine;

namespace TingLu.Tests.Upgrade.Role08
{
    public sealed class StreetRendererTests
    {
        private GameObject cameraObject;
        private GameObject rendererObject;
        private Camera displayCamera;
        private StreetRenderer renderer;

        [TearDown]
        public void TearDown()
        {
            if (rendererObject != null) Object.DestroyImmediate(rendererObject);
            if (cameraObject != null) Object.DestroyImmediate(cameraObject);
        }

        private void ConfigureRenderer()
        {
            cameraObject = new GameObject("Street renderer test camera");
            displayCamera = cameraObject.AddComponent<Camera>();
            rendererObject = new GameObject("Street renderer test host");
            renderer = rendererObject.AddComponent<StreetRenderer>();
            renderer.Configure(displayCamera, 19, new StreetConfig());
        }

        private Mesh RenderSurfaces(params StreetKnownSurface[] surfaces)
        {
            renderer.Render(new StreetMemoryFrame(surfaces, new StreetKnownObject[0]),
                new PerceptionFrame(1, 1, new Observation[0]), StreetFixtures.Player(),
                new UserSettings(DisplayMode.Standard, OutputMode.Headphones, 1, false, false), 1);
            MeshFilter filter = rendererObject.GetComponentInChildren<MeshFilter>(true);
            Assert.That(filter, Is.Not.Null);
            return filter.sharedMesh;
        }

        private static Vector2[] UvsAt(Mesh mesh, float x, float z)
        {
            Vector3[] vertices = mesh.vertices;
            Vector2[] uvs = mesh.uv;
            var matches = new List<Vector2>();
            for (int i = 0; i < vertices.Length; i++)
                if (Mathf.Abs(vertices[i].x - x) <= 0.0001f && Mathf.Abs(vertices[i].z - z) <= 0.0001f)
                    matches.Add(uvs[i]);
            return matches.ToArray();
        }

        private static Vector2 UvAt(Mesh mesh, float x, float z)
        {
            Vector2[] matches = UvsAt(mesh, x, z);
            Assert.That(matches, Is.Not.Empty, "Expected a triangulated vertex at the requested world coordinate.");
            return matches[0];
        }

        private void RenderAndAssertArea(StreetKnownSurface surface)
        {
            renderer.Render(new StreetMemoryFrame(new[] { surface }, new StreetKnownObject[0]),
                new PerceptionFrame(1, 1, new Observation[0]), StreetFixtures.Player(),
                new UserSettings(DisplayMode.Standard, OutputMode.Headphones, 1, false, false), 1);

            MeshFilter filter = rendererObject.GetComponentInChildren<MeshFilter>(true);
            Assert.That(filter, Is.Not.Null);
            Vector3[] vertices = filter.sharedMesh.vertices;
            int[] triangles = filter.sharedMesh.triangles;
            Assert.That(triangles.Length, Is.GreaterThan(0), "A known ground fragment must produce surface triangles.");

            double renderedArea = 0.0;
            for (int i = 0; i < triangles.Length; i += 3)
            {
                Vector3 a = vertices[triangles[i]];
                Vector3 b = vertices[triangles[i + 1]];
                Vector3 c = vertices[triangles[i + 2]];
                renderedArea += System.Math.Abs(Vector3.Cross(b - a, c - a).y) * 0.5;
                AssertTriangleSamplesInside(surface.Polygon, a, b, c);
            }
            Assert.That(renderedArea, Is.EqualTo(System.Math.Abs(StreetGeometry.Area(surface.Polygon))).Within(0.0001),
                "Triangles must cover the input area without adding area outside its observed boundary.");
        }

        private static void AssertTriangleSamplesInside(System.Collections.Generic.IReadOnlyList<Point2> polygon,
            Vector3 a, Vector3 b, Vector3 c)
        {
            AssertPointInside(polygon, new Point2(a.x, a.z));
            AssertPointInside(polygon, new Point2(b.x, b.z));
            AssertPointInside(polygon, new Point2(c.x, c.z));
            AssertPointInside(polygon, new Point2((a.x + b.x) * 0.5f, (a.z + b.z) * 0.5f));
            AssertPointInside(polygon, new Point2((b.x + c.x) * 0.5f, (b.z + c.z) * 0.5f));
            AssertPointInside(polygon, new Point2((c.x + a.x) * 0.5f, (c.z + a.z) * 0.5f));
            AssertPointInside(polygon, new Point2((a.x + b.x + c.x) / 3f, (a.z + b.z + c.z) / 3f));
        }

        private static void AssertPointInside(System.Collections.Generic.IReadOnlyList<Point2> polygon, Point2 point)
        {
            bool inside = false;
            for (int i = 0, j = polygon.Count - 1; i < polygon.Count; j = i++)
            {
                Point2 a = polygon[i], b = polygon[j];
                double cross = ((double)b.X - a.X) * ((double)point.Y - a.Y) -
                    ((double)b.Y - a.Y) * ((double)point.X - a.X);
                double dot = ((double)point.X - a.X) * ((double)point.X - b.X) +
                    ((double)point.Y - a.Y) * ((double)point.Y - b.Y);
                if (System.Math.Abs(cross) <= 0.00001 && dot <= 0.0000001) return;
                if ((a.Y > point.Y) != (b.Y > point.Y) &&
                    point.X < ((double)b.X - a.X) * (point.Y - a.Y) / ((double)b.Y - a.Y) + a.X)
                    inside = !inside;
            }
            Assert.That(inside, Is.True, "A rendered triangle sample crossed outside the licensed polygon.");
        }

        [Test]
        public void AdjacentDifferentShapedFragmentsShareTextureDirectionAndPhaseAtTheirBoundary()
        {
            ConfigureRenderer();
            var verticalAxis = new Point2(0, 1);
            // The first fragment is wide and shallow; the second is narrow and tall.
            // Polygon-derived axes would rotate at their shared boundary.
            var wide = new StreetKnownSurface("tactile", StreetGeometry.Rect(0, 0, 5, 1), 0, 4, verticalAxis);
            Point2[] tallLShape =
            {
                new Point2(5, 0), new Point2(6, 0), new Point2(6, 5),
                new Point2(5.5f, 5), new Point2(5.5f, 1), new Point2(5, 1)
            };
            var tall = new StreetKnownSurface("tactile", tallLShape, 0, 4, verticalAxis);
            Mesh mesh = RenderSurfaces(wide, tall);

            Vector2[] lowerSeam = UvsAt(mesh, 5, 0);
            Vector2[] upperSeam = UvsAt(mesh, 5, 1);
            Assert.That(lowerSeam.Length, Is.GreaterThanOrEqualTo(2), "Both clipped patches should contribute the shared endpoint.");
            Assert.That(upperSeam.Length, Is.GreaterThanOrEqualTo(2), "Both clipped patches should contribute the shared endpoint.");
            foreach (Vector2 uv in lowerSeam)
            {
                Assert.That(uv.x, Is.EqualTo(0).Within(0.0001f));
                Assert.That(uv.y, Is.EqualTo(-5).Within(0.0001f));
            }
            foreach (Vector2 uv in upperSeam)
            {
                Assert.That(uv.x, Is.EqualTo(1).Within(0.0001f));
                Assert.That(uv.y, Is.EqualTo(-5).Within(0.0001f));
            }
        }

        [Test]
        public void ReClippingKeepsTexturePhaseAndHonorsNinetyDegreeSourceAxis()
        {
            ConfigureRenderer();
            // A non-unit input exercises the contract's canonical axis normalization.
            var verticalAxis = new Point2(0, 2);
            Mesh originalMesh = RenderSurfaces(new StreetKnownSurface("tactile",
                StreetGeometry.Rect(0, 0, 4, 4), 0, 4, verticalAxis));
            float[,] corners = { { 0, 0 }, { 4, 0 }, { 4, 4 }, { 0, 4 } };
            var originalUvs = new Vector2[corners.GetLength(0)];
            for (int i = 0; i < originalUvs.Length; i++)
                originalUvs[i] = UvAt(originalMesh, corners[i, 0], corners[i, 1]);

            Mesh clippedMesh = RenderSurfaces(
                new StreetKnownSurface("tactile", StreetGeometry.Rect(0, 0, 2, 4), 0, 4, verticalAxis),
                new StreetKnownSurface("tactile", StreetGeometry.Rect(2, 0, 4, 4), 0, 4, verticalAxis));

            for (int i = 0; i < originalUvs.Length; i++)
            {
                Vector2 clippedUv = UvAt(clippedMesh, corners[i, 0], corners[i, 1]);
                Assert.That(clippedUv.x, Is.EqualTo(originalUvs[i].x).Within(0.0001f),
                    "Re-clipping must not change the U phase at a surviving world point.");
                Assert.That(clippedUv.y, Is.EqualTo(originalUvs[i].y).Within(0.0001f),
                    "Re-clipping must not change the V phase at a surviving world point.");
            }

            Assert.That(UvAt(clippedMesh, 4, 4).x, Is.EqualTo(4).Within(0.0001f),
                "The 90-degree source axis maps world Y to the texture U coordinate.");
            Assert.That(UvAt(clippedMesh, 4, 4).y, Is.EqualTo(-4).Within(0.0001f),
                "The perpendicular texture coordinate uses the same world-space origin.");
        }

        private static bool TriangleContains(Vector3 a, Vector3 b, Vector3 c, Point2 point)
        {
            double ab = ((double)b.x - a.x) * (point.Y - a.z) - ((double)b.z - a.z) * (point.X - a.x);
            double bc = ((double)c.x - b.x) * (point.Y - b.z) - ((double)c.z - b.z) * (point.X - b.x);
            double ca = ((double)a.x - c.x) * (point.Y - c.z) - ((double)a.z - c.z) * (point.X - c.x);
            return (ab >= -0.000001 && bc >= -0.000001 && ca >= -0.000001) ||
                (ab <= 0.000001 && bc <= 0.000001 && ca <= 0.000001);
        }

        private static Point2[] MemoryMergedSelfTouchingAsphaltPatch()
        {
            return new[]
            {
                new Point2(-1.28729463f, -10.4095516f),
                new Point2(-1.34520638f, -10.3663788f),
                new Point2(-4f, -13.9661f),
                new Point2(-1.34520721f, -10.3663778f),
                new Point2(-4f, -13.9661016f),
                new Point2(-4f, -14.0094328f),
                new Point2(-4f, -14.0451622f),
                new Point2(-4f, -14.0800867f),
                new Point2(-1.19038355f, -10.4839144f),
                new Point2(-1.23866653f, -10.4465294f),
                new Point2(-2.65362215f, -12.2905283f),
                new Point2(-4f, -14.0451622f),
                new Point2(-1.23867f, -10.4465265f)
            };
        }

        private static void AddSurfaceBatchScratchEntries(StreetRenderer target)
        {
            var batchesField = typeof(StreetRenderer).GetField("surfaceBatches",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            Assert.That(batchesField, Is.Not.Null);
            var batches = (System.Collections.IDictionary)batchesField.GetValue(target);
            foreach (System.Collections.DictionaryEntry entry in batches)
            {
                object batch = entry.Value;
                GetBatchScratchList(batch, "Vertices").Add(new Vector3(1, 2, 3));
                GetBatchScratchList(batch, "Uvs").Add(new Vector2(1, 2));
                GetBatchScratchList(batch, "Colors").Add(Color.white);
                GetBatchScratchList(batch, "Triangles").Add(0);
                return;
            }
            Assert.Fail("The render should have created a surface batch before checking its scratch buffers.");
        }

        private static void AssertSurfaceBatchScratchEmpty(StreetRenderer target)
        {
            var batchesField = typeof(StreetRenderer).GetField("surfaceBatches",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            var batches = (System.Collections.IDictionary)batchesField.GetValue(target);
            foreach (System.Collections.DictionaryEntry entry in batches)
            {
                object batch = entry.Value;
                Assert.That(GetBatchScratchList(batch, "Vertices"), Is.Empty);
                Assert.That(GetBatchScratchList(batch, "Uvs"), Is.Empty);
                Assert.That(GetBatchScratchList(batch, "Colors"), Is.Empty);
                Assert.That(GetBatchScratchList(batch, "Triangles"), Is.Empty);
            }
        }

        private static System.Collections.IList GetBatchScratchList(object batch, string name)
        {
            var field = batch.GetType().GetField(name,
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, "Expected staging field " + name + ".");
            return (System.Collections.IList)field.GetValue(batch);
        }

        [Test]
        public void RenderUsesOnlyTheConfiguredLayerAndKeepsMeshColorsVertexAligned()
        {
            const int layer = 19;
            cameraObject = new GameObject("Street renderer test camera");
            displayCamera = cameraObject.AddComponent<Camera>();
            rendererObject = new GameObject("Street renderer test host");
            renderer = rendererObject.AddComponent<StreetRenderer>();
            renderer.Configure(displayCamera, layer, new StreetConfig());

            var surface = new StreetKnownSurface("sidewalk", StreetGeometry.Rect(-1, -1, 1, 1), 0, 4);
            var memory = new StreetMemoryFrame(new[] { surface }, new StreetKnownObject[0]);
            renderer.Render(memory, new PerceptionFrame(1, 1, new Observation[0]), StreetFixtures.Player(),
                new UserSettings(DisplayMode.Standard, OutputMode.Headphones, 1, false, false), 1);

            Assert.That(displayCamera.clearFlags, Is.EqualTo(CameraClearFlags.SolidColor));
            Assert.That(displayCamera.backgroundColor, Is.EqualTo(Color.black));
            Assert.That(displayCamera.cullingMask, Is.EqualTo(1 << layer));
            Assert.That(renderer.ActiveSurfaceBatchCount, Is.EqualTo(1));
            MeshFilter[] filters = rendererObject.GetComponentsInChildren<MeshFilter>(true);
            Assert.That(filters, Has.Length.EqualTo(1));
            Assert.That(filters[0].sharedMesh.colors, Has.Length.EqualTo(filters[0].sharedMesh.vertexCount));

            AddSurfaceBatchScratchEntries(renderer);
            renderer.Clear();

            Assert.That(renderer.ActiveSurfaceBatchCount, Is.Zero);
            Assert.That(renderer.ActiveStrokeCount, Is.Zero);
            AssertSurfaceBatchScratchEmpty(renderer);
        }

        [Test]
        public void StableMemoryReusesSurfaceBatchWhilePlayerAndPerceptionStillRenderPerFrame()
        {
            ConfigureRenderer();
            var memory = new StreetMemoryFrame(
                new[] { new StreetKnownSurface("sidewalk", StreetGeometry.Rect(-1, -1, 1, 1), 0, 10) },
                new StreetKnownObject[0]);
            var settings = new UserSettings(DisplayMode.Standard, OutputMode.Headphones, 1, false, false);
            renderer.Render(memory, new PerceptionFrame(1, 1, new Observation[0]), StreetFixtures.Player(), settings, 1);
            MeshFilter filter = rendererObject.GetComponentInChildren<MeshFilter>(true);
            Mesh firstMesh = filter.sharedMesh;
            float firstPlayerX = rendererObject.GetComponentInChildren<LineRenderer>(true).GetPosition(0).x;
            Assert.That(renderer.SurfaceBatchRebuildCount, Is.EqualTo(1));

            var sound = new Observation("car", ObjectCategory.Dynamic, MemoryPolicy.Never,
                ObservationSource.PassiveSound, InformationKind.SoundLocation, new Point2(2, 0),
                new Segment2[0], null, 1, 4, PerceptionSemantic.Person);
            renderer.Render(memory, new PerceptionFrame(2, 2, new[] { sound }),
                StreetFixtures.Player(new Point2(1, 0)), settings, 2);

            Assert.That(renderer.SurfaceBatchRebuildCount, Is.EqualTo(1),
                "A stable memory frame must skip surface mesh clearing, triangulation and upload.");
            Assert.That(filter.sharedMesh, Is.SameAs(firstMesh));
            Assert.That(renderer.ActiveStrokeCount, Is.GreaterThan(3),
                "Current perception strokes must be rebuilt even when surface geometry is reused.");
            Assert.That(rendererObject.GetComponentInChildren<LineRenderer>(true).GetPosition(0).x,
                Is.Not.EqualTo(firstPlayerX), "Player strokes must still follow the current state on a surface-cache hit.");

            var replacementMemory = new StreetMemoryFrame(memory.Surfaces, memory.Objects);
            renderer.Render(replacementMemory, new PerceptionFrame(3, 2, new Observation[0]),
                StreetFixtures.Player(new Point2(1, 0)), settings, 2);
            Assert.That(renderer.SurfaceBatchRebuildCount, Is.EqualTo(2),
                "A new immutable memory frame must invalidate the one-entry surface cache.");
        }

        [Test]
        public void SurfaceCacheInvalidatesForTimeBoundariesSettingsTransformAndClear()
        {
            ConfigureRenderer();
            var memory = new StreetMemoryFrame(
                new[] { new StreetKnownSurface("sidewalk", StreetGeometry.Rect(-1, -1, 1, 1), 1, 3) },
                new StreetKnownObject[0]);
            var perception = new PerceptionFrame(1, 0, new Observation[0]);
            var player = StreetFixtures.Player();
            var standard = new UserSettings(DisplayMode.Standard, OutputMode.Headphones, 1, false, false);
            renderer.Render(memory, perception, player, standard, 0);
            renderer.Render(memory, perception, player, standard, 0.5);
            Assert.That(renderer.SurfaceBatchRebuildCount, Is.EqualTo(1));

            renderer.Render(memory, perception, player, standard, 1);
            renderer.Render(memory, perception, player, standard, 2);
            Assert.That(renderer.SurfaceBatchRebuildCount, Is.EqualTo(2), "ObservedAt changes the current/stale batch classification.");
            renderer.Render(memory, perception, player, standard, 3);
            Assert.That(renderer.SurfaceBatchRebuildCount, Is.EqualTo(3), "CurrentUntil changes the current/stale batch classification.");
            renderer.Render(memory, perception, player, standard, 2.5);
            Assert.That(renderer.SurfaceBatchRebuildCount, Is.EqualTo(4), "A backwards clock step must invalidate cached time classification.");

            var colorBlind = new UserSettings(DisplayMode.Standard, OutputMode.Headphones, 1, false, false, PaletteMode.ColorBlind);
            renderer.Render(memory, perception, player, colorBlind, 2.6);
            Assert.That(renderer.SurfaceBatchRebuildCount, Is.EqualTo(5));
            var noUi = new UserSettings(DisplayMode.NoUI, OutputMode.Headphones, 1, false, false, PaletteMode.ColorBlind);
            renderer.Render(memory, perception, player, noUi, 2.7);
            Assert.That(renderer.SurfaceBatchRebuildCount, Is.EqualTo(6), "Display changes participate in the surface cache key.");
            var audioOnly = new UserSettings(DisplayMode.AudioOnly, OutputMode.Headphones, 1, false, false, PaletteMode.ColorBlind);
            renderer.Render(memory, perception, player, audioOnly, 2.8);
            Assert.That(renderer.ActiveSurfaceBatchCount, Is.Zero);
            Assert.That(renderer.SurfaceBatchRebuildCount, Is.EqualTo(7));
            renderer.Render(memory, perception, player, audioOnly, 2.9);
            Assert.That(renderer.SurfaceBatchRebuildCount, Is.EqualTo(7), "Repeated AudioOnly frames remain blank without rebuilding.");
            renderer.Render(memory, perception, player, colorBlind, 2.9);
            Assert.That(renderer.ActiveSurfaceBatchCount, Is.EqualTo(1));
            Assert.That(renderer.SurfaceBatchRebuildCount, Is.EqualTo(8));

            rendererObject.transform.position = new Vector3(2, 0, 0);
            renderer.Render(memory, perception, player, colorBlind, 2.9);
            Assert.That(renderer.SurfaceBatchRebuildCount, Is.EqualTo(9), "A renderer transform change invalidates local mesh coordinates.");
            renderer.Clear();
            Assert.That(renderer.ActiveSurfaceBatchCount, Is.Zero);
            renderer.Render(memory, perception, player, colorBlind, 2.9);
            Assert.That(renderer.SurfaceBatchRebuildCount, Is.EqualTo(10), "Clear must invalidate the cache before the next render.");
        }

        [Test]
        public void RecordsInitialRebuildAndThirtyStableRenderTimings()
        {
            ConfigureRenderer();
            // Reuse the real memory-merged 13-vertex asphalt fragment regression above.
            Point2[] polygon = MemoryMergedSelfTouchingAsphaltPatch();
            var memory = new StreetMemoryFrame(
                new[] { new StreetKnownSurface("asphalt", polygon, 0, 1000) }, new StreetKnownObject[0]);
            var sound = new Observation("car", ObjectCategory.Dynamic, MemoryPolicy.Never,
                ObservationSource.PassiveSound, InformationKind.SoundLocation, new Point2(3, 1),
                new Segment2[0], null, 0, 4, PerceptionSemantic.Threat);
            var perception = new PerceptionFrame(1, 1, new[] { sound });
            var player = StreetFixtures.Player();
            var settings = new UserSettings(DisplayMode.Standard, OutputMode.Headphones, 1, false, false);

            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            renderer.Render(memory, perception, player, settings, 1);
            stopwatch.Stop();
            double initialRebuildMs = stopwatch.Elapsed.TotalMilliseconds;
            int initialRebuildCount = renderer.SurfaceBatchRebuildCount;
            Assert.That(renderer.ActiveSurfaceBatchCount, Is.EqualTo(1));
            Mesh initialMesh = rendererObject.GetComponentInChildren<MeshFilter>(true).sharedMesh;
            Assert.That(initialMesh.vertexCount, Is.GreaterThan(0));
            Assert.That(initialMesh.triangles, Is.Not.Empty);
            Assert.That(renderer.ActiveStrokeCount, Is.GreaterThan(3),
                "The timed Render path includes the voice arcs and player marker.");

            const int sampleCount = 30;
            var samplesMs = new double[sampleCount];
            for (int i = 0; i < sampleCount; i++)
            {
                stopwatch.Restart();
                renderer.Render(memory, perception, player, settings, 1.1 + i * 0.01);
                stopwatch.Stop();
                samplesMs[i] = stopwatch.Elapsed.TotalMilliseconds;
            }
            System.Array.Sort(samplesMs);
            double p50Ms = (samplesMs[14] + samplesMs[15]) * 0.5;
            double p95Ms = samplesMs[(int)System.Math.Ceiling(0.95 * sampleCount) - 1];
            double maxMs = samplesMs[sampleCount - 1];
            int stableRebuildCount = renderer.SurfaceBatchRebuildCount;

            TestContext.WriteLine(
                "StreetRenderer timing fixture=asphalt-13-vertex+voice initialRebuildMs={0:F3} initialSurfaceBatchRebuildCount={1} stableFrames={2} p50Ms={3:F3} p95Ms={4:F3} maxMs={5:F3} stableSurfaceBatchRebuildCount={6}",
                initialRebuildMs, initialRebuildCount, sampleCount, p50Ms, p95Ms, maxMs, stableRebuildCount);
            Assert.That(initialRebuildCount, Is.EqualTo(1));
            Assert.That(stableRebuildCount, Is.EqualTo(1),
                "All 30 stable frames must hit the unchanged surface batch.");
        }

        [Test]
        public void KnownHeightRaisesContourAndLocalEdgesWithoutExtrudingKnownGround()
        {
            cameraObject = new GameObject("Street height test camera");
            displayCamera = cameraObject.AddComponent<Camera>();
            rendererObject = new GameObject("Street height test host");
            renderer = rendererObject.AddComponent<StreetRenderer>();
            renderer.Configure(displayCamera, 19, new StreetConfig());
            var surface = new StreetKnownSurface("sidewalk", StreetGeometry.Rect(-2, -2, 2, 2), 0, 4);
            var obstacle = new StreetKnownObject("wall", "brick", StreetKind.Permanent,
                StreetRenderPlanTests.RectangleContour(-1, 0, 1, 1), 1.2f, 0, 4);

            renderer.Render(new StreetMemoryFrame(new[] { surface }, new[] { obstacle }),
                new PerceptionFrame(1, 1, new Observation[0]), StreetFixtures.Player(),
                new UserSettings(DisplayMode.Standard, OutputMode.Headphones, 1, false, false), 1);

            MeshFilter filter = rendererObject.GetComponentInChildren<MeshFilter>(true);
            Vector3[] vertices = filter.sharedMesh.vertices;
            Assert.That(vertices, Is.Not.Empty);
            foreach (Vector3 vertex in vertices)
                Assert.That(vertex.y, Is.EqualTo(0.015f).Within(0.001f), "Known surfaces stay flat and are not extruded by object height.");

            bool hasContourBase = false;
            bool hasElevatedContour = false;
            bool hasLocalVerticalEdge = false;
            foreach (LineRenderer line in rendererObject.GetComponentsInChildren<LineRenderer>(true))
            {
                if (line.positionCount < 2) continue;
                Vector3 first = line.GetPosition(0);
                Vector3 second = line.GetPosition(1);
                hasContourBase |= Mathf.Abs(first.y - 0.045f) < 0.001f && Mathf.Abs(second.y - 0.045f) < 0.001f;
                hasElevatedContour |= Mathf.Abs(first.y - 1.245f) < 0.001f && Mathf.Abs(second.y - 1.245f) < 0.001f;
                hasLocalVerticalEdge |= Mathf.Abs(first.y - 0.045f) < 0.001f && Mathf.Abs(second.y - 1.245f) < 0.001f;
            }

            Assert.That(hasContourBase, Is.True);
            Assert.That(hasElevatedContour, Is.True);
            Assert.That(hasLocalVerticalEdge, Is.True);
        }

        [Test]
        public void PlayerArrowUsesKnownGroundHeightAndSignalColorSitsAboveKnownFixture()
        {
            ConfigureRenderer();
            const float groundHeight = 0.15f;
            const float fixtureHeight = 2f;
            var playerGround = new StreetKnownSurface("tactile", StreetGeometry.Rect(-6, -17, -5, -15),
                0, 4, heightBase: groundHeight);
            var lampGround = new StreetKnownSurface("sidewalk", StreetGeometry.Rect(1, 1, 4, 4),
                0, 4, heightBase: groundHeight);
            var lamp = new StreetKnownObject("lamp", "metal", StreetKind.Permanent,
                StreetRenderPlanTests.RectangleContour(2, 2, 3, 3), fixtureHeight, 0, 4);
            var memory = new StreetMemoryFrame(new[] { playerGround, lampGround }, new[] { lamp });
            var settings = new UserSettings(DisplayMode.Standard, OutputMode.Headphones, 1, false, false);
            var player = StreetFixtures.Player(new Point2(-5.5f, -16));
            var green = new StreetSignalObservation("lamp", new Point2(2.5f, 2.5f), true, 0, 4);

            renderer.Render(memory, new PerceptionFrame(1, 1, new Observation[0]), player, settings, 1,
                new[] { green });

            Color playerColor = new Color(0.93f, 0.94f, 0.86f, 1f);
            Color signalGreen = new Color(0.28f, 0.95f, 0.42f, 1f);
            int playerLines = 0;
            int greenLines = 0;
            foreach (LineRenderer line in rendererObject.GetComponentsInChildren<LineRenderer>(true))
            {
                if (LineAtHeight(line, 0.06f + groundHeight))
                {
                    playerLines++;
                    AssertColorMatches(line.startColor, playerColor, "The known-ground player arrow keeps its configured marker color.");
                }
                else if (LineAtHeight(line, 0.045f + fixtureHeight + 0.005f))
                {
                    greenLines++;
                    AssertColorMatches(line.startColor, signalGreen, "The fresh green lamp contour keeps its configured color.");
                }
            }
            Assert.That(playerLines, Is.EqualTo(3),
                "All three player-arrow segments must clear the known 0.15 m ground. " + DescribeActiveLines());
            Assert.That(greenLines, Is.EqualTo(4),
                "The four fresh lamp-color contour segments must sit above its known 2 m housing. " + DescribeActiveLines());

            renderer.Render(StreetMemoryFrame.Empty, new PerceptionFrame(2, 2, new Observation[0]),
                player, settings, 2);
            playerLines = 0;
            foreach (LineRenderer line in rendererObject.GetComponentsInChildren<LineRenderer>(true))
            {
                if (!LineAtHeight(line, 0.06f)) continue;
                playerLines++;
                AssertColorMatches(line.startColor, playerColor,
                    "When memory has no known ground, the marker keeps its configured color and existing fallback height.");
            }
            Assert.That(playerLines, Is.EqualTo(3), DescribeActiveLines());
        }

        [Test]
        public void ColorBlindSignalRendererDistinguishesFreshGreenAndRedThenClearsExpiredColor()
        {
            ConfigureRenderer();
            var lamp = new StreetKnownObject("lamp", "metal", StreetKind.Permanent,
                StreetRenderPlanTests.RectangleContour(0, 0, 1, 1), 2f, 0, 4);
            var memory = new StreetMemoryFrame(new StreetKnownSurface[0], new[] { lamp });
            var settings = new UserSettings(DisplayMode.Standard, OutputMode.Headphones, 1, false, false,
                PaletteMode.ColorBlind);
            var player = StreetFixtures.Player(new Point2(5, 5));
            var green = new StreetSignalObservation("lamp", new Point2(0.5f, 0.5f), true, 0, 1.5);
            var red = new StreetSignalObservation("lamp", new Point2(0.5f, 0.5f), false, 1, 1.5);
            Color greenColor = new Color(0.24f, 0.78f, 1f, 1f);
            Color redColor = new Color(1f, 0.66f, 0.18f, 1f);

            renderer.Render(memory, new PerceptionFrame(1, 1, new Observation[0]), player, settings, 1,
                new[] { green });
            AssertSignalColorLines(greenColor, redColor, 4, 0);

            renderer.Render(memory, new PerceptionFrame(2, 1.1, new Observation[0]), player, settings, 1.1,
                new[] { red });
            AssertSignalColorLines(greenColor, redColor, 0, 4);

            renderer.Render(memory, new PerceptionFrame(3, 1.5, new Observation[0]), player, settings, 1.5,
                new[] { red });
            AssertSignalColorLines(greenColor, redColor, 0, 0);
        }

        private void AssertSignalColorLines(Color greenColor, Color redColor, int expectedGreen, int expectedRed)
        {
            Assert.That(greenColor, Is.Not.EqualTo(redColor), "Color-blind mode must encode the two signal states with different colors.");
            int greenLines = 0;
            int redLines = 0;
            const float signalTop = 0.045f + 2f + 0.005f;
            foreach (LineRenderer line in rendererObject.GetComponentsInChildren<LineRenderer>(true))
            {
                if (!LineAtHeight(line, signalTop)) continue;
                if (ColorMatches(line.startColor, greenColor)) greenLines++;
                else if (ColorMatches(line.startColor, redColor)) redLines++;
                else Assert.Fail("A current lamp-top contour has neither the configured green nor red signal color.");
            }
            Assert.That(greenLines, Is.EqualTo(expectedGreen), DescribeActiveLines());
            Assert.That(redLines, Is.EqualTo(expectedRed), DescribeActiveLines());
        }

        private string DescribeActiveLines()
        {
            var descriptions = new List<string>();
            foreach (LineRenderer line in rendererObject.GetComponentsInChildren<LineRenderer>(true))
            {
                if (line.positionCount < 2) continue;
                Vector3 first = line.GetPosition(0);
                Vector3 last = line.GetPosition(1);
                descriptions.Add(string.Format(System.Globalization.CultureInfo.InvariantCulture,
                    "{0}: color={1}, y=({2:F4},{3:F4})", line.name, line.startColor, first.y, last.y));
            }
            return "Active renderer lines: " + string.Join("; ", descriptions.ToArray());
        }

        private static bool LineAtHeight(LineRenderer line, float expectedHeight)
        {
            return line.positionCount >= 2 &&
                Mathf.Abs(line.GetPosition(0).y - expectedHeight) <= 0.0001f &&
                Mathf.Abs(line.GetPosition(1).y - expectedHeight) <= 0.0001f;
        }

        private static void AssertColorMatches(Color actual, Color expected, string message)
        {
            Assert.That(ColorMatches(actual, expected), Is.True,
                message + " Expected " + expected + " but the renderer returned " + actual + ".");
        }

        private static bool ColorMatches(Color actual, Color expected)
        {
            // LineRenderer exposes its stored gradient through 8-bit color keys.
            // Allow at most one channel step for round-vs-truncate conversion; this
            // still rejects a wrong palette or an uncolored stroke decisively.
            Color32 actualBytes = (Color32)actual;
            Color32 expectedBytes = (Color32)expected;
            return Mathf.Abs((int)actualBytes.r - expectedBytes.r) <= 1 &&
                Mathf.Abs((int)actualBytes.g - expectedBytes.g) <= 1 &&
                Mathf.Abs((int)actualBytes.b - expectedBytes.b) <= 1 &&
                Mathf.Abs((int)actualBytes.a - expectedBytes.a) <= 1;
        }

        [Test]
        public void MergedTactilePatchWithCollinearClipVerticesRendersItsEntireArea()
        {
            ConfigureRenderer();

            // Reduced from the failed 183-vertex west-tactile patch produced by
            // StreetLevelFactory spawn -> Probe -> StreetMemory.Apply. The short
            // collinear runs retain the real world coordinates and lost 3.1 m^2
            // patch shape while keeping this renderer-only regression compact.
            Point2[] polygon =
            {
                new Point2(-5.5f, -16f),
                new Point2(-5.2f, -15.74827f),
                new Point2(-5.2f, -10.68f),
                new Point2(-5.77880955f, -10.68f),
                new Point2(-5.8f, -10.68f),
                new Point2(-5.8f, -13.2482738f),
                new Point2(-5.8f, -13.2483187f),
                new Point2(-5.8f, -15.74827f)
            };
            var surface = new StreetKnownSurface("tactile", polygon, 0, 4);
            RenderAndAssertArea(surface);
        }

        [Test]
        public void MemoryMergedSelfTouchingAsphaltPatchRendersItsKnownArea()
        {
            ConfigureRenderer();
            RenderAndAssertArea(new StreetKnownSurface("asphalt", MemoryMergedSelfTouchingAsphaltPatch(), 0, 4));
        }

        [Test]
        public void ConcaveKnownPatchDoesNotFillItsUnknownNotch()
        {
            ConfigureRenderer();
            Point2[] polygon =
            {
                new Point2(0, 0), new Point2(3, 0), new Point2(3, 1),
                new Point2(1, 1), new Point2(1, 3), new Point2(0, 3)
            };
            RenderAndAssertArea(new StreetKnownSurface("sidewalk", polygon, 0, 4));

            Mesh mesh = rendererObject.GetComponentInChildren<MeshFilter>(true).sharedMesh;
            Vector3[] vertices = mesh.vertices;
            int[] triangles = mesh.triangles;
            Point2 unknownNotch = new Point2(2, 2);
            for (int i = 0; i < triangles.Length; i += 3)
                Assert.That(TriangleContains(vertices[triangles[i]], vertices[triangles[i + 1]],
                    vertices[triangles[i + 2]], unknownNotch), Is.False,
                    "A concave unknown notch must remain outside every rendered triangle.");
        }
    }
}
