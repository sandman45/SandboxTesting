using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace WowSandbox.EditorTools
{
    /// <summary>
    /// WoW Sandbox → Add Dinghy: the old dinghy (Dinghy) where the Scene view is looking — on the
    /// beach. The stove-in boat (worgen_rowboat_broken01) lies there; the whole one
    /// (worgen_rowboat01) waits, hidden, at its launch spot straight out to sea from it, where
    /// the water's deep enough to float, bow to seaward. Re-running replaces it.
    /// </summary>
    public static class DinghySetup
    {
        const string Folder = "Assets/WowExports";
        const string BrokenFile = "worgen_rowboat_broken01";
        const string WholeFile = "worgen_rowboat01";
        const string Export = "world/expansion03/doodads/worgen/items/ (worgen_rowboat_broken01.m2 and worgen_rowboat01.m2, Models tab, glTF)";
        /// <summary>Same -X facing convention as every other M2; see WarriorSetup.ModelYawOffset.</summary>
        const float ModelYawOffset = 90f;
        // How deep the water must be where she's launched, and how far her keel sits under it.
        const float LaunchDepth = 1.1f;
        const float Keel = 0.25f;

        [MenuItem("WoW Sandbox/Add Dinghy")]
        static void Add()
        {
            var brokenModel = Find(BrokenFile);
            var wholeModel = Find(WholeFile);
            if (brokenModel == null || wholeModel == null)
            {
                Debug.LogError($"[DinghySetup] Export {Export} from wow.export first.");
                return;
            }
            var water = Object.FindAnyObjectByType<WaterVolume>();
            var terrain = Terrain.activeTerrain;
            if (water == null || terrain == null)
            {
                Debug.LogError("[DinghySetup] Needs a Terrain and a WaterVolume (Setup Water).");
                return;
            }
            if (!InteractableSetup.Ground(out var point, out _))
                return;

            var old = GameObject.Find("Dinghy");
            if (old != null && old.GetComponent<Dinghy>() != null)
                Undo.DestroyObjectImmediate(old);

            var size = terrain.terrainData.size;
            var middle = terrain.GetPosition() + new Vector3(size.x * 0.5f, 0f, size.z * 0.5f);
            var seaward = point - middle;
            seaward.y = 0f;
            seaward = seaward.sqrMagnitude > 0.01f ? seaward.normalized : Vector3.forward;

            var root = new GameObject("Dinghy");
            Undo.RegisterCreatedObjectUndo(root, "Add Dinghy");
            root.transform.position = point;

            // Stove in on the sand, lying along the beach.
            var broken = Holder("Broken", root.transform, point, Quaternion.LookRotation(Vector3.Cross(Vector3.up, seaward)));
            var brokenInstance = Model(brokenModel, broken);
            var bounds = InteractableSetup.RendererBounds(brokenInstance);
            float ground = terrain.GetPosition().y + terrain.SampleHeight(point);
            brokenInstance.transform.position += Vector3.up * (ground - bounds.min.y - bounds.size.y * 0.15f);
            MeshColliderTool.AddCollidersTo(brokenInstance);

            // Whole, afloat, out where it's deep enough.
            var launch = LaunchSpot(point, seaward, water);
            var boat = Holder("Boat", root.transform, launch, Quaternion.LookRotation(seaward));
            var wholeInstance = Model(wholeModel, boat);
            bounds = InteractableSetup.RendererBounds(wholeInstance);
            wholeInstance.transform.position += Vector3.up * (water.SurfaceY - Keel - bounds.min.y);
            bounds = InteractableSetup.RendererBounds(wholeInstance);
            var seat = new GameObject("Seat").transform;
            seat.SetParent(boat, false);
            seat.position = new Vector3(bounds.center.x, bounds.min.y + Keel, bounds.center.z);
            // A box to click; it doesn't block the swimmer or the boat's own passenger.
            var box = boat.gameObject.AddComponent<BoxCollider>();
            box.isTrigger = true;
            box.center = boat.InverseTransformPoint(bounds.center);
            box.size = new Vector3(bounds.size.x, bounds.size.y, bounds.size.z);
            boat.gameObject.SetActive(false);

            var dinghy = root.AddComponent<Dinghy>();
            dinghy.displayName = "Broken Dinghy";
            dinghy.range = 3f;
            dinghy.broken = broken;
            dinghy.boat = boat;
            dinghy.seat = seat;
            dinghy.halfLength = Mathf.Max(bounds.extents.x, bounds.extents.z);

            // It moves, so nothing in it may be static-batched in place.
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
                GameObjectUtility.SetStaticEditorFlags(t.gameObject, 0);

            Selection.activeGameObject = root;
            EditorSceneManager.MarkSceneDirty(root.scene);
            Debug.Log($"[DinghySetup] Placed the broken dinghy at {point} ({CompassQuest.SideOf(point)} side) and its launch " +
                      $"spot at {launch}. The whole Boat child is hidden until it's repaired: show it to check the bow faces " +
                      "+Z (turn its model if not) and the Seat sits in it, then hide it again and save the scene.", root);
        }

        /// <summary>Straight out to sea from <paramref name="from"/> until the water's LaunchDepth deep.</summary>
        static Vector3 LaunchSpot(Vector3 from, Vector3 seaward, WaterVolume water)
        {
            for (float d = 1f; d < 80f; d += 0.5f)
            {
                var probe = from + seaward * d;
                if (water.Covers(probe) && water.SurfaceY - Seabed.HeightAt(probe, water) >= LaunchDepth)
                    return new Vector3(probe.x, water.SurfaceY, probe.z);
            }
            Debug.LogWarning("[DinghySetup] No water deep enough straight out from there; put the Boat in the shallows by hand.");
            return new Vector3(from.x, water.SurfaceY, from.z) + seaward * 6f;
        }

        static Transform Holder(string name, Transform parent, Vector3 position, Quaternion rotation)
        {
            var holder = new GameObject(name).transform;
            holder.SetParent(parent, false);
            holder.SetPositionAndRotation(position, rotation);
            return holder;
        }

        static GameObject Model(GameObject model, Transform holder)
        {
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(model, holder);
            instance.transform.localPosition = Vector3.zero;
            instance.transform.localRotation = Quaternion.Euler(0f, ModelYawOffset, 0f);
            return instance;
        }

        /// <summary>The model by file name anywhere under WowExports, glTF first.</summary>
        static GameObject Find(string file)
        {
            GameObject found = null;
            foreach (var guid in AssetDatabase.FindAssets($"{file} t:GameObject", new[] { Folder }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (!string.Equals(Path.GetFileNameWithoutExtension(path), file, System.StringComparison.OrdinalIgnoreCase))
                    continue;
                if (found == null || path.EndsWith(".gltf"))
                    found = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            }
            return found;
        }
    }
}
