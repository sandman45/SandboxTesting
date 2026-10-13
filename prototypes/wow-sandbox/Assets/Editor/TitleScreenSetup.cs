using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace WowSandbox.EditorTools
{
    /// <summary>
    /// Stands the player beside the shipwreck, where the game begins — the title screen
    /// circles them there, and it's the respawn point too. On the landward side of the wreck,
    /// clear of its colliders, facing it.
    /// </summary>
    public static class TitleScreenSetup
    {
        [MenuItem("WoW Sandbox/Place Player at Shipwreck")]
        static void PlacePlayer()
        {
            var player = Object.FindAnyObjectByType<WowCharacterController>();
            var wreck = GameObject.Find("Shipwreck");
            var terrain = Terrain.activeTerrain;
            if (player == null || wreck == null || terrain == null)
            {
                Debug.LogError("[TitleScreenSetup] Needs the player, a Terrain and the Shipwreck (WoW Sandbox → Add Shipwreck).");
                return;
            }

            var renderers = wreck.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0)
                return;
            var bounds = renderers[0].bounds;
            foreach (var renderer in renderers)
                bounds.Encapsulate(renderer.bounds);

            var size = terrain.terrainData.size;
            var middle = terrain.GetPosition() + new Vector3(size.x * 0.5f, 0f, size.z * 0.5f);
            var inland = middle - bounds.center;
            inland.y = 0f;
            inland.Normalize();

            var body = player.GetComponent<CharacterController>();
            float radius = body != null ? body.radius : 0.4f;
            float height = body != null ? body.height : 1.8f;

            // Walk inland from the wreck's middle until the warrior fits without touching it.
            var start = new Vector3(bounds.center.x, 0f, bounds.center.z);
            for (float step = 4f; step < 120f; step += 1f)
            {
                var spot = start + inland * step;
                spot.y = terrain.GetPosition().y + terrain.SampleHeight(spot) + 0.05f;
                if (Physics.CheckCapsule(spot + Vector3.up * (radius + 0.1f), spot + Vector3.up * (height - radius),
                        radius, ~0, QueryTriggerInteraction.Ignore))
                    continue;

                var toWreck = bounds.center - spot;
                toWreck.y = 0f;
                Undo.RecordObject(player.transform, "Place Player at Shipwreck");
                player.transform.SetPositionAndRotation(spot, Quaternion.LookRotation(toWreck));
                EditorSceneManager.MarkSceneDirty(player.gameObject.scene);
                Selection.activeGameObject = player.gameObject;
                Debug.Log("[TitleScreenSetup] Stood the player beside the wreck, facing it. Nudge them if the " +
                          "title shot needs it, then save the scene.", player);
                return;
            }

            Debug.LogError("[TitleScreenSetup] Couldn't find a clear spot inland of the wreck.");
        }
    }
}
