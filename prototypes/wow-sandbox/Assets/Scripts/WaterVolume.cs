using System.Collections.Generic;
using UnityEngine;

namespace WowSandbox
{
    /// <summary>
    /// Marks a body of water for gameplay: where its surface is, and which patch of the
    /// world it covers. The character controller and the camera both ask it the same
    /// question — "am I in the water, and how far under?"
    ///
    /// Deliberately not a trigger collider. A trigger would need a Rigidbody on something,
    /// it would fight the CharacterController's own capsule sweep, and it would still only
    /// tell us "inside/outside" when what we actually need is submersion *depth*. A plane
    /// comparison gives us that exactly, for free, and works for the camera too — which
    /// has no collider at all.
    /// </summary>
    [DisallowMultipleComponent]
    public class WaterVolume : MonoBehaviour
    {
        /// <summary>
        /// World-space Y of the flat water surface — taken from the transform, never stored.
        ///
        /// This used to be a serialized field that WaterSetup wrote at build time, which meant
        /// dragging the water up or down in the scene moved the visible mesh while gameplay
        /// kept testing against the old height: you'd wade well past your head before swimming
        /// engaged. The transform is the single source of truth, so moving the water now just
        /// works. The shader's waves ripple around this height; gameplay treats it as flat.
        /// </summary>
        public float SurfaceY => transform.position.y;

        [Tooltip("Half-extents on X and Z, measured from this object's position.")]
        public Vector2 extents = new Vector2(250f, 250f);

        [Tooltip("How far below the surface the water goes. Anything deeper is treated as " +
                 "outside, which keeps the check honest if the terrain drops away below.")]
        public float depth = 200f;

        static readonly List<WaterVolume> All = new();

        void OnEnable() => All.Add(this);
        void OnDisable() => All.Remove(this);

        /// <summary>True if the point is inside this volume's footprint and below its surface.</summary>
        public bool Contains(Vector3 worldPoint)
        {
            Vector3 local = worldPoint - transform.position;

            return Mathf.Abs(local.x) <= extents.x
                && Mathf.Abs(local.z) <= extents.y
                && worldPoint.y <= SurfaceY
                && worldPoint.y >= SurfaceY - depth;
        }

        /// <summary>
        /// The volume containing this point, or null. Returns the first match — the sandbox
        /// has one global sea level, so overlapping volumes aren't a case worth resolving.
        /// </summary>
        public static WaterVolume Containing(Vector3 worldPoint)
        {
            for (int i = 0; i < All.Count; i++)
            {
                if (All[i].Contains(worldPoint))
                    return All[i];
            }

            return null;
        }

        /// <summary>
        /// How far <paramref name="worldPoint"/> sits below the nearest containing surface.
        /// Zero when it isn't in water at all, so callers can treat it as a simple depth.
        /// </summary>
        public static float SubmersionDepth(Vector3 worldPoint)
        {
            var volume = Containing(worldPoint);
            return volume == null ? 0f : volume.SurfaceY - worldPoint.y;
        }

        void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(0.2f, 0.6f, 0.9f, 0.35f);
            var centre = new Vector3(transform.position.x, SurfaceY - depth * 0.5f, transform.position.z);
            Gizmos.DrawWireCube(centre, new Vector3(extents.x * 2f, depth, extents.y * 2f));
        }
    }
}
