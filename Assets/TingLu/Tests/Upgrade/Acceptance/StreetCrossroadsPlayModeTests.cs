using System.Collections;
using System;
using System.IO;
using System.Reflection;
using NUnit.Framework;
using TingLu.Bootstrap;
using TingLu.Config;
using TingLu.Contracts;
using TingLu.Levels;
using TingLu.Persistence;
using TingLu.Presentation;
using TingLu.UI;
using TingLu.View;
using TingLu.World;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace TingLu.Tests.Upgrade.Acceptance
{
    public sealed class StreetCrossroadsPlayModeTests
    {
        [UnityTest]
        public IEnumerator StreetCrossroadsLoadsOneEntryCameraAndListener()
        {
            AsyncOperation load = SceneManager.LoadSceneAsync("StreetCrossroads", LoadSceneMode.Single);
            Assert.IsNotNull(load, "StreetCrossroads must be included in the Player scene list");
            while (!load.isDone) yield return null;
            yield return null;
            yield return null;

            Assert.AreEqual("StreetCrossroads", SceneManager.GetActiveScene().name);
            Behaviour entry = FindStreetEntry();
            Assert.IsNotNull(entry, "the actual scene must bind StreetEntry");
            Assert.IsTrue(entry.enabled);

            PropertyInfo sessionProperty = entry.GetType().GetProperty("Session", BindingFlags.Public | BindingFlags.Instance);
            PropertyInfo cameraProperty = entry.GetType().GetProperty("PlayerCamera", BindingFlags.Public | BindingFlags.Instance);
            Assert.IsNotNull(sessionProperty);
            Assert.IsNotNull(cameraProperty);
            Assert.IsNotNull(sessionProperty.GetValue(entry, null), "StreetEntry must create the integrated session");
            Camera camera = cameraProperty.GetValue(entry, null) as Camera;
            Assert.IsNotNull(camera);
            Assert.IsTrue(camera.orthographic);
            Assert.AreEqual(1, Object.FindObjectsOfType<Camera>().Length, "one gameplay camera owns the street scene");
            Assert.AreEqual(1, Object.FindObjectsOfType<AudioListener>().Length, "the listener must not be duplicated");
            Assert.AreEqual(0, camera.GetComponents<AudioListener>().Length,
                "the camera object must not accidentally add a second listener");
        }

        [UnityTest]
        public IEnumerator A25AndA30_RealSessionPausesRecoversContinuesAndReachesGoal()
        {
            string saveRoot = Path.Combine(Path.GetTempPath(), "tinglu-upgrade-a30-" + System.Guid.NewGuid().ToString("N"));
            string evidenceRoot = Environment.GetEnvironmentVariable("TINGLU_UPGRADE_EVIDENCE_ROOT");
            if (string.IsNullOrWhiteSpace(evidenceRoot))
                evidenceRoot = Path.Combine(Path.GetTempPath(), "tinglu-upgrade-evidence-" + System.Guid.NewGuid().ToString("N"));
            string previousSaveRoot = Environment.GetEnvironmentVariable("TINGLU_STREET_SAVE_ROOT");
            Environment.SetEnvironmentVariable("TINGLU_STREET_SAVE_ROOT", saveRoot);
            Directory.CreateDirectory(evidenceRoot);

            StreetEntry entry = null;
            StreetSession session = null;
            int collisionAtFinish = 0;
            try
            {
                AsyncOperation load = SceneManager.LoadSceneAsync("StreetCrossroads", LoadSceneMode.Single);
                Assert.IsNotNull(load);
                while (!load.isDone) yield return null;
                yield return null;
                yield return null;

                entry = FindStreetEntry() as StreetEntry;
                Assert.IsNotNull(entry);
                Assert.IsNotNull(entry.Session);
                session = entry.Session;
                entry.enabled = false;
                session.Menu.Activate(MenuOption.Start);
                Assert.IsTrue(session.Running);
                Assert.AreEqual(MenuScreen.Playing, session.Menu.Screen);
                session.Tick(new StreetInputFrame(new Point2(), probe: true), 0.02);
                Assert.Greater(session.Memory.Current.Surfaces.Count, 0, "the production probe must reveal permitted ground area");
                Assert.Greater(session.Perception.Observations.Count, 0, "the real street must reach the production Perception pipeline");
                yield return CaptureVisible(entry, session, evidenceRoot, "a30-start-explored.png");

                yield return WalkTo(session, new Point2(-5.5f, -11.3f), "approach obstacle");
                yield return WalkTo(session, new Point2(-6.5f, -11.3f), "detour west of parked bicycle");
                yield return WalkTo(session, new Point2(-6.5f, -8.7f), "pass parked bicycle");
                yield return WalkTo(session, new Point2(-5.5f, -8.7f), "rejoin tactile route");
                yield return WalkTo(session, new Point2(-5.5f, 5.3f), "reach west checkpoint");
                Assert.AreEqual("checkpoint.west.approach", session.CheckpointId);

                session.Tick(new StreetInputFrame(new Point2(), pause: true), 0.02);
                Assert.IsTrue(session.Clock.Paused);
                Assert.AreEqual(MenuScreen.Pause, session.Menu.Screen);
                PlayerState pausedPlayer = session.Player.Current;
                StreetTrafficState pausedTraffic = session.World.ExportTraffic();
                StreetMemoryFrame pausedMemory = session.Memory.Export();
                PerceptionFrame pausedPerception = session.Perception;
                double pausedTime = session.Clock.Now;
                bool audioWasPaused = ReadUnityAudioPaused(entry);
                yield return CaptureVisible(entry, session, evidenceRoot, "a25-paused.png");

                session.Tick(new StreetInputFrame(new Point2(0, 1), 90, true), 2.0);
                Assert.AreEqual(pausedTime, session.Clock.Now, 1e-9);
                Assert.AreEqual(pausedTraffic.Time, session.World.ExportTraffic().Time, 1e-9);
                CollectionAssert.AreEqual(pausedTraffic.ObjectIds, session.World.ExportTraffic().ObjectIds);
                CollectionAssert.AreEqual(pausedTraffic.Distances, session.World.ExportTraffic().Distances);
                Assert.AreEqual(pausedPlayer.Position.X, session.Player.Current.Position.X, 1e-6f);
                Assert.AreEqual(pausedPlayer.Position.Y, session.Player.Current.Position.Y, 1e-6f);
                Assert.AreEqual(pausedPlayer.Facing.X, session.Player.Current.Facing.X, 1e-6f);
                Assert.AreEqual(pausedPlayer.Facing.Y, session.Player.Current.Facing.Y, 1e-6f);
                Assert.AreEqual(pausedPlayer.CaneReadyAt, session.Player.Current.CaneReadyAt, 1e-9);
                Assert.AreEqual(pausedMemory.Surfaces.Count, session.Memory.Current.Surfaces.Count);
                Assert.AreEqual(pausedPerception.FrameId, session.Perception.FrameId);
                Assert.AreEqual(audioWasPaused, ReadUnityAudioPaused(entry));

                session.Tick(new StreetInputFrame(new Point2(), pause: true), 0.02);
                Assert.IsFalse(session.Clock.Paused);
                Assert.AreEqual(MenuScreen.Playing, session.Menu.Screen);
                Assert.AreEqual(session.Clock.Now, session.Perception.SampledAt, 1e-9,
                    "the first resumed perception frame must use the live restored timeline");
                Assert.IsFalse(ReadUnityAudioPaused(entry));
                yield return WalkTo(session, new Point2(5.5f, 5.3f), "cross the north crosswalk to the east checkpoint");
                Assert.AreEqual("checkpoint.east.landing", session.CheckpointId);
                Assert.AreEqual(0, session.Collisions, "entering the road is not itself a vehicle collision");

                yield return WalkTo(session, new Point2(1.35f, 5.3f), "enter the real northbound vehicle lane");
                double impactWaitStarted = session.Clock.Now;
                int waitTicks = 0;
                while (session.Recoveries == 0 && session.Clock.Now - impactWaitStarted < 80.0)
                {
                    session.Tick(new StreetInputFrame(new Point2()), 0.02);
                    if (++waitTicks % 8 == 0) yield return null;
                }
                Assert.AreEqual(1, session.Recoveries, "a routed vehicle must cause a real collision and one recovery");
                Assert.AreEqual(1, session.Collisions);
                Assert.AreEqual("checkpoint.east.landing", session.CheckpointId);
                Assert.AreEqual(5.5f, session.Player.Current.Position.X, 0.03f);
                Assert.AreEqual(5.3f, session.Player.Current.Position.Y, 0.03f);
                Assert.AreEqual(session.World.ExportTraffic().Time, session.Clock.Now, 1e-9);
                Assert.IsFalse(session.Finished);
                yield return CaptureVisible(entry, session, evidenceRoot, "a30-real-collision-recovery.png");

                Assert.AreEqual(SaveStatus.Success, session.Save());
                session.Menu.Back();
                Assert.AreEqual(MenuScreen.Main, session.Menu.Screen);
                Assert.IsFalse(session.Running);

                StreetSaveService saves = new StreetSaveService(saveRoot, new StreetConfig().SaveNamespace);
                SaveResult<StreetSaveData> saved = saves.Load(StreetLevelFactory.LevelId);
                Assert.AreEqual(SaveStatus.Success, saved.Status);
                Assert.AreEqual(1, saved.Value.Collisions);
                Assert.AreEqual("checkpoint.east.landing", saved.Value.CheckpointId);

                load = SceneManager.LoadSceneAsync("StreetCrossroads", LoadSceneMode.Single);
                Assert.IsNotNull(load);
                while (!load.isDone) yield return null;
                yield return null;
                yield return null;
                entry = FindStreetEntry() as StreetEntry;
                Assert.IsNotNull(entry);
                Assert.IsNotNull(entry.Session);
                entry.enabled = false;
                session = entry.Session;
                MenuItem continueItem = FindMenuItem(session.Menu, MenuOption.Continue);
                Assert.IsNotNull(continueItem);
                Assert.IsTrue(continueItem.Enabled);
                session.Menu.Activate(MenuOption.Continue);
                Assert.IsTrue(session.Running);
                Assert.AreEqual(MenuScreen.Playing, session.Menu.Screen);
                Assert.AreEqual(1, session.Collisions);
                Assert.AreEqual("checkpoint.east.landing", session.CheckpointId);
                Assert.AreEqual(5.5f, session.Player.Current.Position.X, 0.03f);
                Assert.AreEqual(5.3f, session.Player.Current.Position.Y, 0.03f);
                Assert.AreEqual(session.Clock.Now, session.Perception.SampledAt, 1e-9);
                yield return CaptureVisible(entry, session, evidenceRoot, "a30-continued.png");

                yield return WalkTo(session, new Point2(5.5f, 8.0f), "approach occupied east tactile route");
                yield return WalkTo(session, new Point2(8.5f, 8.0f), "detour east around cardboard");
                yield return WalkTo(session, new Point2(8.5f, 18.0f), "walk through the northeast alley");
                yield return WalkTo(session, new Point2(9.45f, 18.0f), "reach the far-side alley goal", expectFinish: true);
                Assert.IsTrue(session.Finished);
                Assert.AreEqual(MenuScreen.Results, session.Menu.Screen);
                collisionAtFinish = session.Collisions;
                Assert.AreEqual(1, collisionAtFinish, "the cumulative collision count survives recovery and continue");
                yield return CaptureVisible(entry, session, evidenceRoot, "a30-finished.png");

                string resultPath = Path.Combine(evidenceRoot, "a25-a30-results.json");
                File.WriteAllText(resultPath, JsonUtility.ToJson(new A30Result
                {
                    status = "PASS",
                    scene = SceneManager.GetActiveScene().name,
                    saveRoot = saveRoot,
                    levelId = session.Level.Id,
                    checkpoint = session.CheckpointId,
                    simulationTime = session.Clock.Now,
                    collisions = collisionAtFinish,
                    recoveries = session.Recoveries,
                    finished = session.Finished,
                    screenshotScope = "Camera.Render world frames; HUD and menus require separate Player screenshots",
                    screenshots = new[] { "a30-start-explored.png", "a25-paused.png", "a30-real-collision-recovery.png", "a30-continued.png", "a30-finished.png" }
                }, true));
                TestContext.Progress.WriteLine("A25/A30 real-scene evidence: " + resultPath);
            }
            finally
            {
                if (session != null) session.Dispose();
                Environment.SetEnvironmentVariable("TINGLU_STREET_SAVE_ROOT", previousSaveRoot);
                if (Directory.Exists(saveRoot)) Directory.Delete(saveRoot, true);
            }
        }

        private static Behaviour FindStreetEntry()
        {
            MonoBehaviour[] behaviours = Object.FindObjectsOfType<MonoBehaviour>();
            foreach (MonoBehaviour behaviour in behaviours)
                if (behaviour != null && behaviour.GetType().FullName == "TingLu.Bootstrap.StreetEntry")
                    return behaviour;
            return null;
        }

        private static MenuItem FindMenuItem(AccessibleMenu menu, MenuOption action)
        {
            foreach (MenuItem item in menu.Items)
                if (item.Action == action) return item;
            return null;
        }

        private static IEnumerator WalkTo(StreetSession session, Point2 target, string label, bool expectFinish = false)
        {
            int steps = 0;
            while (!session.Finished && StreetGeometry.Distance(session.Player.Current.Position, target) > 0.035f && steps < 2000)
            {
                Point2 from = session.Player.Current.Position;
                float dx = target.X - from.X;
                float dy = target.Y - from.Y;
                float length = StreetGeometry.Distance(from, target);
                Point2 facing = session.Player.Current.Facing;
                float turn = Mathf.DeltaAngle(Mathf.Atan2(facing.X, facing.Y) * Mathf.Rad2Deg,
                    Mathf.Atan2(dx, dy) * Mathf.Rad2Deg);
                // Leave enough space between taps to avoid deliberately triggering confusion
                // while this route verifies checkpoints and vehicle recovery.
                bool probe = session.Clock.Now >= session.Player.Current.CaneReadyAt + 1.1 && session.Player.Current.Focus > 45;
                session.Tick(new StreetInputFrame(new Point2(dx / length, dy / length), turn, probe), 0.02);
                steps++;
                if (steps % 8 == 0) yield return null;
            }
            Assert.Less(steps, 2000, "Could not reach " + label + "; current=" + session.Player.Current.Position.X + "," + session.Player.Current.Position.Y);
            if (expectFinish) Assert.IsTrue(session.Finished, "Walking into the goal radius must finish the level.");
            else Assert.Less(StreetGeometry.Distance(session.Player.Current.Position, target), 0.08f,
                "Stopped short while trying to " + label);
        }

        private static IEnumerator CaptureVisible(StreetEntry entry, StreetSession session, string directory, string fileName)
        {
            Camera camera = entry.PlayerCamera;
            StreetCameraRig rig = camera.GetComponent<StreetCameraRig>();
            StreetRenderer renderer = entry.GetComponent<StreetRenderer>();
            StreetHudPresenter hud = entry.GetComponent<StreetHudPresenter>();
            rig.Follow(session.Player.Current.Position, true);
            renderer.Render(session.Memory.Current, session.Perception, session.Player.Current, session.Settings, session.Clock.Now);
            hud.Present(session.Player.Current, session.Settings, null, session.Menu.Screen == MenuScreen.Playing);
            yield return null;
            string path = Path.Combine(directory, fileName);
            // Camera.Render also works in batchmode, where end-of-frame screen captures
            // do not run reliably. This records the world, not an IMGUI/menu screenshot.
            RenderTexture previous = RenderTexture.active;
            RenderTexture previousTarget = camera.targetTexture;
            RenderTexture target = RenderTexture.GetTemporary(1280, 720, 24);
            Texture2D image = new Texture2D(1280, 720, TextureFormat.RGB24, false);
            try
            {
                camera.targetTexture = target;
                camera.Render();
                RenderTexture.active = target;
                image.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0);
                image.Apply();
                File.WriteAllBytes(path, image.EncodeToPNG());
            }
            finally
            {
                camera.targetTexture = previousTarget;
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(target);
                Object.Destroy(image);
            }
            Assert.IsTrue(File.Exists(path), "Screenshot was not written: " + path);
            TestContext.Progress.WriteLine("Screenshot: " + path);
        }

        private static bool ReadUnityAudioPaused(StreetEntry entry)
        {
            FieldInfo playbackField = typeof(StreetEntry).GetField("playback", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(playbackField);
            object playback = playbackField.GetValue(entry);
            FieldInfo pausedField = playback.GetType().GetField("paused", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(pausedField);
            return (bool)pausedField.GetValue(playback);
        }

        [System.Serializable]
        private sealed class A30Result
        {
            public string status, scene, saveRoot, levelId, checkpoint, screenshotScope;
            public double simulationTime;
            public int collisions, recoveries;
            public bool finished;
            public string[] screenshots;
        }
    }
}
