using System.Collections.Generic;
using UnityEngine;

namespace WowSandbox
{
    /// <summary>
    /// A region the water stays out of — a ship's hull, a cellar below sea level. Inside it
    /// the water surface isn't drawn and WaterVolume reports no water, so you neither see the
    /// lake through the deck nor start swimming in the hold.
    ///
    /// The water is one flat sheet across the whole map; nothing about a ship's mesh can stop
    /// it showing inside the hull. This cuts it out instead. Both halves — the shader's discard
    /// and WaterVolume's check — read the same shape, so what you see and where you swim
    /// can't disagree.
    ///
    /// <see cref="Shape.Hull"/> is the one for ships: a profile sliced from the hull mesh
    /// itself (WoW Sandbox → Add Water Dry Zone to Selection does the slicing), so the cutout
    /// follows the pointed bow and the flare of the sides instead of cutting open water out
    /// next to them the way an ellipse does. Ellipse and Box remain for simple shapes.
    /// </summary>
    [ExecuteAlways]
    public class WaterDryZone : MonoBehaviour
    {
        public const int MaxZones = 8;
        public const int MaxHullZones = 2;
        /// <summary>
        /// Horizontal slices through the hull, keel to above the waterline. Enough that the
        /// gap between slices stays small near the waterline, where the cut is visible.
        /// </summary>
        public const int HullLevels = 16;
        /// <summary>Directions sampled around each slice.</summary>
        public const int HullSamples = 48;

        public enum Shape { Ellipse, Box, Hull }

        public Shape shape = Shape.Hull;

        [Header("Ellipse / Box")]
        [Tooltip("Local-space centre, relative to this object.")]
        public Vector3 center;
        [Tooltip("Local-space size. For an ellipse, X and Z are the two diameters.")]
        public Vector3 size = new Vector3(20f, 10f, 60f);

        // Hull profile, in this object's local space. Written by the editor fitter; there's no
        // reason to hand-edit it, so it's hidden rather than shown as hundreds of numbers.
        [HideInInspector] public Vector2 hullCentre;
        [HideInInspector] public float hullBottom;
        [HideInInspector] public float hullTop;
        [HideInInspector] public float[] hullRadii = new float[0];

        public bool HasHullProfile => hullRadii != null && hullRadii.Length == HullLevels * HullSamples;

        static readonly List<WaterDryZone> All = new();

        static readonly Matrix4x4[] Matrices = new Matrix4x4[MaxZones];
        static readonly float[] Shapes = new float[MaxZones];
        static readonly Matrix4x4[] HullMatrices = new Matrix4x4[MaxHullZones];
        static readonly Vector4[] HullParams = new Vector4[MaxHullZones];
        // Packed four to a Vector4: a float array costs a whole shader register per entry.
        static readonly Vector4[] HullRadii = new Vector4[MaxHullZones * HullLevels * HullSamples / 4];

        static readonly int CountId = Shader.PropertyToID("_DryZoneCount");
        static readonly int MatricesId = Shader.PropertyToID("_DryZoneWorldToLocal");
        static readonly int ShapesId = Shader.PropertyToID("_DryZoneShape");
        static readonly int HullCountId = Shader.PropertyToID("_DryHullCount");
        static readonly int HullMatricesId = Shader.PropertyToID("_DryHullWorldToLocal");
        static readonly int HullParamsId = Shader.PropertyToID("_DryHullParams");
        static readonly int HullRadiiId = Shader.PropertyToID("_DryHullRadii");

        void OnEnable()
        {
            All.Add(this);
            Upload();
        }

        void OnDisable()
        {
            All.Remove(this);
            Upload();
        }

        // Every frame, so moving the ship (or resizing a zone) just works.
        void LateUpdate() => Upload();

        void OnValidate()
        {
            size = Vector3.Max(size, Vector3.one * 0.01f);
            if (isActiveAndEnabled)
                Upload();
        }

        /// <summary>World to the zone's unit space, where an ellipse/box spans -0.5..0.5.</summary>
        Matrix4x4 WorldToUnit() =>
            (transform.localToWorldMatrix * Matrix4x4.TRS(center, Quaternion.identity, size)).inverse;

        public bool Contains(Vector3 worldPoint)
        {
            if (shape == Shape.Hull)
                return HasHullProfile && HullContains(transform.InverseTransformPoint(worldPoint));

            Vector3 p = WorldToUnit().MultiplyPoint3x4(worldPoint);
            if (Mathf.Abs(p.y) > 0.5f)
                return false;

            return shape == Shape.Box
                ? Mathf.Abs(p.x) <= 0.5f && Mathf.Abs(p.z) <= 0.5f
                : p.x * p.x + p.z * p.z <= 0.25f;
        }

        /// <summary>
        /// Mirrors HullContains in Water.shader: the radius of the hull at this point's angle
        /// and height, interpolated between the stored slices and directions.
        /// </summary>
        bool HullContains(Vector3 local)
        {
            float level = (local.y - hullBottom) / Mathf.Max(hullTop - hullBottom, 0.0001f) * (HullLevels - 1);
            if (level < 0f || level > HullLevels - 1)
                return false;

            Vector2 offset = new Vector2(local.x, local.z) - hullCentre;
            float angle = Mathf.Atan2(offset.y, offset.x);
            float sample = Mathf.Repeat(angle / (2f * Mathf.PI), 1f) * HullSamples;

            int s0 = Mathf.FloorToInt(sample) % HullSamples;
            int s1 = (s0 + 1) % HullSamples;
            float sT = sample - Mathf.Floor(sample);

            int l0 = Mathf.Min(Mathf.FloorToInt(level), HullLevels - 2);
            float lT = level - l0;

            float r0 = Mathf.Lerp(hullRadii[l0 * HullSamples + s0], hullRadii[l0 * HullSamples + s1], sT);
            float r1 = Mathf.Lerp(hullRadii[(l0 + 1) * HullSamples + s0], hullRadii[(l0 + 1) * HullSamples + s1], sT);

            return offset.magnitude <= Mathf.Lerp(r0, r1, lT);
        }

        /// <summary>True if any active dry zone holds this point.</summary>
        public static bool AnyContains(Vector3 worldPoint)
        {
            for (int i = 0; i < All.Count; i++)
            {
                if (All[i].Contains(worldPoint))
                    return true;
            }

            return false;
        }

        static void Upload()
        {
            int count = 0;
            int hullCount = 0;

            foreach (var zone in All)
            {
                if (zone.shape == Shape.Hull)
                {
                    if (!zone.HasHullProfile || hullCount >= MaxHullZones)
                        continue;

                    HullMatrices[hullCount] = zone.transform.worldToLocalMatrix;
                    HullParams[hullCount] = new Vector4(zone.hullCentre.x, zone.hullCentre.y, zone.hullBottom, zone.hullTop);
                    int offset = hullCount * HullLevels * HullSamples;
                    for (int i = 0; i < zone.hullRadii.Length; i++)
                        HullRadii[(offset + i) >> 2][(offset + i) & 3] = zone.hullRadii[i];
                    hullCount++;
                }
                else if (count < MaxZones)
                {
                    Matrices[count] = zone.WorldToUnit();
                    Shapes[count] = zone.shape == Shape.Box ? 1f : 0f;
                    count++;
                }
            }

            // Fixed-length arrays every time: Unity sizes a global array on its first set.
            Shader.SetGlobalMatrixArray(MatricesId, Matrices);
            Shader.SetGlobalFloatArray(ShapesId, Shapes);
            Shader.SetGlobalFloat(CountId, count);

            Shader.SetGlobalMatrixArray(HullMatricesId, HullMatrices);
            Shader.SetGlobalVectorArray(HullParamsId, HullParams);
            Shader.SetGlobalVectorArray(HullRadiiId, HullRadii);
            Shader.SetGlobalFloat(HullCountId, hullCount);
        }

        void OnDrawGizmos()
        {
            Gizmos.color = new Color(1f, 0.6f, 0.1f, 0.9f);
            Gizmos.matrix = transform.localToWorldMatrix;

            if (shape == Shape.Hull)
            {
                DrawHullGizmo();
                return;
            }

            if (shape == Shape.Box)
            {
                Gizmos.DrawWireCube(center, size);
                return;
            }

            const int segments = 48;
            for (int ring = -1; ring <= 1; ring++)
            {
                float y = center.y + ring * size.y * 0.5f;
                Vector3 previous = EllipsePoint(0f, y);
                for (int i = 1; i <= segments; i++)
                {
                    Vector3 next = EllipsePoint(i / (float)segments * Mathf.PI * 2f, y);
                    Gizmos.DrawLine(previous, next);
                    previous = next;
                }
            }
        }

        void DrawHullGizmo()
        {
            if (!HasHullProfile)
                return;

            for (int level = 0; level < HullLevels; level++)
            {
                float y = Mathf.Lerp(hullBottom, hullTop, level / (float)(HullLevels - 1));
                for (int s = 0; s < HullSamples; s++)
                    Gizmos.DrawLine(HullPoint(level, s, y), HullPoint(level, (s + 1) % HullSamples, y));
            }
        }

        Vector3 HullPoint(int level, int sample, float y)
        {
            float angle = sample / (float)HullSamples * Mathf.PI * 2f;
            float radius = hullRadii[level * HullSamples + sample];
            return new Vector3(hullCentre.x + Mathf.Cos(angle) * radius, y, hullCentre.y + Mathf.Sin(angle) * radius);
        }

        Vector3 EllipsePoint(float angle, float y) =>
            new Vector3(center.x + Mathf.Cos(angle) * size.x * 0.5f, y, center.z + Mathf.Sin(angle) * size.z * 0.5f);
    }
}
