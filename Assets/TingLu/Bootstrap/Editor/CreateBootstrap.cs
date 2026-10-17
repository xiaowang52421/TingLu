using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
namespace TingLu.Bootstrap.Editor
{
    public static class CreateBootstrap
    {
        [MenuItem("TingLu/Create G0 Bootstrap Scene")]
        public static void Create()
        {
            const string path = "Assets/TingLu/Scenes/Startup.unity";
            if (System.IO.File.Exists(path)) { Debug.LogWarning("Startup exists; preserved."); return; }
            System.IO.Directory.CreateDirectory("Assets/TingLu/Scenes");
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            var root = new GameObject("TingLu Bootstrap");
            SceneManager.MoveGameObjectToScene(root, scene);
            root.AddComponent<BootstrapStatus>();
            var cameraObject = new GameObject("Player Camera");
            SceneManager.MoveGameObjectToScene(cameraObject, scene);
            var camera = cameraObject.AddComponent<Camera>();
            camera.orthographic = true; camera.orthographicSize = 8;
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = Color.black;
            camera.transform.position = new Vector3(0, 0, -10);
            camera.cullingMask = 0; // Enable only legal presentation layer during integration.
            EditorSceneManager.SaveScene(scene, path);
            EditorSceneManager.CloseScene(scene, true);
            AssetDatabase.Refresh();
            Debug.Log("Created " + path + ". This scene is the G0 status entry, not the playable Demo.");
        }
    }
}
