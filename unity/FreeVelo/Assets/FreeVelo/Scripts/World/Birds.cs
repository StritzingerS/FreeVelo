using System.Collections.Generic;
using UnityEngine;

namespace FreeVelo.World
{
    /// <summary>
    /// A few birds near the camera: two buzzards circling over the land, mostly gliding with a few wing beats, and a
    /// small flock flapping across. They keep 25–70 m above the ground under them (so never into a slope) and are
    /// placed anew some 200 m away when the camera has left them behind. Simple meshes (body and two wings that
    /// beat), a handful of renderers in all.
    /// </summary>
    public sealed class Birds : MonoBehaviour
    {
        private sealed class Bird
        {
            public Transform Body, LeftWing, RightWing;
            public Vector3 Offset; // in the flock (local: x right, y up, z forward)
            public float Phase;
        }

        private sealed class Flock
        {
            public bool Circling;
            public Vector3 Centre; // circle centre (circling) or position (flying across)
            public float Heading, Angle, Radius, Speed, Height, Span, Beat;
            public readonly List<Bird> Members = new List<Bird>();
        }

        private WorldBuilder _world;
        private readonly List<Flock> _flocks = new List<Flock>();
        private Material _material;
        private Mesh _bodyMesh, _wingMesh;
        private Transform _root;
        private Vector3 _lastEye;

        public void Init(WorldBuilder world)
        {
            _world = world;
            if (_root != null) Destroy(_root.gameObject);
            _flocks.Clear();
        }

        private void LateUpdate()
        {
            using (FreeVelo.App.PerfTimes.Measure("birds")) Tick();
        }

        private void Tick()
        {
            var cam = Camera.main;
            if (_world == null || !_world.IsBuilt || cam == null) return;
            if (_root == null) CreateFlocks(cam.transform.position);
            var eye = cam.transform.position;
            float t = Time.time, dt = Time.deltaTime;
            // The camera jumped (a climb started at its foot, joining a group elsewhere): bring the birds along.
            bool jumped = (eye - _lastEye).sqrMagnitude > 100f * 100f;
            _lastEye = eye;

            foreach (var f in _flocks)
            {
                // Left behind (or never placed): start again 60–120 m off, ahead of the camera.
                var flat = new Vector3(f.Centre.x - eye.x, 0f, f.Centre.z - eye.z);
                if (jumped || flat.magnitude > 260f) Place(f, eye, cam.transform.forward);

                Vector3 position;
                Vector3 forward;
                float bank = 0f;
                if (f.Circling)
                {
                    f.Angle += f.Speed / f.Radius * dt;
                    position = f.Centre + new Vector3(Mathf.Cos(f.Angle) * f.Radius, 0f, Mathf.Sin(f.Angle) * f.Radius);
                    forward = new Vector3(-Mathf.Sin(f.Angle), 0f, Mathf.Cos(f.Angle));
                    bank = -18f; // leaning into the circle
                }
                else
                {
                    // Flying across with a slow, wandering turn.
                    f.Heading += Mathf.Sin((t * 0.13f) + f.Beat) * 9f * dt;
                    forward = Quaternion.Euler(0f, f.Heading, 0f) * Vector3.forward;
                    f.Centre += forward * (f.Speed * dt);
                    position = f.Centre;
                }

                // Height above the ground under the flock, smoothed so it follows the land gently.
                float ground = _world.GroundHeight(position.x, position.z);
                float target = ground + f.Height;
                f.Centre.y = f.Centre.y == 0f ? target : Mathf.Lerp(f.Centre.y, target, 1f - Mathf.Exp(-dt / 3f));
                position.y = f.Centre.y;
                var rotation = Quaternion.LookRotation(forward, Vector3.up) * Quaternion.Euler(0f, 0f, bank);

                foreach (var b in f.Members)
                {
                    var p = position + (rotation * b.Offset);
                    p.y += Mathf.Sin((t * 0.7f) + b.Phase) * 0.6f; // a gentle rise and fall
                    b.Body.SetPositionAndRotation(p, rotation);

                    // Wing beats: buzzards glide and beat a few times now and then; small birds flap in bursts.
                    float cycle = f.Circling ? Mathf.Repeat((t + b.Phase) / 9f, 1f) : Mathf.Repeat((t + b.Phase) / 2.2f, 1f);
                    bool beating = f.Circling ? cycle < 0.12f : cycle < 0.6f;
                    float wing = beating ? Mathf.Sin((t + b.Phase) * f.Beat * Mathf.PI * 2f) * 38f : 6f;
                    b.LeftWing.localRotation = Quaternion.Euler(0f, 0f, wing);
                    b.RightWing.localRotation = Quaternion.Euler(0f, 0f, -wing);
                }
            }
        }

        private void CreateFlocks(Vector3 eye)
        {
            _root = new GameObject("Birds").transform;
            _root.SetParent(transform, false);
            _material = _world.Mat(new Color(0.22f, 0.19f, 0.16f));
            _bodyMesh = BodyMesh();
            _wingMesh = WingMesh();

            // Two buzzards on one wide circle, and four small birds flying across.
            var buzzards = new Flock { Circling = true, Radius = 35f, Speed = 8f, Height = 38f, Span = 1.25f, Beat = 2.2f };
            buzzards.Members.Add(NewBird(buzzards.Span, Vector3.zero, 0f));
            buzzards.Members.Add(NewBird(buzzards.Span * 0.95f, new Vector3(9f, 4f, -14f), 3.7f));
            var flock = new Flock { Circling = false, Speed = 12f, Height = 22f, Span = 0.55f, Beat = 5.5f };
            var offsets = new[] { Vector3.zero, new Vector3(-2.5f, 0.8f, -2f), new Vector3(2.2f, -0.5f, -3.1f), new Vector3(-0.8f, 1.2f, -4.5f) };
            for (int i = 0; i < offsets.Length; i++) flock.Members.Add(NewBird(flock.Span, offsets[i], i * 0.37f));
            _flocks.Add(buzzards);
            _flocks.Add(flock);
            var forward = Camera.main != null ? Camera.main.transform.forward : Vector3.forward;
            foreach (var f in _flocks) Place(f, eye, forward);
        }

        private static void Place(Flock f, Vector3 eye, Vector3 forward)
        {
            float yaw = Mathf.Atan2(forward.x, forward.z) * Mathf.Rad2Deg + Random.Range(-50f, 50f);
            float distance = Random.Range(60f, 120f);
            var at = eye + (Quaternion.Euler(0f, yaw, 0f) * Vector3.forward * distance);
            f.Centre = new Vector3(at.x, 0f, at.z); // height set on the next update
            if (!f.Circling) f.Centre -= Quaternion.Euler(0f, f.Heading, 0f) * Vector3.forward * 60f;
            f.Angle = Random.Range(0f, Mathf.PI * 2f);
            f.Heading = yaw + Random.Range(60f, 120f) * (Random.value < 0.5f ? -1f : 1f); // across the view
        }

        private Bird NewBird(float span, Vector3 offset, float phase)
        {
            var body = new GameObject("Bird").transform;
            body.SetParent(_root, false);
            body.localScale = Vector3.one * span;
            AddMesh(body, _bodyMesh);
            var left = new GameObject("WingL").transform;
            left.SetParent(body, false);
            left.localPosition = new Vector3(-0.04f, 0.02f, 0.02f);
            AddMesh(left, _wingMesh).transform.localScale = new Vector3(-1f, 1f, 1f);
            var right = new GameObject("WingR").transform;
            right.SetParent(body, false);
            right.localPosition = new Vector3(0.04f, 0.02f, 0.02f);
            AddMesh(right, _wingMesh);
            return new Bird { Body = body, LeftWing = left, RightWing = right, Offset = offset, Phase = phase };
        }

        private GameObject AddMesh(Transform parent, Mesh mesh)
        {
            var go = new GameObject("Mesh");
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = _material;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return go;
        }

        /// <summary>A slim body (span 1 = the whole bird): head forward (+z), a fanned tail behind.</summary>
        private static Mesh BodyMesh()
        {
            var v = new[]
            {
                new Vector3(0f, 0f, 0.28f), new Vector3(-0.05f, 0f, 0.05f), new Vector3(0.05f, 0f, 0.05f), new Vector3(0f, 0.05f, 0.05f), new Vector3(0f, -0.04f, 0.05f),
                new Vector3(0f, 0.01f, -0.18f), new Vector3(-0.08f, 0f, -0.3f), new Vector3(0.08f, 0f, -0.3f),
            };
            var tris = new List<int> { 0, 3, 1, 0, 2, 3, 0, 1, 4, 0, 4, 2, 1, 3, 5, 3, 2, 5, 2, 4, 5, 4, 1, 5, 5, 6, 7 };
            return DoubleSided("BirdBody", v, tris);
        }

        /// <summary>One wing to the right (+x): broad at the body, tapering to the tip, swept back a little.</summary>
        private static Mesh WingMesh()
        {
            var v = new[]
            {
                new Vector3(0f, 0f, 0.09f), new Vector3(0f, 0f, -0.1f), new Vector3(0.28f, 0.01f, 0.06f), new Vector3(0.26f, 0.01f, -0.1f), new Vector3(0.5f, 0.02f, -0.02f), new Vector3(0.47f, 0.02f, -0.09f),
            };
            var tris = new List<int> { 0, 2, 1, 1, 2, 3, 2, 4, 3, 3, 4, 5 };
            return DoubleSided("BirdWing", v, tris);
        }

        /// <summary>The triangles from both sides (wings are seen from above and below).</summary>
        private static Mesh DoubleSided(string name, Vector3[] vertices, List<int> tris)
        {
            var all = new List<int>(tris);
            for (int i = 0; i < tris.Count; i += 3) all.AddRange(new[] { tris[i], tris[i + 2], tris[i + 1] });
            var mesh = new Mesh { name = name, vertices = vertices };
            mesh.SetTriangles(all, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
