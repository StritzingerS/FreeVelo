using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace FreeVelo.EditorTools
{
    /// <summary>
    /// Uses a locally imported bicycle model (e.g. from the Asset Store) for the rider avatar.
    /// Asset Store files must not be committed to the public repository, so:
    /// - the fitted prefab is saved to Assets/FreeVeloLocal/Resources (ignored by .gitignore),
    /// - the imported asset folder is added to .git/info/exclude (local ignore, not committed).
    /// </summary>
    public static class FreeVeloBicycleSetup
    {
        public const string LocalFolder = "Assets/FreeVeloLocal/Resources";
        public const string PrefabPath = LocalFolder + "/FreeVeloBicycle.prefab";

        /// <summary>Bike length from wheel to wheel edge, in metres.</summary>
        private const float BikeLength = 1.75f;

        [MenuItem("FreeVelo/Use selected prefab as bicycle")]
        private static void UseSelected()
        {
            var source = Selection.activeGameObject;
            if (source == null || !PrefabUtility.IsPartOfPrefabAsset(source))
            {
                EditorUtility.DisplayDialog("FreeVelo", "Select the bicycle prefab (or model) in the Project window first.", "OK");
                return;
            }

            // Wrap the model in a root and fit it: length along +Z, ~1.75 m long, wheels on y = 0, centred.
            var root = new GameObject("FreeVeloBicycle");
            var model = (GameObject)PrefabUtility.InstantiatePrefab(source);
            model.transform.SetParent(root.transform, false);

            var bounds = Bounds(root);
            if (bounds.size.x > bounds.size.z)
            {
                model.transform.localRotation = Quaternion.Euler(0, 90, 0) * model.transform.localRotation;
                bounds = Bounds(root);
            }

            float scale = BikeLength / Mathf.Max(0.01f, bounds.size.z);
            model.transform.localScale *= scale;
            bounds = Bounds(root);
            model.transform.localPosition -= new Vector3(bounds.center.x, bounds.min.y, bounds.center.z);

            Directory.CreateDirectory(LocalFolder);
            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            Object.DestroyImmediate(root);

            string assetFolder = TopLevelFolder(AssetDatabase.GetAssetPath(source));
            if (assetFolder != null) ExcludeFromGit(assetFolder);

            AssetDatabase.Refresh();
            Selection.activeObject = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            EditorUtility.DisplayDialog(
                "FreeVelo",
                $"Bicycle saved to {PrefabPath}.\n\n" +
                "If it rides backwards, open the prefab and rotate the model child by 180° around Y.\n\n" +
                (assetFolder != null ? $"'{assetFolder}' is now ignored by Git on this computer (Asset Store files must not be pushed)." : string.Empty),
                "OK");
        }

        [MenuItem("FreeVelo/Remove custom bicycle")]
        private static void Remove()
        {
            if (AssetDatabase.DeleteAsset(PrefabPath)) Debug.Log("FreeVelo: custom bicycle removed, the placeholder bike is used.");
        }

        private static Bounds Bounds(GameObject root)
        {
            var renderers = root.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0) return new Bounds(Vector3.zero, Vector3.one);
            var b = renderers[0].bounds;
            foreach (var r in renderers.Skip(1)) b.Encapsulate(r.bounds);
            return b;
        }

        private static string TopLevelFolder(string assetPath)
        {
            var parts = assetPath.Split('/');
            return parts.Length > 2 && parts[0] == "Assets" && parts[1] != "FreeVelo" && parts[1] != "FreeVeloLocal"
                ? "Assets/" + parts[1]
                : null;
        }

        /// <summary>Adds the folder to .git/info/exclude (a local ignore list that is never committed).</summary>
        private static void ExcludeFromGit(string assetFolder)
        {
            var dir = new DirectoryInfo(Application.dataPath);
            while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, ".git"))) dir = dir.Parent;
            if (dir == null) return;

            string projectRelative = Path.GetRelativePath(dir.FullName, Path.Combine(Application.dataPath, "..")).Replace('\\', '/');
            string pattern = $"/{projectRelative}/{assetFolder}/";
            string patternMeta = $"/{projectRelative}/{assetFolder}.meta";
            string exclude = Path.Combine(dir.FullName, ".git", "info", "exclude");
            Directory.CreateDirectory(Path.GetDirectoryName(exclude));
            string existing = File.Exists(exclude) ? File.ReadAllText(exclude) : string.Empty;
            if (!existing.Contains(pattern))
            {
                File.AppendAllText(exclude, $"\n# FreeVelo: local Asset Store content\n{pattern}\n{patternMeta}\n");
            }
        }
    }
}
