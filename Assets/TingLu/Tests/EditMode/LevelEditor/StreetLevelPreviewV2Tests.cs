using System;
using System.Collections;
using System.IO;
using System.Reflection;
using NUnit.Framework;
using TingLu.Bootstrap;
using TingLu.Levels;
using TingLu.Levels.Editor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace TingLu.Tests.StreetEditorV2
{
    public sealed class StreetLevelPreviewV2Tests
    {
        private const string Prefix = "TingLu.V2PreviewTest.";
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

        [UnityTest]
        public IEnumerator CurrentStreetDocumentActuallyRunsAndRestoresTheEditorScene()
        {
            string folder = "Assets/StreetEditorV2Test_" + Guid.NewGuid().ToString("N");
            AssetDatabase.CreateFolder("Assets", Path.GetFileName(folder));
            SessionState.SetString(Prefix + "folder", folder);
            var baseline = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            string baselinePath = folder + "/Baseline.unity";
            Assert.IsTrue(EditorSceneManager.SaveScene(baseline, baselinePath));
            var originalStart = EditorSceneManager.playModeStartScene;
            SessionState.SetString(Prefix + "start", originalStart == null ? "" : AssetDatabase.GetAssetPath(originalStart));
            SessionState.SetString(Prefix + "baseline", baselinePath);
            var document = StreetLevelCodec.Template();
            document.title = "预览必须读取这份数据";
            document.goalText = "这条目标来自新版编辑器。";
            document.spawnX = -5.5f;
            document.spawnY = -15f;
            SessionState.SetString(Prefix + "levelId", document.id);
            var host = typeof(LevelEditorV2Window).Assembly.GetType("TingLu.Levels.Editor.StreetPreviewHost");
            Assert.NotNull(host);
            var prepare = host.GetMethod("Prepare", BindingFlags.Static | BindingFlags.NonPublic);
            Assert.NotNull(prepare);
            string previewPath = (string)prepare.Invoke(null, new object[] { document, false });
            Assert.IsNotEmpty(previewPath);
            SessionState.SetString(Prefix + "preview", previewPath);
            yield return new EnterPlayMode();
            yield return null;
            yield return null;
            var entry = UnityEngine.Object.FindObjectOfType<StreetEntry>();
            Assert.NotNull(entry, "Preview must create the street runtime entry.");
            Assert.NotNull(entry.Session);
            Assert.IsTrue(entry.Session.Running);
            Assert.AreEqual(SessionState.GetString(Prefix + "levelId", ""), entry.Session.Level.Id);
            Assert.AreEqual("预览必须读取这份数据", entry.Session.Level.Title);
            Assert.AreEqual("这条目标来自新版编辑器。", entry.Session.Level.GoalText);
            Assert.AreEqual(-5.5f, entry.Session.Player.Current.Position.X, .01f);
            Assert.AreEqual(-15f, entry.Session.Player.Current.Position.Y, .01f);
            string saves = (string)typeof(StreetEntry).GetField("previewSaveDirectory", Private).GetValue(entry);
            Assert.IsNotEmpty(saves);
            Assert.AreNotEqual(Path.GetFullPath(Application.persistentDataPath), Path.GetFullPath(saves));
            SessionState.SetString(Prefix + "saves", saves);
            Assert.IsTrue(Directory.Exists(saves));
            Assert.Greater(Directory.GetFiles(saves, "*.json", SearchOption.AllDirectories).Length, 0);
            Assert.IsNull(UnityEngine.Object.FindObjectOfType<DemoEntry>(), "Street preview must not use the old M0 runtime.");
            yield return new ExitPlayMode();
            yield return null;
            Assert.AreEqual(SessionState.GetString(Prefix + "baseline", ""), SceneManager.GetActiveScene().path);
            Assert.IsNull(AssetDatabase.LoadAssetAtPath<SceneAsset>(SessionState.GetString(Prefix + "preview", "")));
            var restored = EditorSceneManager.playModeStartScene;
            Assert.AreEqual(SessionState.GetString(Prefix + "start", ""), restored == null ? "" : AssetDatabase.GetAssetPath(restored));
        }

        [UnityTest]
        public IEnumerator ExportedSceneBindsStreetDataAndRunsThroughTheStreetRuntime()
        {
            string folder = "Assets/StreetEditorV2Test_" + Guid.NewGuid().ToString("N");
            AssetDatabase.CreateFolder("Assets", Path.GetFileName(folder));
            SessionState.SetString(Prefix + "folder", folder);
            var baseline = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            Assert.IsTrue(EditorSceneManager.SaveScene(baseline, folder + "/Baseline.unity"));
            var document = StreetLevelCodec.Template();
            document.title = "导出的街区";
            var store = typeof(LevelEditorV2Window).Assembly.GetType("TingLu.Levels.Editor.StreetLevelFileStore");
            Assert.NotNull(store);
            var export = store.GetMethod("ExportScene", BindingFlags.Static | BindingFlags.Public);
            Assert.NotNull(export);
            string path = (string)export.Invoke(null, new object[] { folder + "/MyStreet.unity", document });
            Assert.AreEqual(folder + "/MyStreet.unity", path);
            var scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
            var entry = UnityEngine.Object.FindObjectOfType<StreetEntry>();
            Assert.NotNull(entry);
            var bound = (TextAsset)typeof(StreetEntry).GetField("levelAsset", Private).GetValue(entry);
            Assert.NotNull(bound);
            Assert.AreEqual(document.title, StreetLevelCodec.Decode(bound.text).title);
            // Keep the actual bound-asset runtime path; isolate only its persistence root.
            string saves = Path.Combine(Path.GetTempPath(), "TingLuStreetV2ExportTest", Guid.NewGuid().ToString("N"));
            SessionState.SetString(Prefix + "saves", saves);
            SessionState.SetString(Prefix + "previousRoot", Environment.GetEnvironmentVariable("TINGLU_STREET_SAVE_ROOT") ?? "");
            SessionState.SetBool(Prefix + "restoreRoot", true);
            Environment.SetEnvironmentVariable("TINGLU_STREET_SAVE_ROOT", saves);
            SessionState.SetString(Prefix + "start", EditorSceneManager.playModeStartScene == null ? "" : AssetDatabase.GetAssetPath(EditorSceneManager.playModeStartScene));
            EditorSceneManager.playModeStartScene = AssetDatabase.LoadAssetAtPath<SceneAsset>(path);
            yield return new EnterPlayMode();
            yield return null;
            yield return null;
            entry = UnityEngine.Object.FindObjectOfType<StreetEntry>();
            Assert.NotNull(entry.Session);
            entry.Session.Start(false);
            Assert.IsTrue(entry.Session.Running);
            Assert.AreEqual("导出的街区", entry.Session.Level.Title);
            object config = entry.Session.GetType().GetField("Config").GetValue(entry.Session);
            string saveNamespace = (string)config.GetType().GetField("SaveNamespace").GetValue(config);
            Assert.That(saveNamespace, Does.StartWith("TingLuStreetCustom_"));
            Assert.IsTrue(Directory.Exists(Path.Combine(SessionState.GetString(Prefix + "saves", ""), saveNamespace)));
            yield return new ExitPlayMode();
        }

        [TearDown]
        public void Cleanup()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            if (SessionState.GetBool(Prefix + "restoreRoot", false))
            {
                string previousRoot = SessionState.GetString(Prefix + "previousRoot", "");
                Environment.SetEnvironmentVariable("TINGLU_STREET_SAVE_ROOT", string.IsNullOrEmpty(previousRoot) ? null : previousRoot);
                SessionState.EraseBool(Prefix + "restoreRoot");
            }
            string original = SessionState.GetString(Prefix + "start", "");
            EditorSceneManager.playModeStartScene = string.IsNullOrEmpty(original) ? null : AssetDatabase.LoadAssetAtPath<SceneAsset>(original);
            string folder = SessionState.GetString(Prefix + "folder", "");
            if (!string.IsNullOrEmpty(folder) && folder.StartsWith("Assets/StreetEditorV2Test_", StringComparison.Ordinal))
            {
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                AssetDatabase.DeleteAsset(folder);
            }
            string saves = SessionState.GetString(Prefix + "saves", "");
            if (!string.IsNullOrEmpty(saves) && Path.GetFullPath(saves).StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase) && Directory.Exists(saves))
                Directory.Delete(saves, true);
            foreach (string key in new[] { "folder", "start", "baseline", "levelId", "preview", "saves", "previousRoot" }) SessionState.EraseString(Prefix + key);
        }
    }
}
