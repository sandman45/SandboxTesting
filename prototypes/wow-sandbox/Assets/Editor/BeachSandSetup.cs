using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace WowSandbox.EditorTools
{
    /// <summary>
    /// Paints sand onto the island's terrain where it meets the sea: everything under the water
    /// and a band just above it, fading into the ground above over a ragged edge. Works on the
    /// terrain in place — its shape, the NavMesh and everything standing on it are untouched —
    /// and leaves the higher ground exactly as painted. Re-run to change the band; it reuses
    /// its sand layer.
    ///
    /// The sand is generated in code to match the seabed's (SeabedGenerator), so the shallows
    /// and the open-sea floor agree; pick an exported WoW ground texture instead for real WoW sand.
    /// </summary>
    public class BeachSandSetup : EditorWindow
    {
        const string OutputRoot = "Assets/Terrain";
        const string LayerPath = OutputRoot + "/BeachSandLayer.terrainlayer";
        const string TexturePath = OutputRoot + "/BeachSand.asset";

        [SerializeField] float beachHeight = 1.2f;
        [SerializeField] float blend = 0.8f;
        [SerializeField] float raggedness = 0.6f;
        [SerializeField] float tileSize = 8f;
        [SerializeField] Texture2D wowTexture;

        [MenuItem("WoW Sandbox/Paint Beach Sand")]
        static void Open()
        {
            var window = GetWindow<BeachSandSetup>(true, "Paint Beach Sand");
            window.minSize = new Vector2(380f, 230f);
            // Pick up the layer's current settings, so a re-run starts where the last left off.
            var layer = AssetDatabase.LoadAssetAtPath<TerrainLayer>(LayerPath);
            if (layer != null)
            {
                window.tileSize = layer.tileSize.x;
                if (layer.diffuseTexture is Texture2D texture && AssetDatabase.GetAssetPath(texture) != TexturePath)
                    window.wowTexture = texture;
            }
        }

        void OnGUI()
        {
            EditorGUILayout.HelpBox("Sand under the water and a band above it, blending into the ground above. " +
                                    "Higher ground keeps its own paint.", MessageType.Info);
            beachHeight = EditorGUILayout.FloatField(new GUIContent("Beach height",
                "How far above the water the sand reaches, in world units."), beachHeight);
            blend = EditorGUILayout.FloatField(new GUIContent("Blend",
                "Height over which sand fades into the ground above."), blend);
            raggedness = EditorGUILayout.FloatField(new GUIContent("Raggedness",
                "How far the edge wanders up and down, so it isn't a contour line."), raggedness);
            tileSize = EditorGUILayout.FloatField(new GUIContent("Tile size", "World units per texture repeat."), tileSize);
            wowTexture = (Texture2D)EditorGUILayout.ObjectField(new GUIContent("WoW texture (optional)",
                "An exported WoW ground texture (Textures tab, e.g. a tileset sand). Empty uses generated sand."),
                wowTexture, typeof(Texture2D), false);

            GUILayout.Space(8f);
            if (GUILayout.Button("Paint", GUILayout.Height(28f)))
                Paint();
        }

        void Paint()
        {
            var terrain = Terrain.activeTerrain != null ? Terrain.activeTerrain : FindAnyObjectByType<Terrain>();
            var water = FindAnyObjectByType<WaterVolume>();
            if (terrain == null || terrain.terrainData == null)
            {
                Debug.LogWarning("[BeachSand] No terrain in the scene.");
                return;
            }
            if (water == null)
            {
                Debug.LogWarning("[BeachSand] No WaterVolume in the scene, so there's no waterline to paint from.");
                return;
            }

            var data = terrain.terrainData;
            Undo.RegisterCompleteObjectUndo(data, "Paint Beach Sand");

            // --- The sand layer, added once and reused ------------------------------------
            var layer = SandLayer();
            var layers = data.terrainLayers;
            int sand = System.Array.IndexOf(layers, layer);
            if (sand < 0)
            {
                // Adding a layer keeps the existing paint; the new one starts at zero weight.
                sand = layers.Length;
                var grown = new TerrainLayer[layers.Length + 1];
                layers.CopyTo(grown, 0);
                grown[sand] = layer;
                data.terrainLayers = grown;
            }

            // --- Weights ---------------------------------------------------------------
            int size = data.alphamapResolution;
            int count = data.alphamapLayers;
            var maps = data.GetAlphamaps(0, 0, size, size);
            float waterY = water.SurfaceY;
            float baseY = terrain.transform.position.y;
            const float seed = 41.3f;
            int sandy = 0;

            for (int z = 0; z < size; z++)
            {
                for (int x = 0; x < size; x++)
                {
                    float nx = x / (float)(size - 1);
                    float nz = z / (float)(size - 1);
                    float height = baseY + data.GetInterpolatedHeight(nx, nz);

                    // The edge wanders: two octaves of noise across the island.
                    float wx = nx * data.size.x, wz = nz * data.size.z;
                    float wander = (Mathf.PerlinNoise(wx * 0.05f + seed, wz * 0.05f) - 0.5f) * 2f * raggedness
                                 + (Mathf.PerlinNoise(wx * 0.21f, wz * 0.21f + seed) - 0.5f) * raggedness * 0.5f;
                    float top = waterY + beachHeight + wander;
                    float weight = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(top - blend * 0.5f, top + blend * 0.5f, height));

                    // Sand takes its share; every other layer keeps its proportions in the rest.
                    float others = 0f;
                    for (int l = 0; l < count; l++)
                        if (l != sand) others += maps[z, x, l];
                    for (int l = 0; l < count; l++)
                    {
                        if (l == sand)
                            maps[z, x, l] = weight;
                        else
                            maps[z, x, l] = others > 0f ? maps[z, x, l] / others * (1f - weight) : (1f - weight) / Mathf.Max(count - 1, 1);
                    }
                    if (weight > 0.5f)
                        sandy++;
                }
            }

            data.SetAlphamaps(0, 0, maps);
            EditorUtility.SetDirty(data);
            AssetDatabase.SaveAssets();
            EditorSceneManager.MarkSceneDirty(terrain.gameObject.scene);
            Debug.Log($"[BeachSand] Painted sand up to {beachHeight:0.##} above the water (±{raggedness:0.##}), " +
                      $"{100f * sandy / (size * size):0}% of the terrain. Save the scene.", terrain);
        }

        TerrainLayer SandLayer()
        {
            Directory.CreateDirectory(OutputRoot);
            var layer = AssetDatabase.LoadAssetAtPath<TerrainLayer>(LayerPath);
            if (layer == null)
            {
                layer = new TerrainLayer();
                AssetDatabase.CreateAsset(layer, LayerPath);
            }

            layer.diffuseTexture = wowTexture != null ? MatteCopy(wowTexture) : GeneratedSand();
            layer.tileSize = new Vector2(tileSize, tileSize);
            layer.smoothness = 0f;
            layer.metallic = 0f;
            EditorUtility.SetDirty(layer);
            return layer;
        }

        /// <summary>
        /// Dry, pale sand mottled darker, like the seabed's. Its alpha is zero: URP's terrain
        /// shader reads smoothness from the albedo's alpha (see Fix Terrain Gloss).
        /// </summary>
        static Texture2D GeneratedSand()
        {
            const int size = 256;
            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(TexturePath);
            if (texture != null)
                return texture;

            texture = new Texture2D(size, size, TextureFormat.RGBA32, true) { name = "BeachSand" };
            var light = new Color(0.78f, 0.7f, 0.52f);
            var dark = new Color(0.6f, 0.53f, 0.38f);
            var pixels = new Color[size * size];
            var random = new System.Random(7);
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float coarse = TiledNoise(x, y, size, 0.035f) * 0.7f + TiledNoise(x, y, size, 0.12f) * 0.3f;
                    float grain = (float)random.NextDouble();
                    var c = Color.Lerp(dark, light, Mathf.Clamp01(coarse * 0.75f + grain * 0.25f));
                    c.a = 0f;
                    pixels[y * size + x] = c;
                }
            }
            texture.SetPixels(pixels);
            texture.Apply();
            AssetDatabase.CreateAsset(texture, TexturePath);
            return texture;
        }

        /// <summary>Perlin noise that repeats every <paramref name="size"/> pixels, so the texture tiles without seams.</summary>
        static float TiledNoise(int x, int y, int size, float scale)
        {
            float u = x / (float)size, v = y / (float)size;
            float N(float px, float py) => Mathf.PerlinNoise(px * scale + 11f, py * scale + 7f);
            return N(x, y) * (1f - u) * (1f - v) + N(x - size, y) * u * (1f - v)
                 + N(x, y - size) * (1f - u) * v + N(x - size, y - size) * u * v;
        }

        /// <summary>
        /// A copy of a WoW ground texture with its alpha cleared: WoW keeps specular there, which
        /// URP's terrain would read as smoothness and turn the beach to glass.
        /// </summary>
        static Texture2D MatteCopy(Texture2D source)
        {
            // WoW-derived, so it lives with the exports (gitignored), not in Assets/Terrain.
            const string folder = "Assets/WowExports/_Generated/Terrain";
            Directory.CreateDirectory(folder);
            string path = $"{folder}/BeachSand_{source.name}.asset";
            var existing = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (existing != null)
                return existing;

            string sourcePath = AssetDatabase.GetAssetPath(source);
            var bytes = File.ReadAllBytes(sourcePath);
            var readable = new Texture2D(2, 2, TextureFormat.RGBA32, true);
            readable.LoadImage(bytes);
            var pixels = readable.GetPixels32();
            for (int i = 0; i < pixels.Length; i++)
                pixels[i].a = 0;
            readable.SetPixels32(pixels);
            readable.Apply();
            readable.name = $"BeachSand_{source.name}";
            AssetDatabase.CreateAsset(readable, path);
            return readable;
        }
    }
}
