using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace WowSandbox.EditorTools
{
    /// <summary>
    /// The campfire's looks and the driftwood to build it from, out of exported WoW models.
    ///
    /// Each time Play is pressed (if it's missing), a Campfire prefab is built into a Resources
    /// folder under the gitignored _Generated, since Campfire can't look models up by path at
    /// Play. It holds three looks, one shown at a time (Campfire.Look): Burning
    /// (6fx_campfire01), Cooking (6fx_campfire_cooking01, the skillet over it) and Cold
    /// (6fx_campfire_cold01, the coals once it's out). WoW's flames and smoke are particles, which
    /// don't export, so NightLight adds the flame; the glow sheet under the logs is made
    /// additive, like the chests' glows. Delete the folder (or Rebuild Campfire) after changing
    /// it. Without the models, Campfire builds a plain stand-in instead.
    ///
    /// WoW Sandbox → Scatter Driftwood (which scatters the flint too) lays driftwood along the beach within reach of where the
    /// player wakes — Durotar palm logs and broken planks from the lumber mill, whichever are
    /// exported: GroundItems that wash up again a game day after they're taken.
    /// </summary>
    [InitializeOnLoad]
    static class CampfireBuilder
    {
        const string Folder = "Assets/WowExports/_Generated/Resources/Campfire";
        // What washes up, and how long each lies on the sand (the planks are cut down to size).
        static readonly (string model, float length)[] Driftwood =
        {
            ("12dur_durotar_palmtree_log", 1.1f),
            ("6hu_lumbermill_woodenplank01", 1.3f),
            ("6hu_lumbermill_woodenplank02", 1.5f),
        };
        static readonly (string look, string model)[] Looks =
        {
            (Campfire.BurningLook, "6fx_campfire01"),
            (Campfire.CookingLook, "6fx_campfire_cooking01"),
            (Campfire.ColdLook, "6fx_campfire_cold01"),
        };

        const float DriftwoodRespawnHours = 24f;

        static CampfireBuilder()
        {
            EditorApplication.playModeStateChanged += state =>
            {
                if (state == PlayModeStateChange.ExitingEditMode)
                    Build();
            };
        }

        [MenuItem("WoW Sandbox/Rebuild Campfire")]
        static void Rebuild()
        {
            if (AssetDatabase.IsValidFolder(Folder))
                AssetDatabase.DeleteAsset(Folder);
            Build();
        }

        static GameObject Model(string name)
        {
            string path = AssetDatabase.FindAssets(name)
                .Select(AssetDatabase.GUIDToAssetPath)
                .FirstOrDefault(p => Path.GetFileNameWithoutExtension(p) == name && (p.EndsWith(".gltf") || p.EndsWith(".glb")));
            return path != null ? AssetDatabase.LoadAssetAtPath<GameObject>(path) : null;
        }

        static void Build()
        {
            string path = $"{Folder}/Campfire.prefab";
            if (File.Exists(path))
                return;
            var models = Looks.Select(look => (look.look, model: Model(look.model))).ToArray();
            if (models.Any(look => look.model == null))
            {
                Debug.Log("[CampfireBuilder] The campfire models (" + string.Join(", ", Looks.Select(l => l.model)) +
                          ") aren't all exported — campfires will be a plain stand-in.");
                return;
            }

            var root = new GameObject("Campfire");
            foreach (var (look, model) in models)
            {
                var instance = (GameObject)PrefabUtility.InstantiatePrefab(model, root.transform);
                instance.name = look;
                instance.transform.localPosition = Vector3.zero;
                InteractableSetup.Glow(instance);
                InteractableSetup.Cutouts(instance);
                instance.SetActive(look == Campfire.BurningLook);
            }
            // A fixed box, for clicking it: the models' bounds take in the particles' reach too.
            var box = root.AddComponent<BoxCollider>();
            box.isTrigger = true;
            box.center = new Vector3(0f, 0.3f, 0f);
            box.size = new Vector3(1.8f, 0.6f, 1.8f);

            Directory.CreateDirectory(Folder);
            PrefabUtility.SaveAsPrefabAsset(root, path);
            Object.DestroyImmediate(root);
            AssetDatabase.SaveAssets();
            Debug.Log("[CampfireBuilder] Built the campfire: burning, cooking and cold.");
        }

        /// <summary>
        /// A copy of <paramref name="model"/> under <paramref name="parent"/>, lying down along its
        /// longest side, <paramref name="length"/> long, centred over the parent and resting on y = 0.
        /// </summary>
        static Transform Piece(GameObject model, Transform parent, float length)
        {
            var holder = new GameObject(model.name).transform;
            holder.SetParent(parent, false);
            var look = (GameObject)PrefabUtility.InstantiatePrefab(model, holder);
            look.transform.localPosition = Vector3.zero;

            var size = InteractableSetup.RendererBounds(look).size;
            // Its longest side along local X, so it lies flat.
            if (size.y >= size.x && size.y >= size.z)
                look.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
            else if (size.z > size.x)
                look.transform.localRotation = Quaternion.Euler(0f, 90f, 0f);
            float longest = Mathf.Max(size.x, size.y, size.z);
            if (longest > 0f)
                look.transform.localScale *= length / longest;

            var bounds = InteractableSetup.RendererBounds(look);
            var shift = holder.position - new Vector3(bounds.center.x, bounds.min.y, bounds.center.z);
            look.transform.position += shift;
            return holder;
        }

        /// <summary>
        /// Lays <paramref name="count"/> driftwood on bare sand within <paramref name="reach"/> of
        /// where the player stands in the scene — none touching a rock, boulder, house or anything
        /// else, nor another piece — <paramref name="nearWrecks"/> of them washed up round the
        /// shipwrecks (anything named like one), wherever those are. A fresh scatter replaces the
        /// last when <paramref name="replace"/>.
        /// </summary>
        internal static void ScatterDriftwood(int count, float reach, bool replace, float nearWrecks)
        {
            var pieces = Driftwood.Select(wood => (model: Model(wood.model), wood.length))
                .Where(wood => wood.model != null).ToArray();
            var player = Object.FindAnyObjectByType<WowCharacterController>();
            var water = Object.FindAnyObjectByType<WaterVolume>();
            var terrain = Terrain.activeTerrain;
            if (pieces.Length == 0 || player == null || water == null || terrain == null)
            {
                Debug.LogWarning("[CampfireBuilder] Scatter Driftwood needs the player, the water, the terrain and one of " +
                                 string.Join(", ", Driftwood.Select(wood => wood.model)) + " exported.");
                return;
            }

            Undo.SetCurrentGroupName("Scatter Driftwood");
            if (replace)
                foreach (var old in Object.FindObjectsByType<GroundItem>(FindObjectsSortMode.None)
                             .Where(item => item.item == Campfire.WoodName).ToArray())
                    Undo.DestroyObjectImmediate(old.gameObject);
            var group = GameObject.Find("Driftwood (scattered)");
            if (group == null)
            {
                group = new GameObject("Driftwood (scattered)");
                Undo.RegisterCreatedObjectUndo(group, "Scatter Driftwood");
            }
            Physics.SyncTransforms();

            var obstacles = ObstacleBounds();
            var laid = Object.FindObjectsByType<GroundItem>(FindObjectsSortMode.None)
                .Where(item => item.item == Campfire.WoodName).Select(item => item.transform.position).ToList();
            float sea = water.SurfaceY;
            var centre = player.transform.position;
            int sand = SandLayerIndex(terrain);
            var wrecks = Wrecks();
            int wreckTarget = wrecks.Count > 0 ? Mathf.RoundToInt(count * Mathf.Clamp01(nearWrecks)) : 0;
            int placed = 0, byWrecks = 0;
            for (int attempt = 0; attempt < count * 400 && placed < count; attempt++)
            {
                // The wrecks' share first: anywhere round a wreck, out to WreckSpread past its hull.
                bool byWreck = byWrecks < wreckTarget && attempt < count * 300;
                Vector3 point;
                if (byWreck)
                {
                    var wreck = wrecks[Random.Range(0, wrecks.Count)];
                    point = new Vector3(Random.Range(wreck.min.x - WreckSpread, wreck.max.x + WreckSpread), 0f,
                                        Random.Range(wreck.min.z - WreckSpread, wreck.max.z + WreckSpread));
                }
                else
                {
                    var flat = Random.insideUnitCircle * reach;
                    point = centre + new Vector3(flat.x, 0f, flat.y);
                }
                point.y = terrain.SampleHeight(point) + terrain.GetPosition().y;
                // Just above the sea, not up on the hills.
                if (point.y < sea + 0.3f || point.y > sea + 3f)
                    continue;
                // Mostly on the beach sand (Paint Beach Sand's layer), and mostly down by the
                // tide line, where the sea leaves it: rarely anywhere else. Round a wreck, wherever
                // there's beach — the wreckage lies where it lies.
                float sandHere = SandAt(terrain, sand, point);
                if (Random.value > (byWreck ? Mathf.Max(sandHere, 0.5f) : sandHere * sandHere))
                    continue;
                if (!byWreck && Random.value > Mathf.Lerp(0.3f, 1f, Mathf.InverseLerp(sea + 3f, sea + 0.4f, point.y)))
                    continue;
                // Bare beach below: not on top of a rock or the wreck.
                if (!Physics.Raycast(point + Vector3.up * 100f, Vector3.down, out var hit, 110f, ~0, QueryTriggerInteraction.Ignore) ||
                    hit.collider is not TerrainCollider)
                    continue;
                // A little room between pieces (less round a wreck, where it piles up).
                float apart = byWreck ? 1.5f : 2f;
                if (laid.Any(other => (other - hit.point).sqrMagnitude < apart * apart))
                    continue;

                var (model, length) = pieces[Random.Range(0, pieces.Length)];
                var root = Lay(Campfire.WoodName, group.transform, hit, model, length * Random.Range(0.8f, 1.2f), obstacles);
                if (root == null)
                    continue;
                var item = root.AddComponent<GroundItem>();
                item.item = Campfire.WoodName;
                item.count = Random.Range(1, 3);
                item.respawnHours = DriftwoodRespawnHours;
                item.displayName = Campfire.WoodName;
                laid.Add(hit.point);
                placed++;
                if (byWreck)
                    byWrecks++;
            }

            EditorSceneManager.MarkSceneDirty(group.scene);
            Selection.activeGameObject = group;
            Debug.Log(placed < count
                ? $"[CampfireBuilder] Only found room for {placed} of {count} driftwood on clear sand within {reach:0} m of the player."
                : $"[CampfireBuilder] Scattered {placed} driftwood along the beach ({byWrecks} round " +
                  $"{wrecks.Count} shipwreck(s)). Save the scene to keep them.");
        }

        /// <summary>
        /// Lays <paramref name="model"/>, <paramref name="length"/> long, on the ground at
        /// <paramref name="hit"/> under <paramref name="group"/>, turned any way and with a click
        /// box — unless it would be into something: a collider touching it, or inside any solid
        /// model's bounds. Null (and nothing left behind) then.
        /// </summary>
        static GameObject Lay(string name, Transform group, RaycastHit hit, GameObject model, float length,
                              List<Bounds> obstacles)
        {
            var root = new GameObject(name);
            root.transform.SetParent(group, true);
            root.transform.SetPositionAndRotation(hit.point,
                Quaternion.FromToRotation(Vector3.up, hit.normal) * Quaternion.Euler(0f, Random.Range(0f, 360f), 0f));
            var piece = Piece(model, root.transform, length);
            piece.localPosition += Vector3.down * 0.04f;
            // Detail doodads (the flint's stones) come on see-through sheets.
            InteractableSetup.Cutouts(piece.gameObject);

            var bounds = InteractableSetup.RendererBounds(root);
            bounds.Expand(new Vector3(0.3f, 0f, 0.3f));
            bounds.center += Vector3.up * 0.1f;
            bool blocked = Physics.OverlapBox(bounds.center, bounds.extents, Quaternion.identity, ~0,
                               QueryTriggerInteraction.Ignore).Any(collider => collider is not TerrainCollider)
                           || obstacles.Any(obstacle => obstacle.Intersects(bounds));
            if (blocked)
            {
                Object.DestroyImmediate(root);
                return null;
            }

            Undo.RegisterCreatedObjectUndo(root, "Scatter " + name);
            InteractableSetup.AddBoxCollider(root, trigger: true, minSize: 0.5f);
            return root;
        }

        const string FlintModel = "12rocks01";
        const float FlintSize = 0.7f;

        /// <summary>
        /// Lays <paramref name="count"/> flint — a few stones on the sand, each giving Flint & Steel
        /// to a castaway who hasn't one — round the shipwrecks (or near where the player wakes, with
        /// no wreck), clear of everything as the driftwood is. Replaces the last lot when
        /// <paramref name="replace"/>.
        /// </summary>
        internal static void ScatterFlint(int count, bool replace)
        {
            var model = Model(FlintModel);
            var player = Object.FindAnyObjectByType<WowCharacterController>();
            var water = Object.FindAnyObjectByType<WaterVolume>();
            var terrain = Terrain.activeTerrain;
            if (model == null || player == null || water == null || terrain == null)
            {
                Debug.LogWarning($"[CampfireBuilder] Scatter Flint needs the player, the water, the terrain and {FlintModel} exported.");
                return;
            }

            Undo.SetCurrentGroupName("Scatter Flint");
            if (replace)
                foreach (var old in Object.FindObjectsByType<GroundItem>(FindObjectsSortMode.None)
                             .Where(item => item.item == Campfire.FlintName).ToArray())
                    Undo.DestroyObjectImmediate(old.gameObject);
            var group = GameObject.Find("Flint (scattered)");
            if (group == null)
            {
                group = new GameObject("Flint (scattered)");
                Undo.RegisterCreatedObjectUndo(group, "Scatter Flint");
            }
            Physics.SyncTransforms();

            var obstacles = ObstacleBounds();
            var laid = Object.FindObjectsByType<GroundItem>(FindObjectsSortMode.None)
                .Where(item => item.item == Campfire.FlintName).Select(item => item.transform.position).ToList();
            var wrecks = Wrecks();
            if (wrecks.Count == 0)
                wrecks.Add(new Bounds(player.transform.position, Vector3.one * 20f));
            float sea = water.SurfaceY;
            int placed = 0;
            for (int attempt = 0; attempt < count * 500 && placed < count; attempt++)
            {
                var wreck = wrecks[Random.Range(0, wrecks.Count)];
                var point = new Vector3(Random.Range(wreck.min.x - WreckSpread, wreck.max.x + WreckSpread), 0f,
                                        Random.Range(wreck.min.z - WreckSpread, wreck.max.z + WreckSpread));
                point.y = terrain.SampleHeight(point) + terrain.GetPosition().y;
                if (point.y < sea + 0.3f || point.y > sea + 4f)
                    continue;
                if (!Physics.Raycast(point + Vector3.up * 100f, Vector3.down, out var hit, 110f, ~0, QueryTriggerInteraction.Ignore) ||
                    hit.collider is not TerrainCollider)
                    continue;
                // Spread round the wreckage rather than in one heap.
                if (laid.Any(other => (other - hit.point).sqrMagnitude < 4f * 4f))
                    continue;

                var root = Lay("Flint", group.transform, hit, model, FlintSize * Random.Range(0.85f, 1.15f), obstacles);
                if (root == null)
                    continue;
                var item = root.AddComponent<GroundItem>();
                item.item = Campfire.FlintName;
                item.count = 1;
                item.unique = true;
                item.displayName = "Flint";
                laid.Add(hit.point);
                placed++;
            }

            EditorSceneManager.MarkSceneDirty(group.scene);
            Selection.activeGameObject = group;
            Debug.Log(placed < count
                ? $"[CampfireBuilder] Only found room for {placed} of {count} flint round the wreckage."
                : $"[CampfireBuilder] Scattered {placed} flint round {wrecks.Count} shipwreck(s). Save the scene to keep them.");
        }

        /// <summary>
        /// The bounds of every solid model, colliders or not: a rock's mesh collider is only its
        /// surface, so a piece wholly inside one touches nothing, and only its bounds catch it.
        /// Not soft plants (bushes, ferns, flowers — driftwood can lie among those), palms (their
        /// crowns would rule out the beach; their trunk colliders do instead), characters, the sea
        /// or seabed (too big to be in the way), or driftwood.
        /// </summary>
        static List<Bounds> ObstacleBounds()
        {
            var found = new List<Bounds>();
            foreach (var renderer in Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None))
            {
                if (renderer is ParticleSystemRenderer || !renderer.enabled)
                    continue;
                var size = renderer.bounds.size;
                if (Mathf.Max(size.x, size.z) > 100f)
                    continue;
                if (renderer.GetComponentInParent<GroundItem>() != null || renderer.GetComponentInParent<WaterVolume>() != null ||
                    renderer.GetComponentInParent<TreeSway>() != null || InWreck(renderer.transform) ||
                    renderer.GetComponentInParent<WowCharacterController>() != null ||
                    renderer.GetComponentInParent<UnityEngine.AI.NavMeshAgent>() != null)
                    continue;
                if (SoftPlant(renderer.transform))
                    continue;
                found.Add(renderer.bounds);
            }
            return found;
        }

        /// <summary>Which of the terrain's layers is Paint Beach Sand's, or -1 (then everywhere counts as sand).</summary>
        static int SandLayerIndex(Terrain terrain)
        {
            var layers = terrain.terrainData.terrainLayers;
            for (int i = 0; i < layers.Length; i++)
                if (layers[i] != null && (AssetDatabase.GetAssetPath(layers[i]).EndsWith("BeachSandLayer.terrainlayer") ||
                                          layers[i].name.ToLowerInvariant().Contains("sand")))
                    return i;
            return -1;
        }

        /// <summary>How much sand is painted at <paramref name="point"/>, 0-1.</summary>
        static float SandAt(Terrain terrain, int sand, Vector3 point)
        {
            if (sand < 0)
                return 1f;
            var data = terrain.terrainData;
            var local = point - terrain.GetPosition();
            int x = Mathf.Clamp(Mathf.FloorToInt(local.x / data.size.x * data.alphamapWidth), 0, data.alphamapWidth - 1);
            int z = Mathf.Clamp(Mathf.FloorToInt(local.z / data.size.z * data.alphamapHeight), 0, data.alphamapHeight - 1);
            return data.GetAlphamaps(x, z, 1, 1)[0, 0, sand];
        }

        // How far past a wreck's hull its wreckage washes up.
        const float WreckSpread = 10f;

        /// <summary>
        /// The bounds of every shipwreck in the scene — the topmost object named like one (Add
        /// Shipwreck's "Shipwreck", or any placed by hand). Not sunk ones, nor floating wreckage
        /// (its model is tradeskill_fishschool_shipwreck): nothing washes up off those.
        /// </summary>
        static List<Bounds> Wrecks()
        {
            var found = new List<Bounds>();
            foreach (var t in Object.FindObjectsByType<Transform>(FindObjectsSortMode.None))
            {
                if (!t.name.ToLowerInvariant().Contains("shipwreck") || (t.parent != null && InWreck(t.parent)))
                    continue;
                if (t.name.Contains(" - Sunk ") || t.GetComponentInParent<FishingSpot>(true) != null)
                    continue;
                var bounds = InteractableSetup.RendererBounds(t.gameObject);
                if (bounds.size.sqrMagnitude > 0f)
                    found.Add(bounds);
            }
            return found;
        }

        // A wreck is kept out of by its colliders, not its bounds: a long hull lying at an angle has
        // bounds taking in half the beach beside it — where its wreckage ought to be.
        static bool InWreck(Transform t)
        {
            for (; t != null; t = t.parent)
                if (t.name.ToLowerInvariant().Contains("shipwreck"))
                    return true;
            return false;
        }

        // Vegetation Scatter's groups of things you can walk through.
        static readonly string[] SoftGroups = { "Bushs", "Bushes", "Flowers", "Ferns" };

        static bool SoftPlant(Transform t)
        {
            for (; t != null; t = t.parent)
                if (SoftGroups.Contains(t.name))
                    return true;
            return false;
        }
    }

    /// <summary>WoW Sandbox → Scatter Driftwood: how many pieces, how far out, and whether they replace the last lot.</summary>
    public class DriftwoodScatterWindow : EditorWindow
    {
        const string CountKey = "WowSandbox.Driftwood.Count";
        const string ReachKey = "WowSandbox.Driftwood.Reach";
        const string ReplaceKey = "WowSandbox.Driftwood.Replace";
        const string WrecksKey = "WowSandbox.Driftwood.NearWrecks";
        const string FlintKey = "WowSandbox.Flint.Count";

        [MenuItem("WoW Sandbox/Scatter Driftwood")]
        static void ShowWindow()
        {
            var window = GetWindow<DriftwoodScatterWindow>(true, "Scatter Driftwood");
            window.minSize = new Vector2(340f, 300f);
        }

        void OnGUI()
        {
            EditorGUILayout.HelpBox("Lays driftwood (palm logs and planks) on bare sand around where the player " +
                                    "wakes — never touching a rock, boulder, house or another piece. Each washes " +
                                    "up again a game day after it's taken.", MessageType.Info);
            int count = EditorGUILayout.IntSlider("How many", EditorPrefs.GetInt(CountKey, 12), 1, 60);
            float reach = EditorGUILayout.Slider("Within (m)", EditorPrefs.GetFloat(ReachKey, 50f), 10f, 120f);
            float nearWrecks = EditorGUILayout.Slider(new GUIContent("Round shipwrecks",
                "The share of them washed up round the shipwrecks (anything named like one)."),
                EditorPrefs.GetFloat(WrecksKey, 0.4f), 0f, 1f);
            bool replace = EditorGUILayout.Toggle("Replace the last lot", EditorPrefs.GetBool(ReplaceKey, true));
            EditorPrefs.SetInt(CountKey, count);
            EditorPrefs.SetFloat(ReachKey, reach);
            EditorPrefs.SetBool(ReplaceKey, replace);
            EditorPrefs.SetFloat(WrecksKey, nearWrecks);

            EditorGUILayout.Space();
            using (new EditorGUI.DisabledScope(Application.isPlaying))
                if (GUILayout.Button("Scatter Driftwood", GUILayout.Height(28f)))
                    CampfireBuilder.ScatterDriftwood(count, reach, replace, nearWrecks);
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Flint", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("A few stones (12rocks01) round the wreckage, each giving Flint & Steel to " +
                                    "a castaway who hasn't got one.", MessageType.None);
            int flint = EditorGUILayout.IntSlider("How many", EditorPrefs.GetInt(FlintKey, 4), 1, 20);
            EditorPrefs.SetInt(FlintKey, flint);
            using (new EditorGUI.DisabledScope(Application.isPlaying))
                if (GUILayout.Button("Scatter Flint", GUILayout.Height(28f)))
                    CampfireBuilder.ScatterFlint(flint, replace);

            if (Application.isPlaying)
                EditorGUILayout.HelpBox("Stop Play first — scene changes made in Play are lost.", MessageType.Warning);
        }
    }
}
