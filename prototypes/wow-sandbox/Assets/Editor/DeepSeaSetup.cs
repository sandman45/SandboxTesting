using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace WowSandbox.EditorTools
{
    /// <summary>
    /// Turns on the deadly open sea: places the Ancient Leviathan hidden in the scene and
    /// gives the player DeepSeaDanger. Spawn some hostile sharks first (Spawn Sea Creatures)
    /// — they're what DeepSeaDanger copies when it summons a pack.
    /// </summary>
    public static class DeepSeaSetup
    {
        const string LeviathanPath = "Assets/WowExports/creatures/AncientLeviathan.gltf";
        const string ObjectName = "AncientLeviathan";
        /// <summary>Same -X facing convention as every other M2; see WarriorSetup.ModelYawOffset.</summary>
        const float ModelYawOffset = 90f;

        [MenuItem("WoW Sandbox/Setup Deep Sea Danger")]
        static void Setup()
        {
            var player = Object.FindAnyObjectByType<HealthController>();
            if (player == null)
            {
                Debug.LogError("[DeepSeaSetup] No player in the scene — run Spawn Warrior Player first.");
                return;
            }

            if (player.GetComponent<DeepSeaDanger>() == null)
                Undo.AddComponent<DeepSeaDanger>(player.gameObject);

            PlaceLeviathan();
            EditorSceneManager.MarkSceneDirty(player.gameObject.scene);

            if (Object.FindAnyObjectByType<SwimmingCreature>() == null)
                Debug.LogWarning("[DeepSeaSetup] No sea creatures yet — spawn hostile sharks with Spawn Sea " +
                                 "Creatures, or the open sea will only ever send the leviathan.");
        }

        static void PlaceLeviathan()
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(LeviathanPath);
            if (model == null)
            {
                Debug.LogWarning($"[DeepSeaSetup] No leviathan at {LeviathanPath} — the open sea will only send sharks.");
                return;
            }

            var existing = Object.FindAnyObjectByType<Leviathan>(FindObjectsInactive.Include);
            if (existing != null)
                Undo.DestroyObjectImmediate(existing.gameObject);

            var controller = M2AnimatorBuilder.BuildSwimming(LeviathanPath, "Assets/WowExports/_Generated/AncientLeviathan");

            var root = new GameObject(ObjectName);
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(model, root.transform);
            instance.transform.localRotation = Quaternion.Euler(0f, ModelYawOffset, 0f);
            instance.transform.localPosition = Vector3.zero;

            // Centre the body on the root, so Leviathan can aim its jaws by pointing forward.
            var renderers = instance.GetComponentsInChildren<Renderer>();
            if (renderers.Length > 0)
            {
                var bounds = renderers[0].bounds;
                foreach (var renderer in renderers)
                    bounds.Encapsulate(renderer.bounds);
                instance.transform.localPosition = -bounds.center;
            }

            if (controller != null)
            {
                var animator = instance.GetComponentInChildren<Animator>();
                if (animator == null)
                    animator = instance.AddComponent<Animator>();
                animator.runtimeAnimatorController = controller;
                animator.applyRootMotion = false;
                // It's huge and mostly off screen; keep it animating when its bounds leave view.
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            }

            root.AddComponent<Leviathan>();
            root.SetActive(false);
            Undo.RegisterCreatedObjectUndo(root, "Setup Leviathan");
            Debug.Log("[DeepSeaSetup] Placed the Ancient Leviathan (hidden until it's called) and gave the " +
                      "player DeepSeaDanger. Swim out past the island's shelf to test, or use the " +
                      "component's ⋮ menu: Summon Sharks Now / Summon Leviathan Now.");
        }
    }
}
