using Unity.AI.Navigation;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using UnityEngine.AI;

namespace WowSandbox.EditorTools
{
    /// <summary>
    /// Bakes a NavMesh for the scene and scatters wandering chickens on it.
    ///
    /// The hut doesn't need any special handling to be solid: NavMesh baking treats its
    /// walls as unwalkable, so agents have no path through them. That's separate from the
    /// MeshColliders the player needs — the navmesh stops the chickens, the colliders stop
    /// you, and both are wanted.
    /// </summary>
    public static class ChickenSetup
    {
        const string ModelPath = "Assets/WowExports/creature/chicken2/chicken2_white.gltf";
        const string GeneratedRoot = "Assets/WowExports/_Generated/Chicken";

        /// <summary>Same -X facing convention as the warrior; see WarriorSetup.ModelYawOffset.</summary>
        const float ModelYawOffset = 90f;

        const int ChickenCount = 8;
        const float ScatterRadius = 10f;

        [MenuItem("WoW Sandbox/Bake NavMesh")]
        public static void BakeNavMesh()
        {
            var surface = Object.FindFirstObjectByType<NavMeshSurface>();
            if (surface == null)
            {
                var go = new GameObject("NavMesh");
                surface = go.AddComponent<NavMeshSurface>();
                surface.collectObjects = CollectObjects.All;
                // Render meshes, not colliders — the terrain and the hut both have renderers,
                // and this works even if someone hasn't run the collider tool yet.
                surface.useGeometry = NavMeshCollectGeometry.RenderMeshes;
                Undo.RegisterCreatedObjectUndo(go, "Create NavMesh Surface");
            }

            surface.BuildNavMesh();
            EditorUtility.SetDirty(surface);
            Debug.Log("[ChickenSetup] NavMesh baked. Re-run this after moving the hut or regenerating terrain.");
        }

        [MenuItem("WoW Sandbox/Populate Chickens")]
        public static void PopulateChickens()
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
            if (model == null)
            {
                Debug.LogError($"[ChickenSetup] Could not load the chicken at {ModelPath}.");
                return;
            }

            if (Object.FindFirstObjectByType<NavMeshSurface>() == null)
                BakeNavMesh();

            // Scatter around whatever's selected (the hut, normally), else the origin.
            Vector3 centre = Selection.activeGameObject != null
                ? Selection.activeGameObject.transform.position
                : Vector3.zero;

            var controller = BuildAnimatorController();

            var flock = new GameObject("Chickens");
            Undo.RegisterCreatedObjectUndo(flock, "Populate Chickens");

            int placed = 0;
            for (int i = 0; i < ChickenCount; i++)
            {
                // Ring-ish scatter so they don't all pile up in the middle.
                float angle = (i / (float)ChickenCount) * Mathf.PI * 2f + Random.Range(-0.3f, 0.3f);
                float radius = Random.Range(ScatterRadius * 0.35f, ScatterRadius);
                Vector3 candidate = centre + new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius);

                if (!NavMesh.SamplePosition(candidate, out NavMeshHit hit, 12f, NavMesh.AllAreas))
                    continue;

                CreateChicken(model, controller, hit.position, flock.transform, i);
                placed++;
            }

            if (placed == 0)
            {
                Debug.LogError("[ChickenSetup] No NavMesh near the spawn point — nothing placed. " +
                               "Select the hut (or an object standing on the terrain) and try again.");
                Undo.DestroyObjectImmediate(flock);
                return;
            }

            Selection.activeGameObject = flock;
            Debug.Log($"[ChickenSetup] Placed {placed} chicken(s) around {centre}.");
        }

        static void CreateChicken(GameObject model, RuntimeAnimatorController controller,
                                  Vector3 position, Transform parent, int index)
        {
            var root = new GameObject($"Chicken_{index:00}");
            root.transform.SetParent(parent);
            root.transform.position = position;
            root.transform.rotation = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);

            var instance = (GameObject)PrefabUtility.InstantiatePrefab(model, root.transform);
            instance.transform.localPosition = Vector3.zero;
            instance.transform.localRotation = Quaternion.Euler(0f, ModelYawOffset, 0f);

            var agent = root.AddComponent<NavMeshAgent>();
            agent.speed = 1.1f;          // chickens potter about
            agent.angularSpeed = 400f;
            agent.acceleration = 6f;
            agent.radius = 0.2f;
            agent.height = 0.45f;
            agent.stoppingDistance = 0.1f;
            agent.autoBraking = true;

            root.AddComponent<ChickenWanderer>();

            if (controller != null)
            {
                var animator = instance.GetComponentInChildren<Animator>();
                if (animator == null)
                    animator = instance.AddComponent<Animator>();
                animator.runtimeAnimatorController = controller;
                animator.applyRootMotion = false;
            }
        }

        static AnimatorController BuildAnimatorController() =>
            M2AnimatorBuilder.BuildLocomotion(ModelPath, GeneratedRoot);
    }
}
