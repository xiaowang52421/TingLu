using System;
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

namespace TingLu.Tests.StreetEditorV2
{
    public sealed class StreetActorEditorTests
    {
        private string temporaryRoot;

        [SetUp]
        public void SetUp()
        {
            temporaryRoot = Path.Combine(Path.GetTempPath(), "tinglu-street-actors-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(temporaryRoot);
        }

        [TearDown]
        public void TearDown()
        {
            if (temporaryRoot != null && Directory.Exists(temporaryRoot))
                Directory.Delete(temporaryRoot, true);
        }

        [Test]
        public void ActorKindControlsChineseTypeEvenWhenSoundIdChanges()
        {
            StreetLevelDocument document = StreetLevelCodec.Template();
            var ids = ExistingIds(document);
            StreetObjectData car = StreetEditorCatalog.CreateObject("car", StreetEditorCatalog.Point(40, 40), null, ids);
            document.objects.Add(car);
            StreetEditorCatalog.ConfigureMovement(document, car, true);

            car.soundId = "street.bird";
            Assert.AreEqual("car", StreetEditorCatalog.GetObjectType(car, document));

            StreetEditorCatalog.ApplyObjectType(car, "parkedCar", ids);
            StreetEditorCatalog.ConfigureMovement(document, car, false);
            Assert.AreEqual("parkedCar", StreetEditorCatalog.GetObjectType(car, document));
            Assert.IsFalse(car.soundActive);
            Assert.IsFalse(document.routes.Exists(route => route.objectId == car.id));

            StreetEditorCatalog.ApplyObjectType(car, "car", ids);
            StreetEditorCatalog.ConfigureMovement(document, car, true);
            Assert.AreEqual("car", StreetEditorCatalog.GetObjectType(car, document));
            Assert.IsTrue(car.soundActive);
            Assert.IsTrue(document.routes.Exists(route => route.objectId == car.id));
        }

        [Test]
        public void MovingAndParkedActorsSurviveSaveReloadAndRuntimeCompilation()
        {
            StreetLevelDocument source = CreateActorDocument();
            string path = Path.Combine(temporaryRoot, "动静配置.json");
            StreetLevelFileStore.Save(path, source, true);

            StreetLevelDocument loaded = StreetLevelFileStore.Load(path);
            StreetLevel level = StreetLevelCodec.Compile(loaded);
            Assert.AreEqual(8, loaded.objects.Count(item => item.actorKind != StreetActorKind.None && item.id.StartsWith("test.")));

            string[] movingTypes = { "pedestrian", "car", "bicycle", "electricBike" };
            string[] parkedTypes = { "standingPedestrian", "parkedCar", "parkedBicycle", "parkedElectricBike" };

            for (int i = 0; i < 4; i++)
            {
                StreetObjectData moving = loaded.objects.Single(item => item.id == "test.moving." + i);
                StreetObjectData parked = loaded.objects.Single(item => item.id == "test.parked." + i);
                Assert.AreEqual((StreetActorKind)(i + 1), moving.actorKind);
                Assert.AreEqual((StreetActorKind)(i + 1), parked.actorKind);
                Assert.IsTrue(moving.soundActive);
                Assert.IsFalse(parked.soundActive);
                Assert.AreEqual(movingTypes[i], StreetEditorCatalog.GetObjectType(moving, loaded));
                Assert.AreEqual(parkedTypes[i], StreetEditorCatalog.GetObjectType(parked, loaded));
            }

            List<StreetRouteData> savedRoutes = loaded.routes
                .Where(route => route.objectId.StartsWith("test.moving.")).ToList();
            Assert.AreEqual(4, savedRoutes.Count);
            foreach (StreetRouteData route in savedRoutes)
            {
                int index = Array.IndexOf(new[] { "test.moving.0", "test.moving.1", "test.moving.2", "test.moving.3" }, route.objectId);
                Assert.GreaterOrEqual(index, 0);
                Assert.AreEqual(1.25f + index, route.speed, .001f);
                Assert.AreEqual(index % 2 == 0, route.loop);
                StreetObjectData moving = loaded.objects.Single(item => item.id == route.objectId);
                Assert.AreEqual(moving.position.x, route.points[0].x, .001f);
                Assert.AreEqual(moving.position.x + 4 + index, route.points[1].x, .001f);
                Assert.AreEqual(moving.position.y + .5f, route.points[1].y, .001f);
                Assert.AreEqual(route.speed, level.Routes.Single(item => item.ObjectId == route.objectId).Speed, .001f);
                Assert.AreEqual(route.loop, level.Routes.Single(item => item.ObjectId == route.objectId).Loop);
            }

            foreach (StreetObjectData item in loaded.objects.Where(value => value.id.StartsWith("test.")))
                Assert.AreEqual(item.actorKind, level.Objects.Single(value => value.Id == item.id).ActorKind);
        }

        [Test]
        public void ExportedSceneBindsTheSavedActorMovementConfiguration()
        {
            string folderName = "StreetActorEditorTest_" + Guid.NewGuid().ToString("N");
            string folder = "Assets/" + folderName;
            AssetDatabase.CreateFolder("Assets", folderName);
            SceneSetup[] previousScenes = EditorSceneManager.GetSceneManagerSetup();
            Scene previousActiveScene = SceneManager.GetActiveScene();
            bool canRestoreSceneSetup = previousActiveScene.IsValid() && previousActiveScene.isLoaded &&
                previousScenes != null && previousScenes.Any(scene => scene.isLoaded && scene.isActive);
            try
            {
                Scene baseline = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                Assert.IsTrue(EditorSceneManager.SaveScene(baseline, folder + "/Baseline.unity"));

                StreetLevelDocument source = CreateActorDocument();
                string scenePath = StreetLevelFileStore.ExportScene(folder + "/ActorConfig.unity", source);
                Assert.IsTrue(AssetDatabase.LoadAssetAtPath<SceneAsset>(scenePath) != null);

                Scene exported = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Additive);
                StreetEntry entry = exported.GetRootGameObjects()
                    .Select(root => root.GetComponent<StreetEntry>()).Single(component => component != null);
                FieldInfo levelAssetField = typeof(StreetEntry).GetField("levelAsset", BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.NotNull(levelAssetField);
                TextAsset levelAsset = (TextAsset)levelAssetField.GetValue(entry);
                Assert.NotNull(levelAsset);

                StreetLevelDocument exportedDocument = StreetLevelCodec.Decode(levelAsset.text);
                Assert.AreEqual(StreetLevelCodec.Encode(source), StreetLevelCodec.Encode(exportedDocument));
                StreetLevel exportedLevel = StreetLevelCodec.Compile(exportedDocument);
                Assert.AreEqual(4, exportedLevel.Routes.Count(route => route.ObjectId.StartsWith("test.moving.")));
                Assert.AreEqual(8, exportedLevel.Objects.Count(item => item.ActorKind != StreetActorKind.None && item.Id.StartsWith("test.")));
                Assert.IsTrue(exportedLevel.Routes.Any(route => route.ObjectId.StartsWith("test.moving.") && !route.Loop));
            }
            finally
            {
                if (canRestoreSceneSetup) EditorSceneManager.RestoreSceneManagerSetup(previousScenes);
                else EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                AssetDatabase.DeleteAsset(folder);
            }
        }

        private static StreetLevelDocument CreateActorDocument()
        {
            StreetLevelDocument document = StreetLevelCodec.Template();
            List<string> ids = ExistingIds(document);
            string[] movingTypes = { "pedestrian", "car", "bicycle", "electricBike" };
            string[] parkedTypes = { "standingPedestrian", "parkedCar", "parkedBicycle", "parkedElectricBike" };
            for (int i = 0; i < movingTypes.Length; i++)
            {
                StreetObjectData moving = StreetEditorCatalog.CreateObject(movingTypes[i], "test.moving." + i,
                    StreetEditorCatalog.Point(50 + i * 5, 50), null, ids);
                document.objects.Add(moving);
                ids.Add(moving.id);
                StreetEditorCatalog.ConfigureMovement(document, moving, true);
                StreetRouteData route = document.routes.Single(item => item.objectId == moving.id);
                route.speed = 1.25f + i;
                route.loop = i % 2 == 0;
                route.points[1].x += i + 1;
                route.points[1].y += .5f;

                StreetObjectData parked = StreetEditorCatalog.CreateObject(parkedTypes[i], "test.parked." + i,
                    StreetEditorCatalog.Point(50 + i * 5, 58), null, ids);
                document.objects.Add(parked);
                ids.Add(parked.id);
                StreetEditorCatalog.ConfigureMovement(document, parked, false);
            }
            return document;
        }

        private static List<string> ExistingIds(StreetLevelDocument document)
        {
            var ids = new List<string> { document.id };
            ids.AddRange(document.surfaces.Where(item => item != null).Select(item => item.id));
            ids.AddRange(document.objects.Where(item => item != null).Select(item => item.id));
            ids.AddRange(document.checkpoints.Where(item => item != null).Select(item => item.id));
            return ids;
        }
    }
}
