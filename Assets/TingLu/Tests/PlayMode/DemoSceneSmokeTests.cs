#if UNITY_INCLUDE_TESTS
using System;
using System.Collections;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using TingLu.Bootstrap;
using TingLu.UI;

namespace TingLu.Tests.PlayMode
{
    /// <summary>
    /// Boots the real FirstStep scene through DemoEntry and reports whether the host
    /// actually assembled the session, or silently degraded into its fatal path.
    /// </summary>
    public sealed class DemoSceneSmokeTests
    {
        private const string ScenePath = "Assets/TingLu/Scenes/Levels/FirstStep.unity";

        [UnityTest]
        public IEnumerator SceneBootsThroughDemoEntryWithoutFatalPath()
        {
            SceneManager.LoadScene(ScenePath, LoadSceneMode.Single);
            yield return null;

            MonoBehaviour entry = null;
            foreach (GameObject root in SceneManager.GetActiveScene().GetRootGameObjects())
            {
                foreach (MonoBehaviour behaviour in root.GetComponentsInChildren<MonoBehaviour>(true))
                {
                    if (behaviour != null && behaviour.GetType().Name == "DemoEntry") { entry = behaviour; break; }
                }
                if (entry != null) break;
            }
            Assert.IsNotNull(entry, "DemoEntry was not found in the loaded FirstStep scene.");
            TextAsset configuredLevel = ReadField(entry, "levelAsset") as TextAsset;
            Assert.IsNotNull(configuredLevel, "The production scene must bind its level JSON asset.");
            Assert.IsTrue(string.IsNullOrEmpty(ReadField(entry, "previewJson") as string),
                "The production scene must use its bound asset rather than editor preview data.");
            var level = TingLu.Levels.LevelCompiler.Compile(TingLu.Levels.LevelCodec.Decode(configuredLevel.text));
            Assert.Greater(level.MovementRoutes.Count, 0, "The production actor must use a configured movement route.");

            for (int i = 0; i < 180; i++) yield return null;

            Assert.IsTrue(entry.enabled,
                "DemoEntry disabled itself, so Start() threw. Fatal message: " + ReadField(entry, "fatal"));

            string fatal = ReadField(entry, "fatal") as string;
            Assert.IsTrue(string.IsNullOrEmpty(fatal), "DemoEntry reported a fatal startup error: " + fatal);

            DemoSession session = ReadField(entry, "session") as DemoSession;
            Assert.IsNotNull(session, "DemoEntry did not construct a DemoSession.");

            Assert.IsNotNull(session.Clock, "DemoSession has no clock.");
            Assert.AreEqual(MenuScreen.Main, session.Menu.Screen, "The real scene must begin at the main menu.");
            Assert.IsTrue(session.Clock.Paused, "The main menu must keep simulation frozen.");
            Assert.IsFalse(session.Running, "Opening a scene must not start or overwrite a saved attempt.");
            Assert.AreEqual(1, UnityEngine.Object.FindObjectsOfType<AudioListener>().Length);
            string screenshot = Environment.GetEnvironmentVariable("TINGLU_SCREENSHOT_PATH");
            if (!string.IsNullOrEmpty(screenshot))
            {
                ScreenCapture.CaptureScreenshot(screenshot);
                for (int i = 0; i < 5; i++) yield return null;
            }
            Debug.Log("[PlayMode] FirstStep scene started at its paused main menu.");
        }

        [UnityTearDown]
        public IEnumerator UnloadScene()
        {
            Scene scene = SceneManager.GetActiveScene();
            Scene empty = SceneManager.CreateScene("TingLu test cleanup " + Guid.NewGuid().ToString("N"));
            SceneManager.SetActiveScene(empty);
            yield return SceneManager.UnloadSceneAsync(scene);
        }

        private static object ReadField(object target, string name)
        {
            FieldInfo field = target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
            return field == null ? null : field.GetValue(target);
        }

    }
}
#endif
