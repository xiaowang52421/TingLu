using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using TingLu.Bootstrap;
using TingLu.Contracts;
using TingLu.Levels;
using TingLu.Levels.Editor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace TingLu.Tests.StreetEditorV2
{
    /// <summary>
    /// Runs one saved and exported actor document through the authored-scene Player path.
    /// The fixture uses a temporary Assets folder and an isolated temporary save root.
    /// </summary>
    public sealed class StreetActorPreviewTests
    {
        private const string Prefix = "TingLu.StreetActorPreviewE2E.";
        private const string SceneSetupKey = "sceneSetup";
        private const string RestoreSceneSetupKey = "restoreSceneSetup";
        private const string SceneSetupCapturedKey = "sceneSetupCaptured";
        private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
        private const BindingFlags PrivateStatic = BindingFlags.Static | BindingFlags.NonPublic;

        [Serializable]
        private sealed class SceneManagerSetupSnapshot
        {
            public SceneSetupEntry[] scenes;
        }

        [Serializable]
        private sealed class SceneSetupEntry
        {
            public string path;
            public bool isLoaded;
            public bool isActive;
        }

        private static readonly string[] MovingTypes = { "pedestrian", "car", "bicycle", "electricBike" };
        private static readonly string[] ParkedTypes = { "standingPedestrian", "parkedCar", "parkedBicycle", "parkedElectricBike" };
        private static readonly StreetActorKind[] ActorKinds =
        {
            StreetActorKind.Pedestrian, StreetActorKind.Car,
            StreetActorKind.Bicycle, StreetActorKind.ElectricBike
        };

        [UnityTest]
        public IEnumerator SavedAndExportedActorConfigurationRunsInTheBoundScene()
        {
            SceneSetup[] previousScenes = EditorSceneManager.GetSceneManagerSetup();
            Scene previousActiveScene = SceneManager.GetActiveScene();
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                Scene open = SceneManager.GetSceneAt(i);
                if (string.IsNullOrEmpty(open.path) && open.isDirty)
                    Assert.Fail("Save the unsaved scene before running the exported-scene integration test.");
            }
            bool canRestoreSceneSetup = previousActiveScene.IsValid() && previousActiveScene.isLoaded &&
                previousScenes != null && previousScenes.Any(scene => scene.isLoaded && scene.isActive) &&
                previousScenes.All(scene => !string.IsNullOrEmpty(scene.path));
            SessionState.SetString(Prefix + SceneSetupKey,
                JsonUtility.ToJson(new SceneManagerSetupSnapshot
                {
                    scenes = (previousScenes ?? new SceneSetup[0]).Select(scene => new SceneSetupEntry
                    {
                        path = scene.path,
                        isLoaded = scene.isLoaded,
                        isActive = scene.isActive
                    }).ToArray()
                }));
            SessionState.SetBool(Prefix + RestoreSceneSetupKey, canRestoreSceneSetup);
            SessionState.SetBool(Prefix + SceneSetupCapturedKey, true);
            if (!canRestoreSceneSetup)
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            string folder = "Assets/StreetActorPreviewE2E_" + Guid.NewGuid().ToString("N");
            AssetDatabase.CreateFolder("Assets", Path.GetFileName(folder));
            SessionState.SetString(Prefix + "folder", folder);
            if (!canRestoreSceneSetup)
                Assert.IsTrue(EditorSceneManager.SaveScene(SceneManager.GetActiveScene(), folder + "/Baseline.unity"));

            string previousStart = EditorSceneManager.playModeStartScene == null
                ? "" : AssetDatabase.GetAssetPath(EditorSceneManager.playModeStartScene);
            SessionState.SetString(Prefix + "start", previousStart);

            StreetLevelDocument authored = CreateDocument();
            SessionState.SetString(Prefix + "levelId", authored.id);
            string savedPath = folder + "/ActorsSaved.json";
            StreetLevelFileStore.Save(savedPath, authored, true);
            SessionState.SetString(Prefix + "saved", savedPath);

            StreetLevelDocument loaded = StreetLevelFileStore.Load(savedPath);
            Assert.AreEqual(StreetLevelCodec.Encode(authored), StreetLevelCodec.Encode(loaded));
            AssertActorDocument(loaded);

            string exportedPath = StreetLevelFileStore.ExportScene(folder + "/ActorPreview.unity", loaded);
            SessionState.SetString(Prefix + "exported", exportedPath);
            Assert.AreEqual(folder + "/ActorPreview.unity", exportedPath);
            Assert.IsNotNull(AssetDatabase.LoadAssetAtPath<SceneAsset>(exportedPath));

            Scene exportedScene = EditorSceneManager.OpenScene(exportedPath, OpenSceneMode.Additive);
            StreetEntry entry = exportedScene.GetRootGameObjects()
                .Select(root => root.GetComponent<StreetEntry>()).SingleOrDefault(component => component != null);
            Assert.NotNull(entry, "The exported scene must contain the street runtime entry.");
            FieldInfo levelField = typeof(StreetEntry).GetField("levelAsset", PrivateInstance);
            Assert.NotNull(levelField);
            TextAsset boundAsset = (TextAsset)levelField.GetValue(entry);
            Assert.NotNull(boundAsset, "The exported entry must use its bound JSON asset.");
            Assert.AreEqual(authored.id, StreetLevelCodec.Decode(boundAsset.text).id);

            string saves = Path.Combine(Path.GetTempPath(), "TingLuStreetActorPreviewE2E", Guid.NewGuid().ToString("N"));
            SessionState.SetString(Prefix + "saves", saves);
            SessionState.SetString(Prefix + "previousRoot", Environment.GetEnvironmentVariable("TINGLU_STREET_SAVE_ROOT") ?? "");
            SessionState.SetBool(Prefix + "restoreRoot", true);
            Environment.SetEnvironmentVariable("TINGLU_STREET_SAVE_ROOT", saves);

            EditorSceneManager.playModeStartScene = AssetDatabase.LoadAssetAtPath<SceneAsset>(exportedPath);
            Assert.NotNull(EditorSceneManager.playModeStartScene);
            yield return new EnterPlayMode();
            yield return null;
            yield return null;

            // Re-resolve native objects after the domain reload. No pre-play scene
            // wrapper or captured query is valid across EnterPlayMode.
            entry = UnityEngine.Object.FindObjectOfType<StreetEntry>();
            Assert.NotNull(entry);
            Assert.AreEqual(SessionState.GetString(Prefix + "exported", ""), entry.gameObject.scene.path);
            Assert.NotNull(entry.Session, "The authored entry must compile the bound level during startup.");
            Assert.AreEqual(SessionState.GetString(Prefix + "levelId", ""), entry.Session.Level.Id);
            FieldInfo runtimeLevelField = typeof(StreetEntry).GetField("levelAsset", PrivateInstance);
            Assert.NotNull(runtimeLevelField);
            Assert.AreEqual(SessionState.GetString(Prefix + "levelId", ""),
                StreetLevelCodec.Decode(((TextAsset)runtimeLevelField.GetValue(entry)).text).id);

            // Stop the Unity Update loop so all simulation steps below come from this test.
            entry.enabled = false;
            var session = entry.Session;
            session.Start(false);
            Assert.IsTrue(session.Running);
            Assert.That(session.Config.SaveNamespace, Does.StartWith("TingLuStreetCustom_"));
            saves = SessionState.GetString(Prefix + "saves", "");
            Assert.IsTrue(Directory.Exists(Path.Combine(saves, session.Config.SaveNamespace)));

            var before = session.World.Capture();
            for (int i = 0; i < MovingTypes.Length; i++)
            {
                string movingId = MovingId(i), parkedId = ParkedId(i);
                StreetRoute route = session.Level.Routes.Single(value => value.ObjectId == movingId);
                Assert.AreEqual(ExpectedSpeed(i), route.Speed, .001f);
                Assert.AreEqual(ExpectedLoop(i), route.Loop);
                Assert.AreEqual(ExpectedEndX(i), route.Points[1].X, .001f);
                Assert.AreEqual(ExpectedEndY(i), route.Points[1].Y, .001f);
                Assert.AreEqual(ActorKinds[i], session.Level.Objects.Single(value => value.Id == movingId).ActorKind);
                Assert.AreEqual(ActorKinds[i], session.Level.Objects.Single(value => value.Id == parkedId).ActorKind);
            }

            session.Tick(new StreetInputFrame(new Point2()), .2);
            var after = session.World.Capture();
            for (int i = 0; i < MovingTypes.Length; i++)
            {
                var movingBefore = before.Objects.Single(value => value.Id == MovingId(i));
                var movingAfter = after.Objects.Single(value => value.Id == MovingId(i));
                var parkedBefore = before.Objects.Single(value => value.Id == ParkedId(i));
                var parkedAfter = after.Objects.Single(value => value.Id == ParkedId(i));
                Assert.Greater(StreetGeometry.Distance(movingBefore.Position, movingAfter.Position), .1f,
                    MovingTypes[i] + " must move in the exported Player session.");
                Assert.AreEqual(parkedBefore.Position.X, parkedAfter.Position.X, .001f);
                Assert.AreEqual(parkedBefore.Position.Y, parkedAfter.Position.Y, .001f);
                Assert.IsTrue(movingAfter.SoundActive, MovingTypes[i] + " movement sound must remain active while moving.");
                Assert.IsFalse(parkedAfter.SoundActive, ParkedTypes[i] + " must stay silent.");
            }

            // A static pedestrian blocks and flashes, but does not cause a vehicle recovery.
            for (int step = 0; step < 600 && session.Collisions == 0; step++)
                session.Tick(new StreetInputFrame(new Point2(0, 1)), .1);
            Assert.Greater(session.Collisions, 0, "Walking into the stationary pedestrian must register ordinary contact.");
            Assert.AreEqual(0, session.Recoveries, "A stationary person must not trigger a vehicle recovery.");
            Assert.Greater(session.CollisionFeedback.TriggerCount, 0);
            Assert.Greater(session.CollisionFeedback.Alpha, 0);

            yield return new ExitPlayMode();
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator RestoreFixture()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) yield return new ExitPlayMode();
            yield return null;

            if (SessionState.GetBool(Prefix + "restoreRoot", false))
            {
                string previousRoot = SessionState.GetString(Prefix + "previousRoot", "");
                Environment.SetEnvironmentVariable("TINGLU_STREET_SAVE_ROOT", string.IsNullOrEmpty(previousRoot) ? null : previousRoot);
            }

            string originalStart = SessionState.GetString(Prefix + "start", "");
            EditorSceneManager.playModeStartScene = string.IsNullOrEmpty(originalStart)
                ? null : AssetDatabase.LoadAssetAtPath<SceneAsset>(originalStart);

            if (SessionState.GetBool(Prefix + SceneSetupCapturedKey, false))
                RestoreSceneSetup();

            string folder = SessionState.GetString(Prefix + "folder", "");
            if (!string.IsNullOrEmpty(folder) && folder.StartsWith("Assets/StreetActorPreviewE2E_", StringComparison.Ordinal))
            {
                if (AssetDatabase.IsValidFolder(folder)) AssetDatabase.DeleteAsset(folder);
            }

            string saves = SessionState.GetString(Prefix + "saves", "");
            string tempRoot = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (!string.IsNullOrEmpty(saves) && Path.GetFullPath(saves).StartsWith(tempRoot, StringComparison.OrdinalIgnoreCase) && Directory.Exists(saves))
                Directory.Delete(saves, true);

            foreach (string key in new[] { SceneSetupKey, "folder", "start", "levelId", "saved", "exported", "saves", "previousRoot" })
                SessionState.EraseString(Prefix + key);
            SessionState.EraseBool(Prefix + "restoreRoot");
            SessionState.EraseBool(Prefix + RestoreSceneSetupKey);
            SessionState.EraseBool(Prefix + SceneSetupCapturedKey);
        }

        private static void RestoreSceneSetup()
        {
            string exportedPath = SessionState.GetString(Prefix + "exported", "");
            if (!string.IsNullOrEmpty(exportedPath))
            {
                Scene exportedScene = SceneManager.GetSceneByPath(exportedPath);
                if (exportedScene.IsValid() && exportedScene.isLoaded)
                    EditorSceneManager.CloseScene(exportedScene, true);
            }

            if (SessionState.GetBool(Prefix + RestoreSceneSetupKey, false))
            {
                string json = SessionState.GetString(Prefix + SceneSetupKey, "");
                SceneManagerSetupSnapshot snapshot = string.IsNullOrEmpty(json)
                    ? null : JsonUtility.FromJson<SceneManagerSetupSnapshot>(json);
                SceneSetup[] expected = ToSceneSetup(snapshot);
                if (expected != null && expected.Any(scene => scene.isLoaded && scene.isActive))
                {
                    SceneSetup[] current = EditorSceneManager.GetSceneManagerSetup();
                    if (!SameSceneSetup(expected, current))
                        EditorSceneManager.RestoreSceneManagerSetup(expected);
                }
            }
            else EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        }

        private static SceneSetup[] ToSceneSetup(SceneManagerSetupSnapshot snapshot)
        {
            if (snapshot == null || snapshot.scenes == null)
                return null;

            var scenes = new SceneSetup[snapshot.scenes.Length];
            for (int i = 0; i < scenes.Length; i++)
                scenes[i] = new SceneSetup
                {
                    path = snapshot.scenes[i].path,
                    isLoaded = snapshot.scenes[i].isLoaded,
                    isActive = snapshot.scenes[i].isActive
                };
            return scenes;
        }

        private static bool SameSceneSetup(SceneSetup[] expected, SceneSetup[] actual)
        {
            if (expected == null || actual == null || expected.Length != actual.Length) return false;
            for (int i = 0; i < expected.Length; i++)
            {
                if (!string.Equals(expected[i].path, actual[i].path, StringComparison.Ordinal) ||
                    expected[i].isLoaded != actual[i].isLoaded || expected[i].isActive != actual[i].isActive)
                    return false;
            }
            return true;
        }

        private static StreetLevelDocument CreateDocument()
        {
            var document = new StreetLevelDocument
            {
                id = "street.actor.preview." + Guid.NewGuid().ToString("N"),
                title = "动静 actor 导出试玩",
                goalText = "沿着人行道前进。",
                spawnX = -25,
                spawnY = -25,
                facingX = 1,
                facingY = 0,
                goalX = -25,
                goalY = 55,
                goalRadius = 1
            };
            document.surfaces.Add(StreetEditorCatalog.CreateSurface("sidewalk", "fixture.walkable",
                StreetEditorCatalog.Rectangle(0, 0, 120, 120), 10));
            var existingIds = new List<string> { document.id, "fixture.walkable" };

            for (int i = 0; i < MovingTypes.Length; i++)
            {
                float x = -18 + i * 12, y = 35;
                string movingId = MovingId(i);
                var moving = StreetEditorCatalog.CreateObject(MovingTypes[i], movingId,
                    StreetEditorCatalog.Point(x, y), null, existingIds);
                document.objects.Add(moving);
                existingIds.Add(movingId);
                StreetEditorCatalog.ConfigureMovement(document, moving, true);

                StreetRouteData route = document.routes.Single(value => value.objectId == movingId);
                route.speed = ExpectedSpeed(i);
                route.loop = ExpectedLoop(i);
                route.points[1].x = ExpectedEndX(i);
                route.points[1].y = ExpectedEndY(i);

                float parkedX = i == 0 ? -25 : -15 + (i - 1) * 15;
                float parkedY = i == 0 ? -22.5f : 0;
                string parkedId = ParkedId(i);
                var parked = StreetEditorCatalog.CreateObject(ParkedTypes[i], parkedId,
                    StreetEditorCatalog.Point(parkedX, parkedY), null, existingIds);
                document.objects.Add(parked);
                existingIds.Add(parkedId);
                StreetEditorCatalog.ConfigureMovement(document, parked, false);
            }
            return document;
        }

        private static void AssertActorDocument(StreetLevelDocument document)
        {
            Assert.IsEmpty(StreetLevelCodec.Validate(document), string.Join("; ", StreetLevelCodec.Validate(document)));
            Assert.AreEqual(4, document.routes.Count);
            for (int i = 0; i < MovingTypes.Length; i++)
            {
                StreetObjectData moving = document.objects.Single(value => value.id == MovingId(i));
                StreetObjectData parked = document.objects.Single(value => value.id == ParkedId(i));
                StreetRouteData route = document.routes.Single(value => value.objectId == MovingId(i));
                Assert.AreEqual(ActorKinds[i], moving.actorKind);
                Assert.AreEqual(ActorKinds[i], parked.actorKind);
                Assert.IsTrue(moving.soundActive);
                Assert.IsFalse(parked.soundActive);
                Assert.AreEqual(ExpectedSpeed(i), route.speed, .001f);
                Assert.AreEqual(ExpectedLoop(i), route.loop);
                Assert.AreEqual(ExpectedEndX(i), route.points[1].x, .001f);
                Assert.AreEqual(ExpectedEndY(i), route.points[1].y, .001f);
            }
        }

        private static string MovingId(int index) { return "fixture.actor.moving." + MovingTypes[index]; }
        private static string ParkedId(int index) { return "fixture.actor.parked." + MovingTypes[index]; }
        private static float ExpectedSpeed(int index) { return 1.4f + .25f * index; }
        private static bool ExpectedLoop(int index) { return index % 2 == 0; }
        private static float ExpectedEndX(int index) { return -10 + 12 * index + index; }
        private static float ExpectedEndY(int index) { return 36.5f; }
    }
}
