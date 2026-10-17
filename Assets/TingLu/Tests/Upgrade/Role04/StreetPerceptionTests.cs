using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using TingLu.Config;
using TingLu.Contracts;
using TingLu.Levels;
using TingLu.Perception;
using TingLu.Tests.Upgrade;
using TingLu.World;

namespace TingLu.Tests.Upgrade.Role04
{
    public sealed class StreetPerceptionTests
    {
        private FixtureClock clock;
        private StreetConfig config;
        private StreetPerception perception;

        [SetUp]
        public void SetUp()
        {
            clock = new FixtureClock { Now = 0 };
            config = new StreetConfig();
            perception = new StreetPerception(config, clock);
        }

        [Test]
        public void ProbeBuildsContinuousSectorAtFrozenOriginAndFacing()
        {
            clock.Now = 3;
            StreetWorldFrame world = Frame(3, new[] { Surface("road", "asphalt", -20, -20, 20, 20, 0) }, null);
            StreetProbeResult north = perception.Probe(Request("north", 3, new Point2(0, 0), new Point2(0, 1), 90, 5), world);
            StreetProbeResult east = perception.Probe(Request("east", 3, new Point2(10, 4), new Point2(1, 0), 90, 5), world);

            Assert.That(north.Coverage.ProbeId, Is.EqualTo("north"));
            Assert.That(north.Coverage.SampledAt, Is.EqualTo(3));
            Assert.That(north.Coverage.Cells.Count, Is.GreaterThan(80), "The fan is continuous geometry, not a gameplay grid.");
            Assert.That(IsCovered(north.Coverage, new Point2(0, 3)), Is.True);
            Assert.That(IsCovered(north.Coverage, Polar(new Point2(0, 0), new Point2(0, 1), 44.9f, 4)), Is.True);
            Assert.That(IsCovered(north.Coverage, new Point2(0, -1)), Is.False);
            Assert.That(IsCovered(north.Coverage, Polar(new Point2(0, 0), new Point2(0, 1), 45.1f, 4)), Is.False);
            Assert.That(IsCovered(north.Coverage, new Point2(0, 5.1f)), Is.False);
            Assert.That(IsCovered(east.Coverage, new Point2(13, 4)), Is.True);
            Assert.That(IsCovered(east.Coverage, new Point2(0, 3)), Is.False);

            float triangleArea = 0;
            foreach (ProbeCell cell in north.Coverage.Cells) triangleArea += Math.Abs(StreetGeometry.Area(cell.Polygon));
            float analyticArea = 0.5f * 5 * 5 * (float)Math.PI / 2f;
            Assert.That(triangleArea, Is.EqualTo(analyticArea).Within(0.01f));
        }

        [Test]
        public void ThinWallBlocksTheAreaBehindItWithoutClosingTheOpenSide()
        {
            clock.Now = 1;
            StreetObject thinWall = Object("thin-wall", StreetKind.Permanent,
                StreetGeometry.Rect(-0.005f, 2f, 0.005f, 2.01f), true, 2f);
            StreetWorldFrame world = Frame(1, null, new[] { thinWall });

            StreetProbeResult result = perception.Probe(Request("thin-wall-probe", 1,
                new Point2(0, 0), new Point2(0, 1), 90, 5), world);

            Assert.That(IsCovered(result.Coverage, new Point2(0, 1.5f)), Is.True);
            Assert.That(IsCovered(result.Coverage, new Point2(0, 3f)), Is.False,
                "A narrow opaque polygon must block the ray behind it even when it is thinner than a coarse ray step.");
            Assert.That(IsCovered(result.Coverage, new Point2(1, 3f)), Is.True,
                "An obstacle in one bearing must not shrink open bearings beside it.");
            Assert.That(result.Objects.Count, Is.EqualTo(1));
            Assert.That(result.Objects[0].Contour.Count, Is.GreaterThan(0));
            Assert.That(result.Coverage.OcclusionBoundary.Count, Is.GreaterThan(0));
        }

        [Test]
        public void AngularEventsPreserveARealNarrowGapBetweenOpaqueWalls()
        {
            clock.Now = 2;
            StreetObject left = Object("left-wall", StreetKind.Permanent,
                StreetGeometry.Rect(-0.2f, 2f, -0.01f, 2.01f), true, 2f);
            StreetObject right = Object("right-wall", StreetKind.Permanent,
                StreetGeometry.Rect(0.01f, 2f, 0.2f, 2.01f), true, 2f);
            StreetWorldFrame world = Frame(2, null, new[] { left, right });

            StreetProbeResult result = perception.Probe(Request("gap", 2,
                new Point2(0, 0), new Point2(0, 1), 80, 5), world);

            Assert.That(IsCovered(result.Coverage, new Point2(0, 3f)), Is.True,
                "The unoccluded opening must remain visible even though it is narrower than the arc sampling interval.");
            Assert.That(IsCovered(result.Coverage, new Point2(-0.03f, 3f)), Is.False);
            Assert.That(IsCovered(result.Coverage, new Point2(0.03f, 3f)), Is.False);
        }

        [Test]
        public void PartiallyCoveredWallEmitsOnlyItsVisibleContour()
        {
            clock.Now = 4;
            StreetObject wall = Object("long-wall", StreetKind.Permanent,
                StreetGeometry.Rect(-10f, 2f, 10f, 2.2f), true, 2f);
            StreetWorldFrame world = Frame(4, null, new[] { wall });

            StreetProbeResult result = perception.Probe(Request("partial-wall", 4,
                new Point2(0, 0), new Point2(0, 1), 40, 5), world);

            Assert.That(IsCovered(result.Coverage, new Point2(0, 1f)), Is.True);
            Assert.That(IsCovered(result.Coverage, new Point2(0, 3f)), Is.False);
            Assert.That(result.Objects.Count, Is.EqualTo(1));
            float visibleLength = ContourLength(result.Objects[0].Contour);
            Assert.That(visibleLength, Is.GreaterThan(1.2f));
            Assert.That(visibleLength, Is.LessThan(1.6f), "A small covered section must not unlock the full twenty-unit wall.");
            foreach (Segment2 segment in result.Objects[0].Contour)
                Assert.That(Math.Abs(segment.A.Y - 2f) + Math.Abs(segment.B.Y - 2f), Is.LessThan(0.001f),
                    "The rear wall edge is beyond the occlusion boundary.");
        }

        [Test]
        public void HigherPrioritySurfaceOverlaysRoadWithoutOverlappingOutputAreas()
        {
            clock.Now = 5;
            StreetSurface road = Surface("road", "asphalt", -10, -10, 10, 10, 0);
            StreetSurface tactile = Surface("tactile", "tactile", -0.5f, 1f, 0.5f, 2f, 10);
            StreetWorldFrame world = Frame(5, new[] { road, tactile }, null);

            StreetProbeResult result = perception.Probe(Request("surface-priority", 5,
                new Point2(0, 0), new Point2(0, 1), 100, 7), world);

            Assert.That(MaterialArea(result.Surfaces, "tactile"), Is.EqualTo(1f).Within(0.001f));
            Assert.That(MaterialArea(result.Surfaces, "asphalt"), Is.GreaterThan(0f));
            foreach (StreetKnownSurface lower in result.Surfaces)
            {
                if (lower.MaterialId != "asphalt") continue;
                foreach (StreetKnownSurface higher in result.Surfaces)
                {
                    if (higher.MaterialId != "tactile") continue;
                    List<Point2> overlap = StreetGeometry.Intersect(lower.Polygon, higher.Polygon);
                    Assert.That(StreetGeometry.Valid(overlap) ? Math.Abs(StreetGeometry.Area(overlap)) : 0f,
                        Is.LessThan(0.0001f));
                }
            }

            float outputArea = 0;
            foreach (StreetKnownSurface surface in result.Surfaces) outputArea += Math.Abs(StreetGeometry.Area(surface.Polygon));
            float coverageArea = 0;
            foreach (ProbeCell cell in result.Coverage.Cells) coverageArea += Math.Abs(StreetGeometry.Area(cell.Polygon));
            Assert.That(outputArea, Is.EqualTo(coverageArea).Within(0.01f));
        }

        [Test]
        public void DisjointPrioritySurfacesDoNotAmplifyLowerSurfaceFragments()
        {
            clock.Now = 5.5;
            StreetSurface near = Surface("near-priority", "tactile", 1f, -0.4f, 2f, 0.4f, 10);
            StreetSurface far = Surface("far-priority", "asphalt", 3f, -1f, 4f, 1f, 0);
            StreetProbeResult result = perception.Probe(Request("disjoint-priority-fragments", 5.5,
                new Point2(0, 0), new Point2(1, 0), 90, 5), Frame(5.5, new[] { far, near }, null));

            int farFragments = result.Surfaces.Count(surface => surface.MaterialId == "asphalt");
            Assert.That(farFragments, Is.GreaterThan(0));
            Assert.That(farFragments, Is.LessThanOrEqualTo(result.Coverage.Cells.Count),
                "A convex lower-priority surface can produce at most one fragment per sector cell when the higher-priority surface is disjoint.");
        }

        [Test]
        public void DisjointPrioritySurfacesPreserveTheLowerSurfaceArea()
        {
            clock.Now = 5.6;
            StreetSurface near = Surface("near-priority-area", "tactile", 1f, -0.4f, 2f, 0.4f, 10);
            StreetSurface far = Surface("far-priority-area", "asphalt", 3f, -1f, 4f, 1f, 0);
            StreetProbeResult result = perception.Probe(Request("disjoint-priority-area", 5.6,
                new Point2(0, 0), new Point2(1, 0), 90, 5), Frame(5.6, new[] { far, near }, null));

            float expectedFarArea = 0f;
            foreach (ProbeCell cell in result.Coverage.Cells)
            {
                List<Point2> clipped = StreetGeometry.Intersect(cell.Polygon, far.Polygon);
                if (StreetGeometry.Valid(clipped)) expectedFarArea += Math.Abs(StreetGeometry.Area(clipped));
            }
            Assert.That(expectedFarArea, Is.GreaterThan(0f));
            Assert.That(MaterialArea(result.Surfaces, "asphalt"), Is.EqualTo(expectedFarArea).Within(0.001f),
                "A disjoint higher-priority surface must not mask any lower-priority area inside the same probe sector.");
        }

        [Test]
        public void SurfaceClippingCoalescesFanSeamsWithoutChangingCoverageOrArea()
        {
            var request = Request("coalesced-fan", 0, new Point2(), new Point2(0, 1), 100, 7);
            var road = Surface("road", "asphalt", -20, -20, 20, 20, 0);
            StreetProbeResult reference = perception.Probe(request, Frame(0, null, null));
            StreetProbeResult result = perception.Probe(request, Frame(0, new[] { road }, null));
            Assert.That(result.Coverage.Cells.Count, Is.EqualTo(reference.Coverage.Cells.Count));
            for (int i = 0; i < reference.Coverage.Cells.Count; i++)
                CollectionAssert.AreEqual(reference.Coverage.Cells[i].Polygon, result.Coverage.Cells[i].Polygon);
            Assert.That(result.Surfaces.Count, Is.LessThan(result.Coverage.Cells.Count / 4),
                "Clipping need not retain radial seams in a wholly visible convex fan.");
            double coverageArea = result.Coverage.Cells.Sum(cell => Math.Abs((double)StreetGeometry.Area(cell.Polygon)));
            Assert.That(MaterialArea(result.Surfaces, "asphalt"), Is.EqualTo(coverageArea).Within(0.001));
        }

        [Test]
        public void CoalescedSurfaceClippingPreservesNarrowGapAndBlockedBearings()
        {
            var left = Object("left-wall", StreetKind.Permanent, StreetGeometry.Rect(-0.2f, 2, -0.01f, 2.01f), true, 2);
            var right = Object("right-wall", StreetKind.Permanent, StreetGeometry.Rect(0.01f, 2, 0.2f, 2.01f), true, 2);
            var result = perception.Probe(Request("surface-gap", 0, new Point2(), new Point2(0, 1), 80, 5),
                Frame(0, new[] { Surface("road", "asphalt", -10, -10, 10, 10, 0) }, new[] { left, right }));
            Func<Point2, bool> visibleSurface = point => result.Surfaces.Any(surface => StreetGeometry.Contains(surface.Polygon, point));
            Assert.IsTrue(visibleSurface(new Point2(0, 3)));
            Assert.IsFalse(visibleSurface(new Point2(-0.03f, 3)));
            Assert.IsFalse(visibleSurface(new Point2(0.03f, 3)));
            double coverageArea = result.Coverage.Cells.Sum(cell => Math.Abs((double)StreetGeometry.Area(cell.Polygon)));
            Assert.That(MaterialArea(result.Surfaces, "asphalt"), Is.EqualTo(coverageArea).Within(0.001));
            foreach (StreetKnownSurface surface in result.Surfaces)
            {
                Point2 center = new Point2(surface.Polygon.Average(p => p.X), surface.Polygon.Average(p => p.Y));
                Assert.IsTrue(IsCovered(result.Coverage, center), "A merged surface must stay within the original visibility union.");
            }
        }

        [Test]
        public void CoalescedFanMatchesPerCellPriorityAndOrdinalTieReference()
        {
            StreetSurface[] surfaces = {
                Surface("z-base", "asphalt", -10, -10, 10, 10, 0),
                Surface("b-same-priority", "brick", -2, 1, 2, 6, 10),
                Surface("a-wins-tie", "tactile", -0.5f, 1, 0.5f, 6, 10),
                Surface("highest", "ramp", -3, 4, 3, 8, 20)
            };
            StreetProbeResult result = perception.Probe(Request("fan-priority", 0, new Point2(), new Point2(0, 1), 100, 7),
                Frame(0, surfaces, null));
            var expected = new Dictionary<string, double>();
            StreetSurface[] ordered = surfaces.OrderByDescending(s => s.Priority).ThenBy(s => s.Id, StringComparer.Ordinal).ToArray();
            foreach (ProbeCell cell in result.Coverage.Cells)
            {
                var prior = new List<StreetSurface>();
                foreach (StreetSurface surface in ordered)
                {
                    var pieces = new List<List<Point2>> { StreetGeometry.Intersect(cell.Polygon, surface.Polygon) };
                    foreach (StreetSurface mask in prior)
                    {
                        var remaining = new List<List<Point2>>();
                        foreach (List<Point2> piece in pieces)
                            if (StreetGeometry.Valid(piece)) remaining.AddRange(StreetGeometry.Subtract(piece, mask.Polygon));
                        pieces = remaining;
                    }
                    if (!expected.ContainsKey(surface.MaterialId)) expected.Add(surface.MaterialId, 0);
                    foreach (List<Point2> piece in pieces)
                        if (StreetGeometry.Valid(piece)) expected[surface.MaterialId] += Math.Abs((double)StreetGeometry.Area(piece));
                    prior.Add(surface);
                }
            }
            foreach (var pair in expected)
                Assert.That(MaterialArea(result.Surfaces, pair.Key), Is.EqualTo(pair.Value).Within(0.002), pair.Key);
            Assert.IsTrue(result.Surfaces.Any(s => s.MaterialId == "tactile" && StreetGeometry.Contains(s.Polygon, new Point2(0, 2))));
            Assert.IsFalse(result.Surfaces.Any(s => s.MaterialId == "brick" && StreetGeometry.Contains(s.Polygon, new Point2(0, 2))));
        }

        [Test]
        public void SurfacePreparationRefreshesWhenSameSizedSnapshotReplacesItsSurfaces()
        {
            StreetSurface first = Surface("road", "asphalt", -10, -10, 10, 10, 0);
            var request = Request("cache-first", 0, new Point2(), new Point2(0, 1), 90, 5);
            StreetProbeResult before = perception.Probe(request, Frame(0, new[] { first }, null));
            Assert.That(MaterialArea(before.Surfaces, "asphalt"), Is.GreaterThan(0));
            StreetSurface replacement = Surface("road", "brick", -10, -10, 10, 10, 0);
            StreetProbeResult after = perception.Probe(Request("cache-replaced", 0,
                new Point2(), new Point2(0, 1), 90, 5), Frame(0, new[] { replacement }, null));
            Assert.That(MaterialArea(after.Surfaces, "asphalt"), Is.Zero);
            Assert.That(MaterialArea(after.Surfaces, "brick"), Is.EqualTo(MaterialArea(before.Surfaces, "asphalt")).Within(0.001));
        }

        [Test]
        public void ContactConfirmsOnlyLocalObjectContourAndDoesNotRevealGround()
        {
            clock.Now = 6;
            StreetSurface road = Surface("road", "asphalt", -10, -10, 10, 10, 0);
            StreetObject box = Object("box", StreetKind.Temporary,
                StreetGeometry.Rect(-1f, 1f, 1f, 1.2f), false, 0.5f);
            StreetWorldFrame world = Frame(6, new[] { road }, new[] { box });

            StreetProbeResult result = perception.Contact(new StreetContact("box", new Point2(0f, 1f), false), world);

            Assert.That(result.Coverage.Cells.Count, Is.Zero, "Contact cannot prove nearby empty space.");
            Assert.That(result.Surfaces.Count, Is.Zero, "Body contact does not reveal ground materials.");
            Assert.That(result.Objects.Count, Is.EqualTo(1));
            Assert.That(result.Objects[0].Kind, Is.EqualTo(StreetKind.Temporary));
            Assert.That(result.Objects[0].Contour.Count, Is.GreaterThan(0));
            foreach (Segment2 segment in result.Objects[0].Contour)
            {
                Assert.That(StreetGeometry.Distance(segment.A, new Point2(0f, 1f)),
                    Is.LessThanOrEqualTo(config.ContactRadius + 0.0001f));
                Assert.That(StreetGeometry.Distance(segment.B, new Point2(0f, 1f)),
                    Is.LessThanOrEqualTo(config.ContactRadius + 0.0001f));
            }
        }

        [Test]
        public void VehicleBodyContactDoesNotConfirmGroundOrWholeVehicle()
        {
            clock.Now = 7;
            StreetSurface road = Surface("road", "asphalt", -10, -10, 10, 10, 0);
            StreetObject vehicle = new StreetObject("car", StreetKind.Active, new Point2(0, 0),
                StreetGeometry.Rect(-1f, -1f, 1f, 1f), "asphalt", true, true, true,
                0, 1, "street.car", true, PerceptionSemantic.Threat);
            StreetWorldFrame world = Frame(7, new[] { road }, new[] { vehicle });

            StreetProbeResult result = perception.Contact(new StreetContact("car", new Point2(0, 0), true), world);

            Assert.That(result.Coverage.Cells.Count, Is.Zero);
            Assert.That(result.Surfaces.Count, Is.Zero);
            Assert.That(result.Objects.Count, Is.Zero);
        }

        [Test]
        public void ProbeRejectsAWorldFrameFromAnotherSampleTime()
        {
            clock.Now = 8;
            StreetProbeRequest request = Request("same-frame", 8, new Point2(0, 0), new Point2(0, 1), 90, 5);

            Assert.Throws<ArgumentException>(() => perception.Probe(request, Frame(8.01, null, null)));
        }

        [Test]
        public void AcousticProjectionFiltersInvalidCandidatesWithoutAddingGeometryOrKnowledge()
        {
            clock.Now = 10;
            config.SoundSeconds = 0.4f;
            config.AcousticRadius = 1f;
            perception = new StreetPerception(config, clock);
            var candidates = new AcousticQueryResult[]
            {
                Sound("heard-bird", null, true, 3f, 0.01f, 0.1f, 9.8, new Point2(3f, 4f), PerceptionSemantic.Unknown),
                Sound("inactive", "inactive", false, 1f, 1f, 0f, 9.9, new Point2(1f, 0f), PerceptionSemantic.Person),
                Sound("inaudible", "inaudible", true, 1f, 0f, 0f, 9.9, new Point2(1f, 0f), PerceptionSemantic.Person),
                Sound("occluded", "occluded", true, 1f, 1f, 1f, 9.9, new Point2(1f, 0f), PerceptionSemantic.Person),
                Sound("stale", "stale", true, 1f, 1f, 0f, 9.5, new Point2(1f, 0f), PerceptionSemantic.Person),
                Sound("future", "future", true, 1f, 1f, 0f, 10.1, new Point2(1f, 0f), PerceptionSemantic.Person),
                Sound("far", "far", true, 11f, 1f, 0f, 9.9, new Point2(11f, 0f), PerceptionSemantic.Person),
                null
            };

            PerceptionFrame frame = perception.ProjectAcoustics(candidates, 10);

            Assert.That(frame.SampledAt, Is.EqualTo(10));
            Assert.That(frame.Observations.Count, Is.EqualTo(3),
                "04 preserves 06-approved candidates even when their reported distance or occlusion would fail a second audibility test.");
            Observation observation = frame.Observations.Single(item => item.ObjectId == "heard-bird");
            Assert.That(observation.ObjectId, Is.EqualTo("heard-bird"));
            Assert.That(observation.Source, Is.EqualTo(ObservationSource.PassiveSound));
            Assert.That(observation.Kind, Is.EqualTo(InformationKind.SoundLocation));
            Assert.That(observation.MemoryPolicy, Is.EqualTo(MemoryPolicy.Never));
            Assert.That(observation.ObservedPosition, Is.EqualTo(new Point2(3f, 4f)), "Position is copied from 06's frozen candidate.");
            Assert.That(observation.Semantic, Is.EqualTo(PerceptionSemantic.Unknown), "04 does not infer a category or semantic.");
            Assert.That(observation.ConfirmedContour.Count, Is.Zero, "Sound never creates a geometric contour.");
            Assert.That(observation.ObservedAt, Is.EqualTo(9.8));
            Assert.That(observation.ExpiresAt, Is.EqualTo(10.2).Within(0.00001));
            Assert.That(frame.Observations.Any(item => item.ObjectId == "occluded"), Is.True);
            Assert.That(frame.Observations.Any(item => item.ObjectId == "far"), Is.True);
        }

        [Test]
        public void ClearPublishesAnObservableEmptyFrame()
        {
            clock.Now = 2;
            perception.ProjectAcoustics(new[]
            {
                Sound("source", "object", true, 1f, 1f, 0f, 2, new Point2(0, 2), PerceptionSemantic.Ordinary)
            }, 2);
            long priorFrameId = perception.Current.FrameId;

            clock.Now = 3;
            perception.Clear();

            Assert.That(perception.Current.FrameId, Is.GreaterThan(priorFrameId));
            Assert.That(perception.Current.SampledAt, Is.EqualTo(3));
            Assert.That(perception.Current.Observations.Count, Is.Zero);
        }

        [Test]
        public void PausedProjectionAndProbeReturnNoNewObservations()
        {
            clock.Now = 11;
            clock.Paused = true;
            var sound = Sound("sound", "sound", true, 1f, 1f, 0f, 11, new Point2(1f, 1f), PerceptionSemantic.Person);

            PerceptionFrame frame = perception.ProjectAcoustics(new[] { sound }, 11);
            StreetProbeResult probe = perception.Probe(Request("paused", 11, new Point2(0, 0), new Point2(0, 1), 90, 5),
                Frame(10, null, null));

            Assert.That(frame.Observations.Count, Is.Zero);
            Assert.That(probe.Coverage.Cells.Count, Is.Zero);
            Assert.That(probe.Surfaces.Count, Is.Zero);
        }

        [Test]
        public void SharedRoadFixtureCanBeProbedAndItsActiveObjectHasNoGeometryMemory()
        {
            clock.Now = 0;
            StreetWorldFrame world = StreetFixtures.Road(0);

            StreetProbeResult result = perception.Probe(Request("shared-road", 0,
                new Point2(0, 0), new Point2(0, 1), 100, 7), world);

            Assert.That(result.Surfaces.Count, Is.GreaterThan(0));
            Assert.That(result.Objects.Any(item => item.ObjectId == "box"), Is.True);
            Assert.That(result.Objects.Any(item => item.ObjectId == "wall"), Is.True);
            Assert.That(result.Objects.Any(item => item.ObjectId == "bird"), Is.False);
        }

        [Test]
        public void RealCrossroadsLampIsObservedOnlyWhenItsHousingIsInsideUnoccludedProbeCoverage()
        {
            StreetLevel level = StreetLevelFactory.Create();
            var world = new StreetWorld(level.Surfaces, level.Objects, level.Routes, config);
            StreetWorldFrame frame = world.Capture();
            Point2 origin = new Point2(-9f, 5.3f);
            Point2 east = new Point2(1f, 0f);

            StreetProbeResult facingAway = perception.Probe(Request("real-lamp-facing-away", 0,
                origin, new Point2(-1f, 0f), 100, 7), frame);
            Assert.That(facingAway.Signals, Is.Empty,
                "The known map lamp must not leak its phase when it is behind the player.");
            Assert.That(facingAway.Objects.Any(item => item.ObjectId == "street.signal.ns"), Is.False,
                "An unprobed lamp housing must remain unknown.");

            StreetProbeResult visible = perception.Probe(Request("real-lamp-visible", 0,
                origin, east, 100, 7), frame);
            Assert.That(visible.Objects.Any(item => item.ObjectId == "street.signal.ns"), Is.True,
                "The real north-side signal housing lies inside this probe sector.");
            Assert.That(visible.Signals.Count, Is.EqualTo(1));
            Assert.That(visible.Signals[0].ObjectId, Is.EqualTo("street.signal.ns"));
            Assert.That(visible.Signals[0].IsGreen, Is.True);
            Assert.That(visible.Signals[0].Position, Is.EqualTo(new Point2(-6.35f, 5.3f)));
            Assert.That(visible.Signals[0].ExpiresAt, Is.EqualTo(0.8).Within(0.00001),
                "The lamp observation is short-lived even though the housing remains in permanent memory.");

            var blockedObjects = new List<StreetObject>(level.Objects)
            {
                new StreetObject("test.occluder", StreetKind.Permanent, new Point2(-7.4f, 5.3f),
                    StreetGeometry.Rect(-7.65f, 4.7f, -7.25f, 5.9f), "brick",
                    true, false, true, 0, 2f)
            };
            var blockedWorld = new StreetWorld(level.Surfaces, blockedObjects, level.Routes, config);
            StreetProbeResult blocked = perception.Probe(Request("real-lamp-occluded", 0,
                origin, east, 100, 7), blockedWorld.Capture());
            Assert.That(blocked.Signals, Is.Empty,
                "A wall between the player and the real map lamp must not reveal its red/green phase.");
            Assert.That(blocked.Objects.Any(item => item.ObjectId == "street.signal.ns"), Is.False,
                "Occlusion must hide the lamp housing as well as its phase.");
        }

        [Test]
        public void RealCrossroadsProbeReportsCurrentRedAndGreenAtSignalPhaseChanges()
        {
            StreetLevel level = StreetLevelFactory.Create();
            var world = new StreetWorld(level.Surfaces, level.Objects, level.Routes, config);

            StreetProbeResult northGreen = ProbeRealLamp(world, "street.signal.ns", 0,
                new Point2(-9f, 5.3f));
            Assert.That(northGreen.Signals.Count, Is.EqualTo(1));
            Assert.That(northGreen.Signals[0].IsGreen, Is.True);

            StreetProbeResult eastRed = ProbeRealLamp(world, "street.signal.ew", 0,
                new Point2(2.5f, -6.35f));
            Assert.That(eastRed.Signals.Count, Is.EqualTo(1));
            Assert.That(eastRed.Signals[0].IsGreen, Is.False,
                "The east-west lamp is red while north-south has green.");

            world.Tick(14, false);
            StreetProbeResult northRed = ProbeRealLamp(world, "street.signal.ns", 14,
                new Point2(-9f, 5.3f));
            StreetProbeResult eastClearance = ProbeRealLamp(world, "street.signal.ew", 14,
                new Point2(2.5f, -6.35f));
            Assert.That(northRed.Signals.Count, Is.EqualTo(1));
            Assert.That(northRed.Signals[0].IsGreen, Is.False,
                "The north-south lamp is red during the clearance interval.");
            Assert.That(eastClearance.Signals.Count, Is.EqualTo(1));
            Assert.That(eastClearance.Signals[0].IsGreen, Is.False,
                "The east-west lamp remains red during the clearance interval.");

            world.Tick(3, false);
            StreetProbeResult eastGreen = ProbeRealLamp(world, "street.signal.ew", 17,
                new Point2(2.5f, -6.35f));
            Assert.That(eastGreen.Signals.Count, Is.EqualTo(1));
            Assert.That(eastGreen.Signals[0].IsGreen, Is.True,
                "The east-west map lamp is green after clearance.");
        }

        private StreetProbeResult ProbeRealLamp(StreetWorld world, string signalId, double time, Point2 origin)
        {
            clock.Now = time;
            return perception.Probe(Request("real-" + signalId + "-" + time, time, origin,
                new Point2(1f, 0f), 100, 7), world.Capture());
        }

        private StreetProbeRequest Request(string id, double time, Point2 origin, Point2 facing, float angle, float radius)
        {
            return new StreetProbeRequest(id, time, origin, facing, angle, radius);
        }

        [Test]
        public void ProbeRetainsOriginalSurfaceTextureAxisAcrossDifferentClippingShapes()
        {
            var source = new StreetSurface("tactile.vertical", "tactile", StreetGeometry.Rect(-0.4f, -10, 0.4f, 10));
            var world = Frame(0, new[] { source }, null);
            var first = perception.Probe(Request("first-axis", 0, new Point2(0, -4), new Point2(0, 1), 100, 7), world);
            var second = perception.Probe(Request("second-axis", 0, new Point2(-2, 0), new Point2(1, 0), 100, 7), world);
            Assert.That(first.Surfaces.Count, Is.GreaterThan(0));
            Assert.That(second.Surfaces.Count, Is.GreaterThan(0));
            foreach (var patch in first.Surfaces.Concat(second.Surfaces))
            {
                Assert.That(patch.TextureAxis, Is.EqualTo(new Point2(0, 1)));
                Assert.That(patch.Polygon.All(p => p.X >= -0.40001f && p.X <= 0.40001f), Is.True);
            }
        }

        [Test]
        public void SurfaceOrientationIsStableAcrossWindingAndKeepsCrosswalkBarsTransverse()
        {
            var polygon = StreetGeometry.Rect(-1, -10, 1, 10);
            var vertical = new StreetSurface("vertical", "tactile", polygon);
            var reverse = new StreetSurface("reverse", "tactile", polygon.Reverse());
            Assert.That(vertical.TextureAxis, Is.EqualTo(new Point2(0, 1)));
            Assert.That(reverse.TextureAxis, Is.EqualTo(vertical.TextureAxis));
            Assert.That(new StreetSurface("horizontal", "tactile", StreetGeometry.Rect(-10, -1, 10, 1)).TextureAxis,
                Is.EqualTo(new Point2(1, 0)));
            Assert.That(new StreetSurface("slabs", "sidewalk", polygon).TextureAxis, Is.EqualTo(new Point2(1, 0)));
            Assert.That(new StreetSurface("crossing", "crosswalk", polygon).TextureAxis, Is.EqualTo(new Point2(1, 0)));
            Point2 axis = StreetTextureOrientation.Canonical(new Point2(-3, -7));
            for (int i = 0; i < 100; i++)
                Assert.That(StreetTextureOrientation.Canonical(axis), Is.EqualTo(axis));
            Assert.Throws<ArgumentException>(() => new StreetKnownSurface("sidewalk", polygon, 0, 1, new Point2()));
            Assert.Throws<ArgumentException>(() => new StreetKnownSurface("sidewalk", polygon, 0, 1, new Point2(float.NaN, 1)));
        }

        private static StreetWorldFrame Frame(double time, IEnumerable<StreetSurface> surfaces, IEnumerable<StreetObject> objects)
        {
            return new StreetWorldFrame(time,
                surfaces ?? new StreetSurface[0],
                objects ?? new StreetObject[0]);
        }

        private static StreetSurface Surface(string id, string material, float x0, float y0, float x1, float y1, int priority)
        {
            return new StreetSurface(id, material, StreetGeometry.Rect(x0, y0, x1, y1), priority);
        }

        private static StreetObject Object(string id, StreetKind kind, Point2[] polygon, bool blocksProbe, float height)
        {
            return new StreetObject(id, kind, new Point2(0, 0), polygon, "brick",
                solid: blocksProbe, blocksProbe: blocksProbe, height: height);
        }

        private static AcousticQueryResult Sound(string source, string objectId, bool active, float distance,
            float discernibility, float occlusion, double sampledAt, Point2 position, PerceptionSemantic semantic)
        {
            return new AcousticQueryResult(source, objectId, active, position, new Point2(0, 1), distance,
                discernibility, occlusion, "test", sampledAt, semantic, ObjectCategory.Event, "brick");
        }

        private static Point2 Polar(Point2 origin, Point2 facing, float yawDegrees, float distance)
        {
            Point2 direction = StreetCoordinates.Turn(facing, yawDegrees);
            return new Point2(origin.X + direction.X * distance, origin.Y + direction.Y * distance);
        }

        private static bool IsCovered(ProbeCoverage coverage, Point2 point)
        {
            foreach (ProbeCell cell in coverage.Cells)
                if (StreetGeometry.Contains(cell.Polygon, point)) return true;
            return false;
        }

        private static float MaterialArea(IReadOnlyList<StreetKnownSurface> surfaces, string material)
        {
            float area = 0;
            foreach (StreetKnownSurface surface in surfaces)
                if (surface.MaterialId == material) area += Math.Abs(StreetGeometry.Area(surface.Polygon));
            return area;
        }

        private static float ContourLength(IReadOnlyList<Segment2> contour)
        {
            float length = 0;
            foreach (Segment2 segment in contour) length += StreetGeometry.Distance(segment.A, segment.B);
            return length;
        }
    }
}
