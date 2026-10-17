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

namespace TingLu.Tests.LevelEditor
{
    // These exercise actual Unity editor/asset/undo APIs. They do not simulate OS drag/drop,
    // file pickers or close-confirmation dialogs. One UnityTest enters/exits actual Play Mode.
    [TestFixture]
    public sealed class LevelEditorUnityTests
    {
        private const string WelcomeKey="TingLu.LevelEditor.Welcomed";
        private LevelEditorWindow window;
        private string temporaryFiles,temporaryAssets;
        private bool hadWelcome,wasWelcomed;
        private const BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;

        [SetUp]
        public void SetUp()
        {
            hadWelcome=EditorPrefs.HasKey(WelcomeKey);wasWelcomed=EditorPrefs.GetBool(WelcomeKey,false);
            EditorPrefs.SetBool(WelcomeKey,true); // No first-use modal during unattended tests.
            temporaryFiles=Path.Combine(Path.GetTempPath(),"听路 Unity 关卡测试",Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(temporaryFiles);
            string assetName="LevelEditorUnityTest_"+Guid.NewGuid().ToString("N");
            temporaryAssets="Assets/"+assetName;AssetDatabase.CreateFolder("Assets",assetName);
            window=ScriptableObject.CreateInstance<LevelEditorWindow>();
        }

        [TearDown]
        public void TearDown()
        {
            if(window!=null)
            {
                var buffer=Read<LevelEditBuffer>(window,"buffer");
                if(buffer!=null)Undo.ClearUndo(buffer);
                window.DiscardChanges();UnityEngine.Object.DestroyImmediate(window);
            }
            // Never delete a folder containing scripts/modules, even after an interrupted test.
            if(!string.IsNullOrEmpty(temporaryAssets)&&AssetDatabase.IsValidFolder(temporaryAssets))
            {
                string absolute=EditorProjectPaths.AssetAbsolutePath(temporaryAssets);
                if(Directory.GetFiles(absolute,"*.cs",SearchOption.AllDirectories).Length==0&&Directory.GetFiles(absolute,"*.asmdef",SearchOption.AllDirectories).Length==0)
                    AssetDatabase.DeleteAsset(temporaryAssets);
            }
            if(!string.IsNullOrEmpty(temporaryFiles)&&Directory.Exists(temporaryFiles))Directory.Delete(temporaryFiles,true);
            if(hadWelcome)EditorPrefs.SetBool(WelcomeKey,wasWelcomed);else EditorPrefs.DeleteKey(WelcomeKey);
        }

        private static T Read<T>(object obj,string name)
        {return (T)obj.GetType().GetField(name,Private).GetValue(obj);}
        private static void Write(object obj,string name,object value)
        {obj.GetType().GetField(name,Private).SetValue(obj,value);}
        private static object Invoke(object obj,string name,params object[] args)
        {
            try{return obj.GetType().GetMethod(name,Private).Invoke(obj,args);}
            catch(TargetInvocationException error){throw error.InnerException??error;}
        }
        private LevelDocument Document { get { return Read<LevelDocument>(window,"document"); } }
        private void Commit(string name)
        {Invoke(window,"Commit",name);Undo.FlushUndoRecordObjects();Undo.IncrementCurrentGroup();}

        [Test]
        public void ActualEditorWindowLoadsTemplateAndUnityUndoRedoRestoresObjectAndNode()
        {
            Assert.NotNull(window);
            Assert.AreEqual("第一步（可编辑模板）",Document.name);
            Assert.AreEqual(8,Document.guide.Count);
            Assert.Less(Document.guide[2].x,-.6f,"Real pole bypass must load in editor window.");
            LevelValidator.RequireValid(Document);
            var pole=Document.items.Find(i=>i.type=="pole");string poleId=pole.id;
            float originalX=pole.x,originalRotation=pole.rotation,originalNodeY=Document.guide[2].y;
            var buffer=Read<LevelEditBuffer>(window,"buffer");Undo.ClearUndo(buffer);Undo.IncrementCurrentGroup();
            pole.x+=1.25f;pole.rotation+=45;Commit("Unity 验证：移动及旋转对象");
            Document.guide[2].y+=.25f;Commit("Unity 验证：编辑盲道节点");
            Assert.AreEqual(originalX+1.25f,Document.items.Find(i=>i.id==poleId).x);
            Assert.AreEqual(originalNodeY+.25f,Document.guide[2].y);
            Undo.PerformUndo();
            Assert.AreEqual(originalNodeY,Document.guide[2].y,"Unity Undo invokes window reload.");
            Assert.AreEqual(originalX+1.25f,Document.items.Find(i=>i.id==poleId).x);
            Undo.PerformUndo();
            Assert.AreEqual(originalX,Document.items.Find(i=>i.id==poleId).x);
            Assert.AreEqual(originalRotation,Document.items.Find(i=>i.id==poleId).rotation);
            Undo.PerformRedo();Undo.PerformRedo();
            Assert.AreEqual(originalX+1.25f,Document.items.Find(i=>i.id==poleId).x);
            Assert.AreEqual(originalRotation+45,Document.items.Find(i=>i.id==poleId).rotation);
            Assert.AreEqual(originalNodeY+.25f,Document.guide[2].y);
            Assert.AreEqual(LevelCodec.Encode(Document),buffer.json,"Window and Unity undo buffer must agree.");
        }

        [Test]
        public void WindowSaveAndReloadUseActualFilesystemAndPreserveStableIds()
        {
            var edited=LevelCodec.Duplicate(Document);edited.name="真实 Unity 保存重开";
            var pole=edited.items.Find(i=>i.type=="pole");pole.x=2.25f;string id=pole.id;
            string path=Path.Combine(temporaryFiles,"中文 文件 第二关.json");
            Invoke(window,"SetDocument",edited,path);Write(window,"savedJson","");
            Assert.IsTrue((bool)Invoke(window,"Save",false),"No file picker: configured path saves real JSON.");
            Assert.IsTrue(File.Exists(path));Assert.IsFalse(window.hasUnsavedChanges);
            edited=LevelCodec.Load(path);Invoke(window,"SetDocument",edited,path);
            Document.items.Find(i=>i.id==id).x=3.25f;Commit("改动第二关");
            Assert.IsTrue((bool)Invoke(window,"Save",false));Assert.IsTrue(File.Exists(path+".bak"));
            Invoke(window,"SetDocument",FirstStepTemplate.Blank(),"");
            Invoke(window,"SetDocument",LevelCodec.Load(path),path);
            Assert.AreEqual("真实 Unity 保存重开",Document.name);
            Assert.AreEqual(3.25f,Document.items.Find(i=>i.id==id).x);
            Assert.AreEqual(edited.id,Document.id);Assert.IsFalse(window.hasUnsavedChanges);
        }

        [Test]
        public void WindowTracksMovedJsonAssetByGuidAndWritesOnlyTheNewPath()
        {
            string oldPath=temporaryAssets+"/旧 文件.json",newPath=temporaryAssets+"/重命名 第二关.json";
            var data=FirstStepTemplate.Blank();data.name="资产移动保存验收";
            Invoke(window,"SetDocument",data,oldPath);Write(window,"savedJson","");
            Assert.IsTrue((bool)Invoke(window,"Save",false));
            string guid=AssetDatabase.AssetPathToGUID(oldPath);Assert.IsNotEmpty(guid);
            Assert.AreEqual(oldPath,Read<string>(window,"filePath"));Assert.AreEqual(guid,Read<string>(window,"fileAssetGuid"));
            Assert.AreEqual("",AssetDatabase.MoveAsset(oldPath,newPath));AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            Document.name="移动后保存内容";Commit("资产移动后编辑");
            Assert.IsTrue((bool)Invoke(window,"Save",false));
            Assert.IsFalse(File.Exists(EditorProjectPaths.AssetAbsolutePath(oldPath)),"Saving may not recreate the original asset location.");
            Assert.AreEqual("移动后保存内容",LevelCodec.Load(EditorProjectPaths.AssetAbsolutePath(newPath)).name);
            Assert.AreEqual(newPath,Read<string>(window,"filePath"));Assert.AreEqual(guid,Read<string>(window,"fileAssetGuid"));
        }

        [Test]
        public void ActualTemplateTextAssetAndVoiceMaterialResolveThroughUnityAssetDatabase()
        {
            string path=EditorProjectPaths.DataAssetDirectory+"/FirstStepTemplate.json";
            var asset=AssetDatabase.LoadAssetAtPath<TextAsset>(path);
            Assert.NotNull(asset,"Unity must import JSON template as a TextAsset.");
            var template=LevelCodec.Decode(asset.text);LevelValidator.RequireValid(template);
            Assert.AreEqual(8,template.guide.Count);
            var material=EditorProjectPaths.RequireVoiceMaterial();Assert.NotNull(material);Assert.NotNull(material.shader);
            Assert.IsNotEmpty(AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(material)));
        }

        [Test]
        public void PreviewEntryConfigurationAndMaterialSurviveRealUnitySceneSerialization()
        {
            var baseline=SceneManager.GetActiveScene();
            if(string.IsNullOrEmpty(baseline.path))Assert.IsTrue(EditorSceneManager.SaveScene(baseline,temporaryAssets+"/Baseline.unity"));
            var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Additive);
            string scenePath=temporaryAssets+"/PreviewSerialized.unity";
            var data=FirstStepTemplate.Blank();data.name="序列化试玩";
            string isolatedSave=Path.Combine(temporaryFiles,"试玩 专属存档");
            try
            {
                var root=new GameObject("Serialized preview entry");SceneManager.MoveGameObjectToScene(root,scene);
                var entry=root.AddComponent<DemoEntry>();entry.ConfigurePreview(LevelCodec.Encode(data),isolatedSave);
                var serialized=new SerializedObject(entry);
                serialized.FindProperty("voiceMaterial").objectReferenceValue=EditorProjectPaths.RequireVoiceMaterial();
                serialized.ApplyModifiedPropertiesWithoutUndo();
                Assert.IsTrue(EditorSceneManager.SaveScene(scene,scenePath));
            }
            finally{if(scene.IsValid()&&scene.isLoaded)EditorSceneManager.CloseScene(scene,true);}
            scene=EditorSceneManager.OpenScene(scenePath,OpenSceneMode.Additive);
            try
            {
                var entry=scene.GetRootGameObjects()[0].GetComponent<DemoEntry>();Assert.NotNull(entry);
                var serialized=new SerializedObject(entry);
                var decoded=LevelCodec.Decode(serialized.FindProperty("previewJson").stringValue);
                Assert.AreEqual(data.id,decoded.id);Assert.AreEqual(data.name,decoded.name);
                Assert.AreEqual(isolatedSave,serialized.FindProperty("previewSaveDirectory").stringValue);
                Assert.AreSame(EditorProjectPaths.RequireVoiceMaterial(),serialized.FindProperty("voiceMaterial").objectReferenceValue);
                Assert.AreNotEqual(Path.GetFullPath(Application.persistentDataPath),Path.GetFullPath(isolatedSave));
            }
            finally{if(scene.IsValid()&&scene.isLoaded)EditorSceneManager.CloseScene(scene,true);}
        }

        [Test]
        public void RouteEditingCopiesAndDeletesActorReferencesAndUnityUndoRestoresMovedNodes()
        {
            var data=FirstStepTemplate.Blank();var actor=LevelCatalog.Create("walker",4,3);data.items.Add(actor);
            var route=new LevelMovementRoute{actorId=actor.id,loop=false};
            route.waypoints.Add(new LevelMovementWaypoint(0,4,3));route.waypoints.Add(new LevelMovementWaypoint(2,4,5));data.movementRoutes.Add(route);
            Invoke(window,"SetDocument",data,"");Undo.IncrementCurrentGroup();
            Invoke(window,"AppendMovementPoint",actor,new TingLu.Contracts.Point2(6,5));Undo.FlushUndoRecordObjects();Undo.IncrementCurrentGroup();
            Assert.AreEqual(4,Document.movementRoutes[0].waypoints[2].timeSeconds);
            Assert.AreEqual(6,Document.movementRoutes[0].waypoints[2].x);
            float originalActorX=actor.x,originalFirstX=route.waypoints[0].x;
            Invoke(window,"TranslateMovementRoute",actor,2f,1f);actor.x+=2;actor.y+=1;Commit("人物及路线平移");
            Assert.AreEqual(originalFirstX+2,Document.movementRoutes[0].waypoints[0].x);
            Undo.PerformUndo();Assert.AreEqual(originalActorX,Document.items.Find(i=>i.id==actor.id).x);Assert.AreEqual(originalFirstX,Document.movementRoutes[0].waypoints[0].x);
            Undo.PerformRedo();actor=Document.items.Find(i=>i.id==actor.id);
            Write(window,"clipboard",(string)Invoke(window,"EncodeItem",actor));Invoke(window,"Paste");Undo.FlushUndoRecordObjects();Undo.IncrementCurrentGroup();
            string copiedId=Read<string>(window,"selectedId");Assert.AreNotEqual(actor.id,copiedId);
            var copy=Document.movementRoutes.Find(r=>r.actorId==copiedId);Assert.NotNull(copy);
            var original=Document.movementRoutes.Find(r=>r.actorId==actor.id);
            Assert.AreNotEqual(original.id,copy.id);Assert.AreNotEqual(original.waypoints[0].id,copy.waypoints[0].id);
            Assert.AreEqual(original.waypoints[0].x+.5f,copy.waypoints[0].x);
            Invoke(window,"Delete");Undo.FlushUndoRecordObjects();Undo.IncrementCurrentGroup();
            Assert.IsNull(Document.items.Find(i=>i.id==copiedId));Assert.IsNull(Document.movementRoutes.Find(r=>r.actorId==copiedId));
            Undo.PerformUndo();Assert.NotNull(Document.items.Find(i=>i.id==copiedId));Assert.NotNull(Document.movementRoutes.Find(r=>r.actorId==copiedId));
        }

        [UnityTest]
        public IEnumerator PreparedPreviewActuallyEntersPlayReadsDataAndRestoresSceneAfterExit()
        {
            if(Environment.GetEnvironmentVariable("TINGLU_EDITOR_RELOCATION_TESTS")!="1")
                Assert.Ignore("Play/untitled scene setup runs only in the isolated verification project.");
            var baseline=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
            Assert.IsTrue(string.IsNullOrEmpty(baseline.path));Assert.IsFalse(baseline.isDirty);
            string baselinePath=SceneManager.GetActiveScene().path;
            var originalStart=EditorSceneManager.playModeStartScene;
            string originalStartPath=originalStart==null?"":AssetDatabase.GetAssetPath(originalStart);
            var data=FirstStepTemplate.Blank();data.items[0].x=20;data.items[0].y=30;
            data.items[1].x=20;data.items[1].y=40;data.guide[0].x=20;data.guide[0].y=30;data.guide[1].x=20;data.guide[1].y=40;
            var host=typeof(LevelEditorWindow).Assembly.GetType("TingLu.Levels.Editor.LevelPreviewHost");
            string previewPath=(string)host.GetMethod("Prepare",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{data,false});
            Assert.IsNotEmpty(previewPath);Assert.That(SceneManager.GetActiveScene().path,Does.Contain("Untitled-"));
            string expectedId=data.id;
            SessionState.SetString("TingLu.Tests.PreviewPath",previewPath);SessionState.SetString("TingLu.Tests.ExpectedPreviewId",expectedId);
            SessionState.SetString("TingLu.Tests.Baseline",baselinePath);SessionState.SetString("TingLu.Tests.Start",originalStartPath);
            yield return new EnterPlayMode();
            yield return null;yield return null;
            var entry=UnityEngine.Object.FindObjectOfType<DemoEntry>();Assert.NotNull(entry);
            var session=Read<DemoSession>(entry,"session");Assert.NotNull(session);Assert.IsTrue(session.Running);
            Assert.AreEqual(20,session.Player.Current.Position.X);Assert.AreEqual(30,session.Player.Current.Position.Y);
            string isolated=Read<string>(entry,"previewSaveDirectory");Assert.That(isolated,Does.Contain("TingLuLevelPreview"));
            Assert.AreNotEqual(Path.GetFullPath(Application.persistentDataPath),Path.GetFullPath(isolated));
            Assert.IsTrue(File.Exists(Path.Combine(isolated,"TingLu","resume.json")));
            yield return new ExitPlayMode();
            yield return null;
            Assert.IsNull(AssetDatabase.LoadAssetAtPath<SceneAsset>(SessionState.GetString("TingLu.Tests.PreviewPath","")));
            Assert.AreEqual(SessionState.GetString("TingLu.Tests.Baseline",""),SceneManager.GetActiveScene().path);
            Assert.IsFalse(SceneManager.GetActiveScene().isDirty,"Clean untitled scene must not gain a save prompt after preview.");
            var restored=EditorSceneManager.playModeStartScene;
            Assert.AreEqual(SessionState.GetString("TingLu.Tests.Start",""),restored==null?"":AssetDatabase.GetAssetPath(restored));
        }

        [Test]
        public void ProjectAndDataPathsIgnoreChangedWorkingDirectoryAndRejectTraversal()
        {
            string cwd=Directory.GetCurrentDirectory();string project=Directory.GetParent(Path.GetFullPath(Application.dataPath)).FullName;
            string expectedData=Path.Combine(project,EditorProjectPaths.DataAssetDirectory.Replace('/',Path.DirectorySeparatorChar));
            try
            {
                Directory.SetCurrentDirectory(temporaryFiles);
                Assert.AreEqual(project,EditorProjectPaths.ProjectDirectory);
                Assert.AreEqual(Path.GetFullPath(expectedData),EditorProjectPaths.DataDirectory);
                Assert.AreEqual(Path.GetFullPath(Path.Combine(project,"Assets", "路径 空格.json")),EditorProjectPaths.AbsoluteFilePath("Assets/路径 空格.json"));
                Assert.That(EditorProjectPaths.AssetAbsolutePath(EditorProjectPaths.PreviewAssetDirectory),Does.StartWith(Path.GetFullPath(Application.dataPath)));
                Assert.Throws<ArgumentException>(()=>EditorProjectPaths.AssetAbsolutePath("Assets/../Packages/bad.json"));
                Assert.Throws<ArgumentException>(()=>EditorProjectPaths.AssetAbsolutePath("../Assets/bad.json"));
                Assert.Throws<ArgumentException>(()=>EditorProjectPaths.AssetAbsolutePath(Path.GetTempPath()));
                Assert.NotNull(EditorProjectPaths.RequireVoiceMaterial());
            }
            finally{Directory.SetCurrentDirectory(cwd);}
        }

        [Test]
        public void ModuleAndVoiceMaterialMoveAndReturnWithoutLosingGuidResolution()
        {
            if(Environment.GetEnvironmentVariable("TINGLU_EDITOR_RELOCATION_TESTS")!="1")
                Assert.Ignore("Use Verify-LevelEditorUnity.ps1 to relocate an isolated project before starting Unity.");
            string module=EditorProjectPaths.ModuleAssetDirectory;
            Assert.AreNotEqual("Assets/TingLu/Levels",module);
            Assert.IsTrue(Directory.Exists(EditorProjectPaths.DataDirectory));
            Assert.NotNull(AssetDatabase.LoadAssetAtPath<TextAsset>(EditorProjectPaths.DataAssetDirectory+"/FirstStepTemplate.json"));
            Assert.AreNotEqual("Assets/TingLu/VisualAssets/VoiceLine.mat",AssetDatabase.GetAssetPath(EditorProjectPaths.RequireVoiceMaterial()));
            Assert.AreEqual(module+"/Editor/PreviewTemporary",EditorProjectPaths.PreviewAssetDirectory);
        }
    }
}
