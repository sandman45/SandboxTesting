using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace WowSandbox.EditorTools
{
    /// <summary>
    /// Wrecks a ship on the beach: the md_shipwreck WMO pieces (the broken hull with its sand
    /// drifts, and the stern) half-buried at the waterline on the far side of the island from
    /// the pirate ship and the vendor, with colliders, and the NavMesh re-baked round them.
    ///
    /// The pieces are dungeon-sized — the hull is ~97 units long at full size — so they go in
    /// scaled down. Running it again replaces the last wreck; move or rotate it freely after.
    /// </summary>
    public class ShipwreckSetup : EditorWindow
    {
        const string HullPath = "Assets/WowExports/world/wmo/dungeon/md_shipwreck/shipwreck_a.gltf";
        const string SternPath = "Assets/WowExports/world/wmo/dungeon/md_shipwreck/shipwreck_dback.gltf";
        const string GroupName = "Shipwreck";

        bool _hull = true;
        bool _stern = true;
        float _scale = 0.5f;
        float _sink = 0.3f;
        float _bearing;

        [MenuItem("WoW Sandbox/Add Shipwreck")]
        public static void ShowWindow()
        {
            var window = GetWindow<ShipwreckSetup>(true, "Add Shipwreck");
            window.minSize = new Vector2(360f, 220f);
            window._bearing = window.DefaultBearing();
        }

        void OnGUI()
        {
            EditorGUILayout.HelpBox(
                "Places the wreck half-buried where the beach meets the sea, along Bearing from " +
                "the island's centre (0 = north, 90 = east). Starts opposite the pirate ship. " +
                "Re-running replaces the last wreck.", MessageType.None);

            EditorGUILayout.Space();
            _hull = EditorGUILayout.Toggle("Broken hull", _hull);
            _stern = EditorGUILayout.Toggle("Stern", _stern);
            _scale = EditorGUILayout.Slider("Scale", _scale, 0.1f, 1.5f);
            _sink = EditorGUILayout.Slider("Buried", _sink, 0f, 0.8f);
            _bearing = EditorGUILayout.Slider("Bearing", _bearing, 0f, 360f);

            EditorGUILayout.Space();
            using (new EditorGUI.DisabledScope(!_hull && !_stern))
            {
                if (GUILayout.Button("Add", GUILayout.Height(30f)))
                    Add();
            }
        }

        /// <summary>The compass bearing from the island's centre away from the ship.</summary>
        float DefaultBearing()
        {
            var terrain = Terrain.activeTerrain;
            var ship = Object.FindAnyObjectByType<ShipBuoyancy>();
            if (terrain == null || ship == null)
                return 180f;
            var away = Centre(terrain) - ship.transform.position;
            return Mathf.Repeat(Mathf.Atan2(away.x, away.z) * Mathf.Rad2Deg, 360f);
        }

        static Vector3 Centre(Terrain terrain)
        {
            var size = terrain.terrainData.size;
            return terrain.GetPosition() + new Vector3(size.x * 0.5f, 0f, size.z * 0.5f);
        }

        void Add()
        {
            var terrain = Terrain.activeTerrain;
            var water = Object.FindAnyObjectByType<WaterVolume>();
            if (terrain == null || water == null)
            {
                Debug.LogError("[ShipwreckSetup] Needs a Terrain and a WaterVolume (Setup Water).");
                return;
            }

            var outward = Quaternion.Euler(0f, _bearing, 0f) * Vector3.forward;
            if (!Waterline(terrain, water, outward, out var shore))
            {
                Debug.LogError("[ShipwreckSetup] No beach along that bearing — the land never meets the sea. Try another bearing.");
                return;
            }

            var old = GameObject.Find(GroupName);
            if (old != null)
                Undo.DestroyObjectImmediate(old);
            var group = new GameObject(GroupName);
            Undo.RegisterCreatedObjectUndo(group, "Add Shipwreck");
            group.transform.position = shore;

            // Lying along the beach, the stern a little further along it.
            var along = Vector3.Cross(Vector3.up, outward);
            int colliders = 0;
            if (_hull)
                colliders += Place(HullPath, group.transform, shore, Quaternion.LookRotation(along) * Quaternion.Euler(0f, 0f, 6f), terrain);
            if (_stern)
            {
                var spot = shore + along * (_hull ? 40f * _scale : 0f) + outward * 6f * _scale;
                colliders += Place(SternPath, group.transform, spot,
                                   Quaternion.LookRotation(-outward) * Quaternion.Euler(-8f, 20f, -12f), terrain);
            }

            ChickenSetup.BakeNavMesh();
            Selection.activeGameObject = group;
            EditorSceneManager.MarkSceneDirty(group.scene);
            Debug.Log($"[ShipwreckSetup] Wrecked a ship at {shore} ({colliders} colliders) and re-baked the NavMesh. " +
                      "Move it if the spot's wrong, then save the scene.", group);
        }

        /// <summary>Walks out from the island's centre along <paramref name="outward"/> until the ground drops under the sea.</summary>
        static bool Waterline(Terrain terrain, WaterVolume water, Vector3 outward, out Vector3 point)
        {
            var size = terrain.terrainData.size;
            point = Centre(terrain);
            for (float d = 0f; d < Mathf.Max(size.x, size.z); d += 1f)
            {
                var probe = Centre(terrain) + outward * d;
                float ground = terrain.GetPosition().y + terrain.SampleHeight(probe);
                if (ground < water.SurfaceY)
                {
                    point = new Vector3(probe.x, ground, probe.z);
                    return true;
                }
            }
            return false;
        }

        int Place(string path, Transform parent, Vector3 position, Quaternion rotation, Terrain terrain)
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (model == null)
            {
                Debug.LogError($"[ShipwreckSetup] No model at {path}. Export md_shipwreck's pieces from wow.export there.");
                return 0;
            }

            var instance = (GameObject)PrefabUtility.InstantiatePrefab(model, parent);
            instance.transform.SetPositionAndRotation(position, rotation);
            instance.transform.localScale = Vector3.one * _scale;

            // Bottom on the ground, then pushed down into the sand by the Buried fraction.
            var bounds = MeasureBounds(instance);
            float ground = terrain.GetPosition().y + terrain.SampleHeight(position);
            instance.transform.position += Vector3.up * (ground - bounds.min.y - bounds.size.y * _sink);

            return MeshColliderTool.AddCollidersTo(instance);
        }

        static Bounds MeasureBounds(GameObject instance)
        {
            var renderers = instance.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0)
                return new Bounds(instance.transform.position, Vector3.one);
            var bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++)
                bounds.Encapsulate(renderers[i].bounds);
            return bounds;
        }
    }
}
