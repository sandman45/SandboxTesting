using System.IO;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace WowSandbox.EditorTools
{
    /// <summary>
    /// Floods the sandbox to a chosen sea level: builds the water mesh, its material and
    /// textures, the gameplay volume the swim code reads, and the navigation cutout that
    /// keeps NPCs on dry land.
    ///
    /// TerrainGenerator normalises its heightmap to the full 0..1 range, so the terrain's
    /// lowest point is always exactly 0. That means a sea level given as a fraction of the
    /// terrain's height behaves predictably across seeds: low values pool in the basins and
    /// leave the hills dry. One number to tune.
    ///
    /// Everything it writes is generated in code — nothing WoW-derived — so the water is
    /// rebuildable from this menu item alone, the same way the terrain is.
    /// </summary>
    public class WaterSetup : EditorWindow
    {
        const string OutputRoot = "Assets/Water";
        const string SurfaceName = "WaterSurface";

        // Level
        float _seaLevel = 0.15f;
        float _margin = 1.1f;
        int _resolution = 128;

        // Look
        Color _shallow = new Color(0.30f, 0.68f, 0.72f, 0.55f);
        Color _deep = new Color(0.02f, 0.16f, 0.30f, 0.95f);
        float _depthMaxDistance = 6f;
        float _foamDistance = 0.7f;
        float _waveAmplitude = 0.15f;
        float _waveLength = 9f;
        float _waveSpeed = 0.8f;
        float _tileA = 12f;
        float _tileB = 25f;
        float _normalStrength = 0.6f;
        float _reflectionStrength = 0.7f;

        // Integration
        bool _addUnderwaterEffect = true;
        bool _blockNavigation = true;
        float _navBlockDepth = 0.5f;

        int _seed = 4242;

        [MenuItem("WoW Sandbox/Setup Water")]
        public static void ShowWindow()
        {
            var window = GetWindow<WaterSetup>(true, "Setup Water");
            window.minSize = new Vector2(360f, 560f);
        }

        void OnGUI()
        {
            var terrain = FindTerrain();

            EditorGUILayout.LabelField("Level", EditorStyles.boldLabel);
            _seaLevel = EditorGUILayout.Slider(
                new GUIContent("Sea level",
                    "Fraction of the terrain's height. The terrain's lowest point is 0, so " +
                    "small values fill just the basins."),
                _seaLevel, 0f, 1f);

            if (terrain != null)
            {
                EditorGUILayout.LabelField(" ", $"world Y = {SurfaceHeight(terrain):F2}",
                    EditorStyles.miniLabel);
            }
            else
            {
                EditorGUILayout.HelpBox("No Terrain in the scene. Run WoW Sandbox → Generate " +
                                        "Terrain first — the water is sized from it.",
                                        MessageType.Warning);
            }

            _margin = EditorGUILayout.Slider(
                new GUIContent("Overhang", "Extends the water past the terrain edge so there's " +
                                           "no visible seam at the horizon."),
                _margin, 1f, 3f);
            _resolution = EditorGUILayout.IntSlider(
                new GUIContent("Grid resolution", "Quads per side. Only matters for the waves — " +
                                                  "a flat plane would need 1."),
                _resolution, 8, 256);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Look", EditorStyles.boldLabel);
            _shallow = EditorGUILayout.ColorField("Shallow", _shallow);
            _deep = EditorGUILayout.ColorField("Deep", _deep);
            _depthMaxDistance = EditorGUILayout.Slider(
                new GUIContent("Depth to full colour", "How deep before the water reaches its " +
                                                       "deep colour."),
                _depthMaxDistance, 1f, 30f);
            _foamDistance = EditorGUILayout.Slider("Foam width", _foamDistance, 0f, 4f);
            _normalStrength = EditorGUILayout.Slider("Ripple strength", _normalStrength, 0f, 2f);
            _tileA = EditorGUILayout.Slider("Ripple size (large)", _tileA, 2f, 60f);
            _tileB = EditorGUILayout.Slider("Ripple size (small)", _tileB, 2f, 60f);
            _reflectionStrength = EditorGUILayout.Slider("Sky reflection", _reflectionStrength, 0f, 1f);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Waves", EditorStyles.boldLabel);
            _waveAmplitude = EditorGUILayout.Slider(
                new GUIContent("Amplitude",
                    "Kept small on purpose: gameplay treats the water as flat at sea level, so " +
                    "tall waves would disagree with where swimming starts."),
                _waveAmplitude, 0f, 1f);
            _waveLength = EditorGUILayout.Slider("Wave length", _waveLength, 1f, 40f);
            _waveSpeed = EditorGUILayout.Slider("Wave speed", _waveSpeed, 0f, 3f);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Integration", EditorStyles.boldLabel);
            _addUnderwaterEffect = EditorGUILayout.Toggle(
                new GUIContent("Underwater view", "Adds UnderwaterEffect to the main camera."),
                _addUnderwaterEffect);
            _blockNavigation = EditorGUILayout.Toggle(
                new GUIContent("Keep NPCs out", "Marks everything under the surface unwalkable " +
                                                "so wandering NPCs route around the lakes."),
                _blockNavigation);
            using (new EditorGUI.DisabledScope(!_blockNavigation))
            {
                _navBlockDepth = EditorGUILayout.Slider(
                    new GUIContent("Wading depth", "NPCs may still walk this far into the water."),
                    _navBlockDepth, 0f, 3f);
            }

            _seed = EditorGUILayout.IntField("Ripple seed", _seed);

            EditorGUILayout.Space();
            using (new EditorGUI.DisabledScope(terrain == null))
            {
                if (GUILayout.Button("Build Water", GUILayout.Height(32f)))
                    Build(terrain);
            }

            EditorGUILayout.Space();
            EditorGUILayout.HelpBox(
                "Writes to " + OutputRoot + ". Regenerable from the settings above, so that " +
                "folder is gitignored like Assets/Terrain.\n\n" +
                "Re-run WoW Sandbox → Bake NavMesh afterwards.",
                MessageType.Info);
        }

        static Terrain FindTerrain() => Object.FindFirstObjectByType<Terrain>();

        float SurfaceHeight(Terrain terrain) =>
            terrain.transform.position.y + terrain.terrainData.size.y * _seaLevel;

        void Build(Terrain terrain)
        {
            var shader = Shader.Find("WowSandbox/Water");
            if (shader == null)
            {
                Debug.LogError("[WaterSetup] WowSandbox/Water shader not found — check it compiled.");
                return;
            }

            Directory.CreateDirectory(OutputRoot);

            var terrainData = terrain.terrainData;
            float surfaceY = SurfaceHeight(terrain);
            Vector3 terrainOrigin = terrain.transform.position;
            var centre = new Vector3(
                terrainOrigin.x + terrainData.size.x * 0.5f,
                surfaceY,
                terrainOrigin.z + terrainData.size.z * 0.5f);

            float width = terrainData.size.x * _margin;
            float length = terrainData.size.z * _margin;

            // Replace any previous surface so repeated runs don't stack planes on each other.
            var existing = GameObject.Find(SurfaceName);
            if (existing != null)
                Undo.DestroyObjectImmediate(existing);

            var mesh = BuildGrid(width, length, _resolution);
            var material = BuildMaterial(shader);

            var go = new GameObject(SurfaceName);
            go.transform.position = centre;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;

            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;

            var volume = go.AddComponent<WaterVolume>();
            volume.surfaceY = surfaceY;
            volume.extents = new Vector2(width * 0.5f, length * 0.5f);
            volume.depth = Mathf.Max(surfaceY - terrainOrigin.y, 1f) + 50f;

            // The NavMeshSurface collects render meshes across the whole scene, so without
            // this the water plane itself bakes as a walkable floor and the NPCs stroll
            // across the lake. Excluding it is separate from marking the water unwalkable.
            go.AddComponent<NavMeshModifier>().ignoreFromBuild = true;

            Undo.RegisterCreatedObjectUndo(go, "Build Water");

            if (_blockNavigation)
                BuildNavCutout(go, terrain, surfaceY, width, length);

            if (_addUnderwaterEffect)
                AddUnderwaterEffect();

            WarnAboutPipeline(material);
            WarnAboutSpawn(terrain, surfaceY);

            AssetDatabase.SaveAssets();
            Selection.activeGameObject = go;

            // Editor scripts that touch the scene must say so, or the change is quietly lost
            // on reload — and discarded outright if this ran during Play mode.
            EditorSceneManager.MarkSceneDirty(go.scene);
            if (Application.isPlaying)
                Debug.LogWarning("[WaterSetup] Built during Play mode — Unity discards scene " +
                                 "changes on exit. Rebuild in Edit mode to keep it.");

            Debug.Log($"[WaterSetup] Water at Y={surfaceY:F2} covering {width:F0}x{length:F0} units. " +
                      "Re-run WoW Sandbox → Bake NavMesh so the NPCs pick up the change.");
        }

        /// <summary>
        /// A flat subdivided plane, centred on its own origin. The subdivision exists purely
        /// so the vertex shader has somewhere to put the waves.
        /// </summary>
        static Mesh BuildGrid(float width, float length, int resolution)
        {
            int verticesPerSide = resolution + 1;
            var vertices = new Vector3[verticesPerSide * verticesPerSide];
            var normals = new Vector3[vertices.Length];
            var uvs = new Vector2[vertices.Length];
            var triangles = new int[resolution * resolution * 6];

            for (int z = 0; z < verticesPerSide; z++)
            {
                for (int x = 0; x < verticesPerSide; x++)
                {
                    float u = x / (float)resolution;
                    float v = z / (float)resolution;
                    int i = z * verticesPerSide + x;

                    vertices[i] = new Vector3((u - 0.5f) * width, 0f, (v - 0.5f) * length);
                    normals[i] = Vector3.up;
                    uvs[i] = new Vector2(u, v);
                }
            }

            int t = 0;
            for (int z = 0; z < resolution; z++)
            {
                for (int x = 0; x < resolution; x++)
                {
                    int bottomLeft = z * verticesPerSide + x;
                    int topLeft = bottomLeft + verticesPerSide;

                    triangles[t++] = bottomLeft;
                    triangles[t++] = topLeft;
                    triangles[t++] = bottomLeft + 1;

                    triangles[t++] = bottomLeft + 1;
                    triangles[t++] = topLeft;
                    triangles[t++] = topLeft + 1;
                }
            }

            var mesh = new Mesh { name = "SandboxWater" };
            if (vertices.Length > 65535)
                mesh.indexFormat = IndexFormat.UInt32;

            mesh.vertices = vertices;
            mesh.normals = normals;
            mesh.uv = uvs;
            mesh.triangles = triangles;
            mesh.RecalculateBounds();

            string path = $"{OutputRoot}/SandboxWater.mesh";
            AssetDatabase.DeleteAsset(path);
            AssetDatabase.CreateAsset(mesh, path);
            return mesh;
        }

        Material BuildMaterial(Shader shader)
        {
            var normalA = BuildRippleNormalMap("WaterNormalA", _seed, 0.045f, 0.11f);
            var normalB = BuildRippleNormalMap("WaterNormalB", _seed + 991, 0.09f, 0.23f);

            var material = new Material(shader) { name = "Water" };
            material.SetColor("_ShallowColor", _shallow);
            material.SetColor("_DeepColor", _deep);
            material.SetFloat("_DepthMaxDistance", _depthMaxDistance);
            material.SetFloat("_FoamDistance", _foamDistance);
            material.SetTexture("_NormalMapA", normalA);
            material.SetTexture("_NormalMapB", normalB);
            material.SetFloat("_NormalStrength", _normalStrength);
            // zw is world units per tile, so bigger numbers mean bigger, lazier ripples.
            material.SetVector("_ScrollA", new Vector4(0.03f, 0.02f, _tileA, _tileA));
            material.SetVector("_ScrollB", new Vector4(-0.02f, 0.035f, _tileB, _tileB));
            material.SetFloat("_ReflectionStrength", _reflectionStrength);
            material.SetFloat("_WaveAmplitude", _waveAmplitude);
            material.SetFloat("_WaveLength", _waveLength);
            material.SetFloat("_WaveSpeed", _waveSpeed);

            SetRefraction(material, SupportsOpaqueTexture());

            string path = $"{OutputRoot}/Water.mat";
            AssetDatabase.DeleteAsset(path);
            AssetDatabase.CreateAsset(material, path);
            return material;
        }

        static void SetRefraction(Material material, bool enabled)
        {
            material.SetFloat("_RefractionOn", enabled ? 1f : 0f);
            if (enabled)
                material.EnableKeyword("_REFRACTION_ON");
            else
                material.DisableKeyword("_REFRACTION_ON");
        }

        /// <summary>
        /// A tiling ripple normal map, built from two octaves of Perlin noise and
        /// differentiated with a Sobel filter. Generated rather than imported for the same
        /// reason TerrainGenerator generates its ground texture: no external dependency, and
        /// nothing WoW-derived that couldn't be committed.
        ///
        /// The normal is written as plain RGB. Unity's UnpackNormal would expect DXT5nm
        /// (x in alpha, y in green) on desktop, which is why the shader calls
        /// UnpackNormalRGB explicitly instead — a code-created Texture2D never passes through
        /// a TextureImporter, so it can't be tagged as a normal map.
        /// </summary>
        static Texture2D BuildRippleNormalMap(string name, int seed, float coarse, float fine)
        {
            const int size = 256;
            var random = new System.Random(seed);
            float offset = random.Next(0, 10000);

            // Height field first, then differentiate it. Sampling the noise on a torus keeps
            // the result tiling, which matters because the shader scrolls these forever.
            var heights = new float[size, size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    heights[y, x] = TilingNoise(x, y, size, coarse, offset) * 0.65f
                                  + TilingNoise(x, y, size, fine, offset + 500f) * 0.35f;
                }
            }

            var texture = new Texture2D(size, size, TextureFormat.RGBA32, true, linear: true)
            {
                name = name,
                wrapMode = TextureWrapMode.Repeat,
                filterMode = FilterMode.Bilinear
            };

            var pixels = new Color[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    // Central differences, wrapping at the edges so the tile seams match.
                    float left = heights[y, (x - 1 + size) % size];
                    float right = heights[y, (x + 1) % size];
                    float down = heights[(y - 1 + size) % size, x];
                    float up = heights[(y + 1) % size, x];

                    var normal = new Vector3((left - right) * 4f, (down - up) * 4f, 1f).normalized;

                    pixels[y * size + x] = new Color(
                        normal.x * 0.5f + 0.5f,
                        normal.y * 0.5f + 0.5f,
                        normal.z * 0.5f + 0.5f,
                        1f);
                }
            }

            texture.SetPixels(pixels);
            texture.Apply();

            string path = $"{OutputRoot}/{name}.asset";
            AssetDatabase.DeleteAsset(path);
            AssetDatabase.CreateAsset(texture, path);
            return texture;
        }

        /// <summary>
        /// Perlin cross-faded with copies of itself shifted by a full tile, so the 0 and size
        /// edges agree. Cheaper than true 4D noise and indistinguishable at ripple scale.
        /// </summary>
        static float TilingNoise(int x, int y, int size, float frequency, float offset)
        {
            float a = Mathf.PerlinNoise(x * frequency + offset, y * frequency + offset);
            float b = Mathf.PerlinNoise((x - size) * frequency + offset, y * frequency + offset);
            float c = Mathf.PerlinNoise(x * frequency + offset, (y - size) * frequency + offset);
            float d = Mathf.PerlinNoise((x - size) * frequency + offset, (y - size) * frequency + offset);

            float tx = x / (float)size;
            float ty = y / (float)size;

            return Mathf.Lerp(Mathf.Lerp(a, b, tx), Mathf.Lerp(c, d, tx), ty);
        }

        /// <summary>
        /// Marks the submerged terrain unwalkable. This is what actually keeps the NPCs out;
        /// the NavMeshModifier on the surface only stops them walking on top of the water.
        /// </summary>
        void BuildNavCutout(GameObject parent, Terrain terrain, float surfaceY, float width, float length)
        {
            float bottom = terrain.transform.position.y - 10f;
            float top = surfaceY - _navBlockDepth;
            float height = Mathf.Max(top - bottom, 0.1f);

            var go = new GameObject("WaterNavCutout");
            go.transform.SetParent(parent.transform, false);
            // Local, because the parent already sits at the water's centre and surface.
            go.transform.localPosition = new Vector3(0f, (top + bottom) * 0.5f - surfaceY, 0f);

            var modifier = go.AddComponent<NavMeshModifierVolume>();
            modifier.size = new Vector3(width, height, length);
            modifier.center = Vector3.zero;
            modifier.area = 1; // "Not Walkable"
        }

        static void AddUnderwaterEffect()
        {
            var camera = Camera.main;
            if (camera == null)
            {
                Debug.LogWarning("[WaterSetup] No camera tagged MainCamera; skipping the " +
                                 "underwater view.");
                return;
            }

            if (camera.GetComponent<UnderwaterEffect>() == null)
            {
                Undo.AddComponent<UnderwaterEffect>(camera.gameObject);
                Debug.Log("[WaterSetup] Added UnderwaterEffect to " + camera.name + ".");
            }
        }

        static bool SupportsOpaqueTexture()
        {
            var asset = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            return asset != null && asset.supportsCameraOpaqueTexture;
        }

        static void WarnAboutPipeline(Material material)
        {
            var asset = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            if (asset == null)
            {
                Debug.LogWarning("[WaterSetup] No URP asset is active — the water shader needs URP.");
                return;
            }

            if (!asset.supportsCameraDepthTexture)
            {
                Debug.LogWarning($"[WaterSetup] \"{asset.name}\" has Depth Texture off. The depth " +
                                 "colour ramp and the shoreline foam both read scene depth, so the " +
                                 "water will be a flat sheet until you enable it.", asset);
            }

            if (!asset.supportsCameraOpaqueTexture)
            {
                Debug.LogWarning($"[WaterSetup] \"{asset.name}\" has Opaque Texture off, so " +
                                 "refraction is disabled on this material. PC_RPAsset has it on; " +
                                 "Mobile_RPAsset doesn't.", asset);
            }
        }

        /// <summary>
        /// TerrainGenerator flattens the spawn pad to whatever the noise happened to give at
        /// the centre, which on some seeds is below sea level. Better to say so than to let
        /// the player start the scene underwater and wonder why.
        /// </summary>
        static void WarnAboutSpawn(Terrain terrain, float surfaceY)
        {
            var player = Object.FindFirstObjectByType<WowCharacterController>();
            if (player == null)
                return;

            Vector3 position = player.transform.position;
            float ground = terrain.SampleHeight(position) + terrain.transform.position.y;

            if (ground < surfaceY)
            {
                Debug.LogWarning($"[WaterSetup] The spawn point is {surfaceY - ground:F1} units " +
                                 "under water. Lower the sea level, or regenerate the terrain " +
                                 "with a different seed.", player);
            }
        }
    }
}
