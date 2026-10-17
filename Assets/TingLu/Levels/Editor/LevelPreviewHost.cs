using System;
using System.IO;
using TingLu.Bootstrap;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;

namespace TingLu.Levels.Editor
{
    [InitializeOnLoad] internal static class LevelPreviewHost
    {
        private const string Key="TingLu.LevelEditor.PreviewScene";
        private const string Previous="TingLu.LevelEditor.PreviousPlayScene";
        private const string Reopen="TingLu.LevelEditor.ReopenAfterPreview";
        private const string Untitled="TingLu.LevelEditor.UntitledSnapshots";
        [Serializable] private sealed class SceneCopies { public string[] paths; public bool[] dirty; public string active; }
        static LevelPreviewHost(){EditorApplication.playModeStateChanged+=Changed;}
        public static void Start(LevelDocument document)
        {
            if(!string.IsNullOrEmpty(Prepare(document,true)))EditorApplication.isPlaying=true;
        }
        // The same preparation path is used by production Start and Unity's EnterPlayMode tests.
        internal static string Prepare(LevelDocument document,bool reopenEditor)
        {
            LevelValidator.RequireValid(document);
            if(EditorApplication.isPlayingOrWillChangePlaymode)throw new InvalidOperationException("请先结束当前试玩。");
            var voiceMaterial=EditorProjectPaths.RequireVoiceMaterial();
            int untitledCount=0;
            for(int index=0;index<UnityEngine.SceneManagement.SceneManager.sceneCount;index++)
            {var current=UnityEngine.SceneManagement.SceneManager.GetSceneAt(index);if(current.isLoaded&&string.IsNullOrEmpty(current.path))untitledCount++;}
            if(untitledCount>1)throw new InvalidOperationException("当前有多个未命名场景，请先保存或关闭多余场景后试玩；场景内容未改动。");
            if(!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())return null;
            string directory=EditorProjectPaths.PreviewAssetDirectory;
            Directory.CreateDirectory(EditorProjectPaths.AssetAbsolutePath(directory));AssetDatabase.Refresh();
            string scenePath=directory+"/Preview-"+Guid.NewGuid().ToString("N")+".unity";
            var previous=EditorSceneManager.playModeStartScene;
            // Unity refuses additive scenes while an untitled scene is open. Preserve it in a
            // private snapshot rather than silently saving to/overwriting any user scene file.
            var copies=new System.Collections.Generic.List<string>();var dirty=new System.Collections.Generic.List<bool>();string activeCopy=null;
            var scene=default(UnityEngine.SceneManagement.Scene);
            try
            {
            for(int index=0;index<UnityEngine.SceneManagement.SceneManager.sceneCount;index++)
            {
                var current=UnityEngine.SceneManagement.SceneManager.GetSceneAt(index);
                if(!current.isLoaded||!string.IsNullOrEmpty(current.path))continue;
                bool active=current==UnityEngine.SceneManagement.SceneManager.GetActiveScene();
                bool wasDirty=current.isDirty;
                string copy=directory+"/Untitled-"+Guid.NewGuid().ToString("N")+".unity";
                if(!EditorSceneManager.SaveScene(current,copy))throw new IOException("无法暂存未命名场景；未开始试玩。");
                copies.Add(copy);dirty.Add(wasDirty);if(active)activeCopy=copy;
                SessionState.SetString(Untitled,JsonUtility.ToJson(new SceneCopies{paths=copies.ToArray(),dirty=dirty.ToArray(),active=activeCopy}));
            }
            SessionState.SetString(Untitled,JsonUtility.ToJson(new SceneCopies{paths=copies.ToArray(),dirty=dirty.ToArray(),active=activeCopy}));
            scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Additive);
                var entry=new GameObject("关卡试玩（隔离存档）").AddComponent<DemoEntry>();
                UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(entry.gameObject,scene);
                string saves=Path.Combine(Path.GetTempPath(),"TingLuLevelPreview",Guid.NewGuid().ToString("N"));
                entry.ConfigurePreview(LevelCodec.Encode(document),saves);
                var serialized=new SerializedObject(entry);
                serialized.FindProperty("voiceMaterial").objectReferenceValue=voiceMaterial;
                serialized.ApplyModifiedPropertiesWithoutUndo();
                if(!EditorSceneManager.SaveScene(scene,scenePath))throw new IOException("无法写入临时试玩场景。");
            }
            catch(Exception error)
            {
                if(scene.IsValid()&&scene.isLoaded)EditorSceneManager.CloseScene(scene,true);
                AssetDatabase.DeleteAsset(scenePath);
                try{RestoreUntitled();}
                catch(Exception recovery)
                {throw new InvalidOperationException("试玩准备失败，未命名场景快照已保留在 "+directory+"，请保留这些文件并恢复场景。",new AggregateException(error,recovery));}
                throw;
            }
            finally{if(scene.IsValid()&&scene.isLoaded)EditorSceneManager.CloseScene(scene,true);}
            SessionState.SetString(Key,scenePath);
            SessionState.SetString(Previous,previous==null?"":AssetDatabase.GetAssetPath(previous));
            SessionState.SetBool(Reopen,reopenEditor);
            EditorSceneManager.playModeStartScene=AssetDatabase.LoadAssetAtPath<SceneAsset>(scenePath);
            return scenePath;
        }
        private static void Changed(PlayModeStateChange change)
        {
            if(change!=PlayModeStateChange.EnteredEditMode)return;
            string path=SessionState.GetString(Key,"");if(string.IsNullOrEmpty(path))return;
            string previous=SessionState.GetString(Previous,"");
            EditorSceneManager.playModeStartScene=string.IsNullOrEmpty(previous)?null:AssetDatabase.LoadAssetAtPath<SceneAsset>(previous);
            RestoreUntitled();
            bool reopen=SessionState.GetBool(Reopen,true);
            AssetDatabase.DeleteAsset(path);SessionState.EraseString(Key);SessionState.EraseString(Previous);SessionState.EraseBool(Reopen);
            if(reopen)LevelEditorWindow.ShowWindow();
        }
        private static void RestoreUntitled()
        {
            string json=SessionState.GetString(Untitled,"");if(string.IsNullOrEmpty(json))return;
            var copies=JsonUtility.FromJson<SceneCopies>(json);
            if(copies!=null&&copies.paths!=null)for(int index=0;index<copies.paths.Length;index++)
            {
                string path=copies.paths[index];
                var saved=UnityEngine.SceneManagement.SceneManager.GetSceneByPath(path);
                if(!saved.IsValid()||!saved.isLoaded)saved=EditorSceneManager.OpenScene(path,OpenSceneMode.Additive);
                var restored=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Additive);
                foreach(var root in saved.GetRootGameObjects())UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(root,restored);
                EditorSceneManager.MoveSceneBefore(restored,saved);
                if(path==copies.active)UnityEngine.SceneManagement.SceneManager.SetActiveScene(restored);
                if(copies.dirty!=null&&index<copies.dirty.Length&&copies.dirty[index])EditorSceneManager.MarkSceneDirty(restored);
                else
                {
                    // Unity 2022.3 exposes this editor operation internally; retain a clean
                    // untitled scene's status without writing any user scene file.
                    var clear=typeof(EditorSceneManager).GetMethod("ClearSceneDirtiness",System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.NonPublic,null,new[]{typeof(UnityEngine.SceneManagement.Scene)},null);
                    if(clear!=null)clear.Invoke(null,new object[]{restored});
                }
                EditorSceneManager.CloseScene(saved,true);AssetDatabase.DeleteAsset(path);
            }
            SessionState.EraseString(Untitled);
        }
    }
}
