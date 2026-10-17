using System;
using System.Collections.Generic;
using NUnit.Framework;
using TingLu.Contracts;
using TingLu.Memory;

namespace TingLu.Tests.Upgrade.Role05
{
    public sealed class StreetMemoryTests
    {
        [Test]
        public void PermanentSurfaceUnionStaysContinuousDisjointAndRefreshesOnlyObservedArea()
        {
            var memory = new StreetMemory();
            var first = StreetGeometry.Rect(0, 0, 2, 2);
            memory.Apply(Result(1, new[] { first },
                new[] { Surface("asphalt", first, 1, 2) }, null));

            var overlap = StreetGeometry.Rect(1, 0, 3, 2);
            memory.Apply(Result(3, new[] { overlap },
                new[] { Surface("asphalt", overlap, 3, 4) }, null));

            Assert.That(TotalSurfaceArea(memory.Current.Surfaces), Is.EqualTo(6f).Within(0.001f));
            AssertDisjoint(memory.Current.Surfaces);
            Assert.That(SurfaceAt(memory.Current, new Point2(0.5f, 1)).CurrentUntil, Is.EqualTo(2));
            Assert.That(SurfaceAt(memory.Current, new Point2(2.5f, 1)).CurrentUntil, Is.EqualTo(4));

            memory.Apply(Result(5, new[] { overlap },
                new[] { Surface("asphalt", overlap, 5, 6) }, null));
            Assert.That(TotalSurfaceArea(memory.Current.Surfaces), Is.EqualTo(6f).Within(0.001f));
            Assert.That(memory.Current.Surfaces.Count, Is.EqualTo(2), "Repeating the same probe must not accumulate duplicate fragments.");
            AssertDisjoint(memory.Current.Surfaces);
            Assert.That(SurfaceAt(memory.Current, new Point2(2.5f, 1)).CurrentUntil, Is.EqualTo(6));
        }

        [Test]
        public void RepeatedNearSeamProbePreservesUnionThroughStaleImport()
        {
            var memory = new StreetMemory();
            var left = StreetGeometry.Rect(0, 0, 1, 2);
            var right = StreetGeometry.Rect(1.00001f, 0, 2, 2);
            double expectedArea = Math.Abs(StreetGeometry.Area(left)) + Math.Abs(StreetGeometry.Area(right));

            for (int i = 0; i < 4; i++)
            {
                double time = 1 + i;
                memory.Apply(Result(time, new[] { left, right }, new[]
                {
                    Surface("asphalt", left, time, time + 2),
                    Surface("asphalt", right, time, time + 2)
                }, null));
                Assert.That(TotalSurfaceArea(memory.Current.Surfaces), Is.EqualTo(expectedArea).Within(0.000001f),
                    "Near but separate boundaries must not be snapped together or have their gap filled.");
                Assert.That(memory.Current.Surfaces.Count, Is.EqualTo(2),
                    "A near seam is not a shared edge and must remain represented by two exact pieces.");
                AssertDisjoint(memory.Current.Surfaces);
            }

            StreetMemoryFrame saved = memory.Export();
            var restored = new StreetMemory();
            restored.Import(saved, true);
            AssertAllStale(restored.Current);
            Assert.That(TotalSurfaceArea(restored.Current.Surfaces), Is.EqualTo(expectedArea).Within(0.000001f));

            for (int i = 0; i < 4; i++)
            {
                double time = 5 + i;
                restored.Apply(Result(time, new[] { left, right }, new[]
                {
                    Surface("asphalt", left, time, time + 2),
                    Surface("asphalt", right, time, time + 2)
                }, null));
                Assert.That(TotalSurfaceArea(restored.Current.Surfaces), Is.EqualTo(expectedArea).Within(0.000001f),
                    "Repeated probes after restore must preserve the exact confirmed area.");
                Assert.That(restored.Current.Surfaces.Count, Is.EqualTo(2),
                    "Repeated probes after restore must not accumulate boundary fragments.");
                Assert.That(SurfaceAt(restored.Current, new Point2(0.5f, 1)).ObservedAt, Is.EqualTo(time));
                Assert.That(SurfaceAt(restored.Current, new Point2(1.5f, 1)).ObservedAt, Is.EqualTo(time));
            }
            AssertDisjoint(restored.Current.Surfaces);
        }

        [Test]
        public void AdjacentSameAgeSurfaceFragmentsMergeIntoOneConvexPiece()
        {
            var memory = new StreetMemory();
            var left = StreetGeometry.Rect(0, 0, 1, 1);
            var right = StreetGeometry.Rect(1, 0, 2, 1);
            var axis = new Point2(1, 1);

            memory.Apply(Result(2, new[] { StreetGeometry.Rect(0, 0, 2, 1) }, new[]
            {
                Surface("sidewalk", left, 2, 4, axis),
                Surface("sidewalk", right, 2, 4, new Point2(-3, -3))
            }, null));

            Assert.That(memory.Current.Surfaces.Count, Is.EqualTo(1));
            Assert.That(TotalSurfaceArea(memory.Current.Surfaces), Is.EqualTo(2f).Within(0.001f));
            Assert.That(StreetGeometry.Contains(memory.Current.Surfaces[0].Polygon, new Point2(1.5f, 0.5f)), Is.True);
            AssertAxis(memory.Current.Surfaces[0], new Point2(1, 1));
        }

        [Test]
        public void AdjacentSameMaterialSurfacesWithDifferentTextureAxesStaySeparate()
        {
            var memory = new StreetMemory();
            Point2[] left = StreetGeometry.Rect(0, 0, 1, 1);
            Point2[] right = StreetGeometry.Rect(1, 0, 2, 1);
            memory.Apply(Result(2, new[] { StreetGeometry.Rect(0, 0, 2, 1) }, new[]
            {
                Surface("tactile", left, 2, 4, new Point2(1, 0)),
                Surface("tactile", right, 2, 4, new Point2(0, 1))
            }, null));

            Assert.That(memory.Current.Surfaces.Count, Is.EqualTo(2),
                "Adjacent surfaces with different texture axes must not merge.");
            Assert.That(TotalSurfaceArea(memory.Current.Surfaces), Is.EqualTo(2f).Within(0.001f));
            AssertAxis(SurfaceAt(memory.Current, new Point2(0.5f, 0.5f)), new Point2(1, 0));
            AssertAxis(SurfaceAt(memory.Current, new Point2(1.5f, 0.5f)), new Point2(0, 1));
        }

        [Test]
        public void IncomingAdjacentBatchRefreshPreservesOldMetadataAndCanBeClippedAgain()
        {
            var memory = new StreetMemory();
            Point2[] oldArea = StreetGeometry.Rect(0, 0, 8, 8);
            var oldAxis = new Point2(1, 1);
            memory.Apply(Result(1, new[] { oldArea }, new[] { Surface("sidewalk", oldArea, 1, 9, oldAxis) }, null));

            var incoming = new List<StreetKnownSurface>();
            for (int y = 0; y < 8; y++)
                for (int x = 4; x < 12; x++)
                    incoming.Add(Surface("sidewalk", StreetGeometry.Rect(x, y, x + 1, y + 1), 3, 5));

            memory.Apply(Result(3, new[] { StreetGeometry.Rect(4, 0, 12, 8) }, incoming.ToArray(), null));

            float retainedArea = 0, refreshedArea = 0;
            foreach (StreetKnownSurface surface in memory.Current.Surfaces)
            {
                if (surface.ObservedAt == 1) retainedArea += Math.Abs(StreetGeometry.Area(surface.Polygon));
                if (surface.ObservedAt == 3) refreshedArea += Math.Abs(StreetGeometry.Area(surface.Polygon));
            }
            Assert.That(TotalSurfaceArea(memory.Current.Surfaces), Is.EqualTo(96f).Within(0.001f));
            Assert.That(retainedArea, Is.EqualTo(32f).Within(0.001f));
            Assert.That(refreshedArea, Is.EqualTo(64f).Within(0.001f));
            AssertDisjoint(memory.Current.Surfaces);
            StreetKnownSurface retained = SurfaceAt(memory.Current, new Point2(2, 4));
            StreetKnownSurface refreshed = SurfaceAt(memory.Current, new Point2(6, 4));
            Assert.That(retained.ObservedAt, Is.EqualTo(1));
            Assert.That(retained.CurrentUntil, Is.EqualTo(9));
            AssertAxis(retained, oldAxis);
            Assert.That(refreshed.ObservedAt, Is.EqualTo(3));
            Assert.That(refreshed.CurrentUntil, Is.EqualTo(5));
            StreetKnownSurface farCorner = SurfaceAt(memory.Current, new Point2(11.5f, 7.5f));
            Assert.That(farCorner.ObservedAt, Is.EqualTo(3), "The refreshed observation must retain its full extent.");

            Point2[] partialRefresh = StreetGeometry.Rect(6, 0, 7, 8);
            memory.Apply(Result(4, new[] { partialRefresh },
                new[] { Surface("sidewalk", partialRefresh, 4, 6) }, null));
            Assert.That(TotalSurfaceArea(memory.Current.Surfaces), Is.EqualTo(96f).Within(0.001f),
                "A later probe must be able to clip safely against the merged observation.");
            AssertDisjoint(memory.Current.Surfaces);
            Assert.That(SurfaceAt(memory.Current, new Point2(2, 4)).ObservedAt, Is.EqualTo(1));
            Assert.That(SurfaceAt(memory.Current, new Point2(5, 4)).ObservedAt, Is.EqualTo(3));
            Assert.That(SurfaceAt(memory.Current, new Point2(6.5f, 4)).ObservedAt, Is.EqualTo(4));
        }

        [Test]
        public void AdjacentSurfaceBatchMergesDeterministicallyWithoutLosingAreaOrMetadata()
        {
            var fragments = new List<StreetKnownSurface>();
            const int side = 8;
            for (int y = 0; y < side; y++)
                for (int x = 0; x < side; x++)
                    fragments.Add(Surface("sidewalk", StreetGeometry.Rect(x, y, x + 1, y + 1), 2, 5));

            StreetProbeResult result = Result(3, new[] { StreetGeometry.Rect(0, 0, side, side) },
                fragments.ToArray(), null);
            var first = new StreetMemory();
            var second = new StreetMemory();
            first.Apply(result);
            second.Apply(result);

            Assert.That(first.Current.Surfaces.Count, Is.GreaterThan(0));
            Assert.That(first.Current.Surfaces.Count, Is.LessThan(fragments.Count),
                "Adjacent fragments should be coalesced rather than retained as the original grid.");
            Assert.That(TotalSurfaceArea(first.Current.Surfaces), Is.EqualTo(side * side).Within(0.001f));
            AssertDisjoint(first.Current.Surfaces);
            for (int y = 0; y < side; y++)
                for (int x = 0; x < side; x++)
                    Assert.That(SurfaceAt(first.Current, new Point2(x + 0.5f, y + 0.5f)).MaterialId,
                        Is.EqualTo("sidewalk"), "Every input cell must remain covered after coalescing.");
            Assert.That(second.Current.Surfaces.Count, Is.EqualTo(first.Current.Surfaces.Count));
            for (int surfaceIndex = 0; surfaceIndex < first.Current.Surfaces.Count; surfaceIndex++)
            {
                StreetKnownSurface a = first.Current.Surfaces[surfaceIndex];
                StreetKnownSurface b = second.Current.Surfaces[surfaceIndex];
                Assert.That(a.ObservedAt, Is.EqualTo(2));
                Assert.That(a.CurrentUntil, Is.EqualTo(5));
                Assert.That(b.Polygon.Count, Is.EqualTo(a.Polygon.Count));
                for (int vertexIndex = 0; vertexIndex < a.Polygon.Count; vertexIndex++)
                    Assert.That(b.Polygon[vertexIndex], Is.EqualTo(a.Polygon[vertexIndex]),
                        "The same adjacent fragments must merge in a stable order.");
            }
        }

        [Test]
        public void MultipleSharedCollinearEdgesDoNotCreateASelfTouchingSurface()
        {
            var left = new[]
            {
                new Point2(0, 0), new Point2(1, 0), new Point2(1, 1),
                new Point2(1, 2), new Point2(0, 2)
            };
            var right = new[]
            {
                new Point2(1, 0), new Point2(2, 0), new Point2(2, 2),
                new Point2(1, 2), new Point2(1, 1)
            };
            var memory = new StreetMemory();
            memory.Apply(Result(2, new[] { StreetGeometry.Rect(0, 0, 2, 2) }, new[]
            {
                Surface("asphalt", left, 2, 5), Surface("asphalt", right, 2, 5)
            }, null));

            Assert.That(memory.Current.Surfaces.Count, Is.EqualTo(2),
                "A pair sharing multiple edge segments must stay split rather than form a self-touching outline.");
            Assert.That(TotalSurfaceArea(memory.Current.Surfaces), Is.EqualTo(4f).Within(0.001f));
            AssertDisjoint(memory.Current.Surfaces);
            foreach (StreetKnownSurface surface in memory.Current.Surfaces)
            {
                for (int i = 0; i < surface.Polygon.Count; i++)
                {
                    for (int j = i + 1; j < surface.Polygon.Count; j++)
                    {
                        if (j == i + 1 || (i == 0 && j == surface.Polygon.Count - 1)) continue;
                        Assert.That(StreetGeometry.Distance(surface.Polygon[i], surface.Polygon[j]),
                            Is.GreaterThan(StreetGeometry.Epsilon * 2f),
                            "A surface must not repeat a non-adjacent vertex.");
                    }
                }
            }
        }

        [Test]
        public void PermanentSurfaceKnowledgeIsNotDeletedByCoverageWithoutASurfaceObservation()
        {
            var memory = new StreetMemory();
            var area = StreetGeometry.Rect(0, 0, 2, 2);
            memory.Apply(Result(1, new[] { area }, new[] { Surface("sidewalk", area, 1, 3) }, null));

            memory.Apply(Result(2, new[] { StreetGeometry.Rect(0.5f, 0.5f, 1.5f, 1.5f) }, null, null));

            Assert.That(memory.Current.Surfaces.Count, Is.EqualTo(1));
            Assert.That(TotalSurfaceArea(memory.Current.Surfaces), Is.EqualTo(4f).Within(0.001f));
            Assert.That(SurfaceAt(memory.Current, new Point2(1, 1)).CurrentUntil, Is.EqualTo(3));
        }

        [Test]
        public void TemporaryContourRefreshSubtractsOnlyTheCoveredLocalDifference()
        {
            var memory = new StreetMemory();
            var whole = StreetGeometry.Rect(-1, -1, 5, 2);
            var box = RectObject("box", StreetKind.Temporary, 0, StreetGeometry.Rect(0, 0, 4, 1), 1, 10);
            memory.Apply(Result(1, new[] { whole }, null, new[] { box }));

            var strip = StreetGeometry.Rect(1.5f, -1, 2.5f, 2);
            memory.Apply(Result(2, new[] { strip }, null, null));

            StreetKnownObject remaining = FindObject(memory.Current, "box");
            Assert.That(ContourLength(remaining.Contour), Is.EqualTo(8f).Within(0.001f));
            Assert.That(remaining.Contour.Count, Is.EqualTo(6));
            foreach (Segment2 segment in remaining.Contour)
                Assert.That(StreetGeometry.Contains(strip, StreetGeometry.Lerp(segment.A, segment.B, 0.5f)), Is.False);
        }

        [Test]
        public void SameTemporaryIdAtANewPositionDoesNotDeleteTheOldPosition()
        {
            var memory = new StreetMemory();
            var oldObject = RectObject("bike", StreetKind.Temporary, 0,
                StreetGeometry.Rect(0, 0, 1, 1), 1, 10);
            memory.Apply(Result(1, new[] { StreetGeometry.Rect(-1, -1, 2, 2) }, null, new[] { oldObject }));

            var movedObject = RectObject("bike", StreetKind.Temporary, 10,
                StreetGeometry.Rect(0, 0, 1, 1), 2, 11);
            memory.Apply(Result(2, new[] { StreetGeometry.Rect(9, -1, 12, 2) }, null, new[] { movedObject }));

            Assert.That(memory.Current.Objects.Count, Is.EqualTo(2));
            Assert.That(HasContourNear(memory.Current, new Point2(0.5f, 0)), Is.True);
            Assert.That(HasContourNear(memory.Current, new Point2(10.5f, 0)), Is.True);

            memory.Apply(Result(3, new[] { StreetGeometry.Rect(-1, -1, 2, 2) }, null, null));
            Assert.That(memory.Current.Objects.Count, Is.EqualTo(1));
            Assert.That(HasContourNear(memory.Current, new Point2(0.5f, 0)), Is.False);
            Assert.That(HasContourNear(memory.Current, new Point2(10.5f, 0)), Is.True);
        }

        [Test]
        public void EmptyContactCoverageAddsLocalGeometryWithoutClearingOldMemory()
        {
            var memory = new StreetMemory();
            var original = RectObject("box", StreetKind.Temporary, 0,
                StreetGeometry.Rect(0, 0, 1, 1), 1, 10);
            memory.Apply(Result(1, new[] { StreetGeometry.Rect(-1, -1, 2, 2) }, null, new[] { original }));

            var contact = new StreetKnownObject("box", "cardboard", StreetKind.Temporary,
                new[] { new Segment2(new Point2(0, 0), new Point2(0.5f, 0)) }, 0.5f, 2, 11);
            memory.Apply(Result(2, null, null, new[] { contact }));

            Assert.That(memory.Current.Objects.Count, Is.EqualTo(2));
            Assert.That(ContourLength(memory.Current.Objects[0].Contour) + ContourLength(memory.Current.Objects[1].Contour),
                Is.EqualTo(4f).Within(0.001f));
            Assert.That(memory.Current.Objects[0].ObservedAt == 1 || memory.Current.Objects[1].ObservedAt == 1, Is.True);
            Assert.That(memory.Current.Objects[0].ObservedAt == 2 || memory.Current.Objects[1].ObservedAt == 2, Is.True);
        }

        [Test]
        public void PermanentContourSurvivesEmptyReprobeAndRefreshesOnlyWhenObservedAgain()
        {
            var memory = new StreetMemory();
            var wall = RectObject("wall", StreetKind.Permanent, 0,
                StreetGeometry.Rect(0, 0, 1, 1), 1, 3);
            memory.Apply(Result(1, new[] { StreetGeometry.Rect(-1, -1, 2, 2) }, null, new[] { wall }));

            memory.Apply(Result(2, new[] { StreetGeometry.Rect(-1, -1, 2, 2) }, null, null));
            Assert.That(memory.Current.Objects.Count, Is.EqualTo(1));
            Assert.That(ContourLength(memory.Current.Objects[0].Contour), Is.EqualTo(4f).Within(0.001f));
            Assert.That(memory.Current.Objects[0].CurrentUntil, Is.EqualTo(3));

            var observedWall = RectObject("wall", StreetKind.Permanent, 0,
                StreetGeometry.Rect(0, 0, 1, 1), 3, 5);
            memory.Apply(Result(3, new[] { StreetGeometry.Rect(-1, -1, 2, 2) }, null, new[] { observedWall }));
            Assert.That(memory.Current.Objects.Count, Is.EqualTo(1));
            Assert.That(ContourLength(memory.Current.Objects[0].Contour), Is.EqualTo(4f).Within(0.001f));
            Assert.That(memory.Current.Objects[0].CurrentUntil, Is.EqualTo(5));
        }

        [Test]
        public void MarkStaleAndStaleImportPreserveGeometryButZeroEveryCurrentUntil()
        {
            var memory = new StreetMemory();
            var area = StreetGeometry.Rect(0, 0, 2, 2);
            var textureAxis = new Point2(1, 0.5f);
            memory.Apply(Result(1, new[] { area }, new[] { Surface("asphalt", area, 1, 5, textureAxis) }, new[]
            {
                RectObject("wall", StreetKind.Permanent, 0, StreetGeometry.Rect(0, 0, 0.2f, 1), 1, 5),
                RectObject("box", StreetKind.Temporary, 0, StreetGeometry.Rect(1, 1, 1.2f, 1.2f), 1, 5)
            }));
            StreetMemoryFrame saved = memory.Export();
            AssertAxis(saved.Surfaces[0], textureAxis);

            memory.MarkStale();
            Assert.That(memory.Current.Surfaces.Count, Is.EqualTo(1));
            Assert.That(memory.Current.Objects.Count, Is.EqualTo(2));
            AssertAllStale(memory.Current);
            AssertAxis(memory.Current.Surfaces[0], textureAxis);

            var restored = new StreetMemory();
            restored.Import(saved, true);
            Assert.That(restored.Current.Surfaces.Count, Is.EqualTo(1));
            Assert.That(restored.Current.Objects.Count, Is.EqualTo(2));
            AssertAllStale(restored.Current);
            AssertAxis(restored.Current.Surfaces[0], textureAxis);

            restored.Import(saved, false);
            Assert.That(SurfaceAt(restored.Current, new Point2(1, 1)).CurrentUntil, Is.EqualTo(5));
            Assert.That(FindObject(restored.Current, "wall").CurrentUntil, Is.EqualTo(5));
            AssertAxis(restored.Current.Surfaces[0], textureAxis);
        }

        [Test]
        public void ExportedFrameIsIndependentAndActiveGeometryIsRejected()
        {
            var points = new List<Point2>(StreetGeometry.Rect(0, 0, 1, 1));
            var sourceSurface = new StreetKnownSurface("asphalt", points, 1, 4);
            var input = new List<StreetKnownSurface> { sourceSurface };
            var frame = new StreetMemoryFrame(input, new StreetKnownObject[0]);
            points[0] = new Point2(100, 100);
            input.Clear();

            var memory = new StreetMemory();
            memory.Import(frame, false);
            StreetMemoryFrame exported = memory.Export();
            memory.Apply(Result(2, new[] { StreetGeometry.Rect(2, 0, 3, 1) },
                new[] { Surface("asphalt", StreetGeometry.Rect(2, 0, 3, 1), 2, 5) }, null));

            Assert.That(exported.Surfaces.Count, Is.EqualTo(1));
            Assert.That(exported.Surfaces[0].Polygon[0].X, Is.Not.EqualTo(100));
            Assert.That(frame.Surfaces.Count, Is.EqualTo(1));
            Assert.Throws<ArgumentException>(() => new StreetKnownObject("bird", "brick", StreetKind.Active,
                new[] { new Segment2(new Point2(0, 0), new Point2(1, 0)) }, 1, 1, 2));
        }

        private static StreetProbeResult Result(double time, Point2[][] cellPolygons,
            StreetKnownSurface[] knownSurfaces, StreetKnownObject[] knownObjects)
        {
            var cells = new List<ProbeCell>();
            if (cellPolygons != null)
                foreach (Point2[] polygon in cellPolygons) cells.Add(new ProbeCell(polygon));
            var coverage = new ProbeCoverage("test:" + time, time, cells, new Segment2[0]);
            return new StreetProbeResult(coverage,
                knownSurfaces ?? new StreetKnownSurface[0], knownObjects ?? new StreetKnownObject[0]);
        }

        private static StreetKnownSurface Surface(string material, Point2[] polygon, double at, double until,
            Point2? textureAxis = null)
        { return new StreetKnownSurface(material, polygon, at, until, textureAxis); }

        private static void AssertAxis(StreetKnownSurface surface, Point2 expected)
        {
            Assert.That(surface.TextureAxis.X, Is.EqualTo(expected.X).Within(0.000001f));
            Assert.That(surface.TextureAxis.Y, Is.EqualTo(expected.Y).Within(0.000001f));
        }

        private static StreetKnownObject RectObject(string id, StreetKind kind, float offset,
            Point2[] rect, double at, double until)
        {
            var contour = new Segment2[rect.Length];
            for (int i = 0; i < rect.Length; i++)
            {
                Point2 a = rect[i], b = rect[(i + 1) % rect.Length];
                contour[i] = new Segment2(new Point2(a.X + offset, a.Y), new Point2(b.X + offset, b.Y));
            }
            return new StreetKnownObject(id, "cardboard", kind, contour, 0.5f, at, until);
        }

        private static float TotalSurfaceArea(IReadOnlyList<StreetKnownSurface> values)
        {
            float area = 0;
            foreach (StreetKnownSurface surface in values) area += Math.Abs(StreetGeometry.Area(surface.Polygon));
            return area;
        }

        private static void AssertDisjoint(IReadOnlyList<StreetKnownSurface> values)
        {
            for (int i = 0; i < values.Count; i++)
                for (int j = i + 1; j < values.Count; j++)
                    Assert.That(Math.Abs(StreetGeometry.Area(StreetGeometry.Intersect(values[i].Polygon, values[j].Polygon))),
                        Is.LessThan(0.0001f), "Known surface fragments must not overlap.");
        }

        private static StreetKnownSurface SurfaceAt(StreetMemoryFrame frame, Point2 point)
        {
            foreach (StreetKnownSurface surface in frame.Surfaces)
                if (StreetGeometry.Contains(surface.Polygon, point)) return surface;
            Assert.Fail("Expected a known surface at " + point.X + "," + point.Y);
            return null;
        }

        private static StreetKnownObject FindObject(StreetMemoryFrame frame, string id)
        {
            foreach (StreetKnownObject item in frame.Objects)
                if (StringComparer.Ordinal.Equals(item.ObjectId, id)) return item;
            Assert.Fail("Expected remembered object " + id);
            return null;
        }

        private static float ContourLength(IReadOnlyList<Segment2> contour)
        {
            float length = 0;
            foreach (Segment2 segment in contour) length += StreetGeometry.Distance(segment.A, segment.B);
            return length;
        }

        private static bool HasContourNear(StreetMemoryFrame frame, Point2 point)
        {
            foreach (StreetKnownObject item in frame.Objects)
                foreach (Segment2 segment in item.Contour)
                    if (StreetGeometry.Distance(StreetGeometry.Closest(point, segment), point) < 0.001f) return true;
            return false;
        }

        private static void AssertAllStale(StreetMemoryFrame frame)
        {
            foreach (StreetKnownSurface surface in frame.Surfaces)
                Assert.That(surface.CurrentUntil, Is.Zero);
            foreach (StreetKnownObject item in frame.Objects)
                Assert.That(item.CurrentUntil, Is.Zero);
        }
    }
}
