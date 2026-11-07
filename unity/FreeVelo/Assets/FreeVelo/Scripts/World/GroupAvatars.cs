using System.Collections.Generic;
using FreeVelo.Core.World;
using FreeVelo.Net;
using FreeVelo.Shared.Live;
using UnityEngine;

namespace FreeVelo.World
{
    /// <summary>
    /// The other riders of a group ride in the 3D world: one avatar per rider (jersey colour by slot), placed on the
    /// route at their smoothed position and lane, pedaling with their cadence and leaning into bends.
    /// </summary>
    public sealed class GroupAvatars : MonoBehaviour
    {
        /// <summary>Riders keep to the right half of the 6.5 m road: lateral offset from the centre line.</summary>
        public const float BaseLateralM = 1.4f;

        /// <summary>The own rider's jersey (also their dot in the rider list).</summary>
        public static readonly Color OwnJersey = new Color(0.1f, 0.45f, 0.85f);

        /// <summary>Jerseys of the others (without the own blue, so nobody looks like oneself).</summary>
        private static readonly Color[] Jerseys =
        {
            new Color(0.9f, 0.3f, 0.2f),
            new Color(0.15f, 0.65f, 0.35f),
            new Color(0.95f, 0.75f, 0.15f),
            new Color(0.55f, 0.3f, 0.75f),
            new Color(0.1f, 0.7f, 0.75f),
            new Color(0.9f, 0.45f, 0.65f),
            new Color(0.25f, 0.25f, 0.28f),
        };

        private readonly Dictionary<ushort, Remote> _remotes = new Dictionary<ushort, Remote>();
        private WorldBuilder _world;
        private GroupRideService _group;
        private System.Func<Route> _route;

        /// <summary>Riders on screen this frame (for name tags and emoji bubbles in the HUD).</summary>
        public List<(GroupRider Rider, Vector3 HeadPosition)> Visible { get; } = new List<(GroupRider, Vector3)>();

        public static Color JerseyOf(ushort slot, bool isMe = false) => isMe ? OwnJersey : Jerseys[(slot + Jerseys.Length - 1) % Jerseys.Length];

        /// <summary>Sideways position for a lane (kept on the road).</summary>
        public static float Lateral(float laneM) => Mathf.Clamp(BaseLateralM + laneM, 0.4f, 3.0f);

        /// <summary><paramref name="route"/>: the group ride's route (main loop or round the mountain).</summary>
        public void Initialize(WorldBuilder world, GroupRideService group, System.Func<Route> route)
        {
            _world = world;
            _group = group;
            _route = route;
        }

        private void LateUpdate()
        {
            Visible.Clear();
            if (_group == null || !_group.IsLive)
            {
                Clear();
                return;
            }

            var riders = _group.State.Riders(GroupRideService.NowS);
            var present = new HashSet<ushort>();
            float dt = Time.deltaTime;
            foreach (var r in riders)
            {
                if (r.IsMe) continue;
                present.Add(r.Slot);
                if (!_remotes.TryGetValue(r.Slot, out var remote))
                {
                    remote = new Remote { Avatar = RiderAvatar.Create(_world, JerseyOf(r.Slot)) };
                    remote.Avatar.name = "Rider " + r.DisplayName;
                    _remotes[r.Slot] = remote;
                }

                var route = _route();
                var sample = route.Sample(r.DistanceM);
                float heading = (float)sample.HeadingRad * Mathf.Rad2Deg;
                float pitch = -Mathf.Atan((float)sample.GradePercent / 100f) * Mathf.Rad2Deg;
                float targetLean = (float)Route.LeanAngleDeg(r.SpeedMps, route.CurvatureAt(r.DistanceM));
                remote.Lean = Mathf.Lerp(remote.Lean, targetLean, 1f - Mathf.Exp(-dt / 0.4f));
                var right = Quaternion.Euler(0, heading, 0) * Vector3.right;
                var position = new Vector3((float)sample.X, (float)sample.Y + 0.05f, (float)sample.Z) + (right * Lateral(r.LaneM));
                remote.Avatar.transform.SetPositionAndRotation(position, Quaternion.Euler(pitch, heading, -(remote.Lean + remote.Avatar.StopLeanDeg)));
                remote.Avatar.Animate(r.SpeedMps, r.CadenceRpm, dt);
                Visible.Add((r, position + (Vector3.up * 2.0f)));
            }

            // Riders who left.
            foreach (ushort slot in new List<ushort>(_remotes.Keys))
            {
                if (present.Contains(slot)) continue;
                Destroy(_remotes[slot].Avatar.gameObject);
                _remotes.Remove(slot);
            }
        }

        private void Clear()
        {
            foreach (var remote in _remotes.Values)
            {
                if (remote.Avatar != null) Destroy(remote.Avatar.gameObject);
            }

            _remotes.Clear();
        }

        private sealed class Remote
        {
            public RiderAvatar Avatar;
            public float Lean;
        }
    }
}
