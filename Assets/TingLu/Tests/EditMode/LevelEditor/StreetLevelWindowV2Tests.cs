using System;
using System.IO;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using TingLu.Contracts;
using TingLu.Levels;
using TingLu.Levels.Editor;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

namespace TingLu.Tests.StreetEditorV2
{
    public sealed class StreetLevelWindowV2Tests
    {
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        private const string DraftKey = "TingLu.LevelEditorV2.Draft";
        private LevelEditorV2Window window;
        private string previousDraft, directory;
        private T Read<T>(string name) { return (T)window.GetType().GetField(name, Private).GetValue(window); }
        private void Write(string name, object value) { window.GetType().GetField(name, Private).SetValue(window, value); }
        private object Invoke(string name, params object[] args) { return window.GetType().GetMethod(name, Private).Invoke(window, args); }
        private StreetLevelDocument Document { get { return Read<StreetLevelDocument>("document"); } }

        [SetUp]
        public void Setup()
        {
            previousDraft = SessionState.GetString(DraftKey, "");
            SessionState.EraseString(DraftKey);
            directory = Path.Combine(Path.GetTempPath(), "TingLuV2WindowTest", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            window = ScriptableObject.CreateInstance<LevelEditorV2Window>();
        }

        [TearDown]
        public void Cleanup()
        {
            if (window != null)
            {
                Undo.ClearUndo(Read<LevelEditBuffer>("buffer"));
                window.DiscardChanges();
                UnityEngine.Object.DestroyImmediate(window);
            }
            SessionState.SetString(DraftKey, previousDraft);
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }

        [Test]
        public void FirstTemplateIsAnUnsavedStreetDocument()
        {
            Assert.AreEqual(2, Document.schemaVersion);
            Assert.IsEmpty(Read<string>("filePath"));
            Assert.IsTrue(window.hasUnsavedChanges, "A new template must not pretend it has been saved.");
        }

        [Test]
        public void MovingAnActorMovesItsPolygonAndRouteAndUndoRestoresAllOfThem()
        {
            Invoke("Select", "object", "rider.east.sidewalk");
            float oldX = Document.objects.Find(item => item.id == "rider.east.sidewalk").position.x;
            float oldPolygonX = Document.objects.Find(item => item.id == "rider.east.sidewalk").polygon[0].x;
            float oldRouteX = Document.routes.Find(route => route.objectId == "rider.east.sidewalk").points[1].x;
            Undo.IncrementCurrentGroup();
            Invoke("MoveSelection", 2f, 3f);
            Invoke("Commit", "test move actor");
            Undo.FlushUndoRecordObjects();
            Undo.IncrementCurrentGroup();
            Assert.AreEqual(oldX + 2, Document.objects.Find(item => item.id == "rider.east.sidewalk").position.x, .001f);
            Assert.AreEqual(oldPolygonX + 2, Document.objects.Find(item => item.id == "rider.east.sidewalk").polygon[0].x, .001f);
            Assert.AreEqual(oldRouteX + 2, Document.routes.Find(route => route.objectId == "rider.east.sidewalk").points[1].x, .001f);
            Undo.PerformUndo();
            Assert.AreEqual(oldX, Document.objects.Find(item => item.id == "rider.east.sidewalk").position.x, .001f);
            Assert.AreEqual(oldRouteX, Document.routes.Find(route => route.objectId == "rider.east.sidewalk").points[1].x, .001f);
        }

        [Test]
        public void MovingARampPreservesItsHeightAtTheCorrespondingLocalPoint()
        {
            var ramp = Document.surfaces.Find(surface => surface.id == "ramp.ns.north.west");
            Point2 original = ramp.polygon[0].ToPoint();
            float height = ramp.heightBase + ramp.heightSlopeX * original.X + ramp.heightSlopeY * original.Y;
            Invoke("Select", "surface", ramp.id);
            Invoke("MoveSelection", 3f, 2f);
            Assert.AreEqual(height, ramp.heightBase + ramp.heightSlopeX * (original.X + 3) + ramp.heightSlopeY * (original.Y + 2), .0001f);
        }

        [Test]
        public void GameplayInvalidDraftCanBeSavedReloadedAndRetainedAcrossEditorReload()
        {
            string path = Path.Combine(directory, "草稿.json");
            Document.goalRadius = 0;
            Invoke("Commit", "unfinished goal");
            Write("filePath", path);
            Write("fileAssetGuid", "");
            Assert.IsTrue((bool)Invoke("Save", false));
            Assert.AreEqual(0, StreetLevelFileStore.Load(path).goalRadius);
            Assert.IsFalse(window.hasUnsavedChanges);
            Document.title = "未保存的标题";
            Invoke("Commit", "edit title");
            Invoke("OnDisable");
            Invoke("OnEnable");
            Assert.AreEqual("未保存的标题", Document.title);
            Assert.AreEqual(0, Document.goalRadius);
            Assert.IsTrue(window.hasUnsavedChanges);
            Assert.AreNotEqual("未保存的标题", StreetLevelFileStore.Load(path).title);
        }

        [Test]
        public void CancellingADragRestoresTheExactDocumentAndKeepsUndoBufferConsistent()
        {
            string before = Read<LevelEditBuffer>("buffer").json;
            Write("dragOriginal", before);
            Write("dragging", true);
            Invoke("Select", "object", "rider.east.sidewalk");
            Invoke("MoveSelection", 3f, 4f);
            Invoke("CancelPlacement");
            Assert.AreEqual(before, StreetLevelCodec.Encode(Document));
            Assert.AreEqual(before, Read<LevelEditBuffer>("buffer").json);
            Assert.IsFalse(Read<bool>("dragging"));
        }

        [Test]
        public void MovingTheFirstRouteNodeKeepsTheActorAndPolygonAnchored()
        {
            const string id = "rider.east.sidewalk";
            var actor = Document.objects.Find(item => item.id == id);
            var route = Document.routes.Find(item => item.objectId == id);
            float polygonX = actor.polygon[0].x;
            Invoke("SelectRoute", id);
            Write("selectedRoutePoint", 0);
            Invoke("MoveSelection", 1f, 2f);
            Assert.AreEqual(route.points[0].x, actor.position.x, .001f);
            Assert.AreEqual(route.points[0].y, actor.position.y, .001f);
            Assert.AreEqual(polygonX + 1, actor.polygon[0].x, .001f);
            Assert.IsEmpty(StreetLevelCodec.Validate(Document));
        }

        [UnityTest]
        public IEnumerator EveryWorkspaceAndInspectorRendersWithoutLayoutOrDocumentErrors()
        {
            window.position = new Rect(100, 100, 1100, 700);
            window.Show();
            for (int activeTab = 0; activeTab < 4; activeTab++)
            {
                Write("tab", activeTab);
                window.Repaint();
                yield return null;
                yield return null;
            }
            Write("tab", 1);
            foreach (string kind in new[] { "surface", "object", "checkpoint", "spawn", "goal", "route" })
            {
                if (kind == "route") Invoke("SelectRoute", Document.routes[0].objectId);
                else Invoke("Select", kind, kind == "surface" ? Document.surfaces[0].id : kind == "object" ? Document.objects[0].id : kind == "checkpoint" ? Document.checkpoints[0].id : "");
                window.Repaint();
                yield return null;
                yield return null;
            }
            Assert.IsFalse(Read<List<string>>("issues").Exists(issue => issue.StartsWith("操作未完成")));
            Assert.IsEmpty(StreetLevelCodec.Validate(Document), "Rendering fields must not silently change valid data.");
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void InvalidNumericEditRollsBackToTheLastDrawableDocument()
        {
            float before = Document.spawnX;
            Document.spawnX = float.NaN;
            Invoke("Commit", "invalid number");
            Assert.AreEqual(before, Document.spawnX);
            Assert.DoesNotThrow(() => StreetLevelCodec.Decode(Read<LevelEditBuffer>("buffer").json));
        }
    }
}
