using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace FreeVelo.EditorTools
{
    /// <summary>
    /// Render shots without anyone at the Game view: when RenderShots/request.json appears next to the Assets folder
    /// (and there is no done.json yet), the editor enters play mode, FreeVelo.App.ShotRunner renders the requested
    /// camera views to RenderShots/out/*.png and writes done.json, and the editor leaves play mode again.
    /// Before rendering it refreshes the assets and waits for the scripts to compile, so changed files are picked up
    /// without anyone clicking into the editor (Unity refreshes only on focus otherwise); an empty RenderShots/refresh
    /// file does just the refresh. Neither happens in play mode.
    /// Works in the open editor (it polls once a second) and from the command line with the editor closed:
    ///   Unity -batchmode -projectPath unity/FreeVelo -executeMethod FreeVelo.EditorTools.FreeVeloRenderShots.RenderBatch
    /// (no -quit and no -nographics; the editor exits by itself when the shots are done).
    /// </summary>
    [InitializeOnLoad]
    public static class FreeVeloRenderShots
    {
        private const string BatchKey = "FreeVelo.RenderShots.Batch";
        private static double _nextPoll;

        private static string Folder => Path.Combine(Directory.GetParent(Application.dataPath).FullName, "RenderShots");

        static FreeVeloRenderShots()
        {
            EditorApplication.update += Poll;
        }

        /// <summary>Command-line entry: renders the request and quits the editor.</summary>
        public static void RenderBatch()
        {
            SessionState.SetBool(BatchKey, true);
            string scene = EditorBuildSettings.scenes.Length > 0 ? EditorBuildSettings.scenes[0].path : null;
            if (!string.IsNullOrEmpty(scene)) EditorSceneManager.OpenScene(scene);
            if (!File.Exists(Path.Combine(Folder, "request.json")))
            {
                Debug.LogError("FreeVelo render shots: RenderShots/request.json is missing");
                EditorApplication.Exit(2);
            }
        }

        [MenuItem("FreeVelo/Render shots now")]
        private static void RenderNow()
        {
            if (File.Exists(Path.Combine(Folder, "done.json"))) File.Delete(Path.Combine(Folder, "done.json"));
            if (File.Exists(Path.Combine(Folder, "running"))) File.Delete(Path.Combine(Folder, "running"));
            _nextPoll = 0;
        }

        private static void Poll()
        {
            if (EditorApplication.timeSinceStartup < _nextPoll) return;
            _nextPoll = EditorApplication.timeSinceStartup + 1;
            string request = Path.Combine(Folder, "request.json"), done = Path.Combine(Folder, "done.json"), running = Path.Combine(Folder, "running");
            bool batch = SessionState.GetBool(BatchKey, false);

            if (EditorApplication.isPlaying)
            {
                // Our run has finished: back to edit mode.
                if (File.Exists(running) && File.Exists(done))
                {
                    File.Delete(running);
                    EditorApplication.ExitPlaymode();
                }

                return;
            }

            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating) return;

            if (File.Exists(done))
            {
                if (batch) EditorApplication.Exit(0);
                return;
            }

            // Without anyone at the editor: Unity only refreshes assets and recompiles scripts when its window gets the
            // focus. So refresh here (outside play mode only), wait until compiling and importing are done, then render.
            string refresh = Path.Combine(Folder, "refresh");
            if (File.Exists(refresh))
            {
                File.Delete(refresh);
                SessionState.SetBool(RefreshedKey, false);
                AssetDatabase.Refresh();
                _nextPoll = EditorApplication.timeSinceStartup + 2;
                return;
            }

            if (File.Exists(request) && !File.Exists(running))
            {
                if (!SessionState.GetBool(RefreshedKey, false))
                {
                    SessionState.SetBool(RefreshedKey, true);
                    AssetDatabase.Refresh();
                    _nextPoll = EditorApplication.timeSinceStartup + 2; // compiling starts after the refresh
                    return;
                }

                SessionState.SetBool(RefreshedKey, false);
                File.WriteAllText(running, System.DateTime.Now.ToString("o"));
                EditorApplication.EnterPlaymode();
            }
        }

        /// <summary>Set once the assets were refreshed for the waiting request (survives the domain reload of a compile).</summary>
        private const string RefreshedKey = "FreeVelo.RenderShots.Refreshed";
    }
}
