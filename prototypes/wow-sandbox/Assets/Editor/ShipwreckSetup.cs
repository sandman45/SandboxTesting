using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace WowSandbox.EditorTools
{
    /// <summary>
    /// Wrecks a ship on the beach, half-buried at the waterline along a bearing from the island's
    /// centre, with colliders, and the NavMesh re-baked round it.
    ///
    /// Type picks the wreck. The Stormbreaker is the opening wreck — md_shipwreck's broken hull and
    /// stern, dungeon-sized (the hull is ~97 units long at full size) so it goes in scaled down —
    /// and is always named "Shipwreck" (Place Player at Shipwreck looks for it), so re-adding it
    /// replaces it. Every other type is an old wreck round the isle, named "Shipwreck - Type", found
    /// by file name anywhere under WowExports; Custom takes any model dragged in. Everything named
    /// "shipwreck" counts as a wreck for the driftwood and flint scatter (sunk ones don't).
    ///
    /// Where = Sunk puts an old wreck on the seabed instead, just inside the Dangerous Waters line
    /// (the reef edge) along the bearing, settled into the silt, listing hard and turned any which
    /// way — named "Shipwreck - Sunk Type". It can bring bubbles rising off the hull, a fish school
    /// round it and a floating wreckage fishing spot on the surface above, all as its children.
    /// </summary>
    public class ShipwreckSetup : EditorWindow
    {
        const string StormbreakerName = "Shipwreck";
        const string Folder = "Assets/WowExports";

        /// <summary>A kind of wreck: its model files (by name, found anywhere under WowExports) and how it sits.</summary>
        class WreckType
        {
            public string Label;
            public string[] Files;   // file names without extension; [0] the main piece
            public float Scale = 1f;
            public float Sink = 0.3f;
            public string Export;    // what to export from wow.export if it's missing
        }

        static readonly WreckType[] Types =
        {
            new WreckType { Label = "Stormbreaker (opening wreck)", Files = new[] { "shipwreck_a", "shipwreck_dback" },
                            Scale = 0.5f, Export = "world/wmo/dungeon/md_shipwreck (shipwreck_a + shipwreck_dback)" },
            // "/" makes a submenu in the Type dropdown. Scales are first guesses from each export's
            // size (the md_shipwreck hull is ~97 long and goes in at 0.5).
            Wmo("Kul Tiran/Wreck 01 (flat hull)", "8nzj_nazjatar_kultiras_shipwreck01"),
            Wmo("Kul Tiran/Wreck 02 (flat hull)", "8nzj_nazjatar_kultiras_shipwreck02"),
            Wmo("Kul Tiran/Wreck 03 (low wreckage)", "8nzj_nazjatar_kultiras_shipwreck03"),
            Wmo("Kul Tiran/Wreck 04", "8nzj_nazjatar_kultiras_shipwreck04"),
            Wmo("Kul Tiran/Wreck 05", "8nzj_nazjatar_kultiras_shipwreck05"),
            Wmo("Dungeon wreck/Broken hull B", "shipwreck_b", 0.5f),
            Wmo("Dungeon wreck/Broken hull C", "shipwreck_c", 0.5f),
            Wmo("Dungeon wreck/Broken hull D", "shipwreck_d", 0.5f),
            Wmo("Dungeon wreck/Bow", "shipwreck_dfront", 0.5f),
            Wmo("Forsaken/Whole wreck", "nd_forsaken_shipwreck", 0.5f),
            Wmo("Forsaken/Chunk 01", "nd_forsaken_shipwreck_chunk01", 0.5f),
            Wmo("Forsaken/Chunk 02", "nd_forsaken_shipwreck_chunk02", 0.5f),
            Wmo("Forsaken/Chunk 03", "nd_forsaken_shipwreck_chunk03", 0.5f),
            Wmo("Orc/Broken ship 01 (no sails)", "orc_ship_broken_01_nosails", 0.5f),
            Wmo("Orc/Broken ship 01", "orc_ship_broken_01", 0.5f),
            Wmo("Orc/Broken ship 02", "orc_ship_broken_02", 0.5f),
            M2("Small/Wrecked rowboat", "wreckedrowboat", 1f, 0.2f),
            M2("Small/Wrecked rowboat (elven)", "be_rowboatwrecked", 1f, 0.2f),
            M2("Debris/Ship pieces 01", "shipwreck_pieces01", 0.3f, 0.2f),
            M2("Debris/Ship pieces 02", "shipwreck_pieces02", 0.3f, 0.2f),
            M2("Debris/Sunken anchor", "sunkenanchor", 0.3f, 0.2f),
        };

        // Search the name in wow.export's WMO tab (or Models tab for an M2) and export as glTF.
        static WreckType Wmo(string label, string file, float scale = 1f) =>
            new WreckType { Label = label, Files = new[] { file }, Scale = scale, Export = $"{file}.wmo (WMO tab, glTF)" };

        static WreckType M2(string label, string file, float scale, float sink) =>
            new WreckType { Label = label, Files = new[] { file }, Scale = scale, Sink = sink, Export = $"{file}.m2 (Models tab, glTF)" };

        int _type;
        GameObject _custom;
        bool _hull = true;
        bool _stern = true;
        bool _replace = true;
        float _scale = 0.5f;
        float _sink = 0.3f;
        float _bearing;
        bool _sunk;
        float _offshore = 50f;
        bool _bubbles = true;
        bool _school = true;
        bool _floating = true;

        const float SunkBuried = 0.15f;

        // The model found for each piece of the chosen type (null where it isn't exported), and
        // which types are exported at all — looked up on opening and on focus, not every repaint.
        GameObject[] _models;
        bool[] _exported;

        bool IsCustom => _type == Types.Length;
        bool IsStormbreaker => _type == 0;

        [MenuItem("WoW Sandbox/Add Shipwreck")]
        public static void ShowWindow()
        {
            var window = GetWindow<ShipwreckSetup>(true, "Add Shipwreck");
            window.minSize = new Vector2(380f, 380f);
            window._bearing = window.DefaultBearing();
            window._offshore = ReefEdge();
            window.Pick(0);
        }

        /// <summary>How far offshore the Dangerous Waters line is (DeepSeaDanger's margin past the terrain).</summary>
        static float DangerLine()
        {
            var danger = Object.FindAnyObjectByType<DeepSeaDanger>();
            return danger != null ? danger.dangerMargin : 60f;
        }

        // A little inside the line, so diving to it never fills the danger bar.
        static float ReefEdge() => Mathf.Max(DangerLine() - 6f, 0f);

        bool Sunk => _sunk && !IsStormbreaker;

        // Picks up models exported while the window was open.
        void OnFocus() => Refresh();

        void Refresh()
        {
            _exported = new bool[Types.Length];
            for (int i = 0; i < Types.Length; i++)
                _exported[i] = Find(Types[i])[0] != null;
            _models = IsCustom ? null : Find(Types[_type]);
        }

        void Pick(int type)
        {
            _type = type;
            if (!IsCustom)
            {
                _scale = Types[type].Scale;
                _sink = Sunk ? SunkBuried : Types[type].Sink;
            }
            Refresh();
        }

        void OnGUI()
        {
            EditorGUILayout.HelpBox(
                "Places the wreck half-buried where the beach meets the sea, along Bearing from " +
                "the island's centre (0 = north, 90 = east). Starts opposite the pirate ship.", MessageType.None);

            EditorGUILayout.Space();
            var labels = new string[Types.Length + 1];
            for (int i = 0; i < Types.Length; i++)
                labels[i] = Types[i].Label + (Exported(i) ? "" : "  (not exported)");
            labels[Types.Length] = "Custom model…";
            int picked = EditorGUILayout.Popup("Type", _type, labels);
            if (picked != _type)
                Pick(picked);

            if (IsCustom)
                _custom = (GameObject)EditorGUILayout.ObjectField("Model", _custom, typeof(GameObject), false);
            else if (!Exported(_type))
                EditorGUILayout.HelpBox($"Not found under {Folder}. Export {Types[_type].Export} from wow.export, " +
                                        "then come back to this window.", MessageType.Warning);

            if (IsStormbreaker)
            {
                _hull = EditorGUILayout.Toggle("Broken hull", _hull);
                _stern = EditorGUILayout.Toggle("Stern", _stern);
            }
            else
            {
                bool sunk = EditorGUILayout.Popup(new GUIContent("Where", "Sunk lies on the seabed at the reef edge."),
                                                  _sunk ? 1 : 0, new[] { "Beach", "Sunk" }) == 1;
                if (sunk != _sunk)
                {
                    _sunk = sunk;
                    _sink = sunk ? SunkBuried : IsCustom ? 0.3f : Types[_type].Sink;
                }
            }
            _scale = EditorGUILayout.Slider("Scale", _scale, 0.1f, 2f);
            _sink = EditorGUILayout.Slider(Sunk ? "Settled in silt" : "Buried", _sink, 0f, 0.8f);
            _bearing = EditorGUILayout.Slider("Bearing", _bearing, 0f, 360f);
            if (Sunk)
                SunkGUI();
            if (!IsStormbreaker)
                _replace = EditorGUILayout.Toggle(new GUIContent("Replace last of this type",
                    "Off adds another alongside it."), _replace);

            EditorGUILayout.Space();
            using (new EditorGUI.DisabledScope(!CanAdd()))
            {
                if (GUILayout.Button("Add", GUILayout.Height(30f)))
                    Add();
            }
        }

        void SunkGUI()
        {
            float line = DangerLine();
            _offshore = EditorGUILayout.Slider(new GUIContent("Offshore", "Metres past the terrain's edge. " +
                $"Dangerous Waters start at {line:0}."), _offshore, 1f, line + 40f);
            if (_offshore >= line)
                EditorGUILayout.HelpBox($"That's past the Dangerous Waters line ({line:0}) — sharks come for anyone diving to it.",
                                        MessageType.Warning);

            var terrain = Terrain.activeTerrain;
            var water = Object.FindAnyObjectByType<WaterVolume>();
            if (terrain != null && water != null && SeabedSpot(terrain, water, Outward(), _offshore, out var spot))
            {
                float depth = water.SurfaceY - spot.y;
                EditorGUILayout.HelpBox($"The seabed there is {depth:0.0} m down (breath lasts about 25 s).",
                                        depth > 18f ? MessageType.Warning : MessageType.Info);
            }

            _bubbles = EditorGUILayout.Toggle(new GUIContent("Bubbles", "A thin stream rising off the hull to the surface."), _bubbles);
            _school = EditorGUILayout.Toggle(new GUIContent("Fish school", "Fish roaming round the wreck."), _school);
            _floating = EditorGUILayout.Toggle(new GUIContent("Floating wreckage", "A fishing spot of flotsam on the surface above."), _floating);
        }

        Vector3 Outward() => Quaternion.Euler(0f, _bearing, 0f) * Vector3.forward;

        /// <summary>
        /// The seabed along <paramref name="outward"/> from the island's centre, where it's
        /// <paramref name="offshore"/> past the terrain's edge (AmbientSwimmer.Offshore). False if that's
        /// not in the sea.
        /// </summary>
        static bool SeabedSpot(Terrain terrain, WaterVolume water, Vector3 outward, float offshore, out Vector3 point)
        {
            var size = terrain.terrainData.size;
            point = default;
            for (float d = 0f; d < Mathf.Max(size.x, size.z) + offshore + 10f; d += 0.5f)
            {
                var probe = Centre(terrain) + outward * d;
                if (AmbientSwimmer.Offshore(probe) < offshore)
                    continue;
                float floor = Seabed.HeightAt(probe, water);
                if (!water.Covers(probe) || floor >= water.SurfaceY)
                    return false;
                point = new Vector3(probe.x, floor, probe.z);
                return true;
            }
            return false;
        }

        bool Exported(int type) => _exported != null && _exported[type];

        bool CanAdd()
        {
            if (IsCustom)
                return _custom != null;
            if (_models == null)
                return false;
            if (!IsStormbreaker)
                return _models[0] != null;
            return (_hull && _models[0] != null) || (_stern && _models[1] != null);
        }

        /// <summary>Each of the type's files, found by name anywhere under WowExports (glTF before OBJ).</summary>
        static GameObject[] Find(WreckType type)
        {
            var found = new GameObject[type.Files.Length];
            for (int i = 0; i < type.Files.Length; i++)
            {
                foreach (var guid in AssetDatabase.FindAssets($"{type.Files[i]} t:GameObject", new[] { Folder }))
                {
                    var path = AssetDatabase.GUIDToAssetPath(guid);
                    if (!string.Equals(Path.GetFileNameWithoutExtension(path), type.Files[i], System.StringComparison.OrdinalIgnoreCase))
                        continue;
                    var model = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                    if (found[i] == null || path.EndsWith(".gltf"))
                        found[i] = model;
                }
            }
            return found;
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

        string GroupName()
        {
            if (IsStormbreaker)
                return StormbreakerName;
            // No "/" in the name: GameObject.Find reads it as a path.
            string label = IsCustom ? _custom.name : Types[_type].Label.Replace("/", " ");
            return $"{StormbreakerName} - {(Sunk ? "Sunk " : "")}{label}";
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

            var outward = Outward();
            if (Sunk)
            {
                AddSunk(terrain, water, outward);
                return;
            }
            if (!Waterline(terrain, water, outward, out var shore))
            {
                Debug.LogError("[ShipwreckSetup] No beach along that bearing — the land never meets the sea. Try another bearing.");
                return;
            }

            var group = NewGroup(shore);
            // Lying along the beach, listing a little.
            var along = Vector3.Cross(Vector3.up, outward);
            var lying = Quaternion.LookRotation(along) * Quaternion.Euler(0f, 0f, 6f);
            int colliders = 0;
            if (IsCustom)
                colliders += Place(_custom, group.transform, shore, lying, terrain, water, false);
            else if (!IsStormbreaker)
                colliders += Place(_models[0], group.transform, shore, lying, terrain, water, false);
            else
            {
                if (_hull)
                    colliders += Place(_models[0], group.transform, shore, lying, terrain, water, false);
                if (_stern)
                {
                    // The stern a little further along the beach.
                    var spot = shore + along * (_hull ? 40f * _scale : 0f) + outward * 6f * _scale;
                    colliders += Place(_models[1], group.transform, spot,
                                       Quaternion.LookRotation(-outward) * Quaternion.Euler(-8f, 20f, -12f), terrain, water, false);
                }
            }

            ChickenSetup.BakeNavMesh();
            Selection.activeGameObject = group;
            EditorSceneManager.MarkSceneDirty(group.scene);
            Debug.Log($"[ShipwreckSetup] Wrecked {group.name} at {shore} ({colliders} colliders) and re-baked the NavMesh. " +
                      "Move it if the spot's wrong, then save the scene.", group);
        }

        /// <summary>The empty group the wreck goes in, at <paramref name="position"/>, replacing the last of its name if asked.</summary>
        GameObject NewGroup(Vector3 position)
        {
            string name = GroupName();
            if (IsStormbreaker || _replace)
            {
                var old = GameObject.Find(name);
                if (old != null)
                    Undo.DestroyObjectImmediate(old);
            }
            var group = new GameObject(name);
            Undo.RegisterCreatedObjectUndo(group, "Add Shipwreck");
            group.transform.position = position;
            return group;
        }

        /// <summary>
        /// The wreck on the seabed at the reef edge: settled into the silt, listing hard and turned
        /// any which way, with its bubbles, fish and flotsam. No NavMesh re-bake — nothing walks there.
        /// </summary>
        void AddSunk(Terrain terrain, WaterVolume water, Vector3 outward)
        {
            if (!SeabedSpot(terrain, water, outward, _offshore, out var spot))
            {
                Debug.LogError("[ShipwreckSetup] That far out along that bearing isn't in the sea. Try another bearing or Offshore.");
                return;
            }

            var group = NewGroup(spot);
            var model = IsCustom ? _custom : _models[0];
            var tilt = Quaternion.Euler(Random.Range(-6f, 6f), Random.Range(0f, 360f), Random.Range(20f, 30f) * (Random.value < 0.5f ? -1f : 1f));
            int colliders = Place(model, group.transform, spot, tilt, terrain, water, true);

            var hull = MeasureBounds(group);
            float depth = water.SurfaceY - spot.y;
            string extras = "";
            if (_bubbles)
            {
                // From the top of the hull, kept under the surface.
                var top = new Vector3(hull.center.x, Mathf.Min(hull.max.y, water.SurfaceY - 1f), hull.center.z);
                var bubbles = new GameObject("Bubbles");
                bubbles.transform.SetParent(group.transform);
                bubbles.transform.position = top;
                bubbles.AddComponent<WreckBubbles>();
                extras += ", bubbles";
            }
            if (_school)
            {
                // Round the hull, from a couple of metres under the surface to just over the wreck.
                var depthRange = new Vector2(Mathf.Min(2f, depth * 0.5f), Mathf.Max(depth - 1f, 2f));
                float roam = Mathf.Max(hull.extents.x, hull.extents.z) + 4f;
                var home = new Vector3(hull.center.x, water.SurfaceY - (depthRange.x + depthRange.y) * 0.5f, hull.center.z);
                if (SeaLifeSetup.AddSchool(home, group.transform, "Fish School", roam, depthRange, _offshore + roam + 10f) != null)
                    extras += ", fish school";
            }
            if (_floating)
            {
                // Beside the hull rather than right over it, drifting a little off where the bubbles come up.
                var beside = Random.insideUnitCircle.normalized * 3f;
                var surface = new Vector3(hull.center.x + beside.x, water.SurfaceY, hull.center.z + beside.y);
                if (SeaLifeSetup.AddFloatingWreckage(surface, group.transform, "Floating Wreckage") != null)
                    extras += ", floating wreckage";
            }

            Selection.activeGameObject = group;
            EditorSceneManager.MarkSceneDirty(group.scene);
            Debug.Log($"[ShipwreckSetup] Sank {group.name} {depth:0.0} m down at {spot}, {_offshore:0} m offshore " +
                      $"({colliders} colliders{extras}). Move it if the spot's wrong, then save the scene.", group);
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

        int Place(GameObject model, Transform parent, Vector3 position, Quaternion rotation, Terrain terrain, WaterVolume water, bool seabed)
        {
            if (model == null)
            {
                Debug.LogError("[ShipwreckSetup] A piece of this wreck isn't exported — skipped it.");
                return 0;
            }

            var instance = (GameObject)PrefabUtility.InstantiatePrefab(model, parent);
            instance.transform.SetPositionAndRotation(position, rotation);
            instance.transform.localScale = Vector3.one * _scale;

            // Bottom on the ground, then pushed down into the sand by the Buried fraction.
            var bounds = MeasureBounds(instance);
            float ground = seabed ? LowestSeabed(bounds, water) : terrain.GetPosition().y + terrain.SampleHeight(position);
            instance.transform.position += Vector3.up * (ground - bounds.min.y - bounds.size.y * _sink);

            return MeshColliderTool.AddCollidersTo(instance);
        }

        /// <summary>The lowest seabed under <paramref name="bounds"/> — its middle and corners — so a hull on a slope doesn't hang off the downhill side.</summary>
        static float LowestSeabed(Bounds bounds, WaterVolume water)
        {
            float lowest = Seabed.HeightAt(bounds.center, water);
            for (int x = -1; x <= 1; x += 2)
                for (int z = -1; z <= 1; z += 2)
                    lowest = Mathf.Min(lowest, Seabed.HeightAt(bounds.center + new Vector3(bounds.extents.x * x, 0f, bounds.extents.z * z), water));
            return lowest;
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
