using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace WowSandbox.EditorTools
{
    /// <summary>
    /// Adds the ship's helm: an invisible click box (ShipHelm) with a spot to stand behind it,
    /// and ShipSailing on the ship. The wheel is part of the hull mesh, so there's no object to
    /// find — this puts the box on the raised deck at the stern as a first guess and selects it,
    /// for you to drag onto the wheel. Its forward (the yellow line) must point to the bow.
    /// Running it again with a helm already there just selects it, so a placed helm is kept.
    /// </summary>
    public static class ShipHelmSetup
    {
        [MenuItem("WoW Sandbox/Add Ship Helm")]
        static void AddHelm()
        {
            var ship = Object.FindAnyObjectByType<ShipBuoyancy>();
            if (ship == null)
            {
                Debug.LogWarning("[ShipHelmSetup] No ShipBuoyancy in the scene — the helm goes on the floating ship.");
                return;
            }

            if (ship.GetComponent<ShipSailing>() == null)
                Undo.AddComponent<ShipSailing>(ship.gameObject);

            var existing = ship.GetComponentInChildren<ShipHelm>(true);
            if (existing != null)
            {
                Selection.activeGameObject = existing.gameObject;
                Debug.Log("[ShipHelmSetup] The ship already has a helm — selected it. Delete it first to place a fresh one.", existing);
                return;
            }

            var water = Object.FindAnyObjectByType<WaterVolume>();
            var player = Object.FindAnyObjectByType<WowCharacterController>();
            var capsule = player != null ? player.GetComponent<CharacterController>() : null;
            float playerHeight = capsule != null ? capsule.height : 2f;
            float waterY = water != null ? water.SurfaceY : ship.transform.position.y;

            Physics.SyncTransforms();
            if (!ShipLadderSetup.ShipFrame(ship.transform, out Vector3 midship, out Vector3 across, out float halfBeam,
                                           out Vector3 along, out float halfLength))
            {
                Debug.LogWarning("[ShipHelmSetup] The ship has no renderers to measure.", ship);
                return;
            }

            Vector3 aft = ShipLadderSetup.SternDirection(ship.transform, midship, along, across, halfBeam, halfLength,
                                                         waterY, playerHeight);
            Vector3 spot = midship + aft * (halfLength * 0.6f);
            // Deck height from the side there, where the ladder setup's search works; the helm
            // goes on the centreline.
            spot.y = ShipLadderSetup.FindSide(ship.transform, spot, across, halfBeam, waterY, playerHeight,
                                              out _, out Vector3 deck)
                ? deck.y
                : ship.transform.position.y;

            var helmObject = new GameObject("ShipHelm");
            Undo.RegisterCreatedObjectUndo(helmObject, "Add Ship Helm");
            helmObject.transform.SetPositionAndRotation(spot, Quaternion.LookRotation(-aft, Vector3.up));
            helmObject.transform.SetParent(ship.transform, true);
            var lossy = ship.transform.lossyScale;
            helmObject.transform.localScale = new Vector3(1f / lossy.x, 1f / lossy.y, 1f / lossy.z);

            var box = helmObject.AddComponent<BoxCollider>();
            box.isTrigger = true;
            box.center = new Vector3(0f, playerHeight * 0.75f, 0f);
            box.size = new Vector3(playerHeight * 1.5f, playerHeight * 1.5f, playerHeight);

            var helm = helmObject.AddComponent<ShipHelm>();
            var stand = new GameObject("Stand").transform;
            stand.SetParent(helmObject.transform, false);
            stand.localPosition = new Vector3(0f, 0.05f, -playerHeight * 0.9f);
            helm.stand = stand;

            Selection.activeGameObject = helmObject;
            EditorSceneManager.MarkSceneDirty(ship.gameObject.scene);
            Debug.Log("[ShipHelmSetup] Added ShipHelm (selected) as a guess on the stern deck. Move it so the " +
                      "yellow box covers the wheel and the yellow line points to the bow, move its Stand child " +
                      "(cyan) to where you should stand behind the wheel, then save the scene.", helmObject);
        }
    }
}
