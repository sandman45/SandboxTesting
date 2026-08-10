using System.IO;
using UnityEditor;
using UnityEngine;

namespace WowSandbox.EditorTools
{
    /// <summary>
    /// Generates a procedural Unity Terrain to stand on, so the sandbox has ground
    /// without needing a WoW ADT export. Everything it produces is self-contained —
    /// the ground texture is generated in code, so there are no external asset
    /// dependencies and nothing WoW-derived is written.
    /// </summary>
    public class TerrainGenerator : EditorWindow
    {
        const string OutputRoot = "Assets/Terrain";

        int _size = 500;                // world units square
        int _heightmapResolution = 513; // must be 2^n + 1
        float _maxHeight = 45f;

        float _noiseScale = 220f;
        int _octaves = 5;
        float _persistence = 0.45f;
        float _lacunarity = 2.1f;
        int _seed = 1337;

        bool _flattenSpawn = true;
        float _spawnRadius = 30f;
        bool _movePlayerToSurface = true;

        /// <summary>
        /// Strips the gloss from terrain already in the scene, without regenerating it
        /// (which would orphan the baked NavMesh and everything standing on it).
        ///
        /// TerrainLayer's own Smoothness value is ignored when SmoothnessSource is set to
        /// read from the diffuse alpha, which is the default. So the fix is in the texture,
        /// not the slider: zero the albedo's alpha channel.
        /// </summary>
        [MenuItem("WoW Sandbox/Fix Terrain Gloss")]
        public static void FixTerrainGloss()
        {
            int fixedLayers = 0;

            foreach (var terrain in Object.FindObjectsByType<Terrain>(FindObjectsSortMode.None))
            {
                var data = terrain.terrainData;
                if (data == null)
                    continue;

                foreach (var layer in data.terrainLayers)
                {
                    if (layer == null)
                        continue;

                    layer.smoothness = 0f;
                    layer.metallic = 0f;
                    EditorUtility.SetDirty(layer);

                    if (layer.diffuseTexture is not Texture2D texture)
                        continue;

                    if (!texture.isReadable)
                    {
                        Debug.LogWarning($"[TerrainGenerator] \"{texture.name}\" isn't readable, so its " +
                                         "alpha can't be rewritten. Regenerate the terrain instead.", texture);
                        continue;
                    }

                    var pixels = texture.GetPixels();
                    for (int i = 0; i < pixels.Length; i++)
                        pixels[i].a = 0f;

                    texture.SetPixels(pixels);
                    texture.Apply();
                    EditorUtility.SetDirty(texture);
                    fixedLayers++;
                }
            }

            AssetDatabase.SaveAssets();
            Debug.Log($"[TerrainGenerator] Removed gloss from {fixedLayers} terrain layer(s).");
        }

        [MenuItem("WoW Sandbox/Generate Terrain")]
        public static void ShowWindow()
        {
            var window = GetWindow<TerrainGenerator>(true, "Generate Terrain");
            window.minSize = new Vector2(340f, 420f);
        }

        void OnGUI()
        {
            EditorGUILayout.LabelField("Size", EditorStyles.boldLabel);
            _size = EditorGUILayout.IntSlider("Size (units)", _size, 100, 2000);
            _maxHeight = EditorGUILayout.Slider("Max height", _maxHeight, 1f, 300f);
            _heightmapResolution = EditorGUILayout.IntPopup("Heightmap res",
                _heightmapResolution,
                new[] { "129", "257", "513", "1025", "2049" },
                new[] { 129, 257, 513, 1025, 2049 });

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Shape", EditorStyles.boldLabel);
            _noiseScale = EditorGUILayout.Slider("Feature size", _noiseScale, 20f, 600f);
            _octaves = EditorGUILayout.IntSlider("Octaves", _octaves, 1, 8);
            _persistence = EditorGUILayout.Slider("Persistence", _persistence, 0.1f, 0.9f);
            _lacunarity = EditorGUILayout.Slider("Lacunarity", _lacunarity, 1.5f, 4f);
            _seed = EditorGUILayout.IntField("Seed", _seed);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Spawn", EditorStyles.boldLabel);
            _flattenSpawn = EditorGUILayout.Toggle("Flatten spawn area", _flattenSpawn);
            using (new EditorGUI.DisabledScope(!_flattenSpawn))
                _spawnRadius = EditorGUILayout.Slider("Spawn radius", _spawnRadius, 5f, 150f);
            _movePlayerToSurface = EditorGUILayout.Toggle("Drop player on it", _movePlayerToSurface);

            EditorGUILayout.Space();
            if (GUILayout.Button("Generate", GUILayout.Height(32f)))
                Generate();

            EditorGUILayout.Space();
            EditorGUILayout.HelpBox(
                "Terrain assets are written to " + OutputRoot + ". They're regenerable from the " +
                "seed above, so you may prefer to gitignore that folder rather than commit it.",
                MessageType.Info);
        }

        void Generate()
        {
            Directory.CreateDirectory(OutputRoot);

            var terrainData = new TerrainData
            {
                heightmapResolution = _heightmapResolution,
                baseMapResolution = 1024,
                alphamapResolution = 512
            };
            // Set size after heightmapResolution — resolution changes reset the size.
            terrainData.size = new Vector3(_size, _maxHeight, _size);

            terrainData.SetHeights(0, 0, BuildHeights(_heightmapResolution));
            terrainData.terrainLayers = new[] { BuildGroundLayer() };

            string dataPath = AssetDatabase.GenerateUniqueAssetPath($"{OutputRoot}/SandboxTerrain.asset");
            AssetDatabase.CreateAsset(terrainData, dataPath);

            var go = Terrain.CreateTerrainGameObject(terrainData);
            go.name = "SandboxTerrain";
            // Centre the terrain on the origin so the player spawns in the middle.
            go.transform.position = new Vector3(-_size * 0.5f, 0f, -_size * 0.5f);

            var terrain = go.GetComponent<Terrain>();
            var urpTerrain = Shader.Find("Universal Render Pipeline/Terrain/Lit");
            if (urpTerrain != null)
            {
                var material = new Material(urpTerrain);
                AssetDatabase.CreateAsset(material,
                    AssetDatabase.GenerateUniqueAssetPath($"{OutputRoot}/SandboxTerrain.mat"));
                terrain.materialTemplate = material;
            }

            Undo.RegisterCreatedObjectUndo(go, "Generate Terrain");

            if (_movePlayerToSurface)
                DropPlayer(terrain);

            AssetDatabase.SaveAssets();
            Selection.activeGameObject = go;
            Debug.Log($"[TerrainGenerator] Generated {_size}x{_size} terrain (max height {_maxHeight}) at {dataPath}.");
        }

        /// <summary>Layered value noise — each octave adds finer detail at lower amplitude.</summary>
        float[,] BuildHeights(int resolution)
        {
            var random = new System.Random(_seed);
            // Random per-octave offsets so different seeds give genuinely different shapes.
            var offsets = new Vector2[_octaves];
            for (int i = 0; i < _octaves; i++)
                offsets[i] = new Vector2(random.Next(-10000, 10000), random.Next(-10000, 10000));

            var heights = new float[resolution, resolution];
            float centre = (resolution - 1) * 0.5f;
            float spawnInHeightmap = _spawnRadius / _size * (resolution - 1);

            float min = float.MaxValue, max = float.MinValue;

            for (int y = 0; y < resolution; y++)
            {
                for (int x = 0; x < resolution; x++)
                {
                    float amplitude = 1f, frequency = 1f, value = 0f, totalAmplitude = 0f;

                    for (int o = 0; o < _octaves; o++)
                    {
                        // Scale into world units first so "feature size" means something real.
                        float sampleX = (x / (float)(resolution - 1) * _size + offsets[o].x) / _noiseScale * frequency;
                        float sampleY = (y / (float)(resolution - 1) * _size + offsets[o].y) / _noiseScale * frequency;

                        value += Mathf.PerlinNoise(sampleX, sampleY) * amplitude;
                        totalAmplitude += amplitude;
                        amplitude *= _persistence;
                        frequency *= _lacunarity;
                    }

                    value /= totalAmplitude;
                    heights[y, x] = value;
                    min = Mathf.Min(min, value);
                    max = Mathf.Max(max, value);
                }
            }

            // Normalise to use the full height range, then carve out the spawn pad.
            float range = Mathf.Max(max - min, 0.0001f);
            float spawnHeight = (heights[(int)centre, (int)centre] - min) / range;

            for (int y = 0; y < resolution; y++)
            {
                for (int x = 0; x < resolution; x++)
                {
                    float h = (heights[y, x] - min) / range;

                    if (_flattenSpawn && spawnInHeightmap > 1f)
                    {
                        float distance = Vector2.Distance(new Vector2(x, y), new Vector2(centre, centre));
                        // Flat in the middle, easing back into the terrain over twice the radius.
                        float blend = Mathf.SmoothStep(0f, 1f,
                            Mathf.InverseLerp(spawnInHeightmap, spawnInHeightmap * 2f, distance));
                        h = Mathf.Lerp(spawnHeight, h, blend);
                    }

                    heights[y, x] = h;
                }
            }

            return heights;
        }

        /// <summary>
        /// A TerrainLayer needs a diffuse texture, so generate a mottled green one rather
        /// than depending on an imported asset.
        /// </summary>
        TerrainLayer BuildGroundLayer()
        {
            const int texSize = 256;
            var texture = new Texture2D(texSize, texSize, TextureFormat.RGBA32, true);
            var random = new System.Random(_seed);
            float offset = random.Next(0, 10000);

            var pixels = new Color[texSize * texSize];
            for (int y = 0; y < texSize; y++)
            {
                for (int x = 0; x < texSize; x++)
                {
                    float n = Mathf.PerlinNoise((x + offset) * 0.08f, (y + offset) * 0.08f);
                    float fine = Mathf.PerlinNoise((x + offset) * 0.31f, (y + offset) * 0.31f);
                    float t = Mathf.Clamp01(n * 0.7f + fine * 0.3f);
                    var ground = Color.Lerp(
                        new Color(0.29f, 0.36f, 0.18f),
                        new Color(0.46f, 0.53f, 0.28f),
                        t);
                    // Alpha is smoothness here, not transparency: with no mask map, URP's
                    // terrain shader reads gloss from the albedo's alpha. Leaving it at 1
                    // makes the ground look like polished glass.
                    ground.a = 0f;
                    pixels[y * texSize + x] = ground;
                }
            }

            texture.SetPixels(pixels);
            texture.Apply();

            string texturePath = AssetDatabase.GenerateUniqueAssetPath($"{OutputRoot}/GroundAlbedo.asset");
            AssetDatabase.CreateAsset(texture, texturePath);

            var layer = new TerrainLayer
            {
                diffuseTexture = texture,
                tileSize = new Vector2(12f, 12f),
                // Dirt and grass are rough. Belt and braces alongside the alpha above.
                smoothness = 0f,
                metallic = 0f
            };
            AssetDatabase.CreateAsset(layer,
                AssetDatabase.GenerateUniqueAssetPath($"{OutputRoot}/GroundLayer.terrainlayer"));

            return layer;
        }

        /// <summary>Puts the player just above the terrain surface at the spawn point.</summary>
        static void DropPlayer(Terrain terrain)
        {
            var player = Object.FindFirstObjectByType<WowCharacterController>();
            if (player == null)
            {
                Debug.LogWarning("[TerrainGenerator] No WowCharacterController in the scene to reposition.");
                return;
            }

            Vector3 position = player.transform.position;
            position.x = 0f;
            position.z = 0f;
            position.y = terrain.SampleHeight(new Vector3(0f, 0f, 0f)) + terrain.transform.position.y + 0.5f;

            // CharacterController overrides direct transform writes, so disable it first.
            var controller = player.GetComponent<CharacterController>();
            bool wasEnabled = controller != null && controller.enabled;
            if (controller != null)
                controller.enabled = false;

            Undo.RecordObject(player.transform, "Drop Player On Terrain");
            player.transform.position = position;

            if (controller != null)
                controller.enabled = wasEnabled;

            Debug.Log($"[TerrainGenerator] Moved {player.name} to {position}.");
        }
    }
}
