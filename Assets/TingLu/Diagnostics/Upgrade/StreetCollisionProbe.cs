#if DEVELOPMENT_BUILD || UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using TingLu.Config;
using TingLu.Contracts;
using TingLu.Levels;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace TingLu.Diagnostics.Upgrade
{
    /// <summary>
    /// Development Player evidence for real StreetSession contacts and collision feedback.
    /// All contact, checkpoint, memory, and feedback changes are produced by production Tick calls.
    /// </summary>
    public sealed class StreetCollisionProbe : MonoBehaviour
    {
        private const string OutputArgument = "-tinglu-collision-probe";
        private const string SaveRootArgument = "-tinglu-street-save-root";
        private static string outputDirectory;
        private static string isolatedSaveRoot;

        private readonly ProbeReport report = new ProbeReport();
        private readonly List<string> playerErrors = new List<string>();
        private readonly List<string> screenshots = new List<string>();
        private MonoBehaviour entry;
        private object session;
        private bool failed;

        private void OnEnable() { Application.logMessageReceived += ObserveLog; }
        private void OnDisable() { Application.logMessageReceived -= ObserveLog; }

        private void ObserveLog(string message, string stack, LogType type)
        {
            if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)
                playerErrors.Add(type + ": " + message);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void ConfigureIsolatedSaveRoot()
        {
            string[] args = Environment.GetCommandLineArgs();
            string output = ArgumentValue(args, OutputArgument);
            if (string.IsNullOrWhiteSpace(output)) return;
            string saveRoot = ArgumentValue(args, SaveRootArgument);
            if (!IsAbsolute(output) || !IsAbsolute(saveRoot))
            {
                Debug.LogError("Street collision probe requires absolute output and isolated save-root paths.");
                return;
            }

            outputDirectory = Path.GetFullPath(output);
            isolatedSaveRoot = Path.GetFullPath(saveRoot);
            string normalSaveRoot = Path.GetFullPath(Application.persistentDataPath).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            string saveRootWithoutTrailingSeparator = isolatedSaveRoot.TrimEnd(Path.DirectorySeparatorChar);
            if (StringComparer.OrdinalIgnoreCase.Equals(saveRootWithoutTrailingSeparator, normalSaveRoot.TrimEnd(Path.DirectorySeparatorChar)) ||
                saveRootWithoutTrailingSeparator.StartsWith(normalSaveRoot, StringComparison.OrdinalIgnoreCase) ||
                StringComparer.OrdinalIgnoreCase.Equals(outputDirectory.TrimEnd(Path.DirectorySeparatorChar), saveRootWithoutTrailingSeparator) ||
                outputDirectory.StartsWith(saveRootWithoutTrailingSeparator + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            {
                Debug.LogError("Street collision probe refuses a production save path or overlapping evidence/save directories.");
                outputDirectory = null;
                isolatedSaveRoot = null;
                return;
            }
            Environment.SetEnvironmentVariable("TINGLU_STREET_SAVE_ROOT", isolatedSaveRoot);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void RegisterAfterSceneLoad()
        {
            if (string.IsNullOrEmpty(outputDirectory)) return;
            var host = new GameObject("TingLu street collision probe");
            DontDestroyOnLoad(host);
            host.AddComponent<StreetCollisionProbe>();
        }

        private IEnumerator Start()
        {
            IEnumerator work = RunProbe();
            while (true)
            {
                bool moved = false;
                object current = null;
                Exception error = null;
                try
                {
                    moved = work.MoveNext();
                    if (moved) current = work.Current;
                }
                catch (Exception exception) { error = exception; }

                if (error != null)
                {
                    Fail("Probe sequence failed: " + error.GetBaseException().Message);
                    break;
                }
                if (!moved) break;
                yield return current;
            }

            report.playerErrors = playerErrors.ToArray();
            if (!failed && playerErrors.Count > 0) Fail("Player logged runtime errors during collision evidence capture.");
            report.status = failed ? "FAIL" : "PASS";
            report.scene = SceneManager.GetActiveScene().name;
            report.screenWidth = Screen.width;
            report.screenHeight = Screen.height;
            report.screenshots = screenshots.ToArray();
            report.completedAtUtc = DateTime.UtcNow.ToString("o");
            WriteReport();
            Application.Quit(failed ? 1 : 0);
        }

        private IEnumerator RunProbe()
        {
            if (!StringComparer.Ordinal.Equals(SceneManager.GetActiveScene().name, "StreetCrossroads"))
            {
                float sceneDeadline = Time.realtimeSinceStartup + 30f;
                while (!StringComparer.Ordinal.Equals(SceneManager.GetActiveScene().name, "StreetCrossroads") &&
                    Time.realtimeSinceStartup < sceneDeadline) yield return null;
            }
            if (!StringComparer.Ordinal.Equals(SceneManager.GetActiveScene().name, "StreetCrossroads"))
                throw new InvalidOperationException("StreetCrossroads scene did not load within 30 seconds.");

            Directory.CreateDirectory(outputDirectory);
            Directory.CreateDirectory(isolatedSaveRoot);
            report.outputDirectory = outputDirectory;
            report.isolatedSaveRoot = isolatedSaveRoot;
            report.screenWidth = Screen.width;
            report.screenHeight = Screen.height;
            report.entryIsolation = new EntryIsolationEvidence();

            Type entryType = Type.GetType("TingLu.Bootstrap.StreetEntry, TingLu.Bootstrap", true);
            foreach (MonoBehaviour component in FindObjectsOfType<MonoBehaviour>())
            {
                if (!entryType.IsInstanceOfType(component) || !component.enabled) continue;
                // StreetEntry owns the production camera, listener, renderer, and HUD children.
                // Disable its root in this test process so the fixture cannot double-render or
                // leave a second active AudioListener in the scene.
                component.gameObject.SetActive(false);
                report.entryIsolation.disabledProductionEntryCount++;
            }

            StreetLevelDocument fixture = CreateFixture();
            string fixtureJson = StreetLevelCodec.Encode(fixture);
            StreetLevelCodec.Compile(StreetLevelCodec.Decode(fixtureJson));
            report.fixtureCodecRoundTrip = true;
            report.fixtureJsonSha256 = Sha256(Encoding.UTF8.GetBytes(fixtureJson));
            File.WriteAllText(Path.Combine(outputDirectory, "collision-fixture.json"), fixtureJson, new UTF8Encoding(false));

            var entryObject = new GameObject("Street collision fixture entry");
            entry = entryObject.AddComponent(entryType) as MonoBehaviour;
            if (entry == null) throw new InvalidOperationException("Could not create a StreetEntry fixture component.");
            string fixtureSave = Path.Combine(isolatedSaveRoot, "fixture-preview");
            entryType.GetMethod("ConfigurePreview", BindingFlags.Instance | BindingFlags.Public)
                .Invoke(entry, new object[] { fixtureJson, fixtureSave });

            float sessionDeadline = Time.realtimeSinceStartup + 30f;
            while (Time.realtimeSinceStartup < sessionDeadline)
            {
                session = ReadMember(entry, "Session");
                if (session != null && Convert.ToBoolean(ReadMember(session, "Running"))) break;
                yield return null;
            }
            if (session == null || !Convert.ToBoolean(ReadMember(session, "Running")))
                throw new InvalidOperationException("Preview StreetSession did not start within 30 seconds.");

            entry.enabled = false;
            report.entryIsolation.previewEntryCreated = true;
            report.entryIsolation.previewSaveDirectory = fixtureSave;
            report.levelId = Convert.ToString(ReadMember(ReadMember(session, "Level"), "Id"));
            report.actorInventory = DescribeActors(fixture);
            report.initialWorld = WorldSnapshot(session);
            report.initialMemory = MemorySnapshot(session);
            report.initialUnknownZone = new UnknownZoneEvidence
            {
                signalWasUnknown = !Contains(report.initialMemory.objectIds, "street.signal.ns"),
                memoryObjectCount = report.initialMemory.objectIds.Length,
                note = "The initial frame is drawn from player memory; the distant signal and unseen world geometry are not imported into the renderer."
            };
            report.unknownZone = report.initialUnknownZone;
            if (!report.initialUnknownZone.signalWasUnknown)
                throw new InvalidOperationException("Fixture signal was known before the player explored its region.");
            Present();
            yield return Capture("00-unknown-before-probe.png");

            // Let every routed actor move for half a simulation second while the player remains still.
            Dictionary<string, PointEvidence> actorPositionsBefore = CaptureRoutedActorPositions(session, fixture);
            for (int i = 0; i < 5; i++)
            {
                Tick(0f, 0f, 0f, false, false, 0.1);
                Present();
                yield return null;
            }
            report.routedActorMotion = CompareRoutedActorPositions(actorPositionsBefore, CaptureRoutedActorPositions(session, fixture));
            AssertRoutesMoved(report.routedActorMotion);

            Tick(0f, 0f, 0f, true, false, 0.02);
            Present();
            report.caneProbe = MemorySnapshot(session);
            report.unknownZone.signalRemainedUnknown = !Contains(report.caneProbe.objectIds, "street.signal.ns");
            report.caneProbe.revealedNorthWall = Contains(report.caneProbe.objectIds, "wall.north");
            if (!report.caneProbe.revealedNorthWall || !report.unknownZone.signalRemainedUnknown)
                throw new InvalidOperationException("A real cane probe did not reveal the nearby wall while preserving the distant unknown zone.");
            yield return Capture("01-real-cane-probe.png");

            // The moving car starts west of the safe spawn and must cross the player through World.Tick.
            int recoveriesBeforeSpawnHit = GetInt(session, "Recoveries");
            int collisionsBeforeSpawnHit = GetInt(session, "Collisions");
            int triggersBeforeSpawnHit = GetInt(ReadMember(session, "CollisionFeedback"), "TriggerCount");
            Present();
            yield return Capture("01-before-first-danger-impact.png");
            bool spawnImpact = false;
            for (int i = 0; i < 400 && !spawnImpact; i++)
            {
                Tick(0f, 0f, 0f, false, false, 0.1);
                Present();
                spawnImpact = GetInt(session, "Recoveries") > recoveriesBeforeSpawnHit;
                if (!spawnImpact) yield return null;
            }
            if (!spawnImpact) throw new InvalidOperationException("The moving car did not contact the stationary player at the spawn checkpoint.");
            report.spawnRecovery = CreateRecoveryEvidence("car.moving", session, collisionsBeforeSpawnHit,
                recoveriesBeforeSpawnHit, triggersBeforeSpawnHit, new PointEvidence(0, 0), "spawn", true);
            if (report.spawnRecovery.recoveryDelta != 1 || report.spawnRecovery.triggerDelta < 1 ||
                Distance(report.spawnRecovery.restoredPosition, new PointEvidence(0, 0)) > 0.08f ||
                report.spawnRecovery.checkpointId != "spawn")
                throw new InvalidOperationException("The no-explicit-checkpoint collision did not restore safely to spawn with real feedback.");
            report.spawnRecovery.memoryAfterRecovery = MemorySnapshot(session);
            report.spawnRecovery.memoryRetained = SameIds(report.caneProbe.objectIds, report.spawnRecovery.memoryAfterRecovery.objectIds);
            if (!report.spawnRecovery.memoryRetained)
                throw new InvalidOperationException("Vehicle recovery discarded previously observed memory.");
            yield return Capture("02-car-contact-spawn-recovery-halo.png");

            yield return ExercisePauseAndPulse(session);
            yield return ExerciseSaveReload(session);
            yield return ExerciseWallContactAndDedup(session);
            yield return TravelToNorthCheckpoint(session);
            yield return WaitForCheckpointBicycle(session);
            yield return TravelToAndProbeRedLight(session);

            var collisionMatrix = gameObject.AddComponent<StreetCollisionMatrixProbe>();
            collisionMatrix.Configure(outputDirectory, isolatedSaveRoot);
            IEnumerator matrixWork = collisionMatrix.RunMatrix();
            while (true)
            {
                bool moved = false;
                object current = null;
                Exception matrixError = null;
                try
                {
                    moved = matrixWork.MoveNext();
                    if (moved) current = matrixWork.Current;
                }
                catch (Exception error) { matrixError = error; }
                if (matrixError != null) throw new InvalidOperationException("Collision matrix failed: " + matrixError.GetBaseException().Message, matrixError);
                if (!moved) break;
                yield return current;
            }
            report.collisionMatrix = collisionMatrix.Result;
            report.collisionMatrixJsonPath = collisionMatrix.JsonPath;
            screenshots.AddRange(collisionMatrix.ScreenshotPaths);
            if (collisionMatrix.Result == null || collisionMatrix.Result.status != "PASS")
                throw new InvalidOperationException("The collision matrix did not report PASS.");
        }

        private IEnumerator ExercisePauseAndPulse(object activeSession)
        {
            var feedback = ReadMember(activeSession, "CollisionFeedback");
            float alphaBeforePause = GetFloat(feedback, "Alpha");
            int triggerCountBeforePause = GetInt(feedback, "TriggerCount");
            Tick(0f, 0f, 0f, false, true, 0.02);
            Present();
            double alphaAtPause = GetFloat(feedback, "Alpha");
            string pauseMenu = Convert.ToString(ReadMember(ReadMember(activeSession, "Menu"), "Screen"));
            for (int i = 0; i < 5; i++) Tick(0f, 0f, 0f, false, false, 0.5);
            double alphaAfterFrozenTicks = GetFloat(feedback, "Alpha");
            report.pauseFreeze = new PauseFreezeEvidence
            {
                menuPaused = pauseMenu == "Paused" || pauseMenu == "Pause",
                triggerCountBefore = triggerCountBeforePause,
                triggerCountAfter = GetInt(feedback, "TriggerCount"),
                alphaBefore = alphaBeforePause,
                alphaAtPause = alphaAtPause,
                alphaAfterFiveHalfSecondTicks = alphaAfterFrozenTicks,
                frozen = Math.Abs(alphaAtPause - alphaAfterFrozenTicks) < 0.000001 &&
                    GetInt(feedback, "TriggerCount") == triggerCountBeforePause
            };
            if (!report.pauseFreeze.menuPaused || !report.pauseFreeze.frozen)
                throw new InvalidOperationException("Paused input did not freeze collision presentation time and pulse count.");
            yield return Capture("03-paused-feedback-frozen.png");

            Tick(0f, 0f, 0f, false, true, 0.02);
            Present();
            report.pauseFreeze.resumedMenu = Convert.ToString(ReadMember(ReadMember(activeSession, "Menu"), "Screen"));
            if (report.pauseFreeze.resumedMenu != "Playing")
                throw new InvalidOperationException("The production pause input path did not resume play.");
            yield return Capture("04-resumed-feedback.png");

            report.pulseSamples = new List<PulseSampleEvidence>();
            double[] sampleAges = { 0, 0.02, 0.04, 0.06, 0.08, 0.10, 0.14, 0.18, 0.24, 0.36, 0.54, 0.74 };
            foreach (double requestedAge in sampleAges)
            {
                while (GetDouble(feedback, "Age") + 0.000001 < requestedAge)
                {
                    Tick(0f, 0f, 0f, false, false, 0.02);
                    Present();
                    yield return null;
                }
                var sample = new PulseSampleEvidence
                {
                    requestedAge = requestedAge,
                    actualAge = GetDouble(feedback, "Age"),
                    alpha = GetFloat(feedback, "Alpha"),
                    triggerCount = GetInt(feedback, "TriggerCount")
                };
                report.pulseSamples.Add(sample);
                yield return Capture("05-halo-age-" + requestedAge.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) + ".png");
                sample.screenshot = screenshots[screenshots.Count - 1];
            }
            float minimum = float.MaxValue, maximum = float.MinValue;
            foreach (PulseSampleEvidence sample in report.pulseSamples)
            { minimum = Math.Min(minimum, sample.alpha); maximum = Math.Max(maximum, sample.alpha); }
            report.pulseVariation = maximum - minimum;
            report.pulseFadedOut = GetDouble(feedback, "Age") >= 0.72 && GetFloat(feedback, "Alpha") <= 0.0001f;
            if (report.pulseVariation < 0.025f || !report.pulseFadedOut)
                throw new InvalidOperationException("The real collision halo did not show a changing pulse followed by a full fade-out.");
        }

        private IEnumerator ExerciseSaveReload(object activeSession)
        {
            object feedback = ReadMember(activeSession, "CollisionFeedback");
            int savedCollisions = GetInt(activeSession, "Collisions");
            string checkpoint = Convert.ToString(ReadMember(activeSession, "CheckpointId"));
            object saveResult = Invoke(activeSession, "Save");
            if (Convert.ToString(saveResult) != "Success")
                throw new InvalidOperationException("The isolated fixture session could not save before reload: " + saveResult);
            Invoke(activeSession, "Start", true);
            Present();
            report.saveReload = new SaveReloadEvidence
            {
                saveStatus = Convert.ToString(saveResult),
                loadedRunning = Convert.ToBoolean(ReadMember(activeSession, "Running")),
                savedCollisionCount = savedCollisions,
                loadedCollisionCount = GetInt(activeSession, "Collisions"),
                savedCheckpointId = checkpoint,
                loadedCheckpointId = Convert.ToString(ReadMember(activeSession, "CheckpointId")),
                feedbackCleared = GetInt(feedback, "TriggerCount") == 0 && GetFloat(feedback, "Alpha") <= 0.0001f,
                memoryAfterLoad = MemorySnapshot(activeSession)
            };
            if (!report.saveReload.loadedRunning || report.saveReload.loadedCollisionCount != savedCollisions ||
                report.saveReload.loadedCheckpointId != checkpoint || !report.saveReload.feedbackCleared)
                throw new InvalidOperationException("Save/reload did not preserve session progress and clear the transient collision pulse.");
            yield return Capture("06-save-reload-feedback-cleared.png");
        }

        private IEnumerator ExerciseWallContactAndDedup(object activeSession)
        {
            int initialRecoveries = GetInt(activeSession, "Recoveries");
            int initialCollisions = GetInt(activeSession, "Collisions");
            int initialTriggers = GetInt(ReadMember(activeSession, "CollisionFeedback"), "TriggerCount");
            bool hit = false;
            for (int i = 0; i < 100 && !hit; i++)
            {
                Tick(0f, 1f, 0f, false, false, 0.1);
                Present();
                hit = GetInt(activeSession, "Collisions") > initialCollisions;
                if (!hit) yield return null;
            }
            if (!hit) throw new InvalidOperationException("Walking north did not produce a real collision with the solid wall.");
            report.wallFirstHit = CreateContactEvidence("wall.north", activeSession, initialCollisions, initialTriggers,
                initialRecoveries, new PointEvidence(0, 0));
            yield return Capture("07-wall-first-contact.png");

            for (int i = 0; i < 10; i++)
            {
                Tick(0f, 1f, 0f, false, false, 0.1);
                Present();
                yield return null;
            }
            report.wallHeldContact = CreateContactEvidence("wall.north", activeSession, initialCollisions, initialTriggers,
                initialRecoveries, new PointEvidence(0, 0));
            report.wallHeldContact.heldCollisionDelta = report.wallHeldContact.collisionDelta - report.wallFirstHit.collisionDelta;
            report.wallHeldContact.heldTriggerDelta = report.wallHeldContact.triggerDelta - report.wallFirstHit.triggerDelta;
            if (report.wallHeldContact.collisionDelta != report.wallFirstHit.collisionDelta ||
                report.wallHeldContact.triggerDelta != report.wallFirstHit.triggerDelta ||
                report.wallHeldContact.recoveryDelta != 0)
                throw new InvalidOperationException("Holding contact against the wall stacked duplicate collision feedback.");
            yield return Capture("08-wall-held-no-repeat.png");

            for (int i = 0; i < 20 && GetFloat(ReadMember(activeSession, "Player"), "Current.Position.Y") > 2.25f; i++)
            {
                Tick(0f, -1f, 0f, false, false, 0.1);
                Present();
                yield return null;
            }
            // Keep the simulation outside the wall for longer than both contact exit and feedback repeat intervals.
            for (int i = 0; i < 8; i++)
            {
                Tick(0f, 0f, 0f, false, false, 0.1);
                Present();
                yield return null;
            }
            int separatedCollisions = GetInt(activeSession, "Collisions");
            int separatedTriggers = GetInt(ReadMember(activeSession, "CollisionFeedback"), "TriggerCount");
            bool secondHit = false;
            for (int i = 0; i < 100 && !secondHit; i++)
            {
                Tick(0f, 1f, 0f, false, false, 0.1);
                Present();
                secondHit = GetInt(activeSession, "Collisions") > separatedCollisions;
                if (!secondHit) yield return null;
            }
            report.wallAfterSeparation = CreateContactEvidence("wall.north", activeSession, separatedCollisions,
                separatedTriggers, initialRecoveries, new PointEvidence(0, 0));
            report.wallAfterSeparation.separatedBeforeRetest = true;
            if (!secondHit || report.wallAfterSeparation.collisionDelta != 1 || report.wallAfterSeparation.triggerDelta != 1 ||
                report.wallAfterSeparation.recoveryDelta != 0)
                throw new InvalidOperationException("The wall did not create exactly one new contact after the player separated and returned.");
            yield return Capture("09-wall-contact-after-separation.png");
        }

        private IEnumerator TravelToNorthCheckpoint(object activeSession)
        {
            // Step back from the wall, go around its west end, then reach the real checkpoint through player movement.
            for (int i = 0; i < 12 && GetFloat(ReadMember(activeSession, "Player"), "Current.Position.Y") > 2.25f; i++)
            {
                Tick(0f, -1f, 0f, false, false, 0.1);
                Present();
                yield return null;
            }
            for (int i = 0; i < 40 && GetFloat(ReadMember(activeSession, "Player"), "Current.Position.X") > -3f; i++)
            {
                Tick(-1f, 0f, 0f, false, false, 0.1);
                Present();
                yield return null;
            }
            for (int i = 0; i < 100 && GetFloat(ReadMember(activeSession, "Player"), "Current.Position.Y") < 8f; i++)
            {
                Tick(0f, 1f, 0f, false, false, 0.1);
                Present();
                yield return null;
            }
            for (int i = 0; i < 40 && GetFloat(ReadMember(activeSession, "Player"), "Current.Position.X") < 0f; i++)
            {
                Tick(1f, 0f, 0f, false, false, 0.1);
                Present();
                yield return null;
            }
            report.northCheckpoint = new CheckpointEvidence
            {
                checkpointId = Convert.ToString(ReadMember(activeSession, "CheckpointId")),
                playerPosition = PlayerPosition(activeSession),
                collisions = GetInt(activeSession, "Collisions"),
                recoveries = GetInt(activeSession, "Recoveries")
            };
            if (report.northCheckpoint.checkpointId != "checkpoint.north" ||
                Distance(report.northCheckpoint.playerPosition, new PointEvidence(0, 8)) > 0.8f)
                throw new InvalidOperationException("The player did not walk to and activate the northern checkpoint.");

            TurnToNorthAndProbe(activeSession);
            report.northCheckpoint.memoryBeforeVehicleRecovery = MemorySnapshot(activeSession);
            Present();
            yield return Capture("10-north-checkpoint-cane-probe.png");
        }

        private IEnumerator WaitForCheckpointBicycle(object activeSession)
        {
            int beforeRecovery = GetInt(activeSession, "Recoveries");
            int beforeCollision = GetInt(activeSession, "Collisions");
            int beforeTriggers = GetInt(ReadMember(activeSession, "CollisionFeedback"), "TriggerCount");
            PointEvidence bikeBeforeImpact = null;
            bool recovered = false;
            for (int i = 0; i < 1000 && !recovered; i++)
            {
                PointEvidence currentBike = WorldObjectPosition(activeSession, "bike.moving");
                Tick(0f, 0f, 0f, false, false, 0.1);
                Present();
                recovered = GetInt(activeSession, "Recoveries") > beforeRecovery;
                if (recovered) bikeBeforeImpact = currentBike;
                else yield return null;
            }
            if (!recovered) throw new InvalidOperationException("The moving bicycle did not contact the player at the explicit checkpoint.");
            report.checkpointRecovery = CreateRecoveryEvidence("bike.moving", activeSession, beforeCollision,
                beforeRecovery, beforeTriggers, new PointEvidence(0, 8), "checkpoint.north", false);
            report.checkpointRecovery.actorPositionBeforeRestore = bikeBeforeImpact;
            report.checkpointRecovery.memoryBeforeRecovery = report.northCheckpoint.memoryBeforeVehicleRecovery;
            report.checkpointRecovery.memoryAfterRecovery = MemorySnapshot(activeSession);
            report.checkpointRecovery.memoryRetained = SameIds(report.checkpointRecovery.memoryBeforeRecovery.objectIds,
                report.checkpointRecovery.memoryAfterRecovery.objectIds);
            report.checkpointRecovery.memoryMarkedStale = AllStale(report.checkpointRecovery.memoryAfterRecovery);
            if (report.checkpointRecovery.recoveryDelta != 1 || report.checkpointRecovery.triggerDelta < 1 ||
                Distance(report.checkpointRecovery.restoredPosition, new PointEvidence(0, 8)) > 0.08f ||
                report.checkpointRecovery.checkpointId != "checkpoint.north" ||
                !report.checkpointRecovery.memoryRetained || !report.checkpointRecovery.memoryMarkedStale)
                throw new InvalidOperationException("Bicycle recovery did not restore the latest checkpoint while preserving stale memory.");
            yield return Capture("11-bicycle-contact-nearest-checkpoint-halo.png");

            Tick(0f, 0f, 0f, false, true, 0.02);
            Present();
            yield return Capture("12-nearest-checkpoint-paused.png");
            Tick(0f, 0f, 0f, false, true, 0.02);
            Present();
        }

        private IEnumerator TravelToAndProbeRedLight(object activeSession)
        {
            // Travel north to the light. This leaves the bicycle's crossing lane before it can return.
            for (int i = 0; i < 80 && GetFloat(ReadMember(activeSession, "Player"), "Current.Position.Y") < 14f; i++)
            {
                Tick(0f, 1f, 0f, false, false, 0.1);
                Present();
                yield return null;
            }
            TurnToNorthAndProbe(activeSession);
            bool atRedStop = false;
            PointEvidence stoppedBefore = null;
            string phase = null;
            for (int i = 0; i < 500 && !atRedStop; i++)
            {
                phase = Convert.ToString(ReadMember(ReadMember(activeSession, "World"), "SignalPhase"));
                PointEvidence car = WorldObjectPosition(activeSession, "car.red-light");
                if (Math.Abs(car.x + 1f) < 0.03f && phase == "ew")
                {
                    atRedStop = true;
                    stoppedBefore = car;
                    break;
                }
                Tick(0f, 0f, 0f, false, false, 0.1);
                Present();
                yield return null;
            }
            if (!atRedStop) throw new InvalidOperationException("Signal-controlled car did not stop at the red NS light.");

            // Observe the real stop line for half a simulation second in the same red phase.
            for (int i = 0; i < 5; i++)
            {
                Tick(0f, 0f, 0f, false, false, 0.1);
                Present();
                yield return null;
            }
            PointEvidence stoppedAfter = WorldObjectPosition(activeSession, "car.red-light");
            phase = Convert.ToString(ReadMember(ReadMember(activeSession, "World"), "SignalPhase"));
            if (phase != "ew" || Distance(stoppedBefore, stoppedAfter) > 0.01f)
                throw new InvalidOperationException("Signal-controlled car failed to remain at the stop line during red.");

            report.redLight = new RedLightEvidence
            {
                phase = phase,
                signalObjectId = "street.signal.ns",
                signalIsGreen = phase == "ns",
                trafficStoppedPositionBefore = stoppedBefore,
                trafficStoppedPositionAfter = stoppedAfter,
                stopLineX = -1f,
                stableForSeconds = 0.5,
                playerPosition = PlayerPosition(activeSession)
            };
            if (report.redLight.signalIsGreen || Math.Abs(report.redLight.trafficStoppedPositionAfter.x + 1f) > 0.03f)
                throw new InvalidOperationException("The real observed NS signal was not red while traffic waited.");
            yield return Capture("13-red-light-traffic-stopped.png");

            // The view is produced by production cane perception and signal observations.
            TurnToNorthAndProbe(activeSession);
            report.redLight.memoryAfterRealProbe = MemorySnapshot(activeSession);
            report.redLight.freshSignalObserved = HasFreshSignal(activeSession, "street.signal.ns", out report.redLight.signalObservedAt, out report.redLight.signalExpiresAt, out report.redLight.signalIsGreen);
            if (!report.redLight.freshSignalObserved || report.redLight.signalIsGreen)
                throw new InvalidOperationException("The real cane probe did not expose a fresh red NS signal observation.");
            Present();
            yield return Capture("14-red-light-player-probe.png");
        }

        private static StreetLevelDocument CreateFixture()
        {
            var level = new StreetLevelDocument
            {
                schemaVersion = StreetLevelDocument.CurrentSchemaVersion,
                id = "probe.street.collision.player",
                title = "Player 碰撞反馈动态取证",
                goalText = "沿着安全路线继续前进。",
                spawnX = 0,
                spawnY = 0,
                facingX = 0,
                facingY = 1,
                goalX = 0,
                goalY = 28,
                goalRadius = 0.8f
            };
            level.surfaces.Add(Surface("fixture.sidewalk", "sidewalk", -25, -22, 25, 34, 10));
            level.surfaces.Add(Surface("fixture.red-light-road", "asphalt", -12, 13, 12, 19, 20));

            level.objects.Add(StaticObject("wall.north", "brick", -2, 3, 2, 3.35f, true, true));
            level.objects.Add(StaticObject("building.east", "brick", 7, -1, 10, 5, true, true));
            level.objects.Add(StaticObject("obstacle.crate", "cardboard", -7, 3, -6.2f, 3.8f, true, false));

            level.objects.Add(Actor("pedestrian.standing", StreetActorKind.Pedestrian, "brick", -1.6f, 1.25f,
                0.25f, 0.25f, "street.walker", true, false, PerceptionSemantic.Person));
            level.objects.Add(Actor("pedestrian.walking", StreetActorKind.Pedestrian, "brick", -6, -5,
                0.25f, 0.25f, "street.walker", true, false, PerceptionSemantic.Person));
            level.routes.Add(Route("pedestrian.walking", new[] { new Point2(-6, -5), new Point2(6, -5) }, 0.8f, null, null));

            level.objects.Add(Actor("car.parked", StreetActorKind.Car, "metal", 5, 1,
                0.7f, 0.38f, "street.car", false, false, PerceptionSemantic.Ordinary));
            level.objects.Add(Actor("bicycle.parked", StreetActorKind.Bicycle, "metal", 5, 3,
                0.55f, 0.28f, "street.cycle", false, false, PerceptionSemantic.Ordinary));
            level.objects.Add(Actor("electric-bike.parked", StreetActorKind.ElectricBike, "metal", 5, 5,
                0.6f, 0.3f, "street.electric", false, false, PerceptionSemantic.Ordinary));

            level.objects.Add(Actor("car.moving", StreetActorKind.Car, "metal", -5, 0,
                0.62f, 0.36f, "street.car", true, true, PerceptionSemantic.Ordinary));
            level.routes.Add(Route("car.moving", new[] { new Point2(-5, 0), new Point2(5, 0) }, 2f, null, null));
            level.objects.Add(Actor("bike.moving", StreetActorKind.Bicycle, "metal", 10, 8,
                0.52f, 0.28f, "street.cycle", true, true, PerceptionSemantic.Ordinary));
            level.routes.Add(Route("bike.moving", new[] { new Point2(10, 8), new Point2(-10, 8) }, 0.5f, null, null));
            level.objects.Add(Actor("electric-bike.moving", StreetActorKind.ElectricBike, "metal", -10, -8,
                0.55f, 0.3f, "street.electric", true, true, PerceptionSemantic.Ordinary));
            level.routes.Add(Route("electric-bike.moving", new[] { new Point2(-10, -8), new Point2(10, -8) }, 0.7f, null, null));

            var signal = new StreetObjectData
            {
                id = "street.signal.ns",
                kind = StreetKind.Permanent,
                position = new StreetPointData(0, 16),
                polygon = Rect( -0.18f, 15.82f, 0.18f, 16.18f),
                materialId = "metal",
                height = 2,
                soundId = "street.signal.ns",
                soundActive = false,
                semantic = PerceptionSemantic.Guide
            };
            level.objects.Add(signal);
            level.objects.Add(Actor("car.red-light", StreetActorKind.Car, "metal", -5, 16,
                0.42f, 0.32f, "street.car", true, true, PerceptionSemantic.Ordinary));
            level.routes.Add(Route("car.red-light", new[] { new Point2(-5, 16), new Point2(5, 16) }, 1f, "ns", 4f));

            level.checkpoints.Add(new StreetCheckpointData
            {
                id = "checkpoint.north",
                position = new StreetPointData(0, 8),
                facing = new StreetPointData(0, 1),
                radius = 0.7f
            });
            return level;
        }

        private static StreetSurfaceData Surface(string id, string material, float x0, float y0, float x1, float y1, int priority)
        {
            return new StreetSurfaceData
            {
                id = id,
                materialId = material,
                polygon = Rect(x0, y0, x1, y1),
                textureAxis = new StreetPointData(1, 0),
                priority = priority,
                heightBase = material == "sidewalk" ? 0.15f : 0f
            };
        }

        private static StreetObjectData StaticObject(string id, string material, float x0, float y0,
            float x1, float y1, bool solid, bool blocksProbe)
        {
            return new StreetObjectData
            {
                id = id,
                kind = StreetKind.Permanent,
                position = new StreetPointData((x0 + x1) * 0.5f, (y0 + y1) * 0.5f),
                polygon = Rect(x0, y0, x1, y1),
                materialId = material,
                solid = solid,
                blocksProbe = blocksProbe,
                acousticLoss = blocksProbe ? 0.5f : 0,
                height = blocksProbe ? 3f : 0.8f,
                semantic = PerceptionSemantic.Ordinary
            };
        }

        private static StreetObjectData Actor(string id, StreetActorKind actorKind, string material,
            float x, float y, float halfX, float halfY, string sound, bool soundActive, bool hazard,
            PerceptionSemantic semantic)
        {
            return new StreetObjectData
            {
                id = id,
                kind = StreetKind.Active,
                position = new StreetPointData(x, y),
                polygon = Rect(x - halfX, y - halfY, x + halfX, y + halfY),
                materialId = material,
                solid = true,
                vehicleHazard = hazard,
                height = actorKind == StreetActorKind.Pedestrian ? 1.7f : 1.2f,
                soundId = sound,
                soundActive = soundActive,
                semantic = semantic,
                actorKind = actorKind
            };
        }

        private static StreetRouteData Route(string id, Point2[] points, float speed, string signalAxis, float? stopLine)
        {
            var route = new StreetRouteData
            {
                objectId = id,
                speed = speed,
                signalAxis = signalAxis,
                offset = 0,
                loop = true,
                stopLineDistance = stopLine
            };
            foreach (Point2 point in points) route.points.Add(StreetPointData.From(point));
            return route;
        }

        private static List<StreetPointData> Rect(float x0, float y0, float x1, float y1)
        {
            return new List<StreetPointData>
            {
                new StreetPointData(x0, y0), new StreetPointData(x1, y0),
                new StreetPointData(x1, y1), new StreetPointData(x0, y1)
            };
        }

        private ActorEvidence[] DescribeActors(StreetLevelDocument fixture)
        {
            var routes = new Dictionary<string, StreetRouteData>(StringComparer.Ordinal);
            foreach (StreetRouteData route in fixture.routes) routes[route.objectId] = route;
            var items = new List<ActorEvidence>();
            foreach (StreetObjectData item in fixture.objects)
            {
                bool hasRoute = routes.ContainsKey(item.id);
                items.Add(new ActorEvidence
                {
                    id = item.id,
                    actorKind = item.actorKind.ToString(),
                    hasRoute = hasRoute,
                    parked = (item.actorKind == StreetActorKind.Car || item.actorKind == StreetActorKind.Bicycle ||
                        item.actorKind == StreetActorKind.ElectricBike) && !hasRoute,
                    stationaryPedestrian = item.actorKind == StreetActorKind.Pedestrian && !hasRoute,
                    moving = hasRoute,
                    routeSpeed = hasRoute ? routes[item.id].speed : 0
                });
            }
            return items.ToArray();
        }

        private static Dictionary<string, PointEvidence> CaptureRoutedActorPositions(object activeSession, StreetLevelDocument fixture)
        {
            var result = new Dictionary<string, PointEvidence>(StringComparer.Ordinal);
            foreach (StreetRouteData route in fixture.routes)
                result[route.objectId] = WorldObjectPosition(activeSession, route.objectId);
            return result;
        }

        private static RouteMotionEvidence[] CompareRoutedActorPositions(Dictionary<string, PointEvidence> before,
            Dictionary<string, PointEvidence> after)
        {
            var result = new List<RouteMotionEvidence>();
            foreach (KeyValuePair<string, PointEvidence> pair in before)
            {
                PointEvidence final;
                if (!after.TryGetValue(pair.Key, out final)) continue;
                result.Add(new RouteMotionEvidence
                {
                    actorId = pair.Key,
                    before = pair.Value,
                    after = final,
                    distanceMoved = Distance(pair.Value, final)
                });
            }
            return result.ToArray();
        }

        private static void AssertRoutesMoved(RouteMotionEvidence[] routes)
        {
            string[] required = { "car.moving", "bike.moving", "electric-bike.moving", "pedestrian.walking" };
            foreach (string id in required)
            {
                bool moved = false;
                foreach (RouteMotionEvidence route in routes)
                    if (route.actorId == id && route.distanceMoved > 0.05f) moved = true;
                if (!moved) throw new InvalidOperationException("Routed actor did not move during a real session tick: " + id);
            }
        }

        private RecoveryEvidence CreateRecoveryEvidence(string actorId, object activeSession, int collisionsBefore,
            int recoveriesBefore, int triggersBefore, PointEvidence expected, string checkpointId, bool safeSpawn)
        {
            return new RecoveryEvidence
            {
                actorId = actorId,
                collisionDelta = GetInt(activeSession, "Collisions") - collisionsBefore,
                recoveryDelta = GetInt(activeSession, "Recoveries") - recoveriesBefore,
                triggerDelta = GetInt(ReadMember(activeSession, "CollisionFeedback"), "TriggerCount") - triggersBefore,
                expectedPosition = expected,
                restoredPosition = PlayerPosition(activeSession),
                checkpointId = Convert.ToString(ReadMember(activeSession, "CheckpointId")),
                safeSpawnFallback = safeSpawn,
                simulationTimeAfterRestore = GetDouble(ReadMember(activeSession, "Clock"), "Now")
            };
        }

        private ContactEvidence CreateContactEvidence(string objectId, object activeSession, int collisionsBefore,
            int triggersBefore, int recoveriesBefore, PointEvidence unused)
        {
            return new ContactEvidence
            {
                objectId = objectId,
                collisionDelta = GetInt(activeSession, "Collisions") - collisionsBefore,
                triggerDelta = GetInt(ReadMember(activeSession, "CollisionFeedback"), "TriggerCount") - triggersBefore,
                recoveryDelta = GetInt(activeSession, "Recoveries") - recoveriesBefore,
                playerPosition = PlayerPosition(activeSession)
            };
        }

        private MemoryEvidence MemorySnapshot(object activeSession)
        {
            object frame = ReadMember(ReadMember(activeSession, "Memory"), "Current");
            object objects = ReadMember(frame, "Objects");
            var ids = new List<string>();
            var freshness = new List<float>();
            foreach (object item in Enumerate(objects))
            {
                ids.Add(Convert.ToString(ReadMember(item, "ObjectId")));
                freshness.Add((float)Convert.ToDouble(ReadMember(item, "CurrentUntil")));
            }
            ids.Sort(StringComparer.Ordinal);
            return new MemoryEvidence { objectIds = ids.ToArray(), objectCount = ids.Count, currentUntilValues = freshness.ToArray() };
        }

        private WorldEvidence WorldSnapshot(object activeSession)
        {
            object frame = Invoke(ReadMember(activeSession, "World"), "Capture");
            object objects = ReadMember(frame, "Objects");
            var ids = new List<string>();
            foreach (object item in Enumerate(objects)) ids.Add(Convert.ToString(ReadMember(item, "Id")));
            ids.Sort(StringComparer.Ordinal);
            return new WorldEvidence { objectIds = ids.ToArray(), objectCount = ids.Count };
        }

        private static bool AllStale(MemoryEvidence memory)
        {
            foreach (float value in memory.currentUntilValues) if (value > 0.0001f) return false;
            return true;
        }

        private static bool SameIds(string[] a, string[] b)
        {
            if (a == null || b == null || a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++) if (!StringComparer.Ordinal.Equals(a[i], b[i])) return false;
            return true;
        }

        private static bool Contains(string[] values, string sought)
        {
            if (values == null) return false;
            for (int i = 0; i < values.Length; i++) if (StringComparer.Ordinal.Equals(values[i], sought)) return true;
            return false;
        }

        private static PointEvidence PlayerPosition(object activeSession)
        {
            object current = ReadMember(ReadMember(activeSession, "Player"), "Current");
            return Point(ReadMember(current, "Position"));
        }

        private static PointEvidence WorldObjectPosition(object activeSession, string id)
        {
            object frame = Invoke(ReadMember(activeSession, "World"), "Capture");
            foreach (object item in Enumerate(ReadMember(frame, "Objects")))
                if (StringComparer.Ordinal.Equals(Convert.ToString(ReadMember(item, "Id")), id))
                    return Point(ReadMember(item, "Position"));
            throw new InvalidOperationException("World snapshot omitted fixture object " + id);
        }

        private void TurnToNorthAndProbe(object activeSession)
        {
            object current = ReadMember(ReadMember(activeSession, "Player"), "Current");
            object facing = ReadMember(current, "Facing");
            float x = GetFloat(facing, "X"), y = GetFloat(facing, "Y");
            float angle = (float)(Math.Atan2(x, y) * 180.0 / Math.PI);
            Tick(0f, 0f, -angle, true, false, 0.02);
            Present();
        }

        private static bool SignalIsGreen(object activeSession, string id)
        {
            object signals = ReadMember(activeSession, "Signals");
            foreach (object signal in Enumerate(signals))
                if (StringComparer.Ordinal.Equals(Convert.ToString(ReadMember(signal, "ObjectId")), id))
                    return Convert.ToBoolean(ReadMember(signal, "IsGreen"));
            return true;
        }

        private static bool HasFreshSignal(object activeSession, string id, out double observedAt, out double expiresAt, out bool green)
        {
            observedAt = -1;
            expiresAt = -1;
            green = true;
            object signals = ReadMember(activeSession, "Signals");
            double now = GetDouble(ReadMember(activeSession, "Clock"), "Now");
            foreach (object signal in Enumerate(signals))
            {
                if (!StringComparer.Ordinal.Equals(Convert.ToString(ReadMember(signal, "ObjectId")), id)) continue;
                observedAt = GetDouble(signal, "ObservedAt");
                expiresAt = GetDouble(signal, "ExpiresAt");
                green = Convert.ToBoolean(ReadMember(signal, "IsGreen"));
                return observedAt <= now && expiresAt > now;
            }
            return false;
        }

        private void Tick(float moveX, float moveY, float turn, bool probe, bool pause, double delta)
        {
            Type frameType = Type.GetType("TingLu.Contracts.StreetInputFrame, TingLu.Contracts", true);
            Type pointType = Type.GetType("TingLu.Contracts.Point2, TingLu.Contracts", true);
            Type menuType = Type.GetType("TingLu.Contracts.MenuAction, TingLu.Contracts", true);
            object move = Activator.CreateInstance(pointType, new object[] { moveX, moveY });
            ConstructorInfo constructor = frameType.GetConstructor(new[] { pointType, typeof(float), typeof(bool), typeof(bool), menuType });
            if (constructor == null) throw new MissingMethodException("StreetInputFrame constructor was not found.");
            object frame = constructor.Invoke(new[] { move, (object)turn, (object)probe, (object)pause, Enum.Parse(menuType, "None") });
            Invoke(session, "Tick", frame, delta);
        }

        private void Present()
        {
            if (entry == null) return;
            Invoke(entry, "PresentCurrentFrame");
        }

        private IEnumerator Capture(string fileName)
        {
            string path = Path.Combine(outputDirectory, fileName);
            yield return new WaitForEndOfFrame();
            ScreenCapture.CaptureScreenshot(path, 1);
            for (int frame = 0; frame < 180; frame++)
            {
                if (File.Exists(path) && new FileInfo(path).Length > 24)
                {
                    screenshots.Add(path);
                    yield break;
                }
                yield return null;
            }
            throw new IOException("Player screenshot was not written: " + path);
        }

        private void Fail(string message)
        {
            failed = true;
            report.error = message;
            Debug.LogError("Street collision probe failed: " + message);
        }

        private void WriteReport()
        {
            try
            {
                File.WriteAllText(Path.Combine(outputDirectory, "street-collision-probe-results.json"),
                    JsonUtility.ToJson(report, true), new UTF8Encoding(false));
            }
            catch (Exception error) { Debug.LogError("Could not write collision probe JSON: " + error.Message); }
        }

        private static object Invoke(object target, string methodName, params object[] args)
        {
            if (target == null) throw new ArgumentNullException("target");
            MethodInfo[] methods = target.GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            for (int i = 0; i < methods.Length; i++)
            {
                if (!StringComparer.Ordinal.Equals(methods[i].Name, methodName)) continue;
                ParameterInfo[] parameters = methods[i].GetParameters();
                if (parameters.Length != args.Length) continue;
                bool compatible = true;
                for (int j = 0; j < parameters.Length; j++)
                {
                    if (args[j] == null ? parameters[j].ParameterType.IsValueType : !parameters[j].ParameterType.IsInstanceOfType(args[j]))
                    { compatible = false; break; }
                }
                if (compatible) return methods[i].Invoke(target, args);
            }
            throw new MissingMethodException(target.GetType().FullName, methodName);
        }

        private static object ReadMember(object target, string path)
        {
            if (target == null) throw new ArgumentNullException("target", "Cannot read " + path + " from null.");
            string[] segments = path.Split('.');
            object current = target;
            for (int i = 0; i < segments.Length; i++)
            {
                if (current == null) throw new NullReferenceException("Member path is null at " + segments[i] + ".");
                Type type = current.GetType();
                const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
                PropertyInfo property = type.GetProperty(segments[i], flags);
                if (property != null) { current = property.GetValue(current, null); continue; }
                FieldInfo field = type.GetField(segments[i], flags);
                if (field != null) { current = field.GetValue(current); continue; }
                throw new MissingMemberException(type.FullName, segments[i]);
            }
            return current;
        }

        private static IEnumerable Enumerate(object value)
        {
            IEnumerable result = value as IEnumerable;
            if (result == null) throw new InvalidOperationException("Expected an enumerable value.");
            return result;
        }

        private static int GetInt(object target, string member) { return Convert.ToInt32(ReadMember(target, member)); }
        private static float GetFloat(object target, string member) { return Convert.ToSingle(ReadMember(target, member)); }
        private static double GetDouble(object target, string member) { return Convert.ToDouble(ReadMember(target, member)); }
        private static PointEvidence Point(object value) { return new PointEvidence(GetFloat(value, "X"), GetFloat(value, "Y")); }
        private static float Distance(PointEvidence a, PointEvidence b)
        {
            float dx = a.x - b.x, dy = a.y - b.y;
            return (float)Math.Sqrt(dx * dx + dy * dy);
        }
        private static bool IsAbsolute(string path) { return !string.IsNullOrWhiteSpace(path) && Path.IsPathRooted(path); }
        private static string ArgumentValue(string[] args, string name)
        {
            for (int i = 0; i + 1 < args.Length; i++) if (StringComparer.Ordinal.Equals(args[i], name)) return args[i + 1];
            return null;
        }
        private static string Sha256(byte[] bytes)
        {
            using (SHA256 sha = SHA256.Create())
            {
                byte[] hash = sha.ComputeHash(bytes);
                var result = new StringBuilder(hash.Length * 2);
                for (int i = 0; i < hash.Length; i++) result.Append(hash[i].ToString("X2"));
                return result.ToString();
            }
        }

        [Serializable]
        private sealed class ProbeReport
        {
            public string status = "RUNNING", scene, error, outputDirectory, isolatedSaveRoot, levelId, completedAtUtc;
            public int screenWidth, screenHeight;
            public bool fixtureCodecRoundTrip;
            public string fixtureJsonSha256;
            public string[] screenshots;
            public string[] playerErrors;
            public EntryIsolationEvidence entryIsolation;
            public ActorEvidence[] actorInventory;
            public WorldEvidence initialWorld;
            public MemoryEvidence initialMemory;
            public UnknownZoneEvidence initialUnknownZone, unknownZone;
            public MemoryEvidence caneProbe;
            public RouteMotionEvidence[] routedActorMotion;
            public RecoveryEvidence spawnRecovery, checkpointRecovery;
            public PauseFreezeEvidence pauseFreeze;
            public List<PulseSampleEvidence> pulseSamples;
            public float pulseVariation;
            public bool pulseFadedOut;
            public SaveReloadEvidence saveReload;
            public ContactEvidence wallFirstHit, wallHeldContact, wallAfterSeparation;
            public CheckpointEvidence northCheckpoint;
            public RedLightEvidence redLight;
            public StreetCollisionMatrixProbe.MatrixEvidence collisionMatrix;
            public string collisionMatrixJsonPath;
        }

        [Serializable] private sealed class EntryIsolationEvidence { public int disabledProductionEntryCount; public bool previewEntryCreated; public string previewSaveDirectory; }
        [Serializable] private sealed class PointEvidence { public float x, y; public PointEvidence() { } public PointEvidence(float px, float py) { x = px; y = py; } }
        [Serializable] private sealed class ActorEvidence { public string id, actorKind; public bool hasRoute, parked, stationaryPedestrian, moving; public float routeSpeed; }
        [Serializable] private sealed class WorldEvidence { public string[] objectIds; public int objectCount; }
        [Serializable] private sealed class MemoryEvidence { public string[] objectIds; public int objectCount; public float[] currentUntilValues; public bool revealedNorthWall; }
        [Serializable] private sealed class UnknownZoneEvidence { public bool signalWasUnknown, signalRemainedUnknown; public int memoryObjectCount; public string note; }
        [Serializable] private sealed class RouteMotionEvidence { public string actorId; public PointEvidence before, after; public float distanceMoved; }
        [Serializable] private sealed class RecoveryEvidence
        {
            public string actorId, checkpointId; public int collisionDelta, recoveryDelta, triggerDelta;
            public PointEvidence expectedPosition, restoredPosition, actorPositionBeforeRestore;
            public bool safeSpawnFallback, memoryRetained, memoryMarkedStale;
            public double simulationTimeAfterRestore;
            public MemoryEvidence memoryBeforeRecovery, memoryAfterRecovery;
        }
        [Serializable] private sealed class PauseFreezeEvidence
        {
            public bool menuPaused, frozen; public string resumedMenu; public int triggerCountBefore, triggerCountAfter;
            public double alphaBefore, alphaAtPause, alphaAfterFiveHalfSecondTicks;
        }
        [Serializable] private sealed class PulseSampleEvidence
        { public double requestedAge, actualAge; public float alpha; public int triggerCount; public string screenshot; }
        [Serializable] private sealed class SaveReloadEvidence
        {
            public string saveStatus, savedCheckpointId, loadedCheckpointId; public bool loadedRunning, feedbackCleared;
            public int savedCollisionCount, loadedCollisionCount; public MemoryEvidence memoryAfterLoad;
        }
        [Serializable] private sealed class ContactEvidence
        {
            public string objectId; public int collisionDelta, triggerDelta, recoveryDelta, heldCollisionDelta, heldTriggerDelta;
            public bool separatedBeforeRetest; public PointEvidence playerPosition;
        }
        [Serializable] private sealed class CheckpointEvidence
        { public string checkpointId; public PointEvidence playerPosition; public int collisions, recoveries; public MemoryEvidence memoryBeforeVehicleRecovery; }
        [Serializable] private sealed class RedLightEvidence
        {
            public string phase, signalObjectId; public bool signalIsGreen, freshSignalObserved;
            public PointEvidence trafficStoppedPositionBefore, trafficStoppedPositionAfter, playerPosition;
            public float stopLineX; public double stableForSeconds, signalObservedAt, signalExpiresAt;
            public MemoryEvidence memoryAfterRealProbe;
        }
    }
}
#endif
