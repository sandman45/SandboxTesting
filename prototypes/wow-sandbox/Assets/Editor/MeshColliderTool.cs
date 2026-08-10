using System.Linq;
using UnityEditor;
using UnityEngine;

namespace WowSandbox.EditorTools
{
    /// <summary>
    /// Adds MeshColliders to imported geometry. wow.export models arrive as plain
    /// renderers with no colliders, so buildings are walk-through until this is run.
    ///
    /// A WMO imports as many separate group meshes (gnomehut has Ext0-5 and Int0-10),
    /// so this walks the whole hierarchy rather than expecting a single mesh.
    /// </summary>
    public static class MeshColliderTool
    {
        [MenuItem("WoW Sandbox/Add Mesh Colliders to Selection", true)]
        static bool ValidateAddColliders() => Selection.gameObjects.Length > 0;

        [MenuItem("WoW Sandbox/Add Mesh Colliders to Selection")]
        static void AddColliders()
        {
            int added = 0, skipped = 0, unreadable = 0;

            foreach (var root in Selection.gameObjects)
            {
                // Include inactive children — WMO groups are often toggled off.
                foreach (var filter in root.GetComponentsInChildren<MeshFilter>(true))
                {
                    var mesh = filter.sharedMesh;
                    if (mesh == null)
                        continue;

                    if (filter.GetComponent<MeshCollider>() != null)
                    {
                        skipped++;
                        continue;
                    }

                    if (!mesh.isReadable)
                    {
                        Debug.LogWarning($"[MeshColliderTool] \"{mesh.name}\" is not readable — " +
                                         "physics can't bake it. Enable Read/Write on the importer.",
                                         filter);
                        unreadable++;
                        continue;
                    }

                    var collider = Undo.AddComponent<MeshCollider>(filter.gameObject);
                    collider.sharedMesh = mesh;
                    added++;
                }

                // Skinned meshes belong to characters; a MeshCollider there would be wrong
                // (it wouldn't follow the animation anyway), so flag rather than guess.
                if (root.GetComponentsInChildren<SkinnedMeshRenderer>(true).Any())
                {
                    Debug.Log($"[MeshColliderTool] \"{root.name}\" contains skinned meshes — skipped. " +
                              "Characters want a CharacterController or a capsule, not a MeshCollider.", root);
                }
            }

            Debug.Log($"[MeshColliderTool] Added {added} MeshCollider(s); " +
                      $"{skipped} already had one; {unreadable} unreadable.");
        }
    }
}
