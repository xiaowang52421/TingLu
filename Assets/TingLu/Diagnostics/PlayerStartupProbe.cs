#if DEVELOPMENT_BUILD || UNITY_EDITOR
using System;
using System.Collections;
using System.IO;
using System.Reflection;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace TingLu.Diagnostics
{
    // Runs only with an explicit QA argument in a development build. Normal launches are untouched.
    public sealed class PlayerStartupProbe : MonoBehaviour
    {
        private string outputDirectory;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Initialize()
        {
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i + 1 < args.Length; i++)
            {
                if (args[i] != "-tinglu-startup-probe") continue;
                if (!Path.IsPathRooted(args[i + 1])) throw new ArgumentException("Startup probe output must be an absolute directory.");
                var host = new GameObject("TingLu startup verification");
                host.AddComponent<PlayerStartupProbe>().outputDirectory = Path.GetFullPath(args[i + 1]);
                return;
            }
        }

        private IEnumerator Start()
        {
            Directory.CreateDirectory(outputDirectory);
            yield return new WaitForSecondsRealtime(10);
            var report = new Report();
            report.scene = SceneManager.GetActiveScene().name;
            report.listeners = UnityEngine.Object.FindObjectsOfType<AudioListener>().Length;
            MonoBehaviour entry = null;
            foreach (var component in UnityEngine.Object.FindObjectsOfType<MonoBehaviour>())
                if (component.GetType().FullName == "TingLu.Bootstrap.DemoEntry" || component.GetType().FullName == "TingLu.Bootstrap.StreetEntry") { entry = component; break; }
            if (entry != null)
            {
                report.entryEnabled = entry.enabled;
                var type = entry.GetType();
                var levelField = type.GetField("levelAsset", BindingFlags.NonPublic | BindingFlags.Instance);
                var levelAsset = levelField==null?null:levelField.GetValue(entry) as TextAsset;
                bool street=type.FullName=="TingLu.Bootstrap.StreetEntry";
                report.levelAssetBound = street || levelAsset != null && !string.IsNullOrEmpty(levelAsset.text);
                report.levelAsset = street?"street-crossroads":levelAsset == null ? null : levelAsset.name;
                report.fatal = type.GetField("fatal", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(entry) as string;
                object session = street?type.GetProperty("Session").GetValue(entry,null):type.GetField("session", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(entry);
                report.sessionCreated = session != null;
                if (session != null)
                {
                    object menu = session.GetType().GetField("Menu").GetValue(session);
                    report.menu = menu.GetType().GetProperty("Screen").GetValue(menu, null).ToString();
                    object settings = session.GetType().GetProperty("Settings").GetValue(session, null);
                    report.display = settings.GetType().GetProperty("Display").GetValue(settings, null).ToString();
                }
            }
            report.status = report.entryEnabled && report.sessionCreated && string.IsNullOrEmpty(report.fatal)
                && report.levelAssetBound && report.listeners == 1 && report.menu == "Main" ? "PASS" : "FAIL";
            report.screenshot = Path.Combine(outputDirectory, "first-step-menu.png");
            ScreenCapture.CaptureScreenshot(report.screenshot);
            yield return new WaitForSecondsRealtime(2);
            report.screenshotCreated = File.Exists(report.screenshot);
            File.WriteAllText(Path.Combine(outputDirectory, "startup-results.json"), JsonUtility.ToJson(report, true));
            Debug.Log("TingLu startup probe " + report.status + ", screenshot=" + report.screenshotCreated);
            Application.Quit(report.status == "PASS" ? 0 : 1);
        }

        [Serializable]
        private sealed class Report
        {
            public string status, scene, fatal, menu, display, screenshot, levelAsset;
            public int listeners;
            public bool entryEnabled, sessionCreated, screenshotCreated, levelAssetBound;
        }
    }
}
#endif
