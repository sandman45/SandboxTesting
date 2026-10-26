using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace WowSandbox.EditorTools
{
    /// <summary>
    /// Puts a merchant on the beach: the bearded pirate export as a quartermaster, standing
    /// on the shore nearest the ship and facing out to it, with a Vendor, a Friendly Health
    /// (so he can be clicked and targeted, not fought) and an animator with his Stand idle
    /// plus Wave and Talk. Gives the player a VendorPanel to trade through. Running it again
    /// with a vendor already in the scene just selects him — move him wherever you like.
    /// </summary>
    public static class VendorSetup
    {
        const string ModelPath = "Assets/WowExports/character/human/bearded-pirate/humanmale_hd.gltf";
        const string GeneratedDir = "Assets/WowExports/_Generated/Quartermaster";
        const string VendorName = "Quartermaster Briggs";
        /// <summary>Same -X facing convention as every other M2; see WarriorSetup.ModelYawOffset.</summary>
        const float ModelYawOffset = 90f;

        [MenuItem("WoW Sandbox/Add Vendor")]
        static void AddVendor()
        {
            var player = Object.FindAnyObjectByType<Inventory>();
            if (player == null)
            {
                Debug.LogError("[VendorSetup] No player with an Inventory — run Spawn Warrior Player first.");
                return;
            }
            if (player.GetComponent<VendorPanel>() == null)
                Undo.AddComponent<VendorPanel>(player.gameObject);

            var existing = Object.FindAnyObjectByType<Vendor>();
            if (existing != null)
            {
                AddTalkGestures(existing);
                Selection.activeGameObject = existing.gameObject;
                EditorSceneManager.MarkSceneDirty(player.gameObject.scene);
                Debug.Log("[VendorSetup] There's already a vendor — gave him any talk gestures he was missing and selected him. Delete him first to place a fresh one.", existing);
                return;
            }

            var model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
            if (model == null)
            {
                Debug.LogError($"[VendorSetup] No model at {ModelPath}. Export the bearded pirate from wow.export as glTF there.");
                return;
            }

            var controller = M2AnimatorBuilder.BuildLocomotion(ModelPath, GeneratedDir);
            if (controller != null)
            {
                M2AnimatorBuilder.AddOneShots(controller, ModelPath, GeneratedDir, ("Wave", "EmoteWave"));
                M2AnimatorBuilder.AddOneShots(controller, ModelPath, GeneratedDir, M2AnimatorBuilder.TalkGestures);
            }

            Shore(player.transform.position, out Vector3 spot, out Vector3 facing);

            var root = new GameObject(VendorName);
            Undo.RegisterCreatedObjectUndo(root, "Add Vendor");
            root.transform.SetPositionAndRotation(spot, Quaternion.LookRotation(facing, Vector3.up));

            var instance = (GameObject)PrefabUtility.InstantiatePrefab(model, root.transform);
            instance.transform.localPosition = Vector3.zero;
            instance.transform.localRotation = Quaternion.Euler(0f, ModelYawOffset, 0f);
            float height = MeasureHeight(instance);
            Vendor.HideEyeGlow(instance);

            // Something for TargetingController's click to hit.
            var capsule = root.AddComponent<CapsuleCollider>();
            capsule.height = height;
            capsule.radius = Mathf.Max(height * 0.18f, 0.1f);
            capsule.center = new Vector3(0f, height * 0.5f, 0f);

            var health = root.AddComponent<Health>();
            health.reaction = Reaction.Friendly;
            root.AddComponent<Vendor>();

            if (controller != null)
            {
                var animator = instance.GetComponentInChildren<Animator>();
                if (animator == null)
                    animator = instance.AddComponent<Animator>();
                animator.runtimeAnimatorController = controller;
                animator.applyRootMotion = false;
            }

            // Character exports leave any equipped helm (his hat) at the feet — put it on his head.
            HelmFitter.Fit(root, instance);

            Selection.activeGameObject = root;
            EditorSceneManager.MarkSceneDirty(root.scene);
            Debug.Log($"[VendorSetup] Placed {VendorName} on the shore facing the ship, and gave the player a " +
                      "VendorPanel. Move him if the spot's wrong, then save the scene.", root);
        }

        /// <summary>
        /// On the beach nearest the ship: walk in from the ship toward the island's middle until
        /// the ground rises out of the water, then a few steps further up the sand. Facing back
        /// out to sea. Without a ship, a few steps from the player.
        /// </summary>
        static void Shore(Vector3 playerPosition, out Vector3 spot, out Vector3 facing)
        {
            var terrain = Terrain.activeTerrain;
            var ship = Object.FindAnyObjectByType<ShipBuoyancy>();
            var water = Object.FindAnyObjectByType<WaterVolume>();
            spot = playerPosition + Vector3.forward * 4f;
            facing = Vector3.back;
            if (terrain == null || ship == null || water == null)
                return;

            var size = terrain.terrainData.size;
            var middle = terrain.GetPosition() + new Vector3(size.x * 0.5f, 0f, size.z * 0.5f);
            var inland = middle - ship.transform.position;
            inland.y = 0f;
            if (inland.sqrMagnitude < 1f)
                return;
            inland.Normalize();

            float Ground(Vector3 p) => terrain.SampleHeight(p) + terrain.GetPosition().y;
            var point = ship.transform.position;
            for (int i = 0; i < 2000; i++)
            {
                point += inland;
                if (Ground(point) > water.SurfaceY + 0.4f)
                {
                    point += inland * 4f;
                    spot = new Vector3(point.x, Ground(point), point.z);
                    facing = -inland;
                    return;
                }
            }
        }

        /// <summary>Adds the talk gestures to an already-placed vendor's animator, if it lacks them.</summary>
        static void AddTalkGestures(Vendor vendor)
        {
            var animator = vendor.GetComponentInChildren<Animator>();
            if (animator != null && animator.runtimeAnimatorController is UnityEditor.Animations.AnimatorController controller)
                M2AnimatorBuilder.AddOneShots(controller, ModelPath, GeneratedDir, M2AnimatorBuilder.TalkGestures);
        }

        static float MeasureHeight(GameObject instance)
        {
            var renderers = instance.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0)
                return 1.8f;
            var bounds = renderers[0].bounds;
            foreach (var renderer in renderers)
                bounds.Encapsulate(renderer.bounds);
            return Mathf.Max(bounds.size.y, 0.1f);
        }
    }
}
