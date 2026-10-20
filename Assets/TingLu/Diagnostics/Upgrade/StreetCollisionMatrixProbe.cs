#if DEVELOPMENT_BUILD || UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using TingLu.Bootstrap;
using TingLu.Config;
using TingLu.Contracts;
using TingLu.Levels;
using TingLu.UI;
using UnityEngine;

namespace TingLu.Diagnostics.Upgrade
{
    /// <summary>
    /// Captures real Player contacts for each supported street actor/obstacle in an isolated
    /// ConfigurePreview session. Every case gets a fresh StreetEntry and save directory.
    /// </summary>
    public sealed class StreetCollisionMatrixProbe : MonoBehaviour
    {
        private static readonly double[] SampleAges = { 0, .02, .04, .06, .08, .10, .14, .18, .30, .50, .74 };
        private string outputDirectory;
        private string isolatedSaveRoot;
        private StreetEntry activeEntry;

        public MatrixEvidence Result { get; private set; }
        public string JsonPath { get; private set; }
        public string[] ScreenshotPaths
        {
            get
            {
                var paths = new List<string>();
                if (Result != null && Result.cases != null)
                    foreach (MatrixCaseEvidence item in Result.cases)
                        if (item.frames != null)
                            foreach (FrameEvidence frame in item.frames)
                                if (!string.IsNullOrEmpty(frame.screenshot)) paths.Add(frame.screenshot);
                return paths.ToArray();
            }
        }

        public void Configure(string evidenceDirectory, string saveRoot)
        {
            if (string.IsNullOrWhiteSpace(evidenceDirectory) || !Path.IsPathRooted(evidenceDirectory))
                throw new ArgumentException("Matrix evidence directory must be absolute.", "evidenceDirectory");
            if (string.IsNullOrWhiteSpace(saveRoot) || !Path.IsPathRooted(saveRoot))
                throw new ArgumentException("Matrix save root must be absolute.", "saveRoot");
            outputDirectory = Path.GetFullPath(evidenceDirectory);
            isolatedSaveRoot = Path.GetFullPath(saveRoot);
        }

        public IEnumerator RunMatrix()
        {
            if (string.IsNullOrEmpty(outputDirectory) || string.IsNullOrEmpty(isolatedSaveRoot))
                throw new InvalidOperationException("Configure must be called before RunMatrix.");

            string matrixDirectory = Path.Combine(outputDirectory, "collision-matrix");
            Directory.CreateDirectory(matrixDirectory);
            JsonPath = Path.Combine(outputDirectory, "street-collision-matrix.json");
            Result = new MatrixEvidence { status = "RUNNING", cases = new List<MatrixCaseEvidence>() };
            WriteMatrix();

            CaseDefinition[] cases = BuildCases();
            StreetEntry[] existingEntries = FindObjectsOfType<StreetEntry>();
            foreach (StreetEntry oldEntry in existingEntries)
            {
                if (oldEntry != null && oldEntry.gameObject.activeSelf)
                    oldEntry.gameObject.SetActive(false);
            }

            for (int index = 0; index < cases.Length; index++)
            {
                CaseDefinition definition = cases[index];
                if (activeEntry != null)
                {
                    activeEntry.gameObject.SetActive(false);
                    Destroy(activeEntry.gameObject);
                    activeEntry = null;
                    yield return null;
                }

                string caseDirectory = Path.Combine(matrixDirectory, definition.id);
                string caseSaveDirectory = Path.Combine(isolatedSaveRoot, "matrix", definition.id);
                Directory.CreateDirectory(caseDirectory);
                Directory.CreateDirectory(caseSaveDirectory);
                StreetLevelDocument document = BuildDocument(definition);
                string json = StreetLevelCodec.Encode(document);
                StreetLevelCodec.Compile(StreetLevelCodec.Decode(json));
                File.WriteAllText(Path.Combine(caseDirectory, "fixture.json"), json, new UTF8Encoding(false));

                var evidence = new MatrixCaseEvidence
                {
                    id = definition.id,
                    kind = definition.kind,
                    moving = definition.expectedMovingAtContact,
                    expectedRecovery = definition.expectedRecovery,
                    blockingExpected = definition.expectedBlocking,
                    expectedObjectId = definition.objectId,
                    expectedPhase = definition.expectedPhase,
                    fixtureJsonPath = Path.Combine(caseDirectory, "fixture.json"),
                    saveDirectory = caseSaveDirectory,
                    frames = new List<FrameEvidence>()
                };
                Result.cases.Add(evidence);
                WriteMatrix();

                var entryObject = new GameObject("Street collision matrix " + definition.id);
                activeEntry = entryObject.AddComponent<StreetEntry>();
                activeEntry.ConfigurePreview(json, caseSaveDirectory);
                float deadline = Time.realtimeSinceStartup + 30f;
                while (Time.realtimeSinceStartup < deadline &&
                    (activeEntry.Session == null || !activeEntry.Session.Running))
                    yield return null;
                if (activeEntry.Session == null || !activeEntry.Session.Running)
                    Fail(evidence, "ConfigurePreview StreetSession did not start.");

                // Preserve the entry-owned camera, renderer, audio listener, and HUD while
                // preventing Unity input from advancing a second simulation timeline.
                activeEntry.enabled = false;
                StreetSession session = activeEntry.Session;
                evidence.courageBefore = session.Player.Current.Courage;
                evidence.positions = new PositionEvidence
                {
                    playerStart = ToEvidence(session.Player.Current.Position),
                    actorStart = ToEvidence(ActorPosition(session, definition.objectId))
                };
                evidence.worldStartTime = session.Clock.Now;
                int collisionsBefore = session.Collisions;
                int recoveriesBefore = session.Recoveries;
                int triggersBefore = session.CollisionFeedback.TriggerCount;
                Point2 beforeActorThisTick = ActorPosition(session, definition.objectId);
                Point2 actorPositionAtContact = beforeActorThisTick;
                bool receivedExpectedContact = false;
                Action<StreetContact> contactHandler = delegate(StreetContact contact)
                {
                    if (contact == null || !StringComparer.Ordinal.Equals(contact.ObjectId, definition.objectId)) return;
                    receivedExpectedContact = true;
                    evidence.contactObjectId = contact.ObjectId;
                    evidence.contactAge = session.CollisionFeedback.Age;
                    evidence.positions.playerAtContact = ToEvidence(session.Player.Current.Position);
                    actorPositionAtContact = ActorPosition(session, definition.objectId);
                    evidence.positions.actorAtContact = ToEvidence(actorPositionAtContact);
                    evidence.actualMovingAtContact = Distance(beforeActorThisTick, actorPositionAtContact) > .0001f;
                };
                session.Player.Contacted += contactHandler;

                if (definition.prepare == Preparation.RedLightStop || definition.prepare == Preparation.RouteEnd)
                {
                    bool prepared = false;
                    for (int tick = 0; tick < 500 && !prepared; tick++)
                    {
                        RequirePlaying(session, evidence);
                        beforeActorThisTick = ActorPosition(session, definition.objectId);
                        session.Tick(new StreetInputFrame(new Point2(0, 0)), .02);
                        activeEntry.PresentCurrentFrame();
                        Point2 actor = ActorPosition(session, definition.objectId);
                        prepared = Math.Abs(actor.X - definition.preparedX) < .0001f && Math.Abs(actor.Y - definition.preparedY) < .0001f;
                        if (!prepared) yield return null;
                    }
                    if (!prepared) Fail(evidence, "Routed actor did not reach its stop line or route endpoint.");
                    for (int stable = 0; stable < 6; stable++)
                    {
                        RequirePlaying(session, evidence);
                        beforeActorThisTick = ActorPosition(session, definition.objectId);
                        session.Tick(new StreetInputFrame(new Point2(0, 0)), .02);
                        activeEntry.PresentCurrentFrame();
                        Point2 after = ActorPosition(session, definition.objectId);
                        if (Distance(beforeActorThisTick, after) > .0001f)
                            Fail(evidence, "Actor moved during its required stopped-contact setup interval.");
                        yield return null;
                    }
                    evidence.stoppedForSeconds = .12;
                    evidence.stoppedPosition = ToEvidence(ActorPosition(session, definition.objectId));
                    evidence.signalPhaseAtContactSetup = session.World.SignalPhase;
                    if (!string.IsNullOrEmpty(definition.expectedPhase) && session.World.SignalPhase != definition.expectedPhase)
                        Fail(evidence, "Signal-controlled actor was not stopped during the expected phase.");
                }

                // Walk away from safe spawn before the dangerous cases. The collision
                // then has an observable return displacement, rather than testing a
                // recovery while the player already stands at the destination.
                if (definition.expectedRecovery)
                {
                    for (int walk = 0; walk < 30; walk++)
                    {
                        RequirePlaying(session, evidence);
                        beforeActorThisTick = ActorPosition(session, definition.objectId);
                        session.Tick(new StreetInputFrame(new Point2(1, 0)), .02);
                        activeEntry.PresentCurrentFrame();
                        yield return null;
                    }
                }
                evidence.positions.playerBeforeContact = ToEvidence(session.Player.Current.Position);
                string frameDirectory = Path.Combine(caseDirectory, "frames");
                Directory.CreateDirectory(frameDirectory);
                activeEntry.PresentCurrentFrame();
                var beforeFrame = new FrameEvidence
                {
                    phase = "before",
                    requestedAge = -1,
                    actualAge = -1,
                    alpha = session.CollisionFeedback.Alpha,
                    triggerCount = session.CollisionFeedback.TriggerCount - triggersBefore,
                    collisionCount = session.Collisions - collisionsBefore,
                    recoveryCount = session.Recoveries - recoveriesBefore,
                    playerPosition = ToEvidence(session.Player.Current.Position)
                };
                string beforeImage = Path.Combine(frameDirectory, "before-contact.png");
                yield return CaptureFrame(beforeImage);
                beforeFrame.screenshot = beforeImage;
                evidence.frames.Add(beforeFrame);
                evidence.beforeScreenshot = beforeImage;
                WriteMatrix();

                Point2 intendedMove = new Point2(definition.playerMoveX, definition.playerMoveY);
                bool contactFound = false;
                for (int tick = 0; tick < 700 && !contactFound; tick++)
                {
                    RequirePlaying(session, evidence);
                    beforeActorThisTick = ActorPosition(session, definition.objectId);
                    session.Tick(new StreetInputFrame(intendedMove), .02);
                    activeEntry.PresentCurrentFrame();
                    contactFound = receivedExpectedContact || session.Collisions > collisionsBefore;
                    if (!contactFound) yield return null;
                }
                if (!receivedExpectedContact || !StringComparer.Ordinal.Equals(evidence.contactObjectId, definition.objectId))
                    Fail(evidence, "A real StreetPlayerController.Contacted event for the fixture actor was not observed.");

                evidence.contactCount = session.Collisions - collisionsBefore;
                evidence.recoveryDelta = session.Recoveries - recoveriesBefore;
                evidence.triggerCount = session.CollisionFeedback.TriggerCount - triggersBefore;
                evidence.courageAfter = session.Player.Current.Courage;
                evidence.positions.playerAfterContact = ToEvidence(session.Player.Current.Position);
                evidence.positions.actorAfterContact = ToEvidence(ActorPosition(session, definition.objectId));
                evidence.checkpointIdAfterContact = session.CheckpointId;
                evidence.recoveryPositionVerified = !definition.expectedRecovery ||
                    (StringComparer.Ordinal.Equals(session.CheckpointId, "spawn") &&
                     Distance(session.Player.Current.Position, new Point2(definition.spawnX, definition.spawnY)) <= .02f &&
                     Distance(new Point2(evidence.positions.playerAtContact.x, evidence.positions.playerAtContact.y),
                         new Point2(definition.spawnX, definition.spawnY)) > .5f);
                evidence.triggerAgeAtContact = session.CollisionFeedback.Age;
                evidence.glowObserved = session.CollisionFeedback.TriggerCount > triggersBefore && session.CollisionFeedback.Alpha > 0;
                evidence.blocked = definition.expectedBlocking && IsBlockedAtObject(definition,
                    session.Player.Current.Position);

                foreach (double age in SampleAges)
                {
                    while (session.CollisionFeedback.Age + .000001 < age)
                    {
                        RequirePlaying(session, evidence);
                        double step = Math.Min(.02, age - session.CollisionFeedback.Age);
                        if (step < .000001) break;
                        session.Tick(new StreetInputFrame(new Point2(0, 0)), step);
                        activeEntry.PresentCurrentFrame();
                        yield return null;
                    }
                    var frame = new FrameEvidence
                    {
                        phase = age == 0 ? "contact" : "pulse",
                        requestedAge = age,
                        actualAge = session.CollisionFeedback.Age,
                        alpha = session.CollisionFeedback.Alpha,
                        triggerCount = session.CollisionFeedback.TriggerCount - triggersBefore,
                        collisionCount = session.Collisions - collisionsBefore,
                        recoveryCount = session.Recoveries - recoveriesBefore,
                        playerPosition = ToEvidence(session.Player.Current.Position)
                    };
                    string imagePath = Path.Combine(frameDirectory,
                        "age-" + age.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) + ".png");
                    yield return CaptureFrame(imagePath);
                    frame.screenshot = imagePath;
                    evidence.frames.Add(frame);
                    if (age == 0) evidence.contactScreenshot = imagePath;
                    if (Math.Abs(age - .74) < .000001) evidence.fadeScreenshot = imagePath;
                    WriteMatrix();
                }

                evidence.couragePreserved = Math.Abs(evidence.courageAfter - evidence.courageBefore) < .0001f;
                evidence.fadeOutVerified = evidence.frames.Count == SampleAges.Length + 1 &&
                    evidence.frames[evidence.frames.Count - 1].alpha <= .0001f;
                evidence.movingClassificationVerified = evidence.actualMovingAtContact == evidence.moving;
                evidence.passed = evidence.contactCount == 1 && evidence.recoveryDelta == (evidence.expectedRecovery ? 1 : 0) &&
                    evidence.triggerCount == 1 && evidence.glowObserved && evidence.couragePreserved &&
                    evidence.movingClassificationVerified && evidence.fadeOutVerified &&
                    evidence.recoveryPositionVerified &&
                    (!evidence.blockingExpected || evidence.blocked);
                session.Player.Contacted -= contactHandler;
                if (!evidence.passed)
                    Fail(evidence, "Contact/recovery/motion/courage/blocking/feedback assertion failed.");
                WriteMatrix();
            }

            if (activeEntry != null)
            {
                activeEntry.gameObject.SetActive(false);
                Destroy(activeEntry.gameObject);
                activeEntry = null;
            }
            Result.status = "PASS";
            Result.completedAtUtc = DateTime.UtcNow.ToString("o");
            WriteMatrix();
        }

        private IEnumerator CaptureFrame(string path)
        {
            yield return new WaitForEndOfFrame();
            ScreenCapture.CaptureScreenshot(path, 1);
            for (int frame = 0; frame < 180; frame++)
            {
                if (File.Exists(path) && new FileInfo(path).Length > 24) yield break;
                yield return null;
            }
            throw new IOException("Collision matrix screenshot was not written: " + path);
        }

        private void WriteMatrix()
        {
            if (Result == null || string.IsNullOrEmpty(JsonPath)) return;
            File.WriteAllText(JsonPath, JsonUtility.ToJson(Result, true), new UTF8Encoding(false));
        }

        private void Fail(MatrixCaseEvidence evidence, string message)
        {
            evidence.passed = false;
            evidence.error = message;
            Result.status = "FAIL";
            Result.error = evidence.id + ": " + message;
            WriteMatrix();
            throw new InvalidOperationException("Collision matrix failed for " + evidence.id + ": " + message);
        }

        private void RequirePlaying(StreetSession session, MatrixCaseEvidence evidence)
        {
            if (session == null || !session.Running || session.Clock.Paused || session.Menu.Screen != MenuScreen.Playing)
                Fail(evidence, "StreetEntry lost gameplay focus, paused, or left the Playing screen during matrix evidence capture.");
        }

        private static StreetLevelDocument BuildDocument(CaseDefinition definition)
        {
            var document = new StreetLevelDocument
            {
                schemaVersion = StreetLevelDocument.CurrentSchemaVersion,
                id = "probe.collision.matrix." + definition.id,
                title = "碰撞矩阵取证 " + definition.id,
                goalText = "沿着安全路线继续前进。",
                spawnX = definition.spawnX,
                spawnY = definition.spawnY,
                facingX = Math.Abs(definition.playerMoveX) + Math.Abs(definition.playerMoveY) < .0001f ? 0 : definition.playerMoveX,
                facingY = Math.Abs(definition.playerMoveX) + Math.Abs(definition.playerMoveY) < .0001f ? 1 : definition.playerMoveY,
                goalX = 24,
                goalY = 24,
                goalRadius = .5f
            };
            document.surfaces.Add(new StreetSurfaceData
            {
                id = "matrix.sidewalk",
                materialId = "sidewalk",
                polygon = Rect(-32, -32, 32, 32),
                textureAxis = new StreetPointData(1, 0),
                priority = 10,
                heightBase = .15f
            });

            bool isStructure = definition.actorKind == StreetActorKind.None;
            var actor = new StreetObjectData
            {
                id = definition.objectId,
                kind = isStructure ? StreetKind.Permanent : StreetKind.Active,
                actorKind = definition.actorKind,
                position = new StreetPointData(definition.actorStartX, definition.actorStartY),
                polygon = Rect(definition.actorStartX - definition.halfX, definition.actorStartY - definition.halfY,
                    definition.actorStartX + definition.halfX, definition.actorStartY + definition.halfY),
                materialId = isStructure ? definition.materialId : "metal",
                solid = true,
                vehicleHazard = definition.actorKind == StreetActorKind.Car || definition.actorKind == StreetActorKind.Bicycle ||
                    definition.actorKind == StreetActorKind.ElectricBike,
                blocksProbe = isStructure,
                acousticLoss = isStructure ? .4f : 0,
                height = isStructure ? 2.5f : 1.2f,
                soundId = isStructure ? null : definition.soundId,
                soundActive = !isStructure,
                semantic = definition.actorKind == StreetActorKind.Pedestrian ? PerceptionSemantic.Person :
                    definition.actorKind == StreetActorKind.None ? PerceptionSemantic.Ordinary : PerceptionSemantic.Threat,
                contactDamage = 99,
                contactStun = 10
            };
            document.objects.Add(actor);

            if (definition.route != null)
            {
                var route = new StreetRouteData
                {
                    objectId = definition.objectId,
                    speed = definition.routeSpeed,
                    signalAxis = definition.signalAxis,
                    stopLineDistance = definition.stopLineDistance,
                    loop = false
                };
                foreach (Point2 point in definition.route) route.points.Add(StreetPointData.From(point));
                document.routes.Add(route);
            }

            if (definition.prepare == Preparation.RedLightStop)
            {
                document.objects.Add(new StreetObjectData
                {
                    id = "street.signal.ns",
                    kind = StreetKind.Permanent,
                    position = new StreetPointData(0, 12),
                    polygon = Rect(-.18f, 11.82f, .18f, 12.18f),
                    materialId = "metal",
                    height = 2,
                    soundId = "street.signal.ns",
                    semantic = PerceptionSemantic.Guide
                });
            }
            return document;
        }

        private static CaseDefinition[] BuildCases()
        {
            return new[]
            {
                Stationary("person-standing", "pedestrian.standing", "Pedestrian", StreetActorKind.Pedestrian, "street.walker", .25f, .30f),
                Routed("person-walking", "pedestrian.walking", "Pedestrian", StreetActorKind.Pedestrian, "street.walker", 4f, false),
                Stationary("car-parked", "car.parked", "Car", StreetActorKind.Car, "street.car", .48f, .30f),
                Stationary("bicycle-parked", "bicycle.parked", "Bicycle", StreetActorKind.Bicycle, "street.cycle", .34f, .22f),
                Stationary("electric-bike-parked", "electric-bike.parked", "ElectricBike", StreetActorKind.ElectricBike, "street.electric", .40f, .24f),
                Routed("car-moving", "car.moving", "Car", StreetActorKind.Car, "street.car", 4f, true),
                Routed("bicycle-moving", "bicycle.moving", "Bicycle", StreetActorKind.Bicycle, "street.cycle", 4f, true),
                Routed("electric-bike-moving", "electric-bike.moving", "ElectricBike", StreetActorKind.ElectricBike, "street.electric", 4f, true),
                Structure("wall", "matrix.wall", "Wall", "brick", .22f, .60f),
                Structure("building", "matrix.building", "Building", "brick", .35f, .50f),
                Structure("barrier", "matrix.barrier", "Barrier", "metal", .55f, .18f),
                StoppedVehicle("vehicle-red-light-stop", Preparation.RedLightStop, "ns"),
                StoppedVehicle("vehicle-route-end", Preparation.RouteEnd, null)
            };
        }

        private static CaseDefinition Stationary(string id, string objectId, string kind,
            StreetActorKind actorKind, string sound, float halfX, float halfY)
        {
            return new CaseDefinition
            {
                id = id, objectId = objectId, kind = kind, actorKind = actorKind, soundId = sound,
                spawnX = -1.5f, spawnY = 0, playerMoveX = 1, playerMoveY = 0,
                actorStartX = 0, actorStartY = 0, halfX = halfX, halfY = halfY,
                expectedBlocking = true
            };
        }

        private static CaseDefinition Routed(string id, string objectId, string kind,
            StreetActorKind actorKind, string sound, float speed, bool danger)
        {
            return new CaseDefinition
            {
                id = id, objectId = objectId, kind = kind, actorKind = actorKind, soundId = sound,
                spawnX = 0, spawnY = 0, playerMoveX = 0, playerMoveY = 0,
                actorStartX = -5, actorStartY = 0, halfX = actorKind == StreetActorKind.Pedestrian ? .25f : .45f,
                halfY = actorKind == StreetActorKind.Pedestrian ? .25f : .30f,
                route = new[] { new Point2(-5, 0), new Point2(5, 0) }, routeSpeed = speed,
                expectedMovingAtContact = true, expectedRecovery = danger
            };
        }

        private static CaseDefinition Structure(string id, string objectId, string kind, string material,
            float halfX, float halfY)
        {
            return new CaseDefinition
            {
                id = id, objectId = objectId, kind = kind, actorKind = StreetActorKind.None, materialId = material,
                spawnX = -1.5f, spawnY = 0, playerMoveX = 1, playerMoveY = 0,
                actorStartX = 0, actorStartY = 0, halfX = halfX, halfY = halfY,
                expectedBlocking = true
            };
        }

        private static CaseDefinition StoppedVehicle(string id, Preparation preparation, string expectedPhase)
        {
            float startX = preparation == Preparation.RedLightStop ? -4 : -5;
            float stopX = preparation == Preparation.RedLightStop ? -1 : 0;
            float endX = preparation == Preparation.RedLightStop ? 4 : 0;
            return new CaseDefinition
            {
                id = id,
                objectId = id == "vehicle-red-light-stop" ? "car.red-light" : "car.route-end",
                kind = "Car",
                actorKind = StreetActorKind.Car,
                soundId = "street.car",
                spawnX = stopX,
                spawnY = -1,
                playerMoveX = 0,
                playerMoveY = 1,
                actorStartX = startX,
                actorStartY = 0,
                halfX = .45f,
                halfY = .32f,
                route = new[] { new Point2(startX, 0), new Point2(endX, 0) },
                routeSpeed = preparation == Preparation.RedLightStop ? 4 : 4,
                signalAxis = preparation == Preparation.RedLightStop ? "ew" : null,
                stopLineDistance = preparation == Preparation.RedLightStop ? 3 : (float?)null,
                prepare = preparation,
                preparedX = stopX,
                preparedY = 0,
                expectedPhase = expectedPhase,
                expectedBlocking = true
            };
        }

        private static Point2 ActorPosition(StreetSession session, string objectId)
        {
            foreach (StreetObject item in session.World.Capture().Objects)
                if (StringComparer.Ordinal.Equals(item.Id, objectId)) return item.Position;
            throw new InvalidOperationException("Collision fixture omitted its actor: " + objectId);
        }

        private static bool IsBlockedAtObject(CaseDefinition definition, Point2 player)
        {
            if (definition.playerMoveX > 0)
                return player.X < definition.actorStartX - definition.halfX + .08f && player.X > definition.spawnX + .15f;
            if (definition.playerMoveY > 0)
                return player.Y < definition.actorStartY - definition.halfY + .08f && player.Y > definition.spawnY + .15f;
            return false;
        }

        private static float Distance(Point2 a, Point2 b)
        {
            float dx = a.X - b.X, dy = a.Y - b.Y;
            return (float)Math.Sqrt(dx * dx + dy * dy);
        }

        private static PointEvidence ToEvidence(Point2 point) { return new PointEvidence { x = point.X, y = point.Y }; }

        private static List<StreetPointData> Rect(float x0, float y0, float x1, float y1)
        {
            return new List<StreetPointData>
            {
                new StreetPointData(x0, y0), new StreetPointData(x1, y0),
                new StreetPointData(x1, y1), new StreetPointData(x0, y1)
            };
        }

        private enum Preparation { None, RedLightStop, RouteEnd }

        private sealed class CaseDefinition
        {
            public string id, objectId, kind, soundId, materialId, signalAxis, expectedPhase;
            public StreetActorKind actorKind;
            public float spawnX, spawnY, playerMoveX, playerMoveY, actorStartX, actorStartY, halfX, halfY;
            public float routeSpeed, preparedX, preparedY;
            public float? stopLineDistance;
            public Point2[] route;
            public Preparation prepare;
            public bool expectedMovingAtContact, expectedRecovery, expectedBlocking;
        }

        [Serializable]
        public sealed class MatrixEvidence
        {
            public string status, error, completedAtUtc;
            public List<MatrixCaseEvidence> cases;
        }

        [Serializable]
        public sealed class MatrixCaseEvidence
        {
            public string id, kind, expectedObjectId, contactObjectId, expectedPhase, signalPhaseAtContactSetup, checkpointIdAfterContact;
            public string fixtureJsonPath, saveDirectory, error;
            public string beforeScreenshot, contactScreenshot, fadeScreenshot;
            public bool moving, actualMovingAtContact, expectedRecovery, blockingExpected, blocked;
            public bool glowObserved, couragePreserved, fadeOutVerified, movingClassificationVerified, recoveryPositionVerified, passed;
            public int contactCount, recoveryDelta, triggerCount;
            public float courageBefore, courageAfter;
            public double worldStartTime, contactAge, triggerAgeAtContact, stoppedForSeconds;
            public PositionEvidence positions;
            public PointEvidence stoppedPosition;
            public List<FrameEvidence> frames;
        }

        [Serializable]
        public sealed class PositionEvidence
        {
            public PointEvidence playerStart, playerBeforeContact, playerAtContact, playerAfterContact;
            public PointEvidence actorStart, actorAtContact, actorAfterContact;
        }

        [Serializable]
        public sealed class PointEvidence { public float x, y; }

        [Serializable]
        public sealed class FrameEvidence
        {
            public string phase;
            public double requestedAge, actualAge;
            public float alpha;
            public int triggerCount, collisionCount, recoveryCount;
            public PointEvidence playerPosition;
            public string screenshot;
        }
    }
}
#endif
