using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace TingLu.Levels.Editor
{
    /// <summary>Resolve asset identities independently from module location and process working directory.</summary>
    public static class EditorProjectPaths
    {
        private const string WindowScriptGuid = "6308673964dca914e96f1d757a7c262f";
        private const string VoiceMaterialGuid = "8a6032d7a1144618bbf409fe8749fc72";

        public static string ProjectDirectory
        {
            get { return Directory.GetParent(Path.GetFullPath(Application.dataPath)).FullName; }
        }

        public static string ModuleAssetDirectory
        {
            get
            {
                string path = AssetDatabase.GUIDToAssetPath(WindowScriptGuid);
                var script = string.IsNullOrEmpty(path) ? null : AssetDatabase.LoadAssetAtPath<MonoScript>(path);
                if (script == null || script.GetClass() != typeof(LevelEditorWindow))
                    throw new InvalidOperationException("无法定位关卡编辑器脚本，请保留工程资产的 .meta 文件并重新导入。");
                string editor = Path.GetDirectoryName(path);
                string module = Path.GetDirectoryName(editor);
                if (string.IsNullOrEmpty(module))
                    throw new InvalidOperationException("关卡编辑器目录结构不完整，请保留模块内的 Editor 子目录。");
                string result = module.Replace('\\', '/');
                AssetAbsolutePath(result); // Validate an Assets-relative location before creating files.
                return result;
            }
        }

        public static string DataAssetDirectory { get { return ModuleAssetDirectory + "/Data"; } }
        public static string PreviewAssetDirectory { get { return ModuleAssetDirectory + "/Editor/PreviewTemporary"; } }
        public static string DataDirectory { get { return AssetAbsolutePath(DataAssetDirectory); } }

        public static string AbsoluteFilePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("文件路径不能为空。");
            return Path.GetFullPath(Path.IsPathRooted(path) ? path : Path.Combine(ProjectDirectory, path));
        }

        // Persist internal files relative to this project. External choices retain their exact destination.
        public static string StoreFilePath(string path)
        {
            if (string.IsNullOrEmpty(path)) return "";
            string full = AbsoluteFilePath(path);
            string root = ProjectDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string prefix = root + Path.DirectorySeparatorChar;
            return full.StartsWith(prefix, PathComparison) ? full.Substring(prefix.Length).Replace('\\', '/') : full;
        }

        public static string FileAssetGuid(string path)
        {
            if (string.IsNullOrEmpty(path)) return "";
            string stored = StoreFilePath(path);
            if (Path.IsPathRooted(stored) || !stored.StartsWith("Assets/", StringComparison.Ordinal)) return "";
            return AssetDatabase.AssetPathToGUID(stored);
        }

        public static string ResolveFilePath(string storedPath, string assetGuid)
        {
            if (string.IsNullOrEmpty(storedPath)) return "";
            if (!Path.IsPathRooted(storedPath) && !string.IsNullOrEmpty(assetGuid))
            {
                string assetPath = AssetDatabase.GUIDToAssetPath(assetGuid);
                if (string.IsNullOrEmpty(assetPath))
                    throw new InvalidOperationException("已打开关卡的资产已丢失，无法安全保存。请重新打开关卡，或另存为新的文件。");
                return AssetAbsolutePath(assetPath);
            }
            return AbsoluteFilePath(storedPath);
        }

        public static bool SameFilePath(string first, string second)
        {
            return string.Equals(AbsoluteFilePath(first), AbsoluteFilePath(second), PathComparison);
        }

        private static StringComparison PathComparison
        {
            get { return Application.platform == RuntimePlatform.WindowsEditor ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal; }
        }

        public static string AssetAbsolutePath(string assetPath)
        {
            if (string.IsNullOrWhiteSpace(assetPath) || Path.IsPathRooted(assetPath))
                throw new ArgumentException("Unity 资产路径必须相对于 Assets 目录。");
            string normalized = assetPath.Replace('\\', '/');
            if (normalized != "Assets" && !normalized.StartsWith("Assets/", StringComparison.Ordinal))
                throw new ArgumentException("Unity 资产路径必须位于 Assets 目录内。");
            string full = AbsoluteFilePath(normalized);
            string assets = Path.GetFullPath(Application.dataPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (!string.Equals(full, assets, StringComparison.OrdinalIgnoreCase) &&
                !full.StartsWith(assets + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Unity 资产路径不能越过 Assets 目录。");
            return full;
        }

        public static Material RequireVoiceMaterial()
        {
            string path = AssetDatabase.GUIDToAssetPath(VoiceMaterialGuid);
            var material = string.IsNullOrEmpty(path) ? null : AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
                throw new InvalidOperationException("找不到声纹显示材质，无法开始试玩。请恢复原材质及其 .meta 文件，或重新导入完整工程。");
            return material;
        }
    }
}
