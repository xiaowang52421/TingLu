using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using TingLu.Levels;
using TingLu.Levels.Editor;

namespace TingLu.Tests.StreetEditorV2
{
    /// <summary>
    /// Contract tests for the schema-2 street editor data path.  These tests stay
    /// separate from the legacy M0 editor tests so a v2 contract change cannot be
    /// hidden by a passing old-document fixture.
    /// </summary>
    public sealed class StreetLevelEditorV2Tests
    {
        private string temporaryRoot;

        [SetUp]
        public void SetUp()
        {
            temporaryRoot = Path.Combine(Path.GetTempPath(), "tinglu-street-editor-v2-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(temporaryRoot);
        }

        [TearDown]
        public void TearDown()
        {
            if (temporaryRoot != null && Directory.Exists(temporaryRoot))
                Directory.Delete(temporaryRoot, true);
        }

        [Test]
        public void TemplateSchema2RoundTripsEveryStreetField()
        {
            StreetLevelDocument source = StreetLevelCodec.Template();
            StreetRouteData unsignaledRoute = source.routes.First(route => route.objectId == "rider.east.sidewalk");
            unsignaledRoute.stopLineDistance = 0;

            Assert.AreEqual(StreetLevelDocument.CurrentSchemaVersion, source.schemaVersion);
            Assert.IsNotEmpty(source.id);
            Assert.IsNotEmpty(source.surfaces);
            Assert.IsNotEmpty(source.objects);
            Assert.IsNotEmpty(source.routes);
            Assert.IsNotEmpty(source.checkpoints);
            Assert.IsEmpty(StreetLevelCodec.Validate(source), string.Join("; ", StreetLevelCodec.Validate(source)));
            StreetObjectData emptyActiveBird = source.objects.Single(item => item.id == "animal.bird.ne");
            Assert.IsEmpty(emptyActiveBird.polygon, "an active sound source can be audible without exposing a geometry contour");
            Assert.AreEqual(0f, emptyActiveBird.height, "sound-only objects need not have visible geometry height");
            Assert.AreEqual(0f, unsignaledRoute.stopLineDistance.Value, "an explicit zero stop-line offset is a valid boundary value");

            StreetLevelDocument copy = StreetLevelCodec.Decode(StreetLevelCodec.Encode(source));
            Assert.AreEqual(source.schemaVersion, copy.schemaVersion);
            Assert.AreEqual(source.id, copy.id);
            Assert.AreEqual(source.title, copy.title);
            Assert.AreEqual(source.goalText, copy.goalText);
            Assert.AreEqual(source.spawnX, copy.spawnX);
            Assert.AreEqual(source.spawnY, copy.spawnY);
            Assert.AreEqual(source.facingX, copy.facingX);
            Assert.AreEqual(source.facingY, copy.facingY);
            Assert.AreEqual(source.goalX, copy.goalX);
            Assert.AreEqual(source.goalY, copy.goalY);
            Assert.AreEqual(source.goalRadius, copy.goalRadius);
            Assert.AreEqual(source.surfaces.Count, copy.surfaces.Count);
            Assert.AreEqual(source.objects.Count, copy.objects.Count);
            Assert.AreEqual(source.routes.Count, copy.routes.Count);
            Assert.AreEqual(source.checkpoints.Count, copy.checkpoints.Count);

            for (int i = 0; i < source.surfaces.Count; i++)
            {
                StreetSurfaceData expected = source.surfaces[i];
                StreetSurfaceData actual = copy.surfaces[i];
                Assert.AreEqual(expected.id, actual.id);
                Assert.AreEqual(expected.materialId, actual.materialId);
                Assert.AreEqual(expected.textureAxis.x, actual.textureAxis.x);
                Assert.AreEqual(expected.textureAxis.y, actual.textureAxis.y);
                Assert.AreEqual(expected.heightBase, actual.heightBase);
                Assert.AreEqual(expected.heightSlopeX, actual.heightSlopeX);
                Assert.AreEqual(expected.heightSlopeY, actual.heightSlopeY);
                Assert.AreEqual(expected.priority, actual.priority);
                AssertPointsEqual(expected.polygon, actual.polygon);
            }

            for (int i = 0; i < source.objects.Count; i++)
            {
                StreetObjectData expected = source.objects[i];
                StreetObjectData actual = copy.objects[i];
                Assert.AreEqual(expected.id, actual.id);
                Assert.AreEqual(expected.kind, actual.kind);
                Assert.AreEqual(expected.materialId, actual.materialId);
                Assert.AreEqual(expected.solid, actual.solid);
                Assert.AreEqual(expected.vehicleHazard, actual.vehicleHazard);
                Assert.AreEqual(expected.blocksProbe, actual.blocksProbe);
                Assert.AreEqual(expected.acousticLoss, actual.acousticLoss);
                Assert.AreEqual(expected.height, actual.height);
                Assert.AreEqual(expected.soundId, actual.soundId);
                Assert.AreEqual(expected.soundActive, actual.soundActive);
                Assert.AreEqual(expected.semantic, actual.semantic);
                Assert.AreEqual(expected.contactDamage, actual.contactDamage);
                Assert.AreEqual(expected.contactStun, actual.contactStun);
                Assert.AreEqual(expected.position.x, actual.position.x);
                Assert.AreEqual(expected.position.y, actual.position.y);
                AssertPointsEqual(expected.polygon, actual.polygon);
            }

            for (int i = 0; i < source.routes.Count; i++)
            {
                StreetRouteData expected = source.routes[i];
                StreetRouteData actual = copy.routes[i];
                Assert.AreEqual(expected.objectId, actual.objectId);
                Assert.AreEqual(expected.speed, actual.speed);
                Assert.AreEqual(expected.signalAxis, actual.signalAxis);
                Assert.AreEqual(expected.offset, actual.offset);
                Assert.AreEqual(expected.loop, actual.loop);
                Assert.AreEqual(expected.stopLineDistance, actual.stopLineDistance);
                AssertPointsEqual(expected.points, actual.points);
            }

            for (int i = 0; i < source.checkpoints.Count; i++)
            {
                StreetCheckpointData expected = source.checkpoints[i];
                StreetCheckpointData actual = copy.checkpoints[i];
                Assert.AreEqual(expected.id, actual.id);
                Assert.AreEqual(expected.radius, actual.radius);
                Assert.AreEqual(expected.position.x, actual.position.x);
                Assert.AreEqual(expected.position.y, actual.position.y);
                Assert.AreEqual(expected.facing.x, actual.facing.x);
                Assert.AreEqual(expected.facing.y, actual.facing.y);
            }
        }

        [Test]
        public void TemplateHasUniqueStableIdsAndCloneDoesNotShareMutableReferences()
        {
            StreetLevelDocument source = StreetLevelCodec.Template();
            StreetLevelDocument anotherTemplate = StreetLevelCodec.Template();
            Assert.AreNotEqual(source.id, anotherTemplate.id, "each new level template must receive an independent level id");
            var ids = new HashSet<string>(StringComparer.Ordinal) { source.id };
            foreach (StreetSurfaceData item in source.surfaces) Assert.IsTrue(ids.Add(item.id), "duplicate surface id");
            foreach (StreetObjectData item in source.objects) Assert.IsTrue(ids.Add(item.id), "duplicate object id");
            foreach (StreetCheckpointData item in source.checkpoints) Assert.IsTrue(ids.Add(item.id), "duplicate checkpoint id");

            StreetLevelDocument clone = StreetLevelCodec.Clone(source);
            Assert.AreEqual(source.id, clone.id, "cloning an existing level must preserve its stable id");
            clone.id = "clone-id";
            clone.surfaces[0].polygon[0].x += 123;
            clone.surfaces[0].textureAxis.x = -clone.surfaces[0].textureAxis.x;
            clone.objects[0].position.x += 123;
            clone.objects[0].polygon[0].y += 123;
            clone.routes[0].points[0].x += 123;
            clone.checkpoints[0].position.y += 123;

            Assert.AreNotEqual(source.id, clone.id);
            Assert.AreNotEqual(source.surfaces[0].polygon[0].x, clone.surfaces[0].polygon[0].x);
            Assert.AreNotEqual(source.surfaces[0].textureAxis.x, clone.surfaces[0].textureAxis.x);
            Assert.AreNotEqual(source.objects[0].position.x, clone.objects[0].position.x);
            Assert.AreNotEqual(source.objects[0].polygon[0].y, clone.objects[0].polygon[0].y);
            Assert.AreNotEqual(source.routes[0].points[0].x, clone.routes[0].points[0].x);
            Assert.AreNotEqual(source.checkpoints[0].position.y, clone.checkpoints[0].position.y);
        }

        [Test]
        public void ValidatorRejectsMissingRequiredCollectionsGoalAndInvalidGeometry()
        {
            StreetLevelDocument missing = StreetLevelCodec.Template();
            missing.surfaces = null;
            List<string> missingErrors = StreetLevelCodec.Validate(missing);
            Assert.IsTrue(missingErrors.Any(error => error.Contains("不能为 null")), string.Join("; ", missingErrors));

            StreetLevelDocument noGoal = StreetLevelCodec.Template();
            noGoal.goalRadius = -1;
            List<string> goalErrors = StreetLevelCodec.Validate(noGoal);
            Assert.IsTrue(goalErrors.Any(error => error.Contains("goalRadius")), string.Join("; ", goalErrors));

            StreetLevelDocument badPolygon = StreetLevelCodec.Template();
            badPolygon.surfaces[0].polygon = new List<StreetPointData>
            {
                new StreetPointData(0, 0), new StreetPointData(3, 3),
                new StreetPointData(0, 3), new StreetPointData(2, 0),
                new StreetPointData(4, 0)
            };
            List<string> polygonErrors = StreetLevelCodec.Validate(badPolygon);
            Assert.IsTrue(polygonErrors.Any(error => error.Contains("自相交")), string.Join("; ", polygonErrors));

            StreetLevelDocument nonFinite = StreetLevelCodec.Template();
            nonFinite.surfaces[0].heightSlopeX = float.NaN;
            nonFinite.objects[0].height = float.PositiveInfinity;
            List<string> finiteErrors = StreetLevelCodec.Validate(nonFinite);
            Assert.IsTrue(finiteErrors.Any(error => error.Contains("有限")), string.Join("; ", finiteErrors));
        }

        [TestCase("schemaVersion")]
        [TestCase("spawnX")]
        [TestCase("spawnY")]
        [TestCase("goalX")]
        [TestCase("goalY")]
        [TestCase("goalRadius")]
        public void CodecRejectsDocumentsMissingRequiredSpawnOrGoalCoordinates(string fieldName)
        {
            string json = StreetLevelCodec.Encode(StreetLevelCodec.Template());
            string field = "\"" + fieldName + "\":";
            Assert.That(json, Does.Contain(field));
            string withoutField = json.Replace(field, "\"omitted_" + fieldName + "\":");

            Assert.Throws<InvalidDataException>(() => StreetLevelCodec.Decode(withoutField),
                "missing required coordinate must not silently become its CLR default: " + fieldName);
        }

        [Test]
        public void CodecRejectsLegacyAndFutureSchemasWithoutReturningData()
        {
            StreetLevelDocument legacy = StreetLevelCodec.Template();
            legacy.schemaVersion = StreetLevelDocument.CurrentSchemaVersion - 1;
            Assert.Throws<InvalidDataException>(() => StreetLevelCodec.Decode(StreetLevelCodec.Encode(legacy)));

            StreetLevelDocument future = StreetLevelCodec.Template();
            future.schemaVersion = StreetLevelDocument.CurrentSchemaVersion + 1;
            Assert.Throws<InvalidDataException>(() => StreetLevelCodec.Decode(StreetLevelCodec.Encode(future)));
        }

        [Test]
        public void FileStoreWritesAndLoadsAtomicallyAndPreservesExistingFileOnRejectedReplacement()
        {
            string path = Path.Combine(temporaryRoot, "街区 schema2.json");
            StreetLevelDocument source = StreetLevelCodec.Template();
            StreetLevelFileStore.Save(path, source, true);
            string original = File.ReadAllText(path);
            StreetLevelDocument loaded = StreetLevelFileStore.Load(path);
            Assert.AreEqual(source.id, loaded.id);
            Assert.IsTrue(File.Exists(path));

            StreetLevelDocument replacement = StreetLevelCodec.Clone(source);
            replacement.title = "第二份街区";
            StreetLevelFileStore.Save(path, replacement, true);
            Assert.AreEqual("第二份街区", StreetLevelFileStore.Load(path).title);
            Assert.IsTrue(File.Exists(path + ".bak"), "replacing a saved document must retain a backup");
            Assert.AreEqual(original, File.ReadAllText(path + ".bak"));
            Assert.IsEmpty(Directory.GetFiles(temporaryRoot, "*.tmp"), "successful replace must not leave a partial temporary file");

            string beforeRejected = File.ReadAllText(path);
            StreetLevelDocument future = StreetLevelCodec.Clone(replacement);
            future.schemaVersion = StreetLevelDocument.CurrentSchemaVersion + 1;
            Assert.Throws<InvalidDataException>(() => StreetLevelFileStore.Save(path, future));
            Assert.AreEqual(beforeRejected, File.ReadAllText(path));
            Assert.IsEmpty(Directory.GetFiles(temporaryRoot, "*.tmp"), "rejected replacement must not leave a temporary file");

            File.WriteAllText(path, "{ this is not a level }");
            string corrupt = File.ReadAllText(path);
            Assert.Throws<InvalidDataException>(() => StreetLevelFileStore.Save(path, source));
            Assert.AreEqual(corrupt, File.ReadAllText(path));
        }

        [Test]
        public void FileStoreAllowsSchemaValidDraftsButRequireValidRejectsUnplayableDataBeforeWriting()
        {
            StreetLevelDocument draft = StreetLevelCodec.Template();
            draft.objects.Clear();
            draft.routes.Clear();
            draft.checkpoints.Clear();
            draft.surfaces.Clear();
            string draftPath = Path.Combine(temporaryRoot, "draft.json");

            StreetLevelFileStore.Save(draftPath, draft, false);
            Assert.AreEqual(draft.id, StreetLevelFileStore.Load(draftPath).id);

            // A structurally valid draft may be saved for later editing, while an
            // empty playable surface set must be rejected by strict compilation.
            string invalidPath = Path.Combine(temporaryRoot, "invalid.json");
            Assert.Throws<InvalidDataException>(() => StreetLevelFileStore.Save(invalidPath, draft, true));
            Assert.IsFalse(File.Exists(invalidPath), "failed strict validation must not create a level file");
        }

        private static void AssertPointsEqual(List<StreetPointData> expected, List<StreetPointData> actual)
        {
            Assert.AreEqual(expected.Count, actual.Count);
            for (int i = 0; i < expected.Count; i++)
            {
                Assert.AreEqual(expected[i].x, actual[i].x);
                Assert.AreEqual(expected[i].y, actual[i].y);
            }
        }
    }
}
