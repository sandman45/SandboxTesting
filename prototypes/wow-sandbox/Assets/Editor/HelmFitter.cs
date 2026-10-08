using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace WowSandbox.EditorTools
{
    /// <summary>
    /// Fits a wow.export character's helm onto its head and records the result in a
    /// HelmAttachment (see there for why the helm needs it).
    ///
    /// The export doesn't carry the head attachment's offset, so this measures it instead:
    /// every vertex weighted mostly to the head bone gives the head's extent, and the helm
    /// is centred over that front-to-back and side-to-side with its top level with the top
    /// of the head. On the Kul Tiran pirate the helm's front-to-back size matches the
    /// head's to the millimetre, which is a good sign this lines up with WoW's placement —
    /// but it is a fit, not WoW's own number.
    /// </summary>
    public static class HelmFitter
    {
        const string HeadBoneName = "bone_Head";
        const string HelmPrefix = "Head_Item";

        [MenuItem("WoW Sandbox/Fit Helms in Scene")]
        static void FitAllInScene()
        {
            int fitted = 0;
            foreach (var animator in Object.FindObjectsByType<Animator>())
            {
                var root = animator.transform.parent != null ? animator.transform.parent.gameObject : animator.gameObject;
                if (root.GetComponent<HelmAttachment>() == null && Fit(root, animator.gameObject))
                    fitted++;
            }
            Debug.Log($"[HelmFitter] Fitted helms on {fitted} character(s).");
        }

        /// <summary>
        /// Adds a HelmAttachment to <paramref name="root"/> if <paramref name="model"/> has a
        /// loose helm. Call while the model is still in its rest pose (straight after
        /// spawning). Returns whether there was a helm to fit.
        /// </summary>
        public static bool Fit(GameObject root, GameObject model)
        {
            var transforms = model.GetComponentsInChildren<Transform>(true);
            var head = transforms.FirstOrDefault(t => t.name == HeadBoneName);
            var pieces = transforms.Where(t => t.name.StartsWith(HelmPrefix) && t.GetComponent<MeshFilter>() != null).ToArray();
            if (head == null || pieces.Length == 0)
                return false;

            // Everything is measured in the model's own space, so the NPC's random facing
            // and the importer's axis conventions both drop out.
            var space = model.transform;
            if (!TryHeadBounds(model, head, space, out var headBounds))
            {
                Debug.LogWarning($"[HelmFitter] {root.name}: found a helm but no head vertices to fit it to.", root);
                return false;
            }

            var helmBounds = MeshBounds(pieces.Select(p => (p.GetComponent<MeshFilter>().sharedMesh, (Transform)p)), space);
            var shift = new Vector3(
                headBounds.center.x - helmBounds.center.x,
                headBounds.max.y - helmBounds.max.y,
                headBounds.center.z - helmBounds.center.z);
            Vector3 worldShift = space.TransformVector(shift);

            // Every piece shares one pose (they're submeshes of the same item at the same origin).
            var first = pieces[0];
            var attachment = Undo.AddComponent<HelmAttachment>(root);
            attachment.headBone = head;
            attachment.pieces = pieces;
            attachment.localPosition = head.InverseTransformPoint(first.position + worldShift);
            attachment.localRotation = Quaternion.Inverse(head.rotation) * first.rotation;
            attachment.localScale = Divide(first.lossyScale, head.lossyScale);
            return true;
        }

        static bool TryHeadBounds(GameObject model, Transform head, Transform space, out Bounds bounds)
        {
            var points = new List<Vector3>();
            foreach (var skinned in model.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                var mesh = skinned.sharedMesh;
                int headIndex = System.Array.IndexOf(skinned.bones, head);
                if (mesh == null || headIndex < 0)
                    continue;

                var vertices = mesh.vertices;
                var weights = mesh.boneWeights;
                for (int i = 0; i < weights.Length; i++)
                {
                    if (HeadWeight(weights[i], headIndex) > 0.5f)
                        points.Add(space.InverseTransformPoint(skinned.transform.TransformPoint(vertices[i])));
                }
            }

            bounds = Encapsulate(points);
            return points.Count > 0;
        }

        static float HeadWeight(BoneWeight w, int bone) =>
            (w.boneIndex0 == bone ? w.weight0 : 0f) + (w.boneIndex1 == bone ? w.weight1 : 0f) +
            (w.boneIndex2 == bone ? w.weight2 : 0f) + (w.boneIndex3 == bone ? w.weight3 : 0f);

        static Bounds MeshBounds(IEnumerable<(Mesh mesh, Transform transform)> meshes, Transform space)
        {
            var points = new List<Vector3>();
            foreach (var (mesh, transform) in meshes)
            {
                if (mesh == null)
                    continue;
                foreach (var vertex in mesh.vertices)
                    points.Add(space.InverseTransformPoint(transform.TransformPoint(vertex)));
            }
            return Encapsulate(points);
        }

        static Bounds Encapsulate(List<Vector3> points)
        {
            if (points.Count == 0)
                return default;
            var bounds = new Bounds(points[0], Vector3.zero);
            foreach (var point in points)
                bounds.Encapsulate(point);
            return bounds;
        }

        static Vector3 Divide(Vector3 a, Vector3 b) =>
            new(a.x / Mathf.Max(b.x, 1e-5f), a.y / Mathf.Max(b.y, 1e-5f), a.z / Mathf.Max(b.z, 1e-5f));
    }
}
