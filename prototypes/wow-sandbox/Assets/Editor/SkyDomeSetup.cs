using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace WowSandbox.EditorTools
{
    /// <summary>
    /// Turns an exported WoW sky dome M2 into a working sky.
    ///
    /// The imported glTF can't be used as-is: its materials come in opaque (the export
    /// declares no alphaMode), so the stacked cloud layers occlude each other as solid
    /// shells, and back-face culling hides the dome entirely when viewed from inside.
    /// This rebuilds every layer on the WowSandbox/SkyDome shader instead.
    /// </summary>
    public class SkyDomeSetup : EditorWindow
    {
        const string GeneratedRoot = "Assets/WowExports/_Generated/Sky";

        GameObject _model;
        // Default well beyond the 500-unit terrain so hills occlude the sky, not the reverse.
        float _scale = 15f;
        float _heightOffset;
        float _tiling = 1f;
        float _scrollSpeed = 0.004f;
        Color _tint = Color.white;
        bool _useProceduralBase = true;

        [MenuItem("WoW Sandbox/Setup Sky Dome")]
        public static void ShowWindow()
        {
            var window = GetWindow<SkyDomeSetup>(true, "Sky Dome");
            window.minSize = new Vector2(370f, 300f);
        }

        void OnGUI()
        {
            EditorGUILayout.HelpBox(
                "Drag an exported sky model (environments/stars/...) into Model. Rebuilds its " +
                "cloud layers as transparent, inside-out, depth-less materials and parents it " +
                "to the camera.",
                MessageType.None);

            EditorGUILayout.Space();
            _model = (GameObject)EditorGUILayout.ObjectField("Model (.gltf)", _model, typeof(GameObject), false);

            EditorGUILayout.Space();
            _heightOffset = EditorGUILayout.Slider(
                new GUIContent("Height offset",
                    "The real lever for how close the sky feels. The dome sits at the camera with " +
                    "its ceiling ~27 units up; negative values bring that ceiling down overhead."),
                _heightOffset, -30f, 50f);

            _tiling = EditorGUILayout.Slider(
                new GUIContent("Cloud tiling", "Above 1 gives smaller, more numerous clouds."),
                _tiling, 0.25f, 6f);

            _scrollSpeed = EditorGUILayout.Slider("Cloud drift", _scrollSpeed, 0f, 0.05f);
            _tint = EditorGUILayout.ColorField("Tint", _tint);

            _scale = EditorGUILayout.Slider(
                new GUIContent("Scale",
                    "Doesn't change how big the clouds look — the dome is centred on the camera, " +
                    "so scaling preserves every angle. It does decide whether distant terrain " +
                    "correctly hides the sky: the dome must be larger than your terrain."),
                _scale, 1f, 60f);

            EditorGUILayout.Space();
            _useProceduralBase = EditorGUILayout.Toggle(
                new GUIContent("Procedural sky behind",
                    "The cloud textures are mostly transparent, so they need a sky behind them. " +
                    "This keeps the camera on Skybox so the procedural sky shows through the gaps."),
                _useProceduralBase);

            EditorGUILayout.Space();
            using (new EditorGUI.DisabledScope(_model == null))
            {
                if (GUILayout.Button("Build Sky Dome", GUILayout.Height(30f)))
                    Build();
            }
        }

        void Build()
        {
            string modelPath = AssetDatabase.GetAssetPath(_model);
            var shader = Shader.Find("WowSandbox/SkyDome");
            if (shader == null)
            {
                Debug.LogError("[SkyDomeSetup] WowSandbox/SkyDome shader not found — check it compiled.");
                return;
            }

            string name = Path.GetFileNameWithoutExtension(modelPath);
            string outputDir = $"{GeneratedRoot}/{name}";
            Directory.CreateDirectory(outputDir);

            // Replace any previous dome so repeated runs don't stack them.
            var existing = Object.FindFirstObjectByType<SkyDomeFollow>();
            if (existing != null)
                Undo.DestroyObjectImmediate(existing.gameObject);

            var root = new GameObject($"SkyDome_{name}");
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(_model, root.transform);
            instance.transform.localPosition = Vector3.zero;
            instance.transform.localRotation = Quaternion.identity;
            root.transform.localScale = Vector3.one * _scale;

            var follow = root.AddComponent<SkyDomeFollow>();
            follow.heightOffset = _heightOffset;

            int layers = RebuildMaterials(instance, shader, outputDir);

            var camera = Camera.main;
            if (camera != null && _useProceduralBase)
            {
                Undo.RecordObject(camera, "Sky Dome Camera Background");
                camera.clearFlags = CameraClearFlags.Skybox;

                if (RenderSettings.skybox == null)
                    Debug.LogWarning("[SkyDomeSetup] No skybox material assigned in Lighting settings — " +
                                     "the gaps between clouds will be flat. Run WoW Sandbox -> " +
                                     "Setup Sky and Sun first.");
            }

            Undo.RegisterCreatedObjectUndo(root, "Build Sky Dome");
            Selection.activeGameObject = root;
            AssetDatabase.SaveAssets();

            // The camera edit above doesn't mark the scene dirty by itself, so it would be
            // quietly lost on reload — and discarded outright if this ran during Play mode.
            EditorSceneManager.MarkSceneDirty(root.scene);
            if (Application.isPlaying)
                Debug.LogWarning("[SkyDomeSetup] Built during Play mode — Unity discards scene changes " +
                                 "on exit. Rebuild in Edit mode to keep it.");

            Debug.Log($"[SkyDomeSetup] Built {name} with {layers} cloud layer(s). " +
                      "Ambient light still comes from the procedural sky material in Lighting settings — " +
                      "the dome is visual only.");
        }

        /// <summary>
        /// Rebuilds every renderer's materials on the sky shader, carrying over the texture
        /// glTFast assigned. Each layer gets a slightly different drift so they parallax
        /// against each other instead of moving as one sheet.
        /// </summary>
        int RebuildMaterials(GameObject instance, Shader shader, string outputDir)
        {
            var cache = new Dictionary<Texture, Material>();
            int layer = 0;

            foreach (var renderer in instance.GetComponentsInChildren<Renderer>(true))
            {
                var rebuilt = new List<Material>();

                foreach (var source in renderer.sharedMaterials)
                {
                    Texture texture = null;
                    if (source != null)
                    {
                        if (source.HasProperty("_BaseMap"))
                            texture = source.GetTexture("_BaseMap");
                        if (texture == null && source.HasProperty("_MainTex"))
                            texture = source.GetTexture("_MainTex");
                        if (texture == null)
                            texture = source.mainTexture;
                    }

                    if (texture != null && cache.TryGetValue(texture, out Material cached))
                    {
                        rebuilt.Add(cached);
                        continue;
                    }

                    var material = new Material(shader)
                    {
                        name = texture != null ? texture.name : $"SkyLayer_{layer:00}"
                    };
                    if (texture != null)
                    {
                        EnsureSkyTextureSettings(texture);
                        material.SetTexture("_BaseMap", texture);
                    }
                    material.SetColor("_BaseColor", _tint);
                    material.SetTextureScale("_BaseMap", Vector2.one * _tiling);

                    // Vary drift per layer: higher layers move a little faster.
                    float speed = _scrollSpeed * (1f + layer * 0.35f);
                    material.SetVector("_ScrollSpeed", new Vector4(speed, speed * 0.25f, 0f, 0f));

                    AssetDatabase.CreateAsset(material, $"{outputDir}/{material.name}_sky.mat");
                    if (texture != null)
                        cache[texture] = material;

                    rebuilt.Add(material);
                    layer++;
                }

                renderer.sharedMaterials = rebuilt.ToArray();
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                renderer.receiveShadows = false;
                renderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            }

            return layer;
        }

        /// <summary>
        /// wow.export's PNGs import with Alpha Is Transparency off, so Unity leaves whatever
        /// RGB sits in the fully-transparent regions untouched. Bilinear filtering then blends
        /// edge texels toward that colour, giving cloud edges a hard, dirty fringe. Turning it
        /// on makes Unity dilate colour outward into the transparent areas so edges fade
        /// cleanly instead.
        /// </summary>
        static void EnsureSkyTextureSettings(Texture texture)
        {
            string path = AssetDatabase.GetAssetPath(texture);
            if (string.IsNullOrEmpty(path))
                return;

            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null)
                return;

            bool changed = false;

            if (!importer.alphaIsTransparency)
            {
                importer.alphaIsTransparency = true;
                changed = true;
            }

            // Scrolling UVs run past 0..1, so the layers must tile rather than clamp.
            if (importer.wrapMode != TextureWrapMode.Repeat)
            {
                importer.wrapMode = TextureWrapMode.Repeat;
                changed = true;
            }

            // Averaging alpha down the mip chain thins cloud edges with distance; this keeps
            // their coverage roughly constant instead.
            if (importer.mipmapEnabled && !importer.mipMapsPreserveCoverage)
            {
                importer.mipMapsPreserveCoverage = true;
                changed = true;
            }

            if (changed)
            {
                importer.SaveAndReimport();
                Debug.Log($"[SkyDomeSetup] Fixed import settings on {Path.GetFileName(path)}.");
            }
        }
    }
}
