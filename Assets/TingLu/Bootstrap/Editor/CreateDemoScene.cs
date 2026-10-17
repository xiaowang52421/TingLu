using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace TingLu.Bootstrap.Editor
{
    public static class CreateDemoScene
    {
        public const string ScenePath = "Assets/TingLu/Scenes/Levels/FirstStep.unity";

        [MenuItem("TingLu/Create First Step Demo Scene")]
        public static void Create()
        {
            if (File.Exists(ScenePath))
            {
                Debug.Log("Existing FirstStep scene preserved: " + ScenePath);
                return;
            }
            Directory.CreateDirectory(Path.GetDirectoryName(ScenePath));
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            var host = new GameObject("TingLu First Step");
            SceneManager.MoveGameObjectToScene(host, scene);
            var entry = host.AddComponent<DemoEntry>();
            const string materialPath = "Assets/TingLu/VisualAssets/VoiceLine.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
            if (material == null)
            {
                var shader = AssetDatabase.LoadAssetAtPath<Shader>("Assets/TingLu/Shaders/VoiceLine.shader");
                if (shader == null) throw new System.InvalidOperationException("VoiceLine shader is missing.");
                Directory.CreateDirectory("Assets/TingLu/VisualAssets");
                material = new Material(shader);
                AssetDatabase.CreateAsset(material, materialPath);
            }
            var serialized = new SerializedObject(entry);
            serialized.FindProperty("voiceMaterial").objectReferenceValue = material;
            var levelAsset=AssetDatabase.LoadAssetAtPath<TextAsset>("Assets/TingLu/Levels/Data/FirstStep.json");
            if(levelAsset==null)throw new System.InvalidOperationException("Default FirstStep level data is missing.");
            serialized.FindProperty("levelAsset").objectReferenceValue = levelAsset;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorSceneManager.SaveScene(scene, ScenePath);
            EditorSceneManager.CloseScene(scene, true);
            AssetDatabase.Refresh();
            Debug.Log("Created " + ScenePath + ". Open this scene and enter Play mode to verify the Demo.");
        }
    }
}
