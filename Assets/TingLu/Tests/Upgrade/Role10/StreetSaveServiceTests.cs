using System;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Security.Cryptography;
using System.Text;
using NUnit.Framework;
using TingLu.Config;
using TingLu.Contracts;
using TingLu.Levels;
using TingLu.Memory;
using TingLu.Perception;
using TingLu.Persistence;
using TingLu.Tests.Upgrade;
using TingLu.World;

namespace TingLu.Tests.Upgrade.Role10
{
    public sealed class StreetSaveServiceTests
    {
        private string temporaryRoot;
        private string saveNamespace;
        private StreetSaveService saves;

        [SetUp]
        public void SetUp()
        {
            temporaryRoot = Path.Combine(Path.GetTempPath(), "TingLuRole10-" + Guid.NewGuid().ToString("N"));
            saveNamespace = "role10-tests";
            saves = new StreetSaveService(temporaryRoot, saveNamespace);
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(temporaryRoot)) Directory.Delete(temporaryRoot, true);
        }

        [Test]
        public void SaveLoadRoundTripsBothPlayerAndTrafficSnapshotsAndStalesMemory()
        {
            StreetSaveData expected = CreateData();

            Assert.AreEqual(SaveStatus.Success, saves.Save(expected));
            SaveResult<StreetSaveData> result = saves.Load(expected.LevelId);

            Assert.AreEqual(SaveStatus.Success, result.Status);
            Assert.IsFalse(result.FromBackup);
            AssertPlayer(expected.Player, result.Value.Player);
            AssertPlayer(expected.CheckpointPlayer, result.Value.CheckpointPlayer);
            AssertTraffic(expected.Traffic, result.Value.Traffic);
            AssertTraffic(expected.CheckpointTraffic, result.Value.CheckpointTraffic);
            Assert.AreEqual(expected.CheckpointId, result.Value.CheckpointId);
            Assert.AreEqual(expected.Collisions, result.Value.Collisions);
            Assert.AreEqual(expected.Generation, result.Value.Generation);
            CollectionAssert.AreEqual(expected.ActivatedCheckpoints, result.Value.ActivatedCheckpoints);
            Assert.AreEqual(expected.Finished, result.Value.Finished);

            Assert.AreEqual(expected.Memory.Surfaces.Count, result.Value.Memory.Surfaces.Count);
            StreetKnownSurface surface = result.Value.Memory.Surfaces[0];
            Assert.AreEqual("ramp", surface.MaterialId);
            Assert.AreEqual(expected.Memory.Surfaces[0].ObservedAt, surface.ObservedAt);
            Assert.AreEqual(0d, surface.CurrentUntil);
            Assert.AreEqual(expected.Memory.Surfaces[0].TextureAxis.X, surface.TextureAxis.X);
            Assert.AreEqual(expected.Memory.Surfaces[0].TextureAxis.Y, surface.TextureAxis.Y);
            Assert.AreEqual(expected.Memory.Surfaces[0].HeightBase, surface.HeightBase);
            Assert.AreEqual(expected.Memory.Surfaces[0].HeightSlopeX, surface.HeightSlopeX);
            Assert.AreEqual(expected.Memory.Surfaces[0].HeightSlopeY, surface.HeightSlopeY);
            CollectionAssert.AreEqual(expected.Memory.Surfaces[0].Polygon, surface.Polygon);

            Assert.AreEqual(expected.Memory.Objects.Count, result.Value.Memory.Objects.Count);
            for (int i = 0; i < expected.Memory.Objects.Count; i++)
            {
                StreetKnownObject source = expected.Memory.Objects[i];
                StreetKnownObject loaded = result.Value.Memory.Objects[i];
                Assert.AreEqual(source.ObjectId, loaded.ObjectId);
                Assert.AreEqual(source.MaterialId, loaded.MaterialId);
                Assert.AreEqual(source.Kind, loaded.Kind);
                Assert.AreEqual(source.Height, loaded.Height);
                Assert.AreEqual(source.ObservedAt, loaded.ObservedAt);
                Assert.AreEqual(0d, loaded.CurrentUntil);
                CollectionAssert.AreEqual(source.Contour, loaded.Contour);
            }
        }

        [Test]
        public void SchemaOnePayloadWithoutTextureAxisLoadsWithWorldXAxisDefault()
        {
            Assert.AreEqual(SaveStatus.Success, saves.Save(CreateData()));
            string primary = PrimaryPath();
            TestEnvelope envelope = ReadEnvelope(primary);
            string legacyPayload = RemoveTextureAxis(envelope.payload);
            envelope.payload = legacyPayload;
            envelope.checksum = Checksum(envelope, legacyPayload);
            WriteEnvelope(primary, envelope);

            SaveResult<StreetSaveData> result = saves.Load("crossroads");

            Assert.AreEqual(SaveStatus.Success, result.Status);
            Assert.AreEqual(1f, result.Value.Memory.Surfaces[0].TextureAxis.X);
            Assert.AreEqual(0f, result.Value.Memory.Surfaces[0].TextureAxis.Y);
            Assert.AreEqual(0d, result.Value.Memory.Surfaces[0].CurrentUntil,
                "the legacy surface remains stale after it is loaded");
        }

        [Test]
        public void SchemaOnePayloadWithoutOptionalHeightFieldsDefaultsToFlatGround()
        {
            Assert.AreEqual(SaveStatus.Success, saves.Save(CreateData()));
            string primary = PrimaryPath();
            TestEnvelope envelope = ReadEnvelope(primary);
            string legacyPayload = RemoveHeightFields(RemoveTextureAxis(envelope.payload));
            envelope.payload = legacyPayload;
            envelope.checksum = Checksum(envelope, legacyPayload);
            WriteEnvelope(primary, envelope);

            SaveResult<StreetSaveData> result = saves.Load("crossroads");

            Assert.AreEqual(1, envelope.schema);
            Assert.AreEqual("1.0.0", envelope.contract);
            Assert.AreEqual(SaveStatus.Success, result.Status);
            StreetKnownSurface surface = result.Value.Memory.Surfaces[0];
            Assert.AreEqual(1f, surface.TextureAxis.X);
            Assert.AreEqual(0f, surface.TextureAxis.Y);
            Assert.AreEqual(0f, surface.HeightBase);
            Assert.AreEqual(0f, surface.HeightSlopeX);
            Assert.AreEqual(0f, surface.HeightSlopeY);
            Assert.AreEqual(0d, surface.CurrentUntil,
                "an older schema-1 surface remains stale after it is loaded");
        }

        [Test]
        public void PerceptionMemorySaveLoadAndRestorePreserveRampHeightPlane()
        {
            StreetLevel level = StreetLevelFactory.Create();
            StreetSurface source = null;
            for (int i = 0; i < level.Surfaces.Count; i++)
                if (level.Surfaces[i].Id == "ramp.ns.north.west") source = level.Surfaces[i];
            Assert.IsNotNull(source);

            var clock = new FixtureClock { Now = 2 };
            var config = new StreetConfig();
            var perception = new StreetPerception(config, clock);
            var world = new StreetWorldFrame(2, new[] { source }, new StreetObject[0]);
            var request = new StreetProbeRequest("ramp-height", 2,
                new Point2(-3.7f, 5.2f), new Point2(-1, 0), 100, 2);
            StreetProbeResult probe = perception.Probe(request, world);
            Assert.IsTrue(probe.Surfaces.Count > 0, "The ramp must be observable from the road side.");
            StreetKnownSurface observed = KnownSurfaceAt(probe.Surfaces, new Point2(-4.1f, 5.2f));
            Assert.IsNotNull(observed, "The road-side ramp plane must be inside the legal probe wedge.");
            Assert.AreEqual(source.HeightBase, observed.HeightBase);
            Assert.AreEqual(source.HeightSlopeX, observed.HeightSlopeX);
            Assert.AreEqual(source.HeightSlopeY, observed.HeightSlopeY);
            Assert.AreEqual(0.0642857f, observed.HeightAt(new Point2(-4.1f, 5.2f)), 0.0001f);

            var memory = new StreetMemory();
            memory.Apply(probe);
            StreetKnownSurface remembered = KnownSurfaceAt(memory.Export().Surfaces, new Point2(-4.1f, 5.2f));
            Assert.IsNotNull(remembered);
            Assert.AreEqual(source.HeightAt(new Point2(-4.1f, 5.2f)),
                remembered.HeightAt(new Point2(-4.1f, 5.2f)), 0.0001f);

            PlayerState player = new PlayerState(PlayerMainState.Active, ActionMode.Walking,
                new Point2(-3.7f, 5.2f), new Point2(-1, 0), 100, 100, 0, 0, 0);
            StreetTrafficState traffic = new StreetTrafficState(2, new string[0], new double[0]);
            StreetSaveData data = new StreetSaveData("ramp.pipeline", "spawn", player, player,
                traffic, traffic, memory.Export(), 0, 0, new[] { "spawn" });
            Assert.AreEqual(SaveStatus.Success, saves.Save(data));
            SaveResult<StreetSaveData> loaded = saves.Load("ramp.pipeline");
            Assert.AreEqual(SaveStatus.Success, loaded.Status);

            var restored = new StreetMemory();
            restored.Import(loaded.Value.Memory, true);
            StreetKnownSurface restoredRamp = KnownSurfaceAt(restored.Export().Surfaces, new Point2(-4.1f, 5.2f));
            Assert.IsNotNull(restoredRamp);
            Assert.AreEqual(source.HeightBase, restoredRamp.HeightBase);
            Assert.AreEqual(source.HeightSlopeX, restoredRamp.HeightSlopeX);
            Assert.AreEqual(source.HeightSlopeY, restoredRamp.HeightSlopeY);
            Assert.AreEqual(source.HeightAt(new Point2(-4.1f, 5.2f)),
                restoredRamp.HeightAt(new Point2(-4.1f, 5.2f)), 0.0001f);
            Assert.AreEqual(0d, restoredRamp.CurrentUntil,
                "restored memory is stale while its height plane remains intact");
        }

        [Test]
        public void InvalidTextureAxisMakesSavePayloadCorrupt()
        {
            Assert.AreEqual(SaveStatus.Success, saves.Save(CreateData()));
            string primary = PrimaryPath();
            TestEnvelope envelope = ReadEnvelope(primary);
            string invalidPayload = ReplaceTextureAxis(envelope.payload, ",\"textureAxis\":{\"x\":0,\"y\":0}");
            envelope.payload = invalidPayload;
            envelope.checksum = Checksum(envelope, invalidPayload);
            WriteEnvelope(primary, envelope);

            SaveResult<StreetSaveData> result = saves.Load("crossroads");

            Assert.AreEqual(SaveStatus.Corrupt, result.Status);
            StringAssert.Contains("texture axis", result.Error.ToLowerInvariant());
        }

        [Test]
        public void CorruptPrimaryLoadsValidBackupWithoutChangingIt()
        {
            StreetSaveData older = CreateData(collisions: 2);
            StreetSaveData newer = CreateData(collisions: 3);
            Assert.AreEqual(SaveStatus.Success, saves.Save(older));
            Assert.AreEqual(SaveStatus.Success, saves.Save(newer));

            string primary = PrimaryPath();
            string backup = primary + ".bak";
            byte[] backupBefore = File.ReadAllBytes(backup);
            File.WriteAllText(primary, "{ damaged primary }");

            SaveResult<StreetSaveData> result = saves.Load(older.LevelId);

            Assert.AreEqual(SaveStatus.Success, result.Status);
            Assert.IsTrue(result.FromBackup);
            Assert.AreEqual(older.Collisions, result.Value.Collisions);
            CollectionAssert.AreEqual(backupBefore, File.ReadAllBytes(backup));
        }

        [Test]
        public void SaveQuarantinesDamagedPrimaryAndKeepsValidBackup()
        {
            StreetSaveData backupData = CreateData(collisions: 1);
            Assert.AreEqual(SaveStatus.Success, saves.Save(backupData));
            Assert.AreEqual(SaveStatus.Success, saves.Save(CreateData(collisions: 2)));

            string primary = PrimaryPath();
            string backup = primary + ".bak";
            byte[] backupBefore = File.ReadAllBytes(backup);
            File.WriteAllText(primary, "{ damaged primary }");
            File.WriteAllText(primary + ".corrupt", "prior quarantine");
            File.WriteAllText(primary + ".corrupt.1", "prior quarantine 1");

            Assert.AreEqual(SaveStatus.Success, saves.Save(CreateData(collisions: 4)));

            SaveResult<StreetSaveData> result = saves.Load(backupData.LevelId);
            Assert.AreEqual(SaveStatus.Success, result.Status);
            Assert.AreEqual(4, result.Value.Collisions);
            CollectionAssert.AreEqual(backupBefore, File.ReadAllBytes(backup));
            Assert.AreEqual("{ damaged primary }", File.ReadAllText(primary + ".corrupt.2"));
            Assert.AreEqual("prior quarantine", File.ReadAllText(primary + ".corrupt"));
            Assert.AreEqual("prior quarantine 1", File.ReadAllText(primary + ".corrupt.1"));
        }

        [Test]
        public void ChecksumDamageIsRejectedAndCannotBeOverwrittenWithoutGoodBackup()
        {
            Assert.AreEqual(SaveStatus.Success, saves.Save(CreateData()));
            string primary = PrimaryPath();
            TestEnvelope envelope = ReadEnvelope(primary);
            envelope.checksum = (envelope.checksum[0] == '0' ? "1" : "0") + envelope.checksum.Substring(1);
            WriteEnvelope(primary, envelope);
            byte[] damagedSave = File.ReadAllBytes(primary);

            SaveResult<StreetSaveData> result = saves.Load("crossroads");

            Assert.AreEqual(SaveStatus.Corrupt, result.Status);
            Assert.AreEqual(SaveStatus.Corrupt, saves.Save(CreateData(collisions: 99)));
            CollectionAssert.AreEqual(damagedSave, File.ReadAllBytes(primary));
        }

        [Test]
        public void MissingPrimaryWithOnlyCorruptBackupRefusesToCreateNewPrimary()
        {
            Assert.AreEqual(SaveStatus.Success, saves.Save(CreateData()));
            string primary = PrimaryPath();
            File.Delete(primary);
            string backup = primary + ".bak";
            File.WriteAllText(backup, "{ damaged backup }");
            byte[] damagedBackup = File.ReadAllBytes(backup);

            Assert.AreEqual(SaveStatus.Corrupt, saves.Save(CreateData(collisions: 99)));

            Assert.IsFalse(File.Exists(primary));
            CollectionAssert.AreEqual(damagedBackup, File.ReadAllBytes(backup));
        }

        [Test]
        public void FailedAtomicReplaceKeepsPrimaryAndRemovesTemporaryFile()
        {
            Assert.AreEqual(SaveStatus.Success, saves.Save(CreateData(collisions: 3)));
            string primary = PrimaryPath();
            byte[] primaryBefore = File.ReadAllBytes(primary);
            Directory.CreateDirectory(primary + ".bak");

            Assert.AreEqual(SaveStatus.IoFailure, saves.Save(CreateData(collisions: 9)));

            CollectionAssert.AreEqual(primaryBefore, File.ReadAllBytes(primary));
            Assert.IsFalse(File.Exists(primary + ".tmp"));
            Assert.IsTrue(Directory.Exists(primary + ".bak"));
            SaveResult<StreetSaveData> result = saves.Load("crossroads");
            Assert.AreEqual(SaveStatus.Success, result.Status);
            Assert.AreEqual(3, result.Value.Collisions);
        }

        [TestCase("primary", 2, "1.0.0")]
        [TestCase("backup", 2, "1.0.0")]
        [TestCase("primary", 1, "1.1.0")]
        [TestCase("backup", 1, "1.1.0")]
        public void FutureSchemaOrContractInEitherFileBlocksLoadAndSave(string location, int schema, string contract)
        {
            Assert.AreEqual(SaveStatus.Success, saves.Save(CreateData(collisions: 1)));
            if (location == "backup") Assert.AreEqual(SaveStatus.Success, saves.Save(CreateData(collisions: 2)));

            string primary = PrimaryPath();
            string backup = primary + ".bak";
            string target = location == "primary" ? primary : backup;
            byte[] primaryBefore = File.ReadAllBytes(primary);
            byte[] backupBefore = File.Exists(backup) ? File.ReadAllBytes(backup) : null;
            WriteEnvelope(target, new TestEnvelope
            {
                schema = schema,
                kind = "tinglu.street",
                contract = contract,
                levelId = "crossroads",
                checksum = "not-validated-for-recognized-future-data",
                payload = "{}"
            });
            byte[] futureBefore = File.ReadAllBytes(target);

            Assert.AreEqual(SaveStatus.FutureVersion, saves.Load("crossroads").Status);
            Assert.AreEqual(SaveStatus.FutureVersion, saves.Save(CreateData(collisions: 99)));

            if (target != primary) CollectionAssert.AreEqual(primaryBefore, File.ReadAllBytes(primary));
            CollectionAssert.AreEqual(futureBefore, File.ReadAllBytes(target));
            if (backupBefore != null && target != backup)
                CollectionAssert.AreEqual(backupBefore, File.ReadAllBytes(backup));
        }

        [Test]
        public void UnrecognizedFutureContractInBackupBlocksLoadAndSaveWithoutChangingEitherFile()
        {
            Assert.AreEqual(SaveStatus.Success, saves.Save(CreateData(collisions: 1)));
            Assert.AreEqual(SaveStatus.Success, saves.Save(CreateData(collisions: 2)));

            string primary = PrimaryPath();
            string backup = primary + ".bak";
            Assert.AreEqual("1.0.0", ReadEnvelope(primary).contract);
            WriteEnvelope(backup, new TestEnvelope
            {
                schema = 1,
                kind = "tinglu.street",
                contract = "2.0.0-beta",
                levelId = "crossroads",
                checksum = "not-validated-for-unrecognized-contract",
                payload = "{}"
            });
            byte[] primaryBefore = File.ReadAllBytes(primary);
            byte[] backupBefore = File.ReadAllBytes(backup);

            SaveResult<StreetSaveData> result = saves.Load("crossroads");

            Assert.AreEqual(SaveStatus.FutureVersion, result.Status);
            StringAssert.Contains("Unrecognized contract version", result.Error);
            Assert.AreEqual(SaveStatus.FutureVersion, saves.Save(CreateData(collisions: 99)));
            CollectionAssert.AreEqual(primaryBefore, File.ReadAllBytes(primary));
            CollectionAssert.AreEqual(backupBefore, File.ReadAllBytes(backup));
        }

        [Test]
        public void UnrecognizedContractInPrimaryBlocksLoadAndSaveDespiteValidBackup()
        {
            Assert.AreEqual(SaveStatus.Success, saves.Save(CreateData(collisions: 1)));
            Assert.AreEqual(SaveStatus.Success, saves.Save(CreateData(collisions: 2)));

            string primary = PrimaryPath();
            string backup = primary + ".bak";
            Assert.AreEqual("1.0.0", ReadEnvelope(backup).contract);
            WriteEnvelope(primary, new TestEnvelope
            {
                schema = 1,
                kind = "tinglu.street",
                contract = "2.0.0-beta",
                levelId = "crossroads",
                checksum = "not-validated-for-unrecognized-contract",
                payload = "{}"
            });
            byte[] primaryBefore = File.ReadAllBytes(primary);
            byte[] backupBefore = File.ReadAllBytes(backup);

            SaveResult<StreetSaveData> result = saves.Load("crossroads");

            Assert.AreEqual(SaveStatus.FutureVersion, result.Status);
            StringAssert.Contains("Unrecognized contract version", result.Error);
            Assert.AreEqual(SaveStatus.FutureVersion, saves.Save(CreateData(collisions: 99)));
            CollectionAssert.AreEqual(primaryBefore, File.ReadAllBytes(primary));
            CollectionAssert.AreEqual(backupBefore, File.ReadAllBytes(backup));
        }

        [Test]
        public void MissingContractRemainsCorruptRatherThanFutureVersion()
        {
            Assert.AreEqual(SaveStatus.Success, saves.Save(CreateData()));
            string primary = PrimaryPath();
            WriteEnvelope(primary, new TestEnvelope
            {
                schema = 1,
                kind = "tinglu.street",
                contract = "",
                levelId = "crossroads",
                checksum = "not-validated-for-missing-contract",
                payload = "{}"
            });
            byte[] primaryBefore = File.ReadAllBytes(primary);

            SaveResult<StreetSaveData> result = saves.Load("crossroads");

            Assert.AreEqual(SaveStatus.Corrupt, result.Status);
            Assert.AreEqual(SaveStatus.Corrupt, saves.Save(CreateData(collisions: 99)));
            CollectionAssert.AreEqual(primaryBefore, File.ReadAllBytes(primary));
        }

        [Test]
        public void OlderSchemaIsReportedAsUnsupportedAndIsNotOverwritten()
        {
            Assert.AreEqual(SaveStatus.Success, saves.Save(CreateData()));
            string primary = PrimaryPath();
            WriteEnvelope(primary, new TestEnvelope
            {
                schema = 0,
                kind = "tinglu.street",
                contract = "0.9.0",
                levelId = "crossroads",
                checksum = "not-validated-for-older-data",
                payload = "{}"
            });
            byte[] olderSave = File.ReadAllBytes(primary);

            SaveResult<StreetSaveData> result = saves.Load("crossroads");

            Assert.AreEqual(SaveStatus.Corrupt, result.Status);
            StringAssert.Contains("unsupported older contract", result.Error);
            Assert.AreEqual(SaveStatus.Corrupt, saves.Save(CreateData(collisions: 99)));
            CollectionAssert.AreEqual(olderSave, File.ReadAllBytes(primary));
        }

        [Test]
        public void NamespacesAndLevelIdsAreIsolated()
        {
            Assert.AreEqual(SaveStatus.Success, saves.Save(CreateData("crossroads")));
            var otherNamespace = new StreetSaveService(temporaryRoot, "role10-other");

            Assert.AreEqual(SaveStatus.Missing, saves.Load("another-level").Status);
            Assert.AreEqual(SaveStatus.Missing, otherNamespace.Load("crossroads").Status);
            Assert.AreEqual(SaveStatus.Success, otherNamespace.Save(CreateData("crossroads", collisions: 8)));
            Assert.AreEqual(3, saves.Load("crossroads").Value.Collisions);
            Assert.AreEqual(8, otherNamespace.Load("crossroads").Value.Collisions);
        }

        [Test]
        public void ActiveSourcesCannotBeRepresentedAsGeometryMemoryOrLoadedFromAFile()
        {
            Assert.Throws<ArgumentException>(() => new StreetKnownObject(
                "walker", "brick", StreetKind.Active, new Segment2[0], 0, 0, 1));
            Assert.AreEqual(SaveStatus.Success, saves.Save(CreateData()));

            string primary = PrimaryPath();
            TestEnvelope envelope = ReadEnvelope(primary);
            string activePayload = envelope.payload.Replace("\"kind\":0", "\"kind\":2");
            Assert.AreNotEqual(envelope.payload, activePayload, "Expected a permanent geometry kind in the fixture payload.");
            envelope.payload = activePayload;
            envelope.checksum = Checksum(envelope, activePayload);
            WriteEnvelope(primary, envelope);

            SaveResult<StreetSaveData> result = saves.Load("crossroads");

            Assert.AreEqual(SaveStatus.Corrupt, result.Status);
            StringAssert.Contains("Active sources cannot be loaded", result.Error);
        }

        private string NamespacePath { get { return Path.Combine(temporaryRoot, saveNamespace); } }

        private static StreetKnownSurface KnownSurfaceAt(System.Collections.Generic.IReadOnlyList<StreetKnownSurface> surfaces,
            Point2 point)
        {
            for (int i = 0; i < surfaces.Count; i++)
                if (StreetGeometry.Contains(surfaces[i].Polygon, point)) return surfaces[i];
            return null;
        }

        private string PrimaryPath()
        {
            string[] paths = Directory.GetFiles(NamespacePath, "street-*")
                .Where(path => string.Equals(Path.GetExtension(path), ".json", StringComparison.OrdinalIgnoreCase))
                .ToArray();
            Assert.AreEqual(1, paths.Length, "Expected one primary save in the isolated namespace.");
            return paths[0];
        }

        private static StreetSaveData CreateData(string levelId = "crossroads", int collisions = 3)
        {
            var player = new PlayerState(PlayerMainState.Active, ActionMode.Walking,
                new Point2(4, 5), new Point2(1, 0), 72, 35, 8, 0, 2);
            var checkpointPlayer = new PlayerState(PlayerMainState.Active, ActionMode.Walking,
                new Point2(-3, 1), new Point2(0, 1), 88, 96, 1, 0, 0);
            var traffic = new StreetTrafficState(18.25, new[] { "car-east", "car-north" }, new[] { 31.5, 6.25 });
            var checkpointTraffic = new StreetTrafficState(4.5, new[] { "car-east" }, new[] { 12.0 });
            var surfaces = new[]
            {
                new StreetKnownSurface("ramp", StreetGeometry.Rect(1, 2, 3, 4), 4, 14,
                    new Point2(1, 0.5f), -0.315f, 0.075f, 0.025f)
            };
            var contour = new[]
            {
                new Segment2(new Point2(-1, 0), new Point2(1, 0)),
                new Segment2(new Point2(1, 0), new Point2(1, 2))
            };
            var objects = new[]
            {
                new StreetKnownObject("wall-east", "brick", StreetKind.Permanent, contour, 2.5f, 5, 15),
                new StreetKnownObject("box-old", "cardboard", StreetKind.Temporary, contour, 0.6f, 6, 16)
            };
            return new StreetSaveData(levelId, "checkpoint-north", player, checkpointPlayer,
                traffic, checkpointTraffic, new StreetMemoryFrame(surfaces, objects),
                collisions, 11, new[] { "spawn", "checkpoint-north" }, true);
        }

        private static void AssertPlayer(PlayerState expected, PlayerState actual)
        {
            Assert.AreEqual(expected.MainState, actual.MainState);
            Assert.AreEqual(expected.Mode, actual.Mode);
            Assert.AreEqual(expected.Position.X, actual.Position.X);
            Assert.AreEqual(expected.Position.Y, actual.Position.Y);
            Assert.AreEqual(expected.Facing.X, actual.Facing.X);
            Assert.AreEqual(expected.Facing.Y, actual.Facing.Y);
            Assert.AreEqual(expected.Courage, actual.Courage);
            Assert.AreEqual(expected.Focus, actual.Focus);
            Assert.AreEqual(expected.CaneReadyAt, actual.CaneReadyAt);
            Assert.AreEqual(expected.StunnedUntil, actual.StunnedUntil);
            Assert.AreEqual(expected.ConfusedUntil, actual.ConfusedUntil);
        }

        private static void AssertTraffic(StreetTrafficState expected, StreetTrafficState actual)
        {
            Assert.AreEqual(expected.Time, actual.Time);
            CollectionAssert.AreEqual(expected.ObjectIds, actual.ObjectIds);
            CollectionAssert.AreEqual(expected.Distances, actual.Distances);
        }

        private static TestEnvelope ReadEnvelope(string path)
        {
            var serializer = new DataContractJsonSerializer(typeof(TestEnvelope));
            using (var stream = File.OpenRead(path)) return (TestEnvelope)serializer.ReadObject(stream);
        }

        private static void WriteEnvelope(string path, TestEnvelope envelope)
        {
            var serializer = new DataContractJsonSerializer(typeof(TestEnvelope));
            using (var stream = File.Create(path)) serializer.WriteObject(stream, envelope);
        }

        private static string RemoveTextureAxis(string payload)
        {
            return RewriteTextureAxis(payload, string.Empty);
        }

        private static string RemoveHeightFields(string payload)
        {
            payload = RemoveJsonField(payload, "heightSlopeY");
            payload = RemoveJsonField(payload, "heightSlopeX");
            return RemoveJsonField(payload, "heightBase");
        }

        private static string RemoveJsonField(string payload, string field)
        {
            string member = ",\"" + field + "\":";
            int memberIndex = payload.IndexOf(member, StringComparison.Ordinal);
            Assert.GreaterOrEqual(memberIndex, 0, "Expected serialized field " + field + ".");
            int valueStart = memberIndex + member.Length;
            int valueEnd = valueStart;
            while (valueEnd < payload.Length && payload[valueEnd] != ',' && payload[valueEnd] != '}') valueEnd++;
            return payload.Substring(0, memberIndex) + payload.Substring(valueEnd);
        }

        private static string ReplaceTextureAxis(string payload, string replacement)
        {
            return RewriteTextureAxis(payload, replacement);
        }

        private static string RewriteTextureAxis(string payload, string replacement)
        {
            const string member = ",\"textureAxis\":";
            int memberIndex = payload.IndexOf(member, StringComparison.Ordinal);
            Assert.GreaterOrEqual(memberIndex, 0,
                "Expected the schema-1 fixture to serialize its surface texture axis.");
            Assert.AreEqual(-1, payload.IndexOf(member, memberIndex + member.Length, StringComparison.Ordinal),
                "Expected exactly one serialized surface texture axis in the fixture.");
            int valueStart = payload.IndexOf('{', memberIndex + member.Length);
            Assert.GreaterOrEqual(valueStart, 0, "Expected the surface texture axis to be a point object.");
            int valueEnd = payload.IndexOf('}', valueStart);
            Assert.GreaterOrEqual(valueEnd, valueStart, "Expected the surface texture axis point to be complete.");
            return payload.Substring(0, memberIndex) + replacement + payload.Substring(valueEnd + 1);
        }

        private static string Checksum(TestEnvelope envelope, string payload)
        {
            var canonical = new StringBuilder();
            canonical.Append(envelope.schema.ToString(System.Globalization.CultureInfo.InvariantCulture)).Append('\n');
            AppendField(canonical, envelope.kind);
            AppendField(canonical, envelope.contract);
            AppendField(canonical, envelope.levelId);
            AppendField(canonical, payload);
            using (SHA256 sha = SHA256.Create())
            {
                byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes(canonical.ToString()));
                var result = new StringBuilder(hash.Length * 2);
                for (int i = 0; i < hash.Length; i++) result.Append(hash[i].ToString("x2", System.Globalization.CultureInfo.InvariantCulture));
                return result.ToString();
            }
        }

        private static void AppendField(StringBuilder target, string value)
        {
            target.Append(value.Length.ToString(System.Globalization.CultureInfo.InvariantCulture)).Append(':').Append(value).Append('\n');
        }

        [DataContract]
        private sealed class TestEnvelope
        {
            [DataMember(IsRequired = true, Order = 0)] public int schema;
            [DataMember(IsRequired = true, Order = 1)] public string kind;
            [DataMember(IsRequired = true, Order = 2)] public string contract;
            [DataMember(IsRequired = true, Order = 3)] public string levelId;
            [DataMember(IsRequired = true, Order = 4)] public string checksum;
            [DataMember(IsRequired = true, Order = 5)] public string payload;
        }
    }
}
