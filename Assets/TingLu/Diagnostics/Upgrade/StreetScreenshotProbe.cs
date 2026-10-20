#if DEVELOPMENT_BUILD || UNITY_EDITOR
using System;
using System.Collections;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using TingLu.Contracts;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace TingLu.Diagnostics.Upgrade
{
    public sealed class StreetScreenshotProbe : MonoBehaviour
    {
        private const string OutputArgument = "-tinglu-street-probe";
        private const string SaveRootArgument = "-tinglu-street-save-root";
        private static string outputDirectory;
        private bool speechStarted;
        private string speechFailure;
        private double initialProbeMilliseconds = -1;
        private double initialRenderMilliseconds = -1;
        private double feedbackRequestedMilliseconds = -1;
        private double feedbackStartedMilliseconds = -1;
        private SessionSnapshot playingBeforePause;
        private SessionSnapshot pausedSnapshot;
        private SessionSnapshot resumedSnapshot;
        private SessionSnapshot advancedSnapshot;
        private RouteEvidence routeEvidence;
        private string routeFailure;

        private void OnEnable() { Application.logMessageReceived += ObserveLog; }
        private void OnDisable() { Application.logMessageReceived -= ObserveLog; }
        private void ObserveLog(string text, string stack, LogType type)
        {
            if (text.StartsWith("TingLu speech started;", StringComparison.Ordinal)) speechStarted = true;
            if (text.StartsWith("Street audio failed: speech_", StringComparison.Ordinal)) speechFailure = text;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void ConfigureIsolatedSaveRoot()
        {
            string[] args = Environment.GetCommandLineArgs();
            string output = ArgumentValue(args, OutputArgument);
            string saveRoot = ArgumentValue(args, SaveRootArgument);
            if (string.IsNullOrWhiteSpace(output)) return;
            if (!IsAbsolute(output) || !IsAbsolute(saveRoot))
            {
                Debug.LogError("Street probe needs absolute output and isolated save-root paths.");
                return;
            }

            outputDirectory = Path.GetFullPath(output);
            string isolatedSaveRoot = Path.GetFullPath(saveRoot);
            string normalSaveRoot = Path.GetFullPath(Application.persistentDataPath).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (StringComparer.OrdinalIgnoreCase.Equals(isolatedSaveRoot.TrimEnd(Path.DirectorySeparatorChar), normalSaveRoot.TrimEnd(Path.DirectorySeparatorChar)) ||
                isolatedSaveRoot.StartsWith(normalSaveRoot, StringComparison.OrdinalIgnoreCase))
            {
                Debug.LogError("Street probe refuses to use Application.persistentDataPath for saves.");
                outputDirectory = null;
                return;
            }
            Environment.SetEnvironmentVariable("TINGLU_STREET_SAVE_ROOT", isolatedSaveRoot);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void RegisterAfterSceneLoad()
        {
            if (string.IsNullOrEmpty(outputDirectory)) return;
            var host = new GameObject("TingLu street screenshot probe");
            DontDestroyOnLoad(host);
            host.AddComponent<StreetScreenshotProbe>();
        }

        private IEnumerator Start()
        {
            Directory.CreateDirectory(outputDirectory);
            float deadline = Time.realtimeSinceStartup + 30f;
            MonoBehaviour entry = null;
            object session = null;
            while (Time.realtimeSinceStartup < deadline)
            {
                if (SceneManager.GetActiveScene().name == "StreetCrossroads")
                {
                    entry = FindEntry();
                    if (entry != null) session = ReadMember(entry, "Session");
                    if (session != null) break;
                }
                yield return null;
            }
            if (entry == null || session == null)
            {
                WriteReport(new ProbeReport { status = "FAIL", scene = SceneManager.GetActiveScene().name, error = "StreetEntry session was not created within 30 seconds." });
                Application.Quit(1);
                yield break;
            }

            bool entryEnabledAtStartup = entry.enabled;
            entry.enabled = false;
            yield return Capture("street-crossroads-menu.png");
            object menu = ReadMember(session, "Menu");
            Type optionType = Type.GetType("TingLu.UI.MenuOption, TingLu.UI", true);
            menu.GetType().GetMethod("Activate").Invoke(menu, new[] { Enum.Parse(optionType, "Start") });
            yield return null;

            try
            {
                Type frameType = Type.GetType("TingLu.Contracts.StreetInputFrame, TingLu.Contracts", true);
                Type pointType = Type.GetType("TingLu.Contracts.Point2, TingLu.Contracts", true);
                Type menuActionType = Type.GetType("TingLu.Contracts.MenuAction, TingLu.Contracts", true);
                object zero = Activator.CreateInstance(pointType, new object[] { 0f, 0f });
                ConstructorInfo frameConstructor = frameType.GetConstructor(new[] { pointType, typeof(float), typeof(bool), typeof(bool), menuActionType });
                object probe = frameConstructor.Invoke(new[] { zero, 0f, true, false, Enum.Parse(menuActionType, "None") });
                var playback = ReadMember(entry, "playback") as IAudioPlayback;
                if (playback == null) throw new InvalidOperationException("Production audio backend is missing.");
                var timer = System.Diagnostics.Stopwatch.StartNew();
                string feedbackEventId = null;
                Action<AudioReceipt> receiptHandler = receipt =>
                {
                    // The first event inside the initial stationary probe is cane.tap.
                    if (feedbackEventId == null && receipt.Status == AudioStatus.Requested)
                    {
                        feedbackEventId = receipt.EventId;
                        feedbackRequestedMilliseconds = timer.Elapsed.TotalMilliseconds;
                    }
                    if (receipt.EventId == feedbackEventId && receipt.Status == AudioStatus.Started)
                        feedbackStartedMilliseconds = timer.Elapsed.TotalMilliseconds;
                };
                playback.Receipt += receiptHandler;
                try
                {
                    session.GetType().GetMethod("Tick").Invoke(session, new[] { probe, (object)0.02 });
                    initialProbeMilliseconds = timer.Elapsed.TotalMilliseconds;
                }
                finally { playback.Receipt -= receiptHandler; }
            }
            catch (Exception exception)
            {
                WriteReport(BuildReport("FAIL", entry, session, "Could not send one production probe frame: " + exception.GetBaseException().Message,
                    entryEnabledAtStartup, true));
                Application.Quit(1);
                yield break;
            }

            try
            {
                var timer = System.Diagnostics.Stopwatch.StartNew();
                RenderCurrentSession(entry, session);
                initialRenderMilliseconds = timer.Elapsed.TotalMilliseconds;
            }
            catch (Exception exception)
            {
                WriteReport(BuildReport("FAIL", entry, session, "Could not render the explored probe frame: " + exception.GetBaseException().Message,
                    entryEnabledAtStartup, true));
                Application.Quit(1);
                yield break;
            }

            for (int frame = 0; frame < 3; frame++) yield return null;
            yield return Capture("street-crossroads-explored.png");

            // Drive the real menu path with the same input edge used by StreetEntry:
            // Playing -> Esc/PausePressed -> Pause -> Esc/PausePressed -> Playing.
            // Entry.Update remains disabled only so the probe can place deterministic
            // snapshots around the production Tick calls; rendering and menu OnGUI stay live.
            try
            {
                playingBeforePause = Snapshot(session);
                InvokeTick(session, 0f, false, true, 0.02);
                RenderCurrentSession(entry, session);
            }
            catch (Exception exception)
            {
                WriteReport(BuildReport("FAIL", entry, session,
                    "Production PausePressed path failed: " + exception.GetBaseException().Message, entryEnabledAtStartup, true));
                Application.Quit(1);
                yield break;
            }
            yield return Capture("street-crossroads-paused.png");
            try
            {
                // Exercise the paused production gate with elapsed time and live
                // gameplay input, rather than proving only that disabled Update
                // cannot advance the simulation between screenshots.
                for (int frame = 0; frame < 3; frame++)
                    InvokeTickWithMove(session, 1f, 0f, 30f, true, 0.25);
                pausedSnapshot = Snapshot(session);
            }
            catch (Exception exception)
            {
                WriteReport(BuildReport("FAIL", entry, session,
                    "Could not capture paused session state: " + exception.GetBaseException().Message, entryEnabledAtStartup, true));
                Application.Quit(1);
                yield break;
            }

            try
            {
                InvokeTick(session, 0f, false, true, 0.02);
                resumedSnapshot = Snapshot(session);
                RenderCurrentSession(entry, session);
            }
            catch (Exception exception)
            {
                WriteReport(BuildReport("FAIL", entry, session,
                    "Production resume PausePressed path failed: " + exception.GetBaseException().Message, entryEnabledAtStartup, true));
                Application.Quit(1);
                yield break;
            }
            yield return Capture("street-crossroads-resumed.png");

            // A no-input simulation step proves the resumed timeline advances without
            // depending on a particular map direction or colliding with a wall.
            try
            {
                InvokeTick(session, 0f, false, false, 0.25);
                advancedSnapshot = Snapshot(session);
            }
            catch (Exception exception)
            {
                WriteReport(BuildReport("FAIL", entry, session,
                    "Resumed session did not advance: " + exception.GetBaseException().Message, entryEnabledAtStartup, true));
                Application.Quit(1);
                yield break;
            }

            // Continue the same player from spawn to the west checkpoint, around the
            // parked bicycle, then probe the NS lamp and cross the marked crosswalk.
            // Every move is a normal StreetInputFrame Tick; no World import or teleport
            // is used. A route failure is a hard probe failure.
            routeEvidence = new RouteEvidence();
            yield return WalkToProduction(session, new Point2(-5.5f, -11.3f), "approach obstacle");
            if (RouteFailed(entry, session, entryEnabledAtStartup)) yield break;
            yield return WalkToProduction(session, new Point2(-6.5f, -11.3f), "detour west of parked bicycle");
            if (RouteFailed(entry, session, entryEnabledAtStartup)) yield break;
            yield return WalkToProduction(session, new Point2(-6.5f, -8.7f), "pass parked bicycle");
            if (RouteFailed(entry, session, entryEnabledAtStartup)) yield break;
            yield return WalkToProduction(session, new Point2(-5.5f, -8.7f), "rejoin tactile route");
            if (RouteFailed(entry, session, entryEnabledAtStartup)) yield break;
            yield return WalkToProduction(session, new Point2(-5.5f, 5.3f), "reach west checkpoint");
            if (RouteFailed(entry, session, entryEnabledAtStartup)) yield break;
            routeEvidence.westCheckpoint = ReadMember(session, "CheckpointId") as string;
            if (!string.Equals(routeEvidence.westCheckpoint, "checkpoint.west.approach", StringComparison.Ordinal))
            {
                routeFailure = "Expected checkpoint.west.approach, got " + routeEvidence.westCheckpoint;
                if (RouteFailed(entry, session, entryEnabledAtStartup)) yield break;
            }
            yield return WaitForProbeReady(session);
            if (RouteFailed(entry, session, entryEnabledAtStartup)) yield break;
            yield return TurnAndProbeSignal(session);
            if (RouteFailed(entry, session, entryEnabledAtStartup)) yield break;
            RenderCurrentSession(entry, session);
            yield return Capture("street-crossroads-ns-signal-ramp.png");
            yield return WalkToProduction(session, new Point2(5.5f, 5.3f), "cross the north crosswalk to east landing");
            if (RouteFailed(entry, session, entryEnabledAtStartup)) yield break;
            routeEvidence.eastCheckpoint = ReadMember(session, "CheckpointId") as string;
            routeEvidence.routePassed = string.Equals(routeEvidence.eastCheckpoint, "checkpoint.east.landing", StringComparison.Ordinal);
            if (!routeEvidence.routePassed)
            {
                routeFailure = "Expected checkpoint.east.landing, got " + routeEvidence.eastCheckpoint;
                if (RouteFailed(entry, session, entryEnabledAtStartup)) yield break;
            }
            RenderCurrentSession(entry, session);
            yield return Capture("street-crossroads-east-landing.png");

            // Synthesis is asynchronous. A three-frame screenshot used to quit before
            // success/failure could be established, producing misleading audio reports.
            float speechDeadline = Time.realtimeSinceStartup + 25f;
            while (!speechStarted && string.IsNullOrEmpty(speechFailure) && Time.realtimeSinceStartup < speechDeadline)
                yield return null;
            ProbeReport report = BuildReport("PASS", entry, session, null, entryEnabledAtStartup, true);
            report.speechBackend = speechStarted ? "STARTED" : (string.IsNullOrEmpty(speechFailure) ? "TIMEOUT" : "FAIL");
            report.speechError = speechFailure;
            report.initialProbeMilliseconds = initialProbeMilliseconds;
            report.initialRenderMilliseconds = initialRenderMilliseconds;
            report.feedbackRequestedMilliseconds = feedbackRequestedMilliseconds;
            report.feedbackStartedMilliseconds = feedbackStartedMilliseconds;
            report.performanceScope = "Initial stationary production probe; Stopwatch includes reflection overhead. First receipt event inside this no-movement Tick is cane.tap. Started means software AudioSource.Play submission, not physical sound latency; render timing includes camera/renderer/HUD setup and excludes GPU/present.";
            report.screenshotScope = "ScreenCapture.CaptureScreenshot after WaitForEndOfFrame captures the requested Player backbuffer, including live AccessibleMenuPanel IMGUI; paused and resumed captures are from the same session. External verifier checks exact requested dimensions.";
            report.screenWidth = Screen.width;
            report.screenHeight = Screen.height;
            report.fullScreen = Screen.fullScreen;
            report.menuScreenshot = Path.Combine(outputDirectory, "street-crossroads-menu.png");
            report.exploredScreenshot = Path.Combine(outputDirectory, "street-crossroads-explored.png");
            report.playingScreenshot = Path.Combine(outputDirectory, "street-crossroads-explored.png");
            report.pausedScreenshot = Path.Combine(outputDirectory, "street-crossroads-paused.png");
            report.resumedScreenshot = Path.Combine(outputDirectory, "street-crossroads-resumed.png");
            report.signalScreenshot = Path.Combine(outputDirectory, "street-crossroads-ns-signal-ramp.png");
            report.eastLandingScreenshot = Path.Combine(outputDirectory, "street-crossroads-east-landing.png");
            report.screenshotsCreated = File.Exists(report.menuScreenshot) && File.Exists(report.exploredScreenshot) &&
                File.Exists(report.pausedScreenshot) && File.Exists(report.resumedScreenshot) &&
                File.Exists(report.signalScreenshot) && File.Exists(report.eastLandingScreenshot);
            report.pauseEvidence = BuildPauseEvidence();
            report.routeEvidence = routeEvidence;
            report.status = report.screenshotsCreated && report.entryEnabled && report.sessionCreated &&
                report.running && report.cameraCount == 1 && report.listenerCount == 1 &&
                report.pauseEvidence != null && report.pauseEvidence.menuPlayingBeforePause && report.pauseEvidence.pauseFrozen &&
                report.pauseEvidence.menuPaused && report.pauseEvidence.menuPlayingAfterResume &&
                report.pauseEvidence.resumeSnapshotPreserved && report.pauseEvidence.resumeAdvanced &&
                routeEvidence != null && routeEvidence.routePassed && routeEvidence.signalCount > 0 &&
                routeEvidence.nsSignalLive ? "PASS" : "FAIL";
            WriteReport(report);
            Debug.Log("TingLu street screenshot probe " + report.status + "; output=" + outputDirectory);
            Application.Quit(report.status == "PASS" ? 0 : 1);
        }

        private static void RenderCurrentSession(MonoBehaviour entry, object session)
        {
            object player = ReadMember(session, "Player");
            object current = ReadMember(player, "Current");
            object position = ReadMember(current, "Position");
            object cameraRig = ReadMember(entry, "cameraRig");
            object renderer = ReadMember(entry, "rendererView");
            object hud = ReadMember(entry, "hud");
            object memory = ReadMember(ReadMember(session, "Memory"), "Current");
            object perception = ReadMember(session, "Perception");
            object settings = ReadMember(session, "Settings");
            object clock = ReadMember(session, "Clock");

            InvokeCompatible(cameraRig, "Follow", position, true);
            object menu = ReadMember(session, "Menu");
            object signals = ReadMember(session, "Signals");
            InvokeCompatible(renderer, "Render", memory, perception, current, settings, ReadMember(clock, "Now"), signals);
            InvokeCompatible(hud, "Present", current, settings, null,
                menu != null && string.Equals(ReadMember(menu, "Screen")?.ToString(), "Playing", StringComparison.Ordinal));
        }

        private static void InvokeTick(object session, float turn, bool probe, bool pause, double delta)
        {
            Type frameType = Type.GetType("TingLu.Contracts.StreetInputFrame, TingLu.Contracts", true);
            Type pointType = Type.GetType("TingLu.Contracts.Point2, TingLu.Contracts", true);
            Type menuActionType = Type.GetType("TingLu.Contracts.MenuAction, TingLu.Contracts", true);
            object zero = Activator.CreateInstance(pointType, new object[] { 0f, 0f });
            ConstructorInfo constructor = frameType.GetConstructor(new[] { pointType, typeof(float), typeof(bool), typeof(bool), menuActionType });
            object frame = constructor.Invoke(new[] { zero, (object)turn, (object)probe, (object)pause, Enum.Parse(menuActionType, "None") });
            session.GetType().GetMethod("Tick").Invoke(session, new[] { frame, (object)delta });
        }

        private static SessionSnapshot Snapshot(object session)
        {
            object clock = ReadMember(session, "Clock");
            object world = ReadMember(session, "World");
            object worldFrame = InvokeNoArguments(world, "Capture");
            object worldObjects = ReadMember(worldFrame, "Objects");
            object player = ReadMember(session, "Player");
            object current = ReadMember(player, "Current");
            object position = ReadMember(current, "Position");
            object facing = ReadMember(current, "Facing");
            object memory = ReadMember(session, "Memory");
            object memoryFrame = ReadMember(memory, "Current");
            object menu = ReadMember(session, "Menu");
            object phase = ReadMember(world, "SignalPhase");
            object nsSignal = null;
            object signals = ReadMember(session, "Signals");
            if (signals is IEnumerable)
                foreach (object signal in (IEnumerable)signals)
                    if (string.Equals(ReadMember(signal, "ObjectId") as string, "street.signal.ns", StringComparison.Ordinal))
                    {
                        nsSignal = signal;
                        break;
                    }
            return new SessionSnapshot {
                sessionIdentity = System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(session).ToString(System.Globalization.CultureInfo.InvariantCulture),
                simulationTime = Convert.ToDouble(ReadMember(clock, "Now")),
                worldTime = Convert.ToDouble(ReadMember(worldFrame, "Time")),
                playerX = Convert.ToSingle(ReadMember(position, "X")),
                playerY = Convert.ToSingle(ReadMember(position, "Y")),
                facingX = Convert.ToSingle(ReadMember(facing, "X")),
                facingY = Convert.ToSingle(ReadMember(facing, "Y")),
                phase = phase == null ? "" : phase.ToString(),
                signalPhase = phase == null ? "" : phase.ToString(),
                signalObjectId = nsSignal == null ? "" : ReadMember(nsSignal, "ObjectId") as string,
                signalIsGreen = nsSignal != null && Convert.ToBoolean(ReadMember(nsSignal, "IsGreen")),
                signalObservedAt = nsSignal == null ? 0 : Convert.ToDouble(ReadMember(nsSignal, "ObservedAt")),
                signalExpiresAt = nsSignal == null ? 0 : Convert.ToDouble(ReadMember(nsSignal, "ExpiresAt")),
                menu = ReadMember(menu, "Screen")?.ToString(),
                clockPaused = Convert.ToBoolean(ReadMember(clock, "Paused")),
                memoryFingerprint = Fingerprint(memoryFrame),
                worldFingerprint = FingerprintWorld(worldObjects)
            };
        }

        private PauseEvidence BuildPauseEvidence()
        {
            if (playingBeforePause == null || pausedSnapshot == null || resumedSnapshot == null || advancedSnapshot == null) return null;
            bool Same(SessionSnapshot a, SessionSnapshot b)
            {
                return Math.Abs(a.simulationTime - b.simulationTime) <= 1e-7 &&
                    string.Equals(a.sessionIdentity, b.sessionIdentity, StringComparison.Ordinal) &&
                    Math.Abs(a.worldTime - b.worldTime) <= 1e-7 &&
                    Math.Abs(a.playerX - b.playerX) <= 1e-6f && Math.Abs(a.playerY - b.playerY) <= 1e-6f &&
                    Math.Abs(a.facingX - b.facingX) <= 1e-6f && Math.Abs(a.facingY - b.facingY) <= 1e-6f &&
                    string.Equals(a.phase, b.phase, StringComparison.Ordinal) &&
                    string.Equals(a.signalPhase, b.signalPhase, StringComparison.Ordinal) &&
                    string.Equals(a.signalObjectId, b.signalObjectId, StringComparison.Ordinal) &&
                    a.signalIsGreen == b.signalIsGreen &&
                    Math.Abs(a.signalObservedAt - b.signalObservedAt) <= 1e-7 &&
                    Math.Abs(a.signalExpiresAt - b.signalExpiresAt) <= 1e-7 &&
                    string.Equals(a.memoryFingerprint, b.memoryFingerprint, StringComparison.Ordinal) &&
                    string.Equals(a.worldFingerprint, b.worldFingerprint, StringComparison.Ordinal);
            }
            return new PauseEvidence {
                menuPlayingBeforePause = playingBeforePause.menu == "Playing" && !playingBeforePause.clockPaused,
                menuPaused = pausedSnapshot.menu == "Pause" && pausedSnapshot.clockPaused,
                menuPlayingAfterResume = resumedSnapshot.menu == "Playing" && !resumedSnapshot.clockPaused,
                pauseFrozen = Same(playingBeforePause, pausedSnapshot),
                resumeSnapshotPreserved = Same(playingBeforePause, resumedSnapshot),
                resumeAdvanced = advancedSnapshot.simulationTime > resumedSnapshot.simulationTime + 0.2 &&
                    advancedSnapshot.worldTime > resumedSnapshot.worldTime + 0.2,
                inputRoute = "StreetSession.Tick(StreetInputFrame.PausePressed) -> production AccessibleMenu.HandleInput -> Pause/Resume callbacks; physical keyboard injection is not claimed",
                before = playingBeforePause,
                paused = pausedSnapshot,
                resumed = resumedSnapshot,
                advanced = advancedSnapshot
            };
        }

        private static string Fingerprint(object frame)
        {
            if (frame == null) return "null";
            var builder = new StringBuilder();
            foreach (string collectionName in new[] { "Surfaces", "Objects" })
            {
                object collection = ReadMember(frame, collectionName);
                if (collection == null) { builder.Append(collectionName).Append(":null;"); continue; }
                builder.Append(collectionName).Append(':');
                foreach (object item in (IEnumerable)collection)
                {
                    builder.Append(ReadMember(item, "MaterialId")).Append('|').Append(ReadMember(item, "ObjectId"))
                        .Append('|').Append(ReadMember(item, "Kind"))
                        .Append('|').Append(Format(ReadMember(item, "ObservedAt")))
                        .Append('|').Append(Format(ReadMember(item, "CurrentUntil")))
                        .Append('|').Append(Format(ReadMember(item, "Height")))
                        .Append('|').Append(Format(ReadMember(item, "HeightBase")))
                        .Append('|').Append(Format(ReadMember(item, "HeightSlopeX")))
                        .Append('|').Append(Format(ReadMember(item, "HeightSlopeY")));
                    object textureAxis = ReadMember(item, "TextureAxis");
                    if (textureAxis != null) builder.Append('|').Append(Format(ReadMember(textureAxis, "X")))
                        .Append(',').Append(Format(ReadMember(textureAxis, "Y")));
                    object polygon = ReadMember(item, "Polygon");
                    if (polygon != null) foreach (object point in (IEnumerable)polygon)
                        builder.Append('|').Append(Format(ReadMember(point, "X")))
                            .Append(',').Append(Format(ReadMember(point, "Y")));
                    object contour = ReadMember(item, "Contour");
                    if (contour != null) foreach (object segment in (IEnumerable)contour)
                    {
                        object a = ReadMember(segment, "A"), b = ReadMember(segment, "B");
                        builder.Append('|').Append(Format(ReadMember(a, "X"))).Append(',').Append(Format(ReadMember(a, "Y")))
                            .Append('>').Append(Format(ReadMember(b, "X"))).Append(',').Append(Format(ReadMember(b, "Y")));
                    }
                    builder.Append(';');
                }
            }
            using (SHA256 sha = SHA256.Create())
                return ToHex(sha.ComputeHash(Encoding.UTF8.GetBytes(builder.ToString())));
        }

        private static string FingerprintWorld(object objects)
        {
            if (objects == null) return "null";
            var builder = new StringBuilder();
            foreach (object item in (IEnumerable)objects)
            {
                builder.Append(ReadMember(item, "Id")).Append('|').Append(ReadMember(item, "SoundActive"));
                object position = ReadMember(item, "Position");
                builder.Append('|').Append(Format(ReadMember(position, "X")))
                    .Append(',').Append(Format(ReadMember(position, "Y"))).Append(';');
            }
            using (SHA256 sha = SHA256.Create())
                return ToHex(sha.ComputeHash(Encoding.UTF8.GetBytes(builder.ToString())));
        }

        private static string ToHex(byte[] bytes)
        {
            var result = new StringBuilder(bytes.Length * 2);
            for (int i = 0; i < bytes.Length; i++) result.Append(bytes[i].ToString("x2"));
            return result.ToString();
        }

        private static string Format(object value)
        {
            if (value == null) return "null";
            if (value.GetType().IsEnum) return value.ToString();
            if (value is float || value is double || value is decimal)
                return ((IFormattable)value).ToString("R", System.Globalization.CultureInfo.InvariantCulture);
            if (value is IFormattable) return ((IFormattable)value).ToString(null, System.Globalization.CultureInfo.InvariantCulture);
            return value.ToString();
        }

        private IEnumerator WalkToProduction(object session, Point2 target, string label)
        {
            int steps = 0;
            while (string.IsNullOrEmpty(routeFailure) && !(bool)ReadMember(session, "Finished") &&
                Distance(ReadMember(ReadMember(ReadMember(session, "Player"), "Current"), "Position"), target) > 0.035f && steps < 2000)
            {
                try
                {
                    object player = ReadMember(session, "Player");
                    object current = ReadMember(player, "Current");
                    object position = ReadMember(current, "Position");
                    object facing = ReadMember(current, "Facing");
                    float dx = target.X - Convert.ToSingle(ReadMember(position, "X"));
                    float dy = target.Y - Convert.ToSingle(ReadMember(position, "Y"));
                    float length = (float)Math.Sqrt(dx * dx + dy * dy);
                    if (length <= 0.035f) break;
                    float moveX = dx / length, moveY = dy / length;
                    float currentAngle = (float)(Math.Atan2(Convert.ToSingle(ReadMember(facing, "X")), Convert.ToSingle(ReadMember(facing, "Y"))) * 180.0 / Math.PI);
                    float targetAngle = (float)(Math.Atan2(dx, dy) * 180.0 / Math.PI);
                    float turn = DeltaAngle(currentAngle, targetAngle);
                    object clock = ReadMember(session, "Clock");
                    object currentState = ReadMember(player, "Current");
                    double now = Convert.ToDouble(ReadMember(clock, "Now"));
                    double ready = Convert.ToDouble(ReadMember(currentState, "CaneReadyAt"));
                    float focus = Convert.ToSingle(ReadMember(currentState, "Focus"));
                    bool probe = now >= ready + 1.1 && focus > 45f;
                    InvokeTickWithMove(session, moveX, moveY, turn, probe, 0.02);
                    steps++;
                    routeEvidence.totalSteps++;
                }
                catch (Exception exception) { routeFailure = label + ": " + exception.GetBaseException().Message; }
                if (steps % 8 == 0) yield return null;
            }
            if (string.IsNullOrEmpty(routeFailure) && (bool)ReadMember(session, "Finished")) { routeFailure = label + ": session finished before target"; yield break; }
            object finalPosition = ReadMember(ReadMember(ReadMember(session, "Player"), "Current"), "Position");
            float remaining = Distance(finalPosition, target);
            if (string.IsNullOrEmpty(routeFailure) && (steps >= 2000 || remaining > 0.08f))
                routeFailure = label + ": target not reached; remaining=" + remaining.ToString("F3", System.Globalization.CultureInfo.InvariantCulture);
        }

        private IEnumerator WaitForProbeReady(object session)
        {
            int steps = 0;
            bool probeReady = false;
            while (string.IsNullOrEmpty(routeFailure) && !probeReady && steps++ < 400)
            {
                try
                {
                    object player = ReadMember(session, "Player");
                    object current = ReadMember(player, "Current");
                    double now = Convert.ToDouble(ReadMember(ReadMember(session, "Clock"), "Now"));
                    double ready = Convert.ToDouble(ReadMember(current, "CaneReadyAt"));
                    float focus = Convert.ToSingle(ReadMember(current, "Focus"));
                    probeReady = now >= ready + 1.1 && focus > 45f;
                    if (!probeReady)
                    {
                        InvokeTickWithMove(session, 0f, 0f, 0f, false, 0.02);
                        routeEvidence.totalSteps++;
                    }
                }
                catch (Exception exception) { routeFailure = "Waiting for NS lamp probe failed: " + exception.GetBaseException().Message; }
                if (!probeReady && string.IsNullOrEmpty(routeFailure) && steps % 8 == 0) yield return null;
            }
            if (probeReady || !string.IsNullOrEmpty(routeFailure)) yield break;
            if (string.IsNullOrEmpty(routeFailure)) routeFailure = "NS lamp probe did not become ready within 8 seconds.";
        }

        private IEnumerator TurnAndProbeSignal(object session)
        {
            try
            {
                object player = ReadMember(session, "Player");
                object current = ReadMember(player, "Current");
                object facing = ReadMember(current, "Facing");
                float currentAngle = (float)(Math.Atan2(Convert.ToSingle(ReadMember(facing, "X")), Convert.ToSingle(ReadMember(facing, "Y"))) * 180.0 / Math.PI);
                float turn = DeltaAngle(currentAngle, -90f);
                InvokeTickWithMove(session, 0f, 0f, turn, true, 0.02);
                object world = ReadMember(session, "World");
                routeEvidence.signalPhase = ReadMember(world, "SignalPhase")?.ToString();
                routeEvidence.signalProbeTime = Convert.ToDouble(ReadMember(ReadMember(session, "Clock"), "Now"));
                object signals = ReadMember(session, "Signals");
                int signalCount = signals is ICollection ? ((ICollection)signals).Count : 0;
                routeEvidence.signalCount = signalCount;
                if (signalCount == 0) routeFailure = "NS lamp probe returned no live signal observation.";
                else foreach (object signal in (IEnumerable)signals)
                {
                    string id = ReadMember(signal, "ObjectId") as string;
                    double now = Convert.ToDouble(ReadMember(ReadMember(session, "Clock"), "Now"));
                    double observed = Convert.ToDouble(ReadMember(signal, "ObservedAt"));
                    double expires = Convert.ToDouble(ReadMember(signal, "ExpiresAt"));
                    if (expires <= now || observed > now) { routeFailure = "NS lamp observation is stale at capture."; break; }
                    if (id == "street.signal.ns")
                    {
                        routeEvidence.nsSignalLive = true;
                        routeEvidence.signalObjectId = id;
                        routeEvidence.signalIsGreen = Convert.ToBoolean(ReadMember(signal, "IsGreen"));
                        routeEvidence.signalObservedAt = observed;
                        routeEvidence.signalExpiresAt = expires;
                    }
                }
                if (string.IsNullOrEmpty(routeFailure) && !routeEvidence.nsSignalLive) routeFailure = "Live probe omitted street.signal.ns.";
            }
            catch (Exception exception) { routeFailure = "NS lamp probe failed: " + exception.GetBaseException().Message; }
            yield return null;
        }

        private bool RouteFailed(MonoBehaviour entry, object session, bool entryEnabledAtStartup)
        {
            if (string.IsNullOrEmpty(routeFailure)) return false;
            ProbeReport report = BuildReport("FAIL", entry, session, "Real route fixture failed: " + routeFailure, entryEnabledAtStartup, true);
            report.routeEvidence = routeEvidence;
            report.signalScreenshot = Path.Combine(outputDirectory, "street-crossroads-ns-signal-ramp.png");
            report.eastLandingScreenshot = Path.Combine(outputDirectory, "street-crossroads-east-landing.png");
            WriteReport(report);
            Application.Quit(1);
            return true;
        }

        private static void InvokeTickWithMove(object session, float moveX, float moveY, float turn, bool probe, double delta)
        {
            Type frameType = Type.GetType("TingLu.Contracts.StreetInputFrame, TingLu.Contracts", true);
            Type pointType = Type.GetType("TingLu.Contracts.Point2, TingLu.Contracts", true);
            Type menuActionType = Type.GetType("TingLu.Contracts.MenuAction, TingLu.Contracts", true);
            object move = Activator.CreateInstance(pointType, new object[] { moveX, moveY });
            ConstructorInfo constructor = frameType.GetConstructor(new[] { pointType, typeof(float), typeof(bool), typeof(bool), menuActionType });
            object frame = constructor.Invoke(new[] { move, (object)turn, (object)probe, false, Enum.Parse(menuActionType, "None") });
            session.GetType().GetMethod("Tick").Invoke(session, new[] { frame, (object)delta });
        }

        private static float Distance(object point, Point2 target)
        {
            float dx = Convert.ToSingle(ReadMember(point, "X")) - target.X;
            float dy = Convert.ToSingle(ReadMember(point, "Y")) - target.Y;
            return (float)Math.Sqrt(dx * dx + dy * dy);
        }

        private static float DeltaAngle(float current, float target)
        {
            float delta = target - current;
            while (delta > 180f) delta -= 360f;
            while (delta < -180f) delta += 360f;
            return delta;
        }

        private IEnumerator Capture(string fileName)
        {
            string path = Path.Combine(outputDirectory, fileName);
            yield return new WaitForEndOfFrame();
            ScreenCapture.CaptureScreenshot(path, 1);
            for (int frame = 0; frame < 180 && !File.Exists(path); frame++) yield return null;
        }

        private static ProbeReport BuildReport(string status, MonoBehaviour entry, object session, string error,
            bool entryEnabledAtStartup, bool disabledForCapture)
        {
            object menu = ReadMember(session, "Menu");
            object level = ReadMember(session, "Level");
            object clock = ReadMember(session, "Clock");
            object player = ReadMember(session, "Player");
            object current = player == null ? null : ReadMember(player, "Current");
            object position = current == null ? null : ReadMember(current, "Position");
            object world = ReadMember(session, "World");
            object worldFrame = InvokeNoArguments(world, "Capture");
            object worldObjects = ReadMember(worldFrame, "Objects");
            System.Collections.ICollection objects = worldObjects as System.Collections.ICollection;
            object menuScreen = ReadMember(menu, "Screen");
            object running = ReadMember(session, "Running");
            object now = ReadMember(clock, "Now");
            object collisions = ReadMember(session, "Collisions");
            object recoveries = ReadMember(session, "Recoveries");
            object finished = ReadMember(session, "Finished");
            return new ProbeReport
            {
                status = status,
                error = error,
                scene = SceneManager.GetActiveScene().name,
                entryEnabled = entryEnabledAtStartup,
                entryDisabledForCapture = disabledForCapture,
                sessionCreated = session != null,
                menu = menuScreen == null ? null : menuScreen.ToString(),
                levelId = level == null ? null : ReadMember(level, "Id") as string,
                running = running is bool && (bool)running,
                simulationTime = now == null ? -1 : Convert.ToDouble(now),
                playerX = position == null ? 0 : Convert.ToSingle(ReadMember(position, "X")),
                playerY = position == null ? 0 : Convert.ToSingle(ReadMember(position, "Y")),
                collisions = Convert.ToInt32(collisions ?? 0),
                recoveries = Convert.ToInt32(recoveries ?? 0),
                finished = finished is bool && (bool)finished,
                objectCount = objects == null ? 0 : objects.Count,
                cameraCount = UnityEngine.Object.FindObjectsOfType<Camera>().Length,
                listenerCount = UnityEngine.Object.FindObjectsOfType<AudioListener>().Length
            };
        }

        private static MonoBehaviour FindEntry()
        {
            foreach (MonoBehaviour behaviour in UnityEngine.Object.FindObjectsOfType<MonoBehaviour>())
                if (behaviour != null && behaviour.GetType().FullName == "TingLu.Bootstrap.StreetEntry") return behaviour;
            return null;
        }

        private static object ReadMember(object target, string name)
        {
            if (target == null) return null;
            const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
            PropertyInfo property = target.GetType().GetProperty(name, flags);
            if (property != null) return property.GetValue(target, null);
            FieldInfo field = target.GetType().GetField(name, flags);
            return field == null ? null : field.GetValue(target);
        }

        private static object InvokeNoArguments(object target, string name)
        {
            if (target == null) return null;
            MethodInfo method = target.GetType().GetMethod(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            return method == null ? null : method.Invoke(target, null);
        }

        private static object InvokeCompatible(object target, string name, params object[] arguments)
        {
            if (target == null) throw new InvalidOperationException("Missing target for " + name + ".");
            const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
            MethodInfo[] methods = target.GetType().GetMethods(flags);
            for (int i = 0; i < methods.Length; i++)
            {
                if (methods[i].Name != name) continue;
                ParameterInfo[] parameters = methods[i].GetParameters();
                if (parameters.Length != arguments.Length) continue;
                bool compatible = true;
                for (int j = 0; j < parameters.Length; j++)
                {
                    if (arguments[j] == null ? parameters[j].ParameterType.IsValueType :
                        !parameters[j].ParameterType.IsInstanceOfType(arguments[j]))
                    {
                        compatible = false;
                        break;
                    }
                }
                if (compatible) return methods[i].Invoke(target, arguments);
            }
            throw new MissingMethodException(target.GetType().FullName, name);
        }

        private static string ArgumentValue(string[] args, string name)
        {
            for (int i = 0; i + 1 < args.Length; i++) if (args[i] == name) return args[i + 1];
            return null;
        }

        private static bool IsAbsolute(string path) { return !string.IsNullOrWhiteSpace(path) && Path.IsPathRooted(path); }

        private void WriteReport(ProbeReport report)
        { File.WriteAllText(Path.Combine(outputDirectory, "street-probe-results.json"), JsonUtility.ToJson(report, true)); }

        [Serializable]
        private sealed class ProbeReport
        {
            public string status, error, scene, menu, levelId, menuScreenshot, exploredScreenshot, playingScreenshot, pausedScreenshot, resumedScreenshot, signalScreenshot, eastLandingScreenshot, screenshotScope, speechBackend, speechError;
            public bool entryEnabled, entryDisabledForCapture, sessionCreated, running, finished, screenshotsCreated, fullScreen;
            public double simulationTime;
            public double initialProbeMilliseconds, initialRenderMilliseconds, feedbackRequestedMilliseconds, feedbackStartedMilliseconds;
            public string performanceScope;
            public int collisions, recoveries, objectCount, cameraCount, listenerCount, screenWidth, screenHeight;
            public float playerX, playerY;
            public PauseEvidence pauseEvidence;
            public RouteEvidence routeEvidence;
        }

        [Serializable]
        private sealed class SessionSnapshot
        {
            public string sessionIdentity, menu, phase, signalPhase, signalObjectId, memoryFingerprint, worldFingerprint;
            public double simulationTime, worldTime;
            public double signalObservedAt, signalExpiresAt;
            public float playerX, playerY, facingX, facingY;
            public bool clockPaused, signalIsGreen;
        }

        [Serializable]
        private sealed class PauseEvidence
        {
            public bool menuPlayingBeforePause, menuPaused, menuPlayingAfterResume, pauseFrozen, resumeSnapshotPreserved, resumeAdvanced;
            public string inputRoute;
            public SessionSnapshot before, paused, resumed, advanced;
        }

        [Serializable]
        private sealed class RouteEvidence
        {
            public bool routePassed, nsSignalLive, signalIsGreen;
            public string westCheckpoint, eastCheckpoint, signalPhase, signalObjectId;
            public int signalCount, totalSteps;
            public double signalProbeTime, signalObservedAt, signalExpiresAt;
        }
    }
}
#endif
