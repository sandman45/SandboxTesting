using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;

namespace WowSandbox.EditorTools
{
    /// <summary>
    /// Adds colliders to imported geometry. wow.export models arrive as plain renderers with
    /// no colliders, so buildings, trees and rocks are all walk-through until this is run.
    ///
    /// Two shapes of model have to be handled, and they are not the same job:
    ///
    /// - **WMOs** (gnomehut) carry no skeleton, so they import as MeshFilter + MeshRenderer.
    ///   A WMO is many separate group meshes — gnomehut has Ext0-5 and Int0-10 — so this
    ///   walks the whole hierarchy rather than expecting one mesh at the root.
    ///
    /// - **M2 doodads** (boulders, palm trees) always carry a skeleton, even when they are
    ///   scenery that never moves. Their geosets have JOINTS_0/WEIGHTS_0 and import as
    ///   SkinnedMeshRenderers with **no MeshFilter**, which is why a MeshFilter-only sweep
    ///   silently misses every tree and rock in the scene. Their single animation has zero
    ///   channels, so the mesh never actually deforms — baking the rest pose gives an exact
    ///   collider.
    ///
    /// Foliage is skipped, but **only on M2 doodads**. A palm arrives as a "_wood_" geoset and
    /// a "_fronds_" one, and colliding with the fronds would put invisible walls out in the
    /// air wherever the leaf cards hang — WoW itself only collides the trunk. WMOs are exempt
    /// from that rule because their "leaves" are architecture: 12tr_amani_hut01's roof is
    /// built from mat_12tr_amani_leafy_roof_01 and mat_12tr_amani_leafs_01, and skipping those
    /// drops you straight through the hut's roof.
    /// </summary>
    public static class MeshColliderTool
    {
        /// <summary>
        /// Baked collider meshes are WoW-derived, so they belong under the gitignored
        /// WowExports tree with the rest of the generated content.
        /// </summary>
        const string BakedRoot = "Assets/WowExports/_Generated/Colliders";

        /// <summary>
        /// Matched against material names, which is where the geoset's identity actually
        /// lives — the GameObject is only ever called "..._Geoset0".
        /// </summary>
        static readonly string[] FoliageHints =
        {
            "frond", "leaf", "leaves", "canopy", "branch", "bush", "grass", "vine", "foliage"
        };

        struct Result
        {
            public int Added, Skipped, Foliage, Unreadable, Baked;
        }

        [MenuItem("WoW Sandbox/Add Mesh Colliders to Selection", true)]
        static bool ValidateAddColliders() => Selection.gameObjects.Length > 0;

        [MenuItem("WoW Sandbox/Add Mesh Colliders to Selection")]
        static void AddColliders() => Report("selection", AddCollidersTo(Selection.gameObjects));

        /// <summary>
        /// Sweeps the whole open scene rather than the selection. Placing thirty doodads by
        /// hand and then remembering to select all thirty is the step that actually gets
        /// missed — and this is idempotent, so it's safe to re-run after every batch.
        /// </summary>
        [MenuItem("WoW Sandbox/Add Colliders to All Scenery")]
        static void AddCollidersToScenery()
        {
            var roots = Object.FindObjectsByType<Transform>(FindObjectsInactive.Include,
                                                            FindObjectsSortMode.None)
                              .Where(t => t.parent == null && IsScenery(t))
                              .Select(t => t.gameObject)
                              .ToArray();

            Report("scene", AddCollidersTo(roots));
            Debug.Log("[MeshColliderTool] Colliders stop the player; the NavMesh stops the NPCs. " +
                      "Re-run WoW Sandbox → Bake NavMesh as well if you've moved anything.");
        }

        /// <summary>
        /// Anything a player should bump into, as opposed to walk or swim through.
        ///
        /// Deliberately keyed on CharacterController and NavMeshAgent rather than on Animator:
        /// glTFast puts an Animator on every M2, scenery included, so excluding by Animator
        /// would throw away exactly the trees and rocks this is meant to catch.
        /// </summary>
        static bool IsScenery(Transform root)
        {
            if (root.GetComponentInChildren<WaterVolume>(true) != null)
                return false;
            if (root.GetComponentInChildren<SkyDomeFollow>(true) != null)
                return false;
            if (root.GetComponentInChildren<CharacterController>(true) != null)
                return false;
            if (root.GetComponentInChildren<NavMeshAgent>(true) != null)
                return false;

            return true;
        }

        static Result AddCollidersTo(GameObject[] roots)
        {
            var result = new Result();
            // One baked mesh per source mesh: ten copies of mz_boulder07 all share it.
            var bakeCache = new Dictionary<Mesh, Mesh>();

            foreach (var root in roots)
            {
                // Include inactive children — WMO groups are often toggled off.
                foreach (var filter in root.GetComponentsInChildren<MeshFilter>(true))
                    AddStaticCollider(filter.gameObject, filter.sharedMesh, ref result);

                foreach (var skinned in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                    AddSkinnedCollider(skinned, bakeCache, ref result);
            }

            if (result.Baked > 0)
                AssetDatabase.SaveAssets();

            return result;
        }

        static void AddStaticCollider(GameObject target, Mesh mesh, ref Result result)
        {
            if (mesh == null || !ShouldCollide(target, ref result))
                return;

            if (!mesh.isReadable)
            {
                Debug.LogWarning($"[MeshColliderTool] \"{mesh.name}\" is not readable — physics " +
                                 "can't bake it. Enable Read/Write on the importer.", target);
                result.Unreadable++;
                return;
            }

            Undo.AddComponent<MeshCollider>(target).sharedMesh = mesh;
            result.Added++;
        }

        /// <summary>
        /// A SkinnedMeshRenderer's sharedMesh sits in bind space, so handing it straight to a
        /// MeshCollider can leave the collider offset from the model. BakeMesh returns the
        /// mesh as currently posed, in the renderer's own local space, which is exactly what
        /// the collider wants. Scale is left off because the MeshCollider already inherits it
        /// from the transform — baking it in too would apply it twice.
        /// </summary>
        static void AddSkinnedCollider(SkinnedMeshRenderer skinned, Dictionary<Mesh, Mesh> cache,
                                       ref Result result)
        {
            var source = skinned.sharedMesh;
            if (source == null || !ShouldCollide(skinned.gameObject, ref result))
                return;

            // The foliage rule lives here rather than in ShouldCollide so it applies to M2
            // doodads only. On a WMO the same words mean architecture, not leaf cards.
            if (IsFoliage(skinned))
            {
                result.Foliage++;
                return;
            }

            if (!source.isReadable)
            {
                Debug.LogWarning($"[MeshColliderTool] \"{source.name}\" is not readable, so its " +
                                 "rest pose can't be baked. Enable Read/Write on the importer.",
                                 skinned);
                result.Unreadable++;
                return;
            }

            if (!cache.TryGetValue(source, out Mesh baked))
            {
                string path = $"{BakedRoot}/{BakedName(skinned, source)}.asset";
                baked = AssetDatabase.LoadAssetAtPath<Mesh>(path);

                if (baked == null)
                {
                    Directory.CreateDirectory(BakedRoot);
                    baked = new Mesh { name = source.name + "_collider" };
                    skinned.BakeMesh(baked, useScale: false);
                    AssetDatabase.CreateAsset(baked, path);
                    result.Baked++;
                }

                cache[source] = baked;
            }

            Undo.AddComponent<MeshCollider>(skinned.gameObject).sharedMesh = baked;
            result.Added++;
        }

        /// <summary>
        /// Stable across runs, so re-running reuses the baked asset instead of piling up
        /// copies. Keyed on the source glTF plus the mesh, since glTFast leaves some geoset
        /// meshes unnamed.
        /// </summary>
        static string BakedName(SkinnedMeshRenderer skinned, Mesh source)
        {
            string assetPath = AssetDatabase.GetAssetPath(source);
            string model = string.IsNullOrEmpty(assetPath)
                ? skinned.transform.root.name
                : Path.GetFileNameWithoutExtension(assetPath);

            string mesh = string.IsNullOrEmpty(source.name) ? skinned.gameObject.name : source.name;
            string combined = $"{model}_{mesh}";

            foreach (char invalid in Path.GetInvalidFileNameChars())
                combined = combined.Replace(invalid, '_');

            return combined;
        }

        static bool ShouldCollide(GameObject target, ref Result result)
        {
            if (target.GetComponent<MeshCollider>() != null)
            {
                result.Skipped++;
                return false;
            }

            return true;
        }

        /// <summary>
        /// True when every material on the renderer looks like leaf geometry. "Every" rather
        /// than "any" matters: a single-geoset model that merges trunk and leaves into one
        /// mesh still needs its collider, or the whole tree becomes walk-through.
        /// </summary>
        static bool IsFoliage(Renderer renderer)
        {
            if (renderer == null)
                return false;

            var materials = renderer.sharedMaterials;
            if (materials == null || materials.Length == 0)
                return false;

            foreach (var material in materials)
            {
                if (material == null)
                    return false;

                string name = material.name.ToLowerInvariant();
                if (!FoliageHints.Any(hint => name.Contains(hint)))
                    return false;
            }

            return true;
        }

        static void Report(string scope, Result result)
        {
            Debug.Log($"[MeshColliderTool] {scope}: added {result.Added} collider(s) " +
                      $"({result.Baked} rest poses baked for skinned doodads); " +
                      $"{result.Skipped} already had one; {result.Foliage} skipped as foliage; " +
                      $"{result.Unreadable} unreadable." +
                      (result.Foliage > 0
                          ? " Foliage is left walk-through on purpose — the trunk carries the collision."
                          : string.Empty));
        }
    }
}
