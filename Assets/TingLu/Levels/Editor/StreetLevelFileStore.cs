using System;
using System.IO;
using System.Text;
using TingLu.Bootstrap;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace TingLu.Levels.Editor
{
    /// <summary>Safe storage and portable scene export for the isolated street-level document format.</summary>
    public static class StreetLevelFileStore
    {
        private static readonly UTF8Encoding Utf8NoBom = new UTF8Encoding(false);

        /// <summary>
        /// Saves a draft by default. Strict compilation is required only when callers explicitly
        /// request a playable document; all writes still require a codec-round-trippable schema.
        /// </summary>
        public static void Save(string path, StreetLevelDocument document, bool requireValid = false)
        {
            if (document == null) throw new ArgumentNullException("document");
            if (requireValid) StreetLevelCodec.Compile(document);

            string json = StreetLevelCodec.Encode(document);
            // Decode before touching disk. This rejects unsupported schema versions and data that
            // cannot be reopened, while allowing gameplay-incomplete drafts to be saved.
            StreetLevelCodec.Decode(json);

            string fullPath = EditorProjectPaths.AbsoluteFilePath(path);
            if (!string.Equals(Path.GetExtension(fullPath), ".json", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("关卡文件必须使用 .json 扩展名。");

            if (File.Exists(fullPath))
            {
                // A damaged, legacy, or future-schema file is never replaced by this editor.
                StreetLevelCodec.Decode(File.ReadAllText(fullPath, Encoding.UTF8));
            }

            string directory = Path.GetDirectoryName(fullPath);
            if (string.IsNullOrEmpty(directory)) throw new InvalidDataException("关卡文件目录无效。");
            Directory.CreateDirectory(directory);

            string temporary = fullPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                byte[] bytes = Utf8NoBom.GetBytes(json);
                using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    stream.Write(bytes, 0, bytes.Length);
                    stream.Flush(true);
                }

                if (File.Exists(fullPath)) File.Replace(temporary, fullPath, fullPath + ".bak", true);
                else File.Move(temporary, fullPath);
            }
            finally
            {
                if (File.Exists(temporary)) File.Delete(temporary);
            }

            RefreshIfAsset(fullPath);
        }

        /// <summary>Loads a playable or incomplete draft without forcing gameplay validation.</summary>
        public static StreetLevelDocument Load(string path)
        {
            string fullPath = EditorProjectPaths.AbsoluteFilePath(path);
            if (!string.Equals(Path.GetExtension(fullPath), ".json", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("关卡文件必须使用 .json 扩展名。");
            return StreetLevelCodec.Decode(File.ReadAllText(fullPath, Encoding.UTF8));
        }

        /// <summary>
        /// Exports a validated document as a Unity scene with a sibling JSON TextAsset. Existing
        /// files are rejected, so an export cannot overwrite a project scene or another level.
        /// The generated scene is closed after export and is not added to Build Settings.
        /// </summary>
        public static string ExportScene(string path, StreetLevelDocument document)
        {
            if (document == null) throw new ArgumentNullException("document");
            StreetLevelCodec.Compile(document);
            RequireNoUntitledScenes();

            string fullScenePath = EditorProjectPaths.AbsoluteFilePath(path);
            if (!string.Equals(Path.GetExtension(fullScenePath), ".unity", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("场景导出路径必须使用 .unity 扩展名。");

            string sceneAssetPath = ToAssetPath(fullScenePath);
            string sceneDirectory = Path.GetDirectoryName(sceneAssetPath).Replace('\\', '/');
            string sceneName = Path.GetFileNameWithoutExtension(sceneAssetPath);
            string levelAssetPath = sceneDirectory + "/" + sceneName + ".json";
            string fullLevelPath = EditorProjectPaths.AssetAbsolutePath(levelAssetPath);

            if (File.Exists(fullScenePath) || File.Exists(fullScenePath + ".meta") || Directory.Exists(fullScenePath))
                throw new IOException("目标场景已存在；请选择一个新文件名，以免覆盖项目场景。");
            if (File.Exists(fullLevelPath) || File.Exists(fullLevelPath + ".meta") || Directory.Exists(fullLevelPath))
                throw new IOException("同名关卡 JSON 已存在；请选择一个新场景文件名。");

            Directory.CreateDirectory(Path.GetDirectoryName(fullScenePath));
            var previousActive = SceneManager.GetActiveScene();
            Scene generated = default(Scene);
            bool jsonCreated = false;
            bool sceneCreated = false;
            try
            {
                // Both destinations were verified absent above; cleanup can safely remove only
                // files created by this export, including a partial failure from Unity's importer.
                jsonCreated = true;
                Save(fullLevelPath, document, true);
                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
                var levelAsset = AssetDatabase.LoadAssetAtPath<TextAsset>(levelAssetPath);
                if (levelAsset == null) throw new InvalidOperationException("Unity 未能导入导出的关卡 JSON。");

                generated = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
                var root = new GameObject("Street Level");
                SceneManager.MoveGameObjectToScene(root, generated);
                var entry = root.AddComponent<StreetEntry>();
                entry.ConfigureLevel(levelAsset);

                sceneCreated = true;
                if (!EditorSceneManager.SaveScene(generated, sceneAssetPath))
                    throw new IOException("Unity 未能保存导出的场景。");
                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
                return sceneAssetPath;
            }
            catch
            {
                if (generated.IsValid() && generated.isLoaded)
                    EditorSceneManager.CloseScene(generated, true);
                if (sceneCreated && File.Exists(fullScenePath)) AssetDatabase.DeleteAsset(sceneAssetPath);
                if (jsonCreated && File.Exists(fullLevelPath)) AssetDatabase.DeleteAsset(levelAssetPath);
                throw;
            }
            finally
            {
                if (generated.IsValid() && generated.isLoaded)
                    EditorSceneManager.CloseScene(generated, true);
                if (previousActive.IsValid() && previousActive.isLoaded)
                    SceneManager.SetActiveScene(previousActive);
            }
        }

        private static string ToAssetPath(string fullPath)
        {
            string stored = EditorProjectPaths.StoreFilePath(fullPath);
            if (Path.IsPathRooted(stored) ||
                !(string.Equals(stored, "Assets", StringComparison.Ordinal) || stored.StartsWith("Assets/", StringComparison.Ordinal)))
                throw new ArgumentException("导出的场景和关卡 JSON 必须位于当前工程的 Assets 目录内。", "path");
            EditorProjectPaths.AssetAbsolutePath(stored);
            return stored;
        }

        private static void RequireNoUntitledScenes()
        {
            for (int index = 0; index < SceneManager.sceneCount; index++)
            {
                Scene scene = SceneManager.GetSceneAt(index);
                if (scene.IsValid() && scene.isLoaded && string.IsNullOrEmpty(scene.path))
                    throw new InvalidOperationException("当前有未命名场景。请先保存或关闭该场景，再导出关卡；导出没有修改现有场景。");
            }
        }

        private static void RefreshIfAsset(string fullPath)
        {
            try
            {
                ToAssetPath(fullPath);
                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            }
            catch (ArgumentException)
            {
                // Standalone JSON drafts outside the project are supported by Save and Load.
            }
        }
    }
}
