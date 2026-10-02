using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace WowSandbox.EditorTools
{
    /// <summary>
    /// Fits a WaterDryZone to a ship by slicing its hull. At several heights through the band
    /// the waves move in, every hull triangle is cut by a horizontal plane; the outline of
    /// those cuts is the hull's cross-section at that height. Sampled around a shared centre,
    /// those outlines give the zone a profile that follows the bow, the stern and the flare of
    /// the sides — what an ellipse sized from the model's bounds couldn't, since the bounds are
    /// mostly rigging.
    /// </summary>
    public static class DryZoneSetup
    {
        // Sliced from the keel up to this far above the water surface. The bottom has to be
        // the keel, not just below the waterline: the swim check reads the same zone, and a
        // hold deeper than the slices counted as lake. The top has to clear storm crests.
        const float SliceAbove = 5f;
        // Just above the keel's lowest vertex, where a slice still cuts something.
        const float KeelClearance = 0.05f;

        // Pulled in from the outer plating so the cut stays inside the hull's wall thickness
        // instead of showing as a sliver of missing water right against the hull.
        const float Inset = 0.15f;

        [MenuItem("WoW Sandbox/Add Water Dry Zone to Selection")]
        public static void AddToSelection()
        {
            var selected = Selection.activeGameObject;
            if (selected == null)
            {
                Debug.LogWarning("[DryZoneSetup] Select the ship first.");
                return;
            }

            // Re-fit an existing zone rather than stacking a second one on the same ship.
            var zone = selected.GetComponentInChildren<WaterDryZone>();
            var ship = zone != null && zone.gameObject == selected && selected.transform.parent != null
                ? selected.transform.parent.gameObject
                : selected;

            if (zone == null)
            {
                var go = new GameObject("WaterDryZone");
                Undo.RegisterCreatedObjectUndo(go, "Add Water Dry Zone");
                go.transform.SetParent(ship.transform, false);
                zone = go.AddComponent<WaterDryZone>();
            }
            else
            {
                Undo.RecordObject(zone, "Fit Water Dry Zone");
            }

            if (!FitToHull(zone, ship.transform))
                return;

            Selection.activeGameObject = zone.gameObject;
            EditorUtility.SetDirty(zone);
            EditorSceneManager.MarkSceneDirty(zone.gameObject.scene);
        }

        static bool FitToHull(WaterDryZone zone, Transform ship)
        {
            var hullFilters = HullFilters(ship);
            if (hullFilters.Count == 0)
            {
                Debug.LogWarning("[DryZoneSetup] No meshes under the selection to slice.", ship);
                return false;
            }

            var water = Object.FindFirstObjectByType<WaterVolume>();
            float waterWorldY = water != null ? water.SurfaceY : zone.transform.position.y;
            float waterLocalY = zone.transform.InverseTransformPoint(new Vector3(0f, waterWorldY, 0f)).y;

            float keel = LowestPoint(hullFilters, zone.transform);
            if (float.IsInfinity(keel))
                return false;

            float bottom = Mathf.Min(keel + KeelClearance, waterLocalY - 1f);
            float top = waterLocalY + SliceAbove;

            var levels = new List<Vector2>[WaterDryZone.HullLevels];
            var heights = new float[WaterDryZone.HullLevels];
            for (int l = 0; l < levels.Length; l++)
            {
                levels[l] = new List<Vector2>();
                heights[l] = Mathf.Lerp(bottom, top, l / (float)(WaterDryZone.HullLevels - 1));
            }

            int triangles = 0;
            foreach (var filter in hullFilters)
            {
                var mesh = filter.sharedMesh;
                Vector3[] vertices = mesh.vertices;
                if (vertices.Length == 0)
                {
                    Debug.LogWarning($"[DryZoneSetup] Couldn't read the vertices of {mesh.name}. Select its " +
                                     "model in the Project window, tick Read/Write in the Model import " +
                                     "settings, Apply, and run this again.", mesh);
                    return false;
                }

                // Into the zone's local space, where the profile is stored.
                Matrix4x4 toZone = zone.transform.worldToLocalMatrix * filter.transform.localToWorldMatrix;
                for (int i = 0; i < vertices.Length; i++)
                    vertices[i] = toZone.MultiplyPoint3x4(vertices[i]);

                int[] indices = mesh.triangles;
                for (int t = 0; t < indices.Length; t += 3)
                {
                    Vector3 a = vertices[indices[t]], b = vertices[indices[t + 1]], c = vertices[indices[t + 2]];
                    for (int l = 0; l < levels.Length; l++)
                    {
                        float y = heights[l];
                        AddCrossing(a, b, y, levels[l]);
                        AddCrossing(b, c, y, levels[l]);
                        AddCrossing(c, a, y, levels[l]);
                    }
                }

                triangles += indices.Length / 3;
            }

            var outlines = levels.Select(ConvexHull).ToArray();

            // One centre for every slice, taken from the waterline slice — the one that
            // matters most — so the slices line up when interpolated between.
            int waterlineLevel = Mathf.RoundToInt(Mathf.InverseLerp(bottom, top, waterLocalY) * (WaterDryZone.HullLevels - 1));
            var reference = outlines[waterlineLevel].Count >= 3
                ? outlines[waterlineLevel]
                : outlines.OrderByDescending(o => o.Count).First();

            if (reference.Count < 3)
            {
                Debug.LogWarning("[DryZoneSetup] The hull doesn't cross the water surface — is the ship " +
                                 $"sitting at the water? (water Y = {waterWorldY:F2})", ship);
                return false;
            }

            Vector2 centre = Centroid(reference);
            var radii = new float[WaterDryZone.HullLevels * WaterDryZone.HullSamples];

            for (int l = 0; l < outlines.Length; l++)
            {
                for (int s = 0; s < WaterDryZone.HullSamples; s++)
                {
                    float angle = s / (float)WaterDryZone.HullSamples * Mathf.PI * 2f;
                    var direction = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
                    float reach = RayToOutline(centre, direction, outlines[l]);
                    radii[l * WaterDryZone.HullSamples + s] = Mathf.Max(reach - Inset, 0f);
                }
            }

            zone.shape = WaterDryZone.Shape.Hull;
            zone.hullCentre = centre;
            zone.hullBottom = bottom;
            zone.hullTop = top;
            zone.hullRadii = radii;

            Debug.Log($"[DryZoneSetup] Fitted the dry zone to {triangles} hull triangles at water Y " +
                      $"{waterWorldY:F2}. Re-run this if you move the ship up or down.", zone);
            return true;
        }

        /// <summary>
        /// The hull itself, not the doodads hung on it: the meshes from whichever source model
        /// contributes the most vertices. A lantern or an anchor near the waterline would
        /// otherwise bulge the outline out into open water.
        /// </summary>
        static List<MeshFilter> HullFilters(Transform ship)
        {
            var filters = ship.GetComponentsInChildren<MeshFilter>()
                .Where(f => f.sharedMesh != null && f.GetComponentInParent<WaterDryZone>() == null)
                .ToList();

            var bySource = filters.GroupBy(f => AssetDatabase.GetAssetPath(f.sharedMesh));
            var hull = bySource.OrderByDescending(g => g.Sum(f => f.sharedMesh.vertexCount)).FirstOrDefault();
            return hull != null ? hull.ToList() : new List<MeshFilter>();
        }

        /// <summary>The hull's lowest vertex in the zone's local space; +Infinity if unreadable.</summary>
        static float LowestPoint(List<MeshFilter> filters, Transform zone)
        {
            float lowest = float.PositiveInfinity;
            foreach (var filter in filters)
            {
                Vector3[] vertices = filter.sharedMesh.vertices;
                if (vertices.Length == 0)
                {
                    Debug.LogWarning($"[DryZoneSetup] Couldn't read the vertices of {filter.sharedMesh.name}. " +
                                     "Tick Read/Write on its model's import settings, Apply, and run this again.",
                                     filter.sharedMesh);
                    return float.PositiveInfinity;
                }

                Matrix4x4 toZone = zone.worldToLocalMatrix * filter.transform.localToWorldMatrix;
                foreach (var vertex in vertices)
                    lowest = Mathf.Min(lowest, toZone.MultiplyPoint3x4(vertex).y);
            }

            return lowest;
        }

        static void AddCrossing(Vector3 a, Vector3 b, float y, List<Vector2> points)
        {
            if ((a.y - y) * (b.y - y) > 0f || Mathf.Approximately(a.y, b.y))
                return;

            float t = (y - a.y) / (b.y - a.y);
            Vector3 p = Vector3.Lerp(a, b, t);
            points.Add(new Vector2(p.x, p.z));
        }

        /// <summary>Andrew's monotone chain. Interior decks and bulkheads drop out.</summary>
        static List<Vector2> ConvexHull(List<Vector2> points)
        {
            var sorted = points.Distinct().OrderBy(p => p.x).ThenBy(p => p.y).ToList();
            if (sorted.Count < 3)
                return sorted;

            var hull = new List<Vector2>();
            for (int pass = 0; pass < 2; pass++)
            {
                int start = hull.Count;
                foreach (var p in sorted)
                {
                    while (hull.Count >= start + 2 && Cross(hull[hull.Count - 2], hull[hull.Count - 1], p) <= 0f)
                        hull.RemoveAt(hull.Count - 1);
                    hull.Add(p);
                }

                hull.RemoveAt(hull.Count - 1);
                sorted.Reverse();
            }

            return hull;
        }

        static float Cross(Vector2 o, Vector2 a, Vector2 b) => (a.x - o.x) * (b.y - o.y) - (a.y - o.y) * (b.x - o.x);

        static Vector2 Centroid(List<Vector2> polygon)
        {
            float area = 0f;
            Vector2 sum = Vector2.zero;
            for (int i = 0; i < polygon.Count; i++)
            {
                Vector2 a = polygon[i], b = polygon[(i + 1) % polygon.Count];
                float cross = a.x * b.y - b.x * a.y;
                area += cross;
                sum += (a + b) * cross;
            }

            return Mathf.Abs(area) < 1e-5f ? polygon.Aggregate(Vector2.zero, (s, p) => s + p) / polygon.Count
                                          : sum / (3f * area);
        }

        /// <summary>Distance from origin along direction to the outline's far side, or 0.</summary>
        static float RayToOutline(Vector2 origin, Vector2 direction, List<Vector2> outline)
        {
            if (outline.Count < 3)
                return 0f;

            float best = 0f;
            for (int i = 0; i < outline.Count; i++)
            {
                Vector2 a = outline[i], b = outline[(i + 1) % outline.Count];
                Vector2 edge = b - a;
                float denominator = direction.x * edge.y - direction.y * edge.x;
                if (Mathf.Abs(denominator) < 1e-6f)
                    continue;

                Vector2 toA = a - origin;
                float t = (toA.x * edge.y - toA.y * edge.x) / denominator;   // along the ray
                float u = (toA.x * direction.y - toA.y * direction.x) / denominator;  // along the edge
                if (t >= 0f && u >= 0f && u <= 1f)
                    best = Mathf.Max(best, t);
            }

            return best;
        }
    }
}
