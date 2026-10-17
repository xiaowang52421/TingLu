using System;
using System.IO;
using System.Collections.Generic;
using TingLu.Bootstrap;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace TingLu.Levels.Editor
{
    /// <summary>Runs a v2 street document in a temporary scene and removes preview state on exit.</summary>
    [InitializeOnLoad]
    internal static class StreetPreviewHost
    {
        private const string Prefix="TingLu.StreetEditorV2.";
        private const string SceneKey=Prefix+"PreviewScene";
        private const string PreviousKey=Prefix+"PreviousPlayScene";
        private const string SaveRootKey=Prefix+"PreviewSaveRoot";
        private const string UntitledKey=Prefix+"UntitledSnapshots";

        [Serializable]
        private sealed class SceneCopies
        {
            public string[] paths;
            public bool[] dirty;
            public string active;
        }

        static StreetPreviewHost()
        {
            EditorApplication.playModeStateChanged+=Changed;
        }

        public static void Start(StreetLevelDocument document)
        {
            if(!string.IsNullOrEmpty(Prepare(document,true)))EditorApplication.isPlaying=true;
        }

        // Kept internal so editor tests can exercise preparation without entering Play Mode.
        internal static string Prepare(StreetLevelDocument document,bool reopenEditor)
        {
            if(document==null)throw new ArgumentNullException("document");
            string json=StreetLevelCodec.Encode(document);
            // Decode the exact serialized payload that the runtime will consume. This catches
            // unsupported or malformed document data before touching any open editor scene.
            StreetLevelDocument validated=StreetLevelCodec.Decode(json);
            StreetLevelCodec.Compile(validated);
            if(EditorApplication.isPlayingOrWillChangePlaymode)throw new InvalidOperationException("请先结束当前街区试玩。");

            int untitledCount=0;
            for(int i=0;i<SceneManager.sceneCount;i++)
            {
                Scene current=SceneManager.GetSceneAt(i);
                if(current.isLoaded&&string.IsNullOrEmpty(current.path))untitledCount++;
            }
            if(untitledCount>1)throw new InvalidOperationException("当前有多个未命名场景，请先保存或关闭多余场景后试玩；场景内容未改动。");
            if(!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())return null;

            string assetDirectory=EditorProjectPaths.ModuleAssetDirectory+"/Editor";
            string scenePath=assetDirectory+"/StreetPreview-"+Guid.NewGuid().ToString("N")+".unity";
            SceneAsset previous=EditorSceneManager.playModeStartScene;
            string previousPath=previous==null?"":AssetDatabase.GetAssetPath(previous);
            string saveRoot=Path.Combine(Path.GetTempPath(),"TingLuStreetPreview",Guid.NewGuid().ToString("N"));
            var copies=new List<string>();
            var dirty=new List<bool>();
            string activeCopy=null;
            Scene preview=default(Scene);

            try
            {
                for(int i=0;i<SceneManager.sceneCount;i++)
                {
                    Scene current=SceneManager.GetSceneAt(i);
                    if(!current.isLoaded||!string.IsNullOrEmpty(current.path))continue;
                    bool wasActive=current==SceneManager.GetActiveScene();
                    bool wasDirty=current.isDirty;
                    string copyPath=assetDirectory+"/StreetPreview-Untitled-"+Guid.NewGuid().ToString("N")+".unity";
                    if(!EditorSceneManager.SaveScene(current,copyPath))throw new IOException("无法暂存未命名场景；未开始试玩。");
                    copies.Add(copyPath);dirty.Add(wasDirty);
                    if(wasActive)activeCopy=copyPath;
                    StoreUntitled(copies,dirty,activeCopy);
                }
                StoreUntitled(copies,dirty,activeCopy);

                preview=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Additive);
                var entryObject=new GameObject("街区关卡试玩（隔离存档）");
                StreetEntry entry=entryObject.AddComponent<StreetEntry>();
                SceneManager.MoveGameObjectToScene(entryObject,preview);
                entry.ConfigurePreview(json,saveRoot);
                if(!EditorSceneManager.SaveScene(preview,scenePath))throw new IOException("无法写入街区临时试玩场景。");

                SessionState.SetString(SceneKey,scenePath);
                SessionState.SetString(PreviousKey,previousPath);
                SessionState.SetString(SaveRootKey,saveRoot);
                EditorSceneManager.playModeStartScene=AssetDatabase.LoadAssetAtPath<SceneAsset>(scenePath);
                return scenePath;
            }
            catch(Exception error)
            {
                if(preview.IsValid()&&preview.isLoaded)EditorSceneManager.CloseScene(preview,true);
                EditorSceneManager.playModeStartScene=LoadSceneAsset(previousPath);
                AssetDatabase.DeleteAsset(scenePath);
                TryDeleteSaveRoot(saveRoot);
                try
                {
                    RestoreUntitled();
                    ClearSessionState();
                }
                catch(Exception recovery)
                {
                    Debug.LogError("街区试玩准备失败，未命名场景快照仍保留在 Assets/TingLu/Levels/Editor。请保留这些文件并恢复场景。\n"+recovery);
                    throw new InvalidOperationException("试玩准备失败；场景恢复未完成，快照已保留。",new AggregateException(error,recovery));
                }
                throw;
            }
            finally
            {
                if(preview.IsValid()&&preview.isLoaded)EditorSceneManager.CloseScene(preview,true);
            }
        }

        private static void StoreUntitled(List<string> paths,List<bool> dirty,string active)
        {
            SessionState.SetString(UntitledKey,JsonUtility.ToJson(new SceneCopies
            {paths=paths.ToArray(),dirty=dirty.ToArray(),active=active}));
        }

        private static void Changed(PlayModeStateChange change)
        {
            if(change!=PlayModeStateChange.EnteredEditMode)return;
            string scenePath=SessionState.GetString(SceneKey,"");
            if(string.IsNullOrEmpty(scenePath))return;
            string previousPath=SessionState.GetString(PreviousKey,"");
            string saveRoot=SessionState.GetString(SaveRootKey,"");
            EditorSceneManager.playModeStartScene=LoadSceneAsset(previousPath);
            try
            {
                RestoreUntitled();
                AssetDatabase.DeleteAsset(scenePath);
                TryDeleteSaveRoot(saveRoot);
                ClearSessionState();
            }
            catch(Exception error)
            {
                Debug.LogError("街区试玩已退出，但场景快照恢复失败；临时场景和存档保留以便恢复。\n"+error);
            }
        }

        private static SceneAsset LoadSceneAsset(string path)
        {
            return string.IsNullOrEmpty(path)?null:AssetDatabase.LoadAssetAtPath<SceneAsset>(path);
        }

        private static void RestoreUntitled()
        {
            string json=SessionState.GetString(UntitledKey,"");
            if(string.IsNullOrEmpty(json))return;
            SceneCopies copies=JsonUtility.FromJson<SceneCopies>(json);
            if(copies!=null&&copies.paths!=null)
            {
                for(int i=0;i<copies.paths.Length;i++)
                {
                    string path=copies.paths[i];
                    Scene saved=SceneManager.GetSceneByPath(path);
                    if(!saved.IsValid()||!saved.isLoaded)saved=EditorSceneManager.OpenScene(path,OpenSceneMode.Additive);
                    Scene restored=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Additive);
                    foreach(GameObject root in saved.GetRootGameObjects())SceneManager.MoveGameObjectToScene(root,restored);
                    EditorSceneManager.MoveSceneBefore(restored,saved);
                    if(path==copies.active)SceneManager.SetActiveScene(restored);
                    bool wasDirty=copies.dirty!=null&&i<copies.dirty.Length&&copies.dirty[i];
                    if(wasDirty)EditorSceneManager.MarkSceneDirty(restored);
                    else ClearDirtiness(restored);
                    EditorSceneManager.CloseScene(saved,true);
                    AssetDatabase.DeleteAsset(path);
                }
            }
            SessionState.EraseString(UntitledKey);
        }

        private static void ClearDirtiness(Scene scene)
        {
            // Unity 2022.3 keeps this editor operation internal. Use reflection so the restored
            // untitled scene retains its original clean/dirty state without saving it to disk.
            var method=typeof(EditorSceneManager).GetMethod("ClearSceneDirtiness",
                System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.NonPublic,
                null,new[]{typeof(Scene)},null);
            if(method!=null)method.Invoke(null,new object[]{scene});
        }

        private static bool IsPreviewSaveRoot(string path,out string fullPath)
        {
            fullPath=null;
            if(string.IsNullOrWhiteSpace(path))return false;
            try
            {
                string parent=Path.GetFullPath(Path.Combine(Path.GetTempPath(),"TingLuStreetPreview"));
                string prefix=parent.TrimEnd(Path.DirectorySeparatorChar,Path.AltDirectorySeparatorChar)+Path.DirectorySeparatorChar;
                fullPath=Path.GetFullPath(path);
                return fullPath.StartsWith(prefix,StringComparison.OrdinalIgnoreCase)&&
                    !string.Equals(fullPath.TrimEnd(Path.DirectorySeparatorChar,Path.AltDirectorySeparatorChar),parent.TrimEnd(Path.DirectorySeparatorChar,Path.AltDirectorySeparatorChar),StringComparison.OrdinalIgnoreCase);
            }
            catch{return false;}
        }

        private static void TryDeleteSaveRoot(string path)
        {
            string fullPath;
            if(!IsPreviewSaveRoot(path,out fullPath))return;
            try{if(Directory.Exists(fullPath))Directory.Delete(fullPath,true);}
            catch(Exception error){Debug.LogWarning("未能清理街区试玩临时存档："+error.Message);}
        }

        private static void ClearSessionState()
        {
            SessionState.EraseString(SceneKey);
            SessionState.EraseString(PreviousKey);
            SessionState.EraseString(SaveRootKey);
            SessionState.EraseString(UntitledKey);
        }
    }
}
