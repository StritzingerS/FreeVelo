using System.Collections.Generic;
using System.IO;
using System.Text;
using FreeVelo.Core.Avatar;
using UnityEngine;
using Num = System.Numerics;

namespace FreeVelo.World
{
    /// <summary>
    /// The skinned rider (Tripo model, rigged), baked by tools/blender/rider_on_bike.py (bake=) into
    /// Resources/FreeVeloRider.bytes. Its rest pose is the seated pose the owner approved in Blender (back, arms,
    /// hands on the hoods); every frame the legs are solved onto the pedals from <see cref="AvatarModel.Pose"/>
    /// with the same rules as in Blender, and while stopping the upper body follows the avatar's "Rider" node with
    /// the hands kept on the hoods. Rig coordinates = avatar coordinates (x right, y up, z forward).
    /// The jersey is recoloured per rider from the texture: alpha 0 marks the jersey, its grey is the shading.
    /// </summary>
    public sealed class RiderSkin
    {
        private sealed class Data
        {
            public float BallHeight, FootDrop, FootLength;
            public string[] Names;
            public int[] Parents;
            public Vector3[] RestPos;
            public Quaternion[] RestRot;
            public readonly List<Mesh> Lods = new List<Mesh>();
            public byte[] Png;
        }

        private static Data _data;
        private static bool _tried;
        private static readonly Dictionary<Color32, Texture2D> Textures = new Dictionary<Color32, Texture2D>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            _data = null;
            _tried = false;
            Textures.Clear();
        }

        /// <summary>True if the baked rider is in Resources and readable.</summary>
        public static bool Available => Load() != null;

        private readonly Transform _rig;
        private readonly Transform[] _bones;
        private readonly Vector3[] _restLocalPos;
        private readonly Quaternion[] _restLocalRot;
        private readonly int _hips;
        private readonly int[] _upperLeg = new int[2], _lowerLeg = new int[2], _foot = new int[2], _toes = new int[2];
        private readonly int[] _upperArm = new int[2], _lowerArm = new int[2], _hand = new int[2];
        private readonly float[] _thigh = new float[2], _shin = new float[2], _upper = new float[2], _fore = new float[2];
        private readonly Vector3[] _elbowBend = new Vector3[2];
        private readonly Data _d;

        /// <summary>Renderers per level of detail (0 = full, 1 = distant riders).</summary>
        public List<Renderer> Lod0 { get; } = new List<Renderer>();

        public List<Renderer> Lod1 { get; } = new List<Renderer>();

        private RiderSkin(Data d, Transform parent, Material material)
        {
            _d = d;
            _rig = new GameObject("RiderSkin").transform;
            _rig.SetParent(parent, false);

            int n = d.Names.Length;
            _bones = new Transform[n];
            for (int i = 0; i < n; i++)
            {
                var b = new GameObject(d.Names[i]).transform;
                b.SetParent(_rig, false);
                b.localPosition = d.RestPos[i];
                b.localRotation = d.RestRot[i];
                _bones[i] = b;
            }

            for (int i = 0; i < n; i++)
                if (d.Parents[i] >= 0) _bones[i].SetParent(_bones[d.Parents[i]], true);

            _restLocalPos = new Vector3[n];
            _restLocalRot = new Quaternion[n];
            for (int i = 0; i < n; i++)
            {
                _restLocalPos[i] = _bones[i].localPosition;
                _restLocalRot[i] = _bones[i].localRotation;
            }

            _hips = Index("Hips");
            string[] sides = { "Right", "Left" };
            for (int s = 0; s < 2; s++)
            {
                _upperLeg[s] = Index(sides[s] + "_UpperLeg");
                _lowerLeg[s] = Index(sides[s] + "_LowerLeg");
                _foot[s] = Index(sides[s] + "_Foot");
                _toes[s] = Index(sides[s] + "_Toes");
                _upperArm[s] = Index(sides[s] + "_UpperArm");
                _lowerArm[s] = Index(sides[s] + "_LowerArm");
                _hand[s] = Index(sides[s] + "_Hand");
                _thigh[s] = Vector3.Distance(d.RestPos[_upperLeg[s]], d.RestPos[_lowerLeg[s]]);
                _shin[s] = Vector3.Distance(d.RestPos[_lowerLeg[s]], d.RestPos[_foot[s]]);
                _upper[s] = Vector3.Distance(d.RestPos[_upperArm[s]], d.RestPos[_lowerArm[s]]);
                _fore[s] = Vector3.Distance(d.RestPos[_lowerArm[s]], d.RestPos[_hand[s]]);

                // Which way the elbow points in the approved pose (kept while the body moves)
                var sh = d.RestPos[_upperArm[s]];
                var wr = d.RestPos[_hand[s]];
                var el = d.RestPos[_lowerArm[s]];
                var axis = (wr - sh).normalized;
                _elbowBend[s] = (el - sh) - (Vector3.Dot(el - sh, axis) * axis);
            }

            for (int lod = 0; lod < d.Lods.Count; lod++)
            {
                var go = new GameObject("RiderSkin_LOD" + lod);
                go.transform.SetParent(_rig, false);
                var smr = go.AddComponent<SkinnedMeshRenderer>();
                smr.sharedMesh = d.Lods[lod];
                smr.bones = _bones;
                smr.sharedMaterial = material;
                smr.quality = lod == 0 ? SkinQuality.Bone4 : SkinQuality.Bone2;
                smr.localBounds = new Bounds(new Vector3(0, 0.9f, 0.15f), new Vector3(1.0f, 2.0f, 1.6f));
                (lod == 0 ? Lod0 : Lod1).Add(smr);
            }
        }

        /// <summary>Builds a rider under <paramref name="parent"/> (null if the bake is missing).</summary>
        public static RiderSkin Create(Transform parent, Color jersey, WorldBuilder world)
        {
            var d = Load();
            if (d == null) return null;
            var material = world.Mat(Color.white, JerseyTexture(d, jersey), 0.2f);
            return new RiderSkin(d, parent, material);
        }

        private int Index(string name)
        {
            int i = System.Array.IndexOf(_d.Names, name);
            if (i < 0) throw new InvalidDataException("rider bone missing: " + name);
            return i;
        }

        /// <summary>
        /// Poses the rider for the avatar pose of this frame (crank angle, stopping blend). Call after
        /// <see cref="AvatarModel.Pose(float, float, Dictionary{AvatarNode, NodePose}, float)"/>.
        /// </summary>
        public void Apply(Dictionary<AvatarNode, NodePose> pose, float stopped)
        {
            for (int i = 0; i < _bones.Length; i++)
            {
                _bones[i].localPosition = _restLocalPos[i];
                _bones[i].localRotation = _restLocalRot[i];
            }

            float t = Mathf.Clamp01(stopped);
            t = t * t * (3f - (2f * t));

            // Upper body: the avatar's rider node (slides off the saddle and sits up while stopping).
            if (pose.TryGetValue(AvatarNode.Rider, out var riderPose))
            {
                var r = Q(riderPose.Rotation);
                var p = V(riderPose.Position);
                _bones[_hips].position = _rig.TransformPoint((r * _d.RestPos[_hips]) + p);
                _bones[_hips].rotation = _rig.rotation * (r * _d.RestRot[_hips]);
            }

            for (int s = 0; s < 2; s++)
            {
                float sign = s == 0 ? 1f : -1f;
                if (!pose.TryGetValue(s == 0 ? AvatarNode.FootRight : AvatarNode.FootLeft, out var footPose)) continue;

                // Legs: ball of the foot on the cleat, foot pitched by the avatar's ankling, knee bent forward.
                var cleat = V(footPose.Position);
                var footRot = Q(footPose.Rotation);
                var ball = cleat + new Vector3(0, _d.BallHeight, 0);
                var dir = footRot * new Vector3(0, -Mathf.Sin(_d.FootDrop), Mathf.Cos(_d.FootDrop));
                var ankle = ball - (dir * _d.FootLength);
                var hip = RigPos(_upperLeg[s]);
                var knee = Solve(hip, ankle, _thigh[s], _shin[s], new Vector3(sign * (0.08f + (0.25f * t)), 0.25f, 1f));
                Aim(_upperLeg[s], _lowerLeg[s], knee);
                Aim(_lowerLeg[s], _foot[s], ankle);
                Aim(_foot[s], _toes[s], ball);

                // Arms: only needed when the body moves; the hands stay where the approved pose put them.
                if (t > 0f)
                {
                    var wrist = _d.RestPos[_hand[s]];
                    var shoulder = RigPos(_upperArm[s]);
                    var elbow = Solve(shoulder, wrist, _upper[s], _fore[s], _elbowBend[s]);
                    Aim(_upperArm[s], _lowerArm[s], elbow);
                    Aim(_lowerArm[s], _hand[s], wrist);
                    _bones[_hand[s]].rotation = _rig.rotation * _d.RestRot[_hand[s]];
                }
            }
        }

        private Vector3 RigPos(int bone) => _rig.InverseTransformPoint(_bones[bone].position);

        /// <summary>Turns <paramref name="bone"/> so its child joint points at <paramref name="target"/> (rig space).</summary>
        private void Aim(int bone, int child, Vector3 target)
        {
            var b = _bones[bone];
            var from = _bones[child].position - b.position;
            var to = _rig.TransformPoint(target) - b.position;
            if (from.sqrMagnitude < 1e-10f || to.sqrMagnitude < 1e-10f) return;
            b.rotation = Quaternion.FromToRotation(from, to) * b.rotation;
        }

        private static Vector3 Solve(Vector3 root, Vector3 target, float upper, float lower, Vector3 pole)
        {
            var j = AvatarModel.SolveJoint(N(root), N(target), upper, lower, N(pole));
            return new Vector3(j.X, j.Y, j.Z);
        }

        private static Num.Vector3 N(Vector3 v) => new Num.Vector3(v.x, v.y, v.z);

        private static Vector3 V(Num.Vector3 v) => new Vector3(v.X, v.Y, v.Z);

        private static Quaternion Q(Rot r)
        {
            var q = r.ToQuaternion();
            return new Quaternion(q.X, q.Y, q.Z, q.W);
        }

        /// <summary>The rider texture with the jersey in <paramref name="jersey"/> (one per colour, shared).</summary>
        private static Texture2D JerseyTexture(Data d, Color jersey)
        {
            var key = (Color32)jersey;
            if (Textures.TryGetValue(key, out var cached) && cached != null) return cached;
            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, true) { name = "RiderSkin_" + ColorUtility.ToHtmlStringRGB(jersey) };
            tex.LoadImage(d.Png, false);
            var px = tex.GetPixels32();
            for (int i = 0; i < px.Length; i++)
            {
                var p = px[i];
                if (p.a < 128)
                {
                    float shade = p.r / 127.5f;
                    p.r = (byte)Mathf.Clamp(jersey.r * shade * 255f, 0, 255);
                    p.g = (byte)Mathf.Clamp(jersey.g * shade * 255f, 0, 255);
                    p.b = (byte)Mathf.Clamp(jersey.b * shade * 255f, 0, 255);
                }

                p.a = 255;
                px[i] = p;
            }

            tex.SetPixels32(px);
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.anisoLevel = 2;
            tex.Apply(true, true);
            Textures[key] = tex;
            return tex;
        }

        private static Data Load()
        {
            if (_data != null || _tried) return _data;
            _tried = true;
            var asset = Resources.Load<TextAsset>("FreeVeloRider");
            if (asset == null) return null;
            try
            {
                _data = Parse(asset.bytes);
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"FreeVelo: skinned rider unreadable ({e.Message}), using the generated rider.");
                _data = null;
            }

            return _data;
        }

        private static Data Parse(byte[] bytes)
        {
            using var r = new BinaryReader(new MemoryStream(bytes));
            if (Encoding.ASCII.GetString(r.ReadBytes(4)) != "FVSK") throw new InvalidDataException("not a rider bake");
            if (r.ReadInt32() != 1) throw new InvalidDataException("unknown rider bake version");
            var d = new Data { BallHeight = r.ReadSingle(), FootDrop = r.ReadSingle(), FootLength = r.ReadSingle() };

            int n = r.ReadInt32();
            d.Names = new string[n];
            d.Parents = new int[n];
            d.RestPos = new Vector3[n];
            d.RestRot = new Quaternion[n];
            for (int i = 0; i < n; i++)
            {
                d.Names[i] = Encoding.UTF8.GetString(r.ReadBytes(r.ReadInt32()));
                d.Parents[i] = r.ReadInt32();
                d.RestPos[i] = new Vector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
                d.RestRot[i] = new Quaternion(r.ReadSingle(), r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
            }

            var bind = new Matrix4x4[n];
            for (int i = 0; i < n; i++) bind[i] = Matrix4x4.TRS(d.RestPos[i], d.RestRot[i], Vector3.one).inverse;

            int lods = r.ReadInt32();
            for (int lod = 0; lod < lods; lod++)
            {
                int vc = r.ReadInt32();
                var pos = new Vector3[vc];
                var nrm = new Vector3[vc];
                var uv = new Vector2[vc];
                var w = new BoneWeight[vc];
                for (int i = 0; i < vc; i++) pos[i] = new Vector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
                for (int i = 0; i < vc; i++) nrm[i] = new Vector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
                for (int i = 0; i < vc; i++) uv[i] = new Vector2(r.ReadSingle(), r.ReadSingle());
                for (int i = 0; i < vc; i++)
                {
                    int b0 = r.ReadUInt16(), b1 = r.ReadUInt16(), b2 = r.ReadUInt16(), b3 = r.ReadUInt16();
                    w[i] = new BoneWeight
                    {
                        boneIndex0 = b0, boneIndex1 = b1, boneIndex2 = b2, boneIndex3 = b3,
                        weight0 = r.ReadSingle(), weight1 = r.ReadSingle(), weight2 = r.ReadSingle(), weight3 = r.ReadSingle(),
                    };
                }

                int ic = r.ReadInt32();
                var idx = new int[ic];
                for (int i = 0; i < ic; i++) idx[i] = r.ReadInt32();

                var mesh = new Mesh { name = "RiderSkin_LOD" + lod };
                if (vc > 65000) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
                mesh.vertices = pos;
                mesh.normals = nrm;
                mesh.uv = uv;
                mesh.boneWeights = w;
                mesh.bindposes = bind;
                mesh.triangles = idx;
                mesh.RecalculateBounds();
                d.Lods.Add(mesh);
            }

            d.Png = r.ReadBytes(r.ReadInt32());
            return d;
        }
    }
}
