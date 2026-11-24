#if UNITY_EDITOR || FREEVELO_SHOTS
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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

            /// <summary>Position and target in the own rider's frame (x right, y up, z forward) instead of the world.</summary>
            public bool rider;

            /// <summary>0–360: the rider rides with the cranks held at this angle (instead of standing in the menu).</summary>
            public float crank = -1;

            /// <summary>Head turn of the riders in degrees (positive = right); 0 = from the road.</summary>
            public float look;

            /// <summary>0 seated, 1 standing, 2 sprinting; -1 = as the ride decides.</summary>
            public int stance = -1;

            /// <summary>More than 1: a sequence of this many frames at <see cref="fps"/> (name_000.png, …) for a video.</summary>
            public int frames = 1;

            public float fps = 24;
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

        /// <summary>The project's "Run in background" while a render run is going on (restored afterwards).</summary>
        private bool _runInBackground;

        /// <summary>An interrupted run (play mode stopped mid-shot) must not leave the camera pinned.</summary>
        private void OnDestroy()
        {
            Time.captureDeltaTime = 0f;
            FreeVeloApp.CameraOverride = null;
            Application.runInBackground = _runInBackground;
        }

        private IEnumerator Start()
        {
            // The project has "Run in background" off (right for the phone): in the editor that pauses or slows play
            // mode whenever the window is not focused. Render runs happen without anyone at the editor, so keep
            // running for the duration of the run only.
            _runInBackground = Application.runInBackground;
            Application.runInBackground = true;
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
            RiderAvatar.ShotCrank = null;
            RiderAvatar.ShotLook = null;
            RiderAvatar.ShotStance = null;
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

                RiderAvatar.ShotCrank = shot.crank >= 0 ? shot.crank : (float?)null;
                RiderAvatar.ShotLook = shot.look != 0 ? shot.look : (float?)null;
                RiderAvatar.ShotStance = shot.stance >= 0 ? shot.stance : (int?)null;
                var shotPos = new Vector3(shot.pos[0], shot.pos[1], shot.pos[2]);
                var shotTarget = new Vector3(shot.target[0], shot.target[1], shot.target[2]);
                if (shot.rider)
                {
                    if (shot.crank >= 0) yield return null; // let the rider take the pose first
                    var riderGo = GameObject.Find("Rider");
                    if (riderGo == null)
                    {
                        result.errors.Add(shot.name + ": no rider");
                        continue;
                    }

                    shotPos = riderGo.transform.TransformPoint(shotPos);
                    shotTarget = riderGo.transform.TransformPoint(shotTarget);
                }

                FreeVeloApp.CameraOverride = (shotPos, shotTarget);
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

                // This frame is rendered into the texture (the HUD is drawn to the screen, so it stays out). A sequence
                // runs the game clock at a fixed step, so the video plays at real speed however long a frame takes.
                cam.targetTexture = rt;
                int frames = Mathf.Clamp(shot.frames, 1, 600);
                if (frames > 1) Time.captureDeltaTime = 1f / Mathf.Clamp(shot.fps, 1f, 60f);
                string error = null;
                for (int frame = 0; frame < frames && error == null; frame++)
                {
                    yield return new WaitForEndOfFrame();
                    try
                    {
                        var old = RenderTexture.active;
                        RenderTexture.active = rt;
                        tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
                        tex.Apply();
                        RenderTexture.active = old;
                        string file = Path.Combine(outDir, Safe(shot.name) + (frames > 1 ? "_" + frame.ToString("000") : "") + ".png");
                        File.WriteAllBytes(file, tex.EncodeToPNG());
                        result.files.Add(Path.GetFileName(file));
                        if (frame == 0) File.WriteAllText(Path.Combine(outDir, Safe(shot.name) + ".txt"), RenderBreakdown(cam));
                    }
                    catch (Exception e)
                    {
                        error = shot.name + ": " + e.Message;
                    }
                }

                Time.captureDeltaTime = 0f;
                cam.targetTexture = null;
                cam.fieldOfView = oldFov;
                if (error != null) result.errors.Add(error);
            }

            Destroy(rt);
            Destroy(tex);
        }

        /// <summary>
        /// What the camera draws, by renderer name (digits stripped) and distance: renderers, submeshes (about one draw
        /// call each, more with shadows) and triangles; plus the instanced scenery. For the phone performance work.
        /// </summary>
        private static string RenderBreakdown(Camera cam)
        {
            var planes = GeometryUtility.CalculateFrustumPlanes(cam);
            var eye = cam.transform.position;
            var rows = new Dictionary<string, (int R, int S, long T)>();
            int totalR = 0, totalS = 0;
            long totalT = 0;
            foreach (var r in UnityEngine.Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None))
            {
                if (!r.enabled || !r.gameObject.activeInHierarchy || !GeometryUtility.TestPlanesAABB(planes, r.bounds)) continue;
                var lod = r.GetComponentInParent<LODGroup>();
                if (lod != null)
                {
                    // only the LOD level the camera picks: Unity's relative height = group size / (2 d tan(fov/2))
                    float d0 = Vector3.Distance(eye, lod.transform.TransformPoint(lod.localReferencePoint));
                    float relative = lod.size / (2f * Mathf.Max(1f, d0) * Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad)) * QualitySettings.lodBias;
                    var lods = lod.GetLODs();
                    int level = -1;
                    for (int i = 0; i < lods.Length; i++) if (relative >= lods[i].screenRelativeTransitionHeight) { level = i; break; }
                    if (level < 0) continue;
                    if (Array.IndexOf(lods[level].renderers, r) < 0) continue;
                }

                var mesh = r is MeshRenderer && r.TryGetComponent<MeshFilter>(out var filter) ? filter.sharedMesh : null;
                if (mesh == null) continue;
                long tris = 0;
                for (int s = 0; s < mesh.subMeshCount; s++) tris += mesh.GetIndexCount(s) / 3;
                float d = Vector3.Distance(new Vector3(eye.x, 0, eye.z), new Vector3(r.bounds.center.x, 0, r.bounds.center.z));
                string band = d < 400 ? "<400" : d < 1500 ? "<1500" : d < 4000 ? "<4000" : ">4000";
                string name = new string(r.gameObject.name.Where(c => !char.IsDigit(c) && c != ',').ToArray()).Trim() + (r.transform.parent != null ? " in " + new string(r.transform.parent.name.Where(c => !char.IsDigit(c) && c != ',').ToArray()).Trim() : "");
                string key = $"{name} [{band}]";
                rows.TryGetValue(key, out var row);
                rows[key] = (row.R + 1, row.S + mesh.subMeshCount, row.T + tris);
                totalR++;
                totalS += mesh.subMeshCount;
                totalT += tris;
            }

            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"renderers {totalR}, submeshes {totalS}, triangles {totalT / 1000}k; instanced scenery {WorldBuilder.InstancesDrawn} in {WorldBuilder.InstanceDrawCalls} calls, {WorldBuilder.InstanceTriangles / 1000}k tris; grass tufts {GrassField.Drawn} drawn of {GrassField.Computed} (cells turned down: {string.Join("/", GrassField.Reject)}); far clip {cam.farClipPlane:0} m");
            foreach (var p in rows.OrderByDescending(p => p.Value.S)) sb.AppendLine($"{p.Value.S,6} submeshes {p.Value.R,5} renderers {p.Value.T / 1000,6}k tris  {p.Key}");
            return sb.ToString();
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
