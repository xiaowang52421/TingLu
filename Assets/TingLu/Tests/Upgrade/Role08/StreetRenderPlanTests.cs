using System.Collections.Generic;
using NUnit.Framework;
using TingLu.Config;
using TingLu.Contracts;
using TingLu.Levels;
using TingLu.Presentation;
using TingLu.Tests.Upgrade;
using TingLu.World;

namespace TingLu.Tests.Upgrade.Role08
{
    public sealed class StreetRenderPlanTests
    {
        [TestCase("sidewalk", (int)StreetSurfacePattern.Slabs)]
        [TestCase("tactile", (int)StreetSurfacePattern.Ribs)]
        [TestCase("tactile.dots", (int)StreetSurfacePattern.Dots)]
        [TestCase("asphalt", (int)StreetSurfacePattern.Aggregate)]
        [TestCase("crosswalk", (int)StreetSurfacePattern.Crosswalk)]
        [TestCase("ramp", (int)StreetSurfacePattern.Ramp)]
        [TestCase("unknown", (int)StreetSurfacePattern.Plain)]
        public void MaterialIdsMapToStableSurfacePatterns(string material, int expected)
        {
            Assert.That((int)StreetRenderPlan.PatternForMaterial(material), Is.EqualTo(expected));
        }

        [Test]
        public void ExpiredMemoryRemainsDimAndOnlyKnownObjectContourReceivesHeight()
        {
            var frame = new StreetMemoryFrame(
                new[] { new StreetKnownSurface("sidewalk", StreetGeometry.Rect(-2, -1, 2, 1), 0, 2) },
                new[]
                {
                    new StreetKnownObject("building", "brick", StreetKind.Permanent,
                        RectangleContour(-1, 2, 1, 3), 1.2f, 0, 2),
                    new StreetKnownObject("barrier", "metal", StreetKind.Temporary,
                        RectangleContour(2, 0, 3, 1), 0.8f, 0, 2)
                });

            StreetRenderPlan plan = Create(frame, new PerceptionFrame(1, 5, new Observation[0]),
                new UserSettings(DisplayMode.Standard, OutputMode.Headphones, 1, false, false), 5);

            Assert.That(plan.Surfaces, Has.Count.EqualTo(1), "Known ground memory remains visible after current visibility ends.");
            Assert.That(plan.Surfaces[0].Current, Is.False);
            Assert.That(plan.Strokes.Exists(stroke => stroke.Style == StreetStrokeStyle.StalePermanent &&
                stroke.Elevation > 1.19f && stroke.Elevation < 1.21f), Is.True,
                "A known permanent contour is lifted by its confirmed local height.");
            Assert.That(plan.Strokes.Exists(stroke => stroke.Style == StreetStrokeStyle.StaleTemporary &&
                stroke.Elevation > 0f), Is.False,
                "An expired temporary outline stays on the ground plane rather than implying a live raised object.");
        }

        [Test]
        public void RealCrossroadsLampShowsOnlyFreshSignalInputAndDropsItAfterExpiryOrRestore()
        {
            StreetLevel level = StreetLevelFactory.Create();
            var sourceWorld = new StreetWorld(level.Surfaces, level.Objects, level.Routes, new StreetConfig());
            sourceWorld.Tick(17, false);
            StreetTrafficState savedGreen = sourceWorld.ExportTraffic();
            StreetObject ewLamp = Find(sourceWorld.Capture(), "street.signal.ew");
            StreetKnownObject knownEwLamp = Remember(ewLamp, 17, 17.8);
            var memory = new StreetMemoryFrame(new StreetKnownSurface[0], new[] { knownEwLamp });
            var settings = new UserSettings(DisplayMode.Standard, OutputMode.Headphones, 1, false, false);
            var green = new StreetSignalObservation(ewLamp.Id, ewLamp.Position, ewLamp.SoundActive, 17, 17.8);

            StreetRenderPlan freshGreen = StreetRenderPlan.Create(memory,
                new PerceptionFrame(1, 17, new Observation[0]), StreetFixtures.Player(), settings, 17.2,
                new[] { green });
            Assert.That(freshGreen.Strokes.Exists(stroke => stroke.Style == StreetStrokeStyle.SignalGreen), Is.True,
                "Only the currently probed east-west lamp receives its fresh green colour.");
            Assert.That(freshGreen.Strokes.Exists(stroke => stroke.Style == StreetStrokeStyle.SignalRed), Is.False);

            StreetRenderPlan expired = StreetRenderPlan.Create(memory,
                new PerceptionFrame(2, 18, new Observation[0]), StreetFixtures.Player(), settings, 18,
                new[] { green });
            Assert.That(expired.Strokes.Exists(stroke => stroke.Style == StreetStrokeStyle.SignalGreen ||
                stroke.Style == StreetStrokeStyle.SignalRed), Is.False,
                "The permanent housing remains known, while its expired colour cannot remain visible.");

            var restored = new StreetWorld(level.Surfaces, level.Objects, level.Routes, new StreetConfig());
            restored.ImportTraffic(savedGreen);
            StreetSignalObservation[] afterRestore = { new StreetSignalObservation(ewLamp.Id, ewLamp.Position,
                Find(restored.Capture(), ewLamp.Id).SoundActive, restored.Capture().Time, 17.8) };
            StreetRenderPlan restoredPlan = StreetRenderPlan.Create(memory,
                new PerceptionFrame(3, 17, new Observation[0]), StreetFixtures.Player(), settings, 17.2,
                afterRestore);
            Assert.That(restoredPlan.Strokes.Exists(stroke => stroke.Style == StreetStrokeStyle.SignalGreen), Is.True,
                "A fresh signal observation reflecting restored world time is rendered.");

            StreetTrafficState savedClearance = TrafficAt(level, 14);
            restored.ImportTraffic(savedClearance);
            StreetRenderPlan afterRestoreBeforeProbe = StreetRenderPlan.Create(memory,
                new PerceptionFrame(4, 14, new Observation[0]), StreetFixtures.Player(), settings, 14.2);
            Assert.That(afterRestoreBeforeProbe.Strokes.Exists(stroke =>
                stroke.Style == StreetStrokeStyle.SignalGreen || stroke.Style == StreetStrokeStyle.SignalRed), Is.False,
                "Restored traffic does not resurrect any lamp colour until a new probe supplies it.");

            StreetObject restoredLamp = Find(restored.Capture(), ewLamp.Id);
            var clearanceObservation = new StreetSignalObservation(ewLamp.Id, ewLamp.Position,
                restoredLamp.SoundActive, 14, 14.8);
            StreetRenderPlan clearancePlan = StreetRenderPlan.Create(memory,
                new PerceptionFrame(4, 14, new Observation[0]), StreetFixtures.Player(), settings, 14.2,
                new[] { clearanceObservation });
            Assert.That(clearancePlan.Strokes.Exists(stroke => stroke.Style == StreetStrokeStyle.SignalRed), Is.True,
                "A fresh observation of the restored clearance phase is shown red.");
            Assert.That(clearancePlan.Strokes.Exists(stroke => stroke.Style == StreetStrokeStyle.SignalGreen), Is.False,
                "The pre-restore green observation does not survive a fresh red clearance sample.");
        }

        [Test]
        public void AudioOnlyPlanContainsNoVisualGeometryOrMarkers()
        {
            StreetRenderPlan plan = Create(StreetMemoryFrame.Empty,
                new PerceptionFrame(1, 0, new Observation[0]),
                new UserSettings(DisplayMode.AudioOnly, OutputMode.Headphones, 1, false, false), 0);

            Assert.That(plan.Surfaces, Is.Empty);
            Assert.That(plan.Strokes, Is.Empty);
        }

        [Test]
        public void SoundLocationDrawsShortArcsWithoutGroundOrObjectSilhouette()
        {
            var sound = new Observation("car", ObjectCategory.Dynamic, MemoryPolicy.Never,
                ObservationSource.PassiveSound, InformationKind.SoundLocation,
                new Point2(4, 2), new Segment2[0], null, 1, 3, PerceptionSemantic.Threat);
            StreetRenderPlan plan = Create(StreetMemoryFrame.Empty,
                new PerceptionFrame(2, 2, new[] { sound }),
                new UserSettings(DisplayMode.Standard, OutputMode.Headphones, 1, false, false), 2);

            Assert.That(plan.Surfaces, Is.Empty);
            Assert.That(plan.Strokes.Exists(stroke => stroke.Style == StreetStrokeStyle.Voice), Is.True);
            Assert.That(plan.Strokes.Exists(stroke => stroke.Style == StreetStrokeStyle.Permanent ||
                stroke.Style == StreetStrokeStyle.CurrentTemporary || stroke.Style == StreetStrokeStyle.StaleTemporary), Is.False);
        }

        [Test]
        public void DynamicGeometryObservationDoesNotBecomeAVisibleSilhouette()
        {
            var observation = new Observation("walker", ObjectCategory.Dynamic, MemoryPolicy.Never,
                ObservationSource.CaneEcho, InformationKind.LocalGeometry,
                new Point2(1, 1), RectangleContour(0, 0, 2, 2), "person", 1, 3);
            StreetRenderPlan plan = Create(StreetMemoryFrame.Empty,
                new PerceptionFrame(3, 2, new[] { observation }),
                new UserSettings(DisplayMode.Standard, OutputMode.Headphones, 1, false, false), 2);

            Assert.That(plan.Strokes.Exists(stroke => stroke.Style == StreetStrokeStyle.Permanent ||
                stroke.Style == StreetStrokeStyle.CurrentTemporary || stroke.Style == StreetStrokeStyle.StaleTemporary), Is.False);
        }

        private static StreetRenderPlan Create(StreetMemoryFrame memory, PerceptionFrame perception,
            UserSettings settings, double now)
        {
            return StreetRenderPlan.Create(memory, perception, StreetFixtures.Player(), settings, now);
        }

        private static StreetTrafficState TrafficAt(StreetLevel level, double time)
        {
            var world = new StreetWorld(level.Surfaces, level.Objects, level.Routes, new StreetConfig());
            world.Tick(time, false);
            return world.ExportTraffic();
        }

        private static StreetObject Find(StreetWorldFrame frame, string id)
        {
            foreach (StreetObject item in frame.Objects)
                if (item.Id == id) return item;
            Assert.Fail("Missing real-map object " + id);
            return null;
        }

        private static StreetKnownObject Remember(StreetObject item, double observedAt, double currentUntil)
        {
            var contour = new List<Segment2>();
            for (int i = 0; i < item.Polygon.Count; i++)
                contour.Add(new Segment2(item.Polygon[i], item.Polygon[(i + 1) % item.Polygon.Count]));
            return new StreetKnownObject(item.Id, item.MaterialId, item.Kind, contour, item.Height,
                observedAt, currentUntil);
        }

        internal static Segment2[] RectangleContour(float x0, float y0, float x1, float y1)
        {
            Point2[] points = StreetGeometry.Rect(x0, y0, x1, y1);
            var contour = new List<Segment2>(points.Length);
            for (int i = 0; i < points.Length; i++)
                contour.Add(new Segment2(points[i], points[(i + 1) % points.Length]));
            return contour.ToArray();
        }
    }
}
