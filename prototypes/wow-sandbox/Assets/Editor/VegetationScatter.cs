using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace WowSandbox.EditorTools
{
    /// <summary>
    /// Dresses the island with vegetation from whatever WoW doodads are exported: palms, bushes
    /// and ferns, flowers, boulders and driftwood, sorted by the words in their names. Each
    /// kind has its own density and rules — undergrowth in thick clusters with clearings
    /// between, on grass only (never the beach sand Paint Beach Sand laid); palms out onto the
    /// sand too; nothing under water, on steep slopes, or in buildings, the ship, the wreck or
    /// the start. Re-running replaces only what it placed (under one "Vegetation" root).
    ///
    /// Palms are full prefab instances with TreeSway, so they rock in the wind. Everything else
    /// is drawn as plain static meshes in its rest pose — WoW's doodads import skinned, and a
    /// thousand skinned bushes would be slow. Trunks and boulders get colliders; bushes,
    /// ferns and flowers you walk through, as in WoW. Leafy textures with see-through edges get
    /// a cut-out, two-sided copy of their material (wow.export never marks them).
    ///
    /// Also paints a grass carpet as terrain detail: a WoW grass texture if you give one, else
    /// generated tufts, on the grass only, waving in the wind.
    /// </summary>
    public class VegetationScatter : EditorWindow
    {
        const string ExportRoot = "Assets/WowExports";
        const string OutputDir = "Assets/WowExports/_Generated/Vegetation";
        const string RootName = "Vegetation";
        const string SandLayerPath = "Assets/Terrain/BeachSandLayer.terrainlayer";
        const string GrassTextureName = "VegetationGrass";

        enum Kind { Palm, Bush, Flower, Boulder, Driftwood }

        /// <summary>Words in an export's file name that say what it is. First match wins.</summary>
        static readonly (string[] words, Kind kind)[] Words =
        {
            (new[] { "fallen", "log", "driftwood", "stump" }, Kind.Driftwood),
            (new[] { "palm" }, Kind.Palm),
            (new[] { "flower", "bloom", "lily", "orchid" }, Kind.Flower),
            (new[] { "bush", "shrub", "fern", "plant", "foliage", "frond", "jungle", "grass", "weed", "vine", "reed", "hedge" }, Kind.Bush),
            (new[] { "boulder", "rock", "stone" }, Kind.Boulder),
        };

        /// <summary>
        /// Per kind, at "lush": how many per 1,000 square units of usable ground, how clustered
        /// (0 even, 1 tight thickets with bare clearings), the size range, the steepest slope,
        /// how far out onto the sand (0 never, 1 anywhere), and whether it's solid.
        /// </summary>
        static readonly Dictionary<Kind, (float density, float cluster, Vector2 scale, float slope, float sand, bool solid)> Rules = new()
        {
            [Kind.Palm] = (1.6f, 0.4f, new Vector2(0.8f, 1.2f), 25f, 1f, true),
            [Kind.Bush] = (14f, 0.85f, new Vector2(0.7f, 1.3f), 35f, 0.25f, false),
            [Kind.Flower] = (6f, 0.7f, new Vector2(0.8f, 1.2f), 30f, 0.15f, false),
            [Kind.Boulder] = (0.8f, 0.2f, new Vector2(0.6f, 1.4f), 50f, 0.6f, true),
            [Kind.Driftwood] = (0.25f, 0f, new Vector2(0.9f, 1.1f), 15f, 1f, true),
        };

        [SerializeField] float lushness = 1f;
        [SerializeField] int seed = 12;
        [SerializeField] float clearStart = 8f;
        [SerializeField] Texture2D grassTexture;
        [SerializeField] float grassDensity = 3f;
        [SerializeField] float grassPatchiness = 0.25f;
        [SerializeField] float grassDistance = 120f;
        [SerializeField] float grassHeight = 0.6f;

        List<(string path, Kind kind)> _found = new();
        Vector2 _scroll;

        [MenuItem("WoW Sandbox/Scatter Vegetation")]
        static void Open()
        {
            var window = GetWindow<VegetationScatter>(true, "Scatter Vegetation");
            window.minSize = new Vector2(440f, 480f);
            window.Discover();
        }

        void OnFocus() => Discover();

        void Discover()
        {
            _found = Directory.GetFiles(ExportRoot, "*.gltf", SearchOption.AllDirectories)
                .Select(p => p.Replace('\\', '/'))
                .Where(p => !p.Contains("/_Generated/") && !p.Contains("/character/") && !p.Contains("/item/") &&
                            !p.Contains("/creature") && !p.Contains("/wmo/"))
                .Select(p => (path: p, kind: KindOf(p)))
                .Where(f => f.kind.HasValue)
                .Select(f => (f.path, f.kind.Value))
                .OrderBy(f => f.Item2).ThenBy(f => f.path)
                .ToList();
        }

        static Kind? KindOf(string path)
        {
            string file = Path.GetFileNameWithoutExtension(path).ToLowerInvariant();
            foreach (var (words, kind) in Words)
                if (words.Any(file.Contains))
                    return kind;
            return null;
        }

        static string PrefKey(string path) => "WowSandbox.Vegetation.Skip." + path;

        void OnGUI()
        {
            EditorGUILayout.HelpBox("Scatters every exported plant and rock it recognises by name. Untick any to leave " +
                                    "them out. Re-running replaces what it placed before; re-bake the NavMesh after.",
                                    MessageType.Info);
            lushness = EditorGUILayout.Slider(new GUIContent("Lushness", "1 is lush tropical; 0.5 moderate."), lushness, 0.1f, 2f);
            seed = EditorGUILayout.IntField(new GUIContent("Seed", "Change for a different layout."), seed);
            clearStart = EditorGUILayout.FloatField(new GUIContent("Clear around start",
                "Radius kept open around the player, the vendor and the wreck."), clearStart);

            GUILayout.Space(4f);
            EditorGUILayout.LabelField("Models found", EditorStyles.boldLabel);
            _scroll = EditorGUILayout.BeginScrollView(_scroll, GUILayout.Height(170f));
            if (_found.Count == 0)
                EditorGUILayout.LabelField("None yet — export palms, bushes, ferns, flowers or boulders.");
            foreach (var (path, kind) in _found)
            {
                bool on = !EditorPrefs.GetBool(PrefKey(path), false);
                bool now = EditorGUILayout.ToggleLeft($"{kind,-9}  {Path.GetFileNameWithoutExtension(path)}", on);
                if (now != on)
                    EditorPrefs.SetBool(PrefKey(path), !now);
            }
            EditorGUILayout.EndScrollView();

            if (GUILayout.Button("Scatter", GUILayout.Height(28f)))
                Scatter();
            if (GUILayout.Button("Clear Scattered"))
                Clear(report: true);

            GUILayout.Space(10f);
            EditorGUILayout.LabelField("Grass carpet", EditorStyles.boldLabel);
            grassTexture = (Texture2D)EditorGUILayout.ObjectField(new GUIContent("WoW grass texture (optional)",
                "An exported grass billboard texture with see-through edges. Empty uses generated tufts."),
                grassTexture, typeof(Texture2D), false);
            grassDensity = EditorGUILayout.Slider(new GUIContent("Density",
                "Tufts per patch of ground: 1 is light, 3 thick, 6+ a solid meadow. Also raises the terrain's own " +
                "Detail Density to full."), grassDensity, 0f, 10f);
            grassPatchiness = EditorGUILayout.Slider(new GUIContent("Patchiness",
                "0 covers every bit of grass evenly; 1 leaves bare patches between clumps."), grassPatchiness, 0f, 1f);
            grassDistance = EditorGUILayout.Slider(new GUIContent("Draw distance",
                "How far away grass is still drawn. Further costs more."), grassDistance, 40f, 250f);
            grassHeight = EditorGUILayout.FloatField("Height", grassHeight);
            if (GUILayout.Button("Paint Grass"))
                PaintGrass();
        }

        // --- Scatter -------------------------------------------------------------------------

        struct Placed { public Vector3 position; public float radius; }

        void Scatter()
        {
            var terrain = Terrain.activeTerrain != null ? Terrain.activeTerrain : FindAnyObjectByType<Terrain>();
            var water = FindAnyObjectByType<WaterVolume>();
            if (terrain == null || water == null)
            {
                Debug.LogWarning("[Vegetation] Needs the terrain and the water in the scene.");
                return;
            }

            var chosen = _found.Where(f => !EditorPrefs.GetBool(PrefKey(f.path), false)).ToList();
            if (chosen.Count == 0)
            {
                Debug.LogWarning("[Vegetation] No models to scatter — export some, or tick them in the list.");
                return;
            }

            Clear(report: false);
            var root = new GameObject(RootName);
            Undo.RegisterCreatedObjectUndo(root, "Scatter Vegetation");
            var groups = new Dictionary<Kind, Transform>();

            var data = terrain.terrainData;
            var origin = terrain.transform.position;
            var sandMap = SandWeights(data, out int mapSize);
            var keepClear = KeepClearPoints();
            var random = new System.Random(seed);
            var grid = new Dictionary<Vector2Int, List<Placed>>();
            var models = new Dictionary<string, Model>();
            var counts = new Dictionary<Kind, int>();
            float waterY = water.SurfaceY;

            foreach (var kind in chosen.Select(c => c.kind).Distinct())
            {
                var paths = chosen.Where(c => c.kind == kind).Select(c => c.path).ToList();
                var rule = Rules[kind];
                float area = data.size.x * data.size.z;
                int attempts = Mathf.RoundToInt(rule.density * lushness * area / 1000f * 2.5f);
                float noiseOffset = (int)kind * 37.1f + seed * 3.3f;
                int placed = 0;

                for (int i = 0; i < attempts; i++)
                {
                    float nx = (float)random.NextDouble(), nz = (float)random.NextDouble();
                    var point = origin + new Vector3(nx * data.size.x, 0f, nz * data.size.z);
                    point.y = origin.y + data.GetInterpolatedHeight(nx, nz);

                    // Clusters: thickets where the noise is high, clearings where it's low.
                    float n = Mathf.PerlinNoise(point.x * 0.025f + noiseOffset, point.z * 0.025f + noiseOffset)
                            * 0.75f + Mathf.PerlinNoise(point.x * 0.09f, point.z * 0.09f + noiseOffset) * 0.25f;
                    float keep = Mathf.Lerp(0.4f, Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.4f, 0.7f, n)), rule.cluster);
                    if (random.NextDouble() > keep)
                        continue;

                    if (point.y < waterY + 0.25f)
                        continue;
                    if (data.GetSteepness(nx, nz) > rule.slope)
                        continue;
                    float sand = Sample(sandMap, mapSize, nx, nz);
                    if (sand > rule.sand + 0.01f && sand > 0.05f)
                        continue;
                    if (keepClear.Any(c => (c - point).sqrMagnitude < clearStart * clearStart))
                        continue;

                    string path = paths[random.Next(paths.Count)];
                    if (!models.TryGetValue(path, out var model))
                        models[path] = model = Model.Load(path, kind);
                    if (model == null)
                        continue;

                    float scale = model.baseScale * Mathf.Lerp(rule.scale.x, rule.scale.y, (float)random.NextDouble());
                    float radius = model.radius * scale;
                    if (Crowded(grid, point, radius) || Obstructed(point, radius, model.height * scale, root.transform))
                        continue;

                    if (!groups.TryGetValue(kind, out var group))
                    {
                        group = new GameObject(kind + "s").transform;
                        group.SetParent(root.transform, false);
                        groups[kind] = group;
                    }

                    var normal = data.GetInterpolatedNormal(nx, nz);
                    float yaw = (float)random.NextDouble() * 360f;
                    // Palms stand upright; low growth and rocks lean with the ground.
                    var up = kind == Kind.Palm ? Vector3.up : Vector3.Slerp(Vector3.up, normal, kind == Kind.Boulder ? 0.8f : 0.5f);
                    var rotation = Quaternion.FromToRotation(Vector3.up, up) * Quaternion.Euler(0f, yaw, 0f);
                    // Rocks sit a little into the ground.
                    var at = point - up * (kind == Kind.Boulder ? model.height * scale * 0.15f : 0.02f);

                    model.Place(group, at, rotation, scale);
                    Add(grid, new Placed { position = point, radius = radius });
                    placed++;
                }
                counts[kind] = placed;
            }

            // Colliders on the solid kinds: palm trunks (not fronds) and rocks.
            foreach (var (kind, group) in groups)
                if (Rules[kind].solid)
                    MeshColliderTool.AddCollidersTo(group.gameObject);

            GameObjectUtility.SetStaticEditorFlags(root, 0);
            foreach (var (kind, group) in groups)
                if (kind != Kind.Palm)
                    foreach (Transform child in group)
                        GameObjectUtility.SetStaticEditorFlags(child.gameObject, StaticEditorFlags.BatchingStatic |
                            StaticEditorFlags.OccluderStatic | StaticEditorFlags.OccludeeStatic | StaticEditorFlags.ContributeGI);

            AssetDatabase.SaveAssets();
            EditorSceneManager.MarkSceneDirty(root.scene);
            Debug.Log("[Vegetation] Scattered " + string.Join(", ", counts.Select(c => $"{c.Value} {c.Key.ToString().ToLowerInvariant()}s")) +
                      ". Re-bake the NavMesh (WoW Sandbox → Bake NavMesh) so NPCs path around trunks and rocks, then save the scene.", root);
        }

        static void Clear(bool report)
        {
            int removed = 0;
            foreach (var t in FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (t != null && t.parent == null && t.name == RootName)
                {
                    Undo.DestroyObjectImmediate(t.gameObject);
                    removed++;
                }
            }
            if (report)
                Debug.Log(removed > 0 ? "[Vegetation] Cleared the scattered vegetation." : "[Vegetation] Nothing scattered to clear.");
        }

        /// <summary>Places to keep open: the player's start, the vendor, the wreck.</summary>
        static List<Vector3> KeepClearPoints()
        {
            var points = new List<Vector3>();
            foreach (var player in FindObjectsByType<WowCharacterController>(FindObjectsSortMode.None))
                points.Add(player.transform.position);
            foreach (var vendor in FindObjectsByType<Vendor>(FindObjectsSortMode.None))
                points.Add(vendor.transform.position);
            return points;
        }

        static bool Crowded(Dictionary<Vector2Int, List<Placed>> grid, Vector3 point, float radius)
        {
            var cell = Cell(point);
            for (int dx = -2; dx <= 2; dx++)
            for (int dz = -2; dz <= 2; dz++)
            {
                if (!grid.TryGetValue(cell + new Vector2Int(dx, dz), out var list))
                    continue;
                foreach (var other in list)
                {
                    float gap = (other.radius + radius) * 0.75f;
                    if ((other.position - point).sqrMagnitude < gap * gap)
                        return true;
                }
            }
            return false;
        }

        static void Add(Dictionary<Vector2Int, List<Placed>> grid, Placed placed)
        {
            var cell = Cell(placed.position);
            if (!grid.TryGetValue(cell, out var list))
                grid[cell] = list = new List<Placed>();
            list.Add(placed);
        }

        static Vector2Int Cell(Vector3 p) => new(Mathf.FloorToInt(p.x / 3f), Mathf.FloorToInt(p.z / 3f));

        /// <summary>Something already standing there that isn't the ground: a building, the ship, a fence, an NPC.</summary>
        static bool Obstructed(Vector3 point, float radius, float height, Transform ours)
        {
            var bottom = point + Vector3.up * (radius + 0.3f);
            var top = point + Vector3.up * Mathf.Max(height, radius + 0.6f);
            foreach (var hit in Physics.OverlapCapsule(bottom, top, Mathf.Max(radius * 0.6f, 0.3f), ~0, QueryTriggerInteraction.Ignore))
            {
                if (hit is TerrainCollider || hit.transform.IsChildOf(ours))
                    continue;
                if (hit.GetComponentInParent<WaterVolume>() != null)
                    continue;
                return true;
            }
            return false;
        }

        static float[,] SandWeights(TerrainData data, out int size)
        {
            size = data.alphamapResolution;
            var sandLayer = AssetDatabase.LoadAssetAtPath<TerrainLayer>(SandLayerPath);
            int index = sandLayer != null ? System.Array.IndexOf(data.terrainLayers, sandLayer) : -1;
            if (index < 0)
                return null;
            var maps = data.GetAlphamaps(0, 0, size, size);
            var sand = new float[size, size];
            for (int z = 0; z < size; z++)
                for (int x = 0; x < size; x++)
                    sand[z, x] = maps[z, x, index];
            return sand;
        }

        static float Sample(float[,] map, int size, float nx, float nz)
        {
            if (map == null)
                return 0f;
            int x = Mathf.Clamp(Mathf.RoundToInt(nx * (size - 1)), 0, size - 1);
            int z = Mathf.Clamp(Mathf.RoundToInt(nz * (size - 1)), 0, size - 1);
            return map[z, x];
        }

        // --- One model ---------------------------------------------------------------------

        /// <summary>An export ready to place: its footprint, its size relative to ones already in the scene, its meshes.</summary>
        class Model
        {
            public GameObject prefab;
            public Kind kind;
            public float radius, height, baseScale = 1f;
            public List<(Mesh mesh, Material[] materials, Matrix4x4 local)> parts = new();

            public static Model Load(string path, Kind kind)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab == null)
                    return null;
                var model = new Model { prefab = prefab, kind = kind };

                var bounds = new Bounds();
                bool first = true;
                foreach (var renderer in prefab.GetComponentsInChildren<Renderer>(true))
                {
                    var mesh = renderer is SkinnedMeshRenderer skin ? skin.sharedMesh
                        : renderer.GetComponent<MeshFilter>() is { } filter ? filter.sharedMesh : null;
                    if (mesh == null)
                        continue;
                    var local = prefab.transform.worldToLocalMatrix * renderer.transform.localToWorldMatrix;
                    model.parts.Add((mesh, renderer.sharedMaterials.Select(m => Cutout(m, kind)).ToArray(), local));
                    var b = TransformBounds(mesh.bounds, local);
                    if (first) { bounds = b; first = false; }
                    else bounds.Encapsulate(b);
                }
                if (first)
                    return null;

                model.radius = Mathf.Max(0.3f, Mathf.Max(bounds.extents.x, bounds.extents.z));
                model.height = Mathf.Max(0.3f, bounds.max.y);
                // Match the size of copies already placed by hand (the palms), if any.
                var existing = FindObjectsByType<Transform>(FindObjectsSortMode.None)
                    .Where(t => t.root.name != RootName && PrefabUtility.IsAnyPrefabInstanceRoot(t.gameObject) &&
                                PrefabUtility.GetCorrespondingObjectFromSource(t.gameObject) == prefab)
                    .Select(t => t.lossyScale.y).ToList();
                if (existing.Count > 0)
                    model.baseScale = existing.Average();
                return model;
            }

            public void Place(Transform parent, Vector3 position, Quaternion rotation, float scale)
            {
                if (kind == Kind.Palm)
                {
                    // Whole prefab, so TreeSway can rock its bone.
                    var palm = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
                    palm.transform.SetPositionAndRotation(position, rotation);
                    palm.transform.localScale = Vector3.one * scale;
                    palm.AddComponent<TreeSway>();
                    return;
                }

                var go = new GameObject(prefab.name);
                go.transform.SetParent(parent, false);
                go.transform.SetPositionAndRotation(position, rotation);
                go.transform.localScale = Vector3.one * scale;
                foreach (var (mesh, materials, local) in parts)
                {
                    var part = new GameObject(mesh.name);
                    part.transform.SetParent(go.transform, false);
                    part.transform.localPosition = local.GetColumn(3);
                    part.transform.localRotation = local.rotation;
                    part.transform.localScale = local.lossyScale;
                    part.AddComponent<MeshFilter>().sharedMesh = mesh;
                    part.AddComponent<MeshRenderer>().sharedMaterials = materials;
                }
            }

            static Bounds TransformBounds(Bounds b, Matrix4x4 m)
            {
                var result = new Bounds(m.MultiplyPoint3x4(b.center), Vector3.zero);
                for (int i = 0; i < 8; i++)
                {
                    var corner = b.center + Vector3.Scale(b.extents, new Vector3(i % 2 == 0 ? -1 : 1, (i / 2) % 2 == 0 ? -1 : 1, i / 4 == 0 ? -1 : 1));
                    result.Encapsulate(m.MultiplyPoint3x4(corner));
                }
                return result;
            }
        }

        /// <summary>
        /// A cut-out, two-sided copy of a leafy material whose texture has see-through pixels,
        /// for the kinds that are leaves. Rocks and wood keep theirs.
        /// </summary>
        static Material Cutout(Material source, Kind kind)
        {
            if (source == null || kind == Kind.Boulder || kind == Kind.Driftwood || kind == Kind.Palm)
                return source;
            var texture = BaseTexture(source);
            if (texture == null || !HasSeeThrough(texture))
                return source;

            Directory.CreateDirectory(OutputDir);
            string path = $"{OutputDir}/{source.name}_Cutout.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
                material = new Material(shader) { name = source.name + "_Cutout" };
                AssetDatabase.CreateAsset(material, path);
            }
            material.SetTexture("_BaseMap", texture);
            material.mainTexture = texture;
            material.SetColor("_BaseColor", Color.white);
            material.SetFloat("_AlphaClip", 1f);
            material.SetFloat("_Cutoff", 0.5f);
            material.EnableKeyword("_ALPHATEST_ON");
            material.SetFloat("_Cull", 0f);
            material.SetFloat("_Smoothness", 0.05f);
            material.enableInstancing = true;
            material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.AlphaTest;
            EditorUtility.SetDirty(material);
            return material;
        }

        static Texture2D BaseTexture(Material material)
        {
            foreach (string name in new[] { "baseColorTexture", "_BaseMap", "_MainTex" })
                if (material.HasProperty(name) && material.GetTexture(name) is Texture2D texture)
                    return texture;
            return material.mainTexture as Texture2D;
        }

        static readonly Dictionary<string, bool> SeeThroughCache = new();

        /// <summary>Whether a texture's own file has a meaningful number of transparent pixels.</summary>
        static bool HasSeeThrough(Texture2D texture)
        {
            string path = AssetDatabase.GetAssetPath(texture);
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
                return false;
            if (SeeThroughCache.TryGetValue(path, out bool cached))
                return cached;
            var copy = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            bool clear = false;
            if (copy.LoadImage(File.ReadAllBytes(path)))
            {
                var pixels = copy.GetPixels32();
                int transparent = 0;
                for (int i = 0; i < pixels.Length; i += 7)
                    if (pixels[i].a < 128) transparent++;
                clear = transparent > pixels.Length / 7 * 0.05f;
            }
            DestroyImmediate(copy);
            return SeeThroughCache[path] = clear;
        }

        // --- Grass carpet ------------------------------------------------------------------

        void PaintGrass()
        {
            var terrain = Terrain.activeTerrain != null ? Terrain.activeTerrain : FindAnyObjectByType<Terrain>();
            var water = FindAnyObjectByType<WaterVolume>();
            if (terrain == null || water == null)
            {
                Debug.LogWarning("[Vegetation] Needs the terrain and the water in the scene.");
                return;
            }
            var data = terrain.terrainData;
            Undo.RegisterCompleteObjectUndo(data, "Paint Grass");

            var texture = grassTexture != null ? grassTexture : GeneratedGrass();
            var prototypes = data.detailPrototypes.Where(p => p.prototypeTexture == null ||
                                                            (p.prototypeTexture.name != GrassTextureName && p.prototypeTexture != grassTexture)).ToList();
            int index = prototypes.Count;
            prototypes.Add(new DetailPrototype
            {
                prototypeTexture = texture,
                renderMode = DetailRenderMode.GrassBillboard,
                usePrototypeMesh = false,
                minWidth = grassHeight * 0.8f,
                maxWidth = grassHeight * 1.4f,
                minHeight = grassHeight * 0.7f,
                maxHeight = grassHeight * 1.3f,
                noiseSpread = 0.3f,
                healthyColor = new Color(0.75f, 0.9f, 0.55f),
                dryColor = new Color(0.8f, 0.78f, 0.5f),
            });
            data.detailPrototypes = prototypes.ToArray();
            if (data.detailResolution < 1024)
                data.SetDetailResolution(1024, 32);
            // Each cell's number is a count of tufts (up to 255), not a 0–16 coverage.
            data.SetDetailScatterMode(DetailScatterMode.InstanceCountMode);

            int size = data.detailResolution;
            var sandMap = SandWeights(data, out int mapSize);
            var layer = new int[size, size];
            float waterY = water.SurfaceY;
            var origin = terrain.transform.position;
            int painted = 0;
            for (int z = 0; z < size; z++)
            {
                for (int x = 0; x < size; x++)
                {
                    float nx = (x + 0.5f) / size, nz = (z + 0.5f) / size;
                    float height = origin.y + data.GetInterpolatedHeight(nx, nz);
                    if (height < waterY + 0.3f || data.GetSteepness(nx, nz) > 35f)
                        continue;
                    float ground = 1f - Sample(sandMap, mapSize, nx, nz);
                    if (ground < 0.6f)
                        continue;
                    float wx = origin.x + nx * data.size.x, wz = origin.z + nz * data.size.z;
                    float patch = Mathf.PerlinNoise(wx * 0.04f + seed, wz * 0.04f);
                    float clump = Mathf.Lerp(1f, Mathf.SmoothStep(0.15f, 1f, patch), grassPatchiness);
                    // A little jitter so cells don't read as a grid.
                    float jitter = 0.85f + 0.3f * Mathf.PerlinNoise(wx * 0.7f, wz * 0.7f + seed);
                    int count = Mathf.Clamp(Mathf.RoundToInt(grassDensity * 4f * ground * clump * jitter), 0, 255);
                    if (count > 0) painted++;
                    layer[z, x] = count;
                }
            }
            data.SetDetailLayer(0, 0, index, layer);

            data.wavingGrassStrength = 0.4f;
            data.wavingGrassSpeed = 0.5f;
            data.wavingGrassAmount = 0.4f;
            data.wavingGrassTint = new Color(0.8f, 0.9f, 0.7f);
            terrain.detailObjectDistance = grassDistance;
            terrain.detailObjectDensity = 1f;
            EditorUtility.SetDirty(data);
            EditorUtility.SetDirty(terrain);
            AssetDatabase.SaveAssets();
            EditorSceneManager.MarkSceneDirty(terrain.gameObject.scene);
            Debug.Log($"[Vegetation] Painted grass on {100f * painted / (size * size):0}% of the terrain. Save the scene.", terrain);
        }

        /// <summary>A few tapering blades with see-through gaps, for the grass carpet.</summary>
        static Texture2D GeneratedGrass()
        {
            string path = $"Assets/Terrain/{GrassTextureName}.asset";
            // Redrawn every time, into the same asset, so the terrain keeps pointing at it.
            var existing = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            Directory.CreateDirectory("Assets/Terrain");
            const int w = 64, h = 64;
            var texture = existing != null ? existing
                : new Texture2D(w, h, TextureFormat.RGBA32, true) { name = GrassTextureName };
            if (existing != null)
                texture.Reinitialize(w, h, TextureFormat.RGBA32, true);
            texture.wrapMode = TextureWrapMode.Clamp;
            var pixels = new Color[w * h];
            var random = new System.Random(3);
            // Many blades per tuft, so even a few tufts read as thick grass.
            for (int blade = 0; blade < 22; blade++)
            {
                float baseX = 6f + (float)random.NextDouble() * (w - 12f);
                float lean = ((float)random.NextDouble() - 0.5f) * 18f;
                float top = h * (0.55f + (float)random.NextDouble() * 0.45f);
                float shade = 0.75f + (float)random.NextDouble() * 0.25f;
                for (int y = 0; y < top; y++)
                {
                    float t = y / top;
                    float cx = baseX + lean * t * t;
                    float half = Mathf.Lerp(2.2f, 0.4f, t);
                    for (int x = Mathf.FloorToInt(cx - half); x <= Mathf.CeilToInt(cx + half); x++)
                    {
                        if (x < 0 || x >= w) continue;
                        var c = Color.Lerp(new Color(0.22f, 0.38f, 0.12f), new Color(0.52f, 0.7f, 0.3f), t) * shade;
                        c.a = 1f;
                        pixels[y * w + x] = c;
                    }
                }
            }
            texture.SetPixels(pixels);
            texture.Apply();
            if (existing == null)
                AssetDatabase.CreateAsset(texture, path);
            else
                EditorUtility.SetDirty(texture);
            return texture;
        }
    }
}
