using System.IO;
using FreeVelo.Core.World.Gen;
using Terrain = FreeVelo.Core.World.Gen.Terrain;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace FreeVelo.EditorTools
{
    /// <summary>
    /// Prepares the world (terrain of the home world and the mountain) at build time, so the app only loads it at
    /// start-up instead of generating it. Written to Resources/FreeVeloWorld.bytes (not in git; about 10 MB in the app).
    /// </summary>
    public static class FreeVeloWorldPrep
    {
        private const string AssetPath = "Assets/FreeVelo/Resources/FreeVeloWorld.bytes";

        [MenuItem("FreeVelo/Prepare world")]
        public static void Prepare() => Prepare(force: true);

        private static void Prepare(bool force)
        {
            var map = WorldMap.Default;
            if (!force && File.Exists(AssetPath))
            {
                using var existing = File.OpenRead(AssetPath);
                if (Terrain.IsSavedFor(map, existing)) return; // up to date
            }

            EditorUtility.DisplayProgressBar("FreeVelo", "Preparing the world …", 0.5f);
            try
            {
                var terrain = Terrain.Build(map);
                Directory.CreateDirectory(Path.GetDirectoryName(AssetPath)!);
                using (var file = File.Create(AssetPath)) terrain.Save(file);
                AssetDatabase.ImportAsset(AssetPath);
                Debug.Log($"FreeVelo: world prepared ({new FileInfo(AssetPath).Length / 1024 / 1024.0:0.0} MB)");
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
        }

        private sealed class PreBuild : IPreprocessBuildWithReport
        {
            public int callbackOrder => 0;

            public void OnPreprocessBuild(BuildReport report) => Prepare(force: false);
        }
    }
}
