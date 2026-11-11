#if UNITY_EDITOR || FREEVELO_SHOTS
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using FreeVelo.World;
using UnityEngine;

namespace FreeVelo.App
{
    /// <summary>
    /// Editor tooling, not part of builds: renders the camera views listed in RenderShots/request.json (next to the
    /// Assets folder) to PNG files in RenderShots/out and writes RenderShots/done.json. The editor script
    /// FreeVeloRenderShots enters play mode when a request appears and leaves it when the shots are done, so
    /// the scene can be checked without anyone looking at the Game view.
    /// </summary>
    public sealed class ShotRunner : MonoBehaviour
    {
        [Serializable]
        public class Shot
        {
            public string name = "shot";
            public float[] pos = new float[3];
            public float[] target = new float[3];
            public float fov = 60;

            /// <summary>Extra seconds to wait before the picture (e.g. for an animation to reach a pose).</summary>
            public float settle;
        }

        [Serializable]
        public class Request
        {
            public int width = 1280;
            public int height = 720;
            public List<Shot> shots = new List<Shot>();
        }

        [Serializable]
        public class Result
        {
            public List<string> files = new List<string>();
            public List<string> errors = new List<string>();
            public string unityVersion = "";
            public string graphicsDevice = "";
            public float seconds;
        }

        public static string Folder => Path.Combine(Directory.GetParent(Application.dataPath).FullName, "RenderShots");

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            // Only when the editor script started play mode for a request ("running" marks that).
            if (!File.Exists(Path.Combine(Folder, "request.json")) || !File.Exists(Path.Combine(Folder, "running"))) return;
            if (File.Exists(Path.Combine(Folder, "done.json"))) return;
            var go = new GameObject("ShotRunner");
            DontDestroyOnLoad(go);
            go.AddComponent<ShotRunner>();
        }

        private IEnumerator Start()
        {
            var result = new Result { unityVersion = Application.unityVersion, graphicsDevice = SystemInfo.graphicsDeviceName + " / " + SystemInfo.graphicsDeviceType };
            float started = Time.realtimeSinceStartup;
            Request request = null;
            try
            {
                request = JsonUtility.FromJson<Request>(File.ReadAllText(Path.Combine(Folder, "request.json")));
            }
            catch (Exception e)
            {
                result.errors.Add("request.json: " + e.Message);
            }

            if (request != null)
            {
                // The world is built in the background at start-up.
                while ((WorldBuilder.Current == null || !WorldBuilder.Current.IsBuilt) && Time.realtimeSinceStartup - started < 180) yield return null;
                if (WorldBuilder.Current == null || !WorldBuilder.Current.IsBuilt) result.errors.Add("the world was not built within 180 s");
                else yield return Shoot(request, result);
            }

            FreeVeloApp.CameraOverride = null;
            result.seconds = Time.realtimeSinceStartup - started;
            try
            {
                File.WriteAllText(Path.Combine(Folder, "done.json"), JsonUtility.ToJson(result, true));
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
        }

        private static IEnumerator Shoot(Request request, Result result)
        {
            string outDir = Path.Combine(Folder, "out");
            Directory.CreateDirectory(outDir);
            int w = Mathf.Clamp(request.width, 64, 4096), h = Mathf.Clamp(request.height, 64, 4096);
            var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32);
            var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
            foreach (var shot in request.shots)
            {
                var cam = Camera.main;
                if (cam == null || shot.pos == null || shot.pos.Length < 3 || shot.target == null || shot.target.Length < 3)
                {
                    result.errors.Add(shot.name + ": no camera or bad position");
                    continue;
                }

                FreeVeloApp.CameraOverride = (new Vector3(shot.pos[0], shot.pos[1], shot.pos[2]), new Vector3(shot.target[0], shot.target[1], shot.target[2]));
                float oldFov = cam.fieldOfView;
                cam.fieldOfView = shot.fov > 1 ? shot.fov : 60;

                // Let the detailed ground and scenery load round the new place: at least 1.5 s, then until the
                // streaming has been quiet for half a second (at most 30 s).
                float begin = Time.realtimeSinceStartup, quietSince = -1;
                while (Time.realtimeSinceStartup - begin < 30)
                {
                    yield return null;
                    bool busy = WorldBuilder.Current != null && WorldBuilder.Current.IsStreaming;
                    if (busy) quietSince = -1;
                    else if (quietSince < 0) quietSince = Time.realtimeSinceStartup;
                    if (Time.realtimeSinceStartup - begin > 1.5f && quietSince >= 0 && Time.realtimeSinceStartup - quietSince > 0.5f) break;
                }

                if (shot.settle > 0) yield return new WaitForSecondsRealtime(shot.settle);

                // This frame is rendered into the texture (the HUD is drawn to the screen, so it stays out).
                cam.targetTexture = rt;
                yield return new WaitForEndOfFrame();
                string error = null;
                try
                {
                    var old = RenderTexture.active;
                    RenderTexture.active = rt;
                    tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
                    tex.Apply();
                    RenderTexture.active = old;
                    string file = Path.Combine(outDir, Safe(shot.name) + ".png");
                    File.WriteAllBytes(file, tex.EncodeToPNG());
                    result.files.Add(Path.GetFileName(file));
                }
                catch (Exception e)
                {
                    error = shot.name + ": " + e.Message;
                }

                cam.targetTexture = null;
                cam.fieldOfView = oldFov;
                if (error != null) result.errors.Add(error);
            }

            Destroy(rt);
            Destroy(tex);
        }

        private static string Safe(string name)
        {
            var chars = (string.IsNullOrEmpty(name) ? "shot" : name).ToCharArray();
            for (int i = 0; i < chars.Length; i++)
            {
                if (!char.IsLetterOrDigit(chars[i]) && chars[i] != '-' && chars[i] != '_') chars[i] = '_';
            }

            return new string(chars);
        }
    }
}
#endif
