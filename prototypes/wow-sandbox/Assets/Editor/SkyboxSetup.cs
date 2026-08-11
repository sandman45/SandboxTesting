using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace WowSandbox.EditorTools
{
    /// <summary>
    /// Sets up a procedural sky and a matching sun.
    ///
    /// Note this is *not* how WoW does skies: a WoW skybox is an M2 dome model with
    /// animated cloud layers, driven per-zone from LightSkybox.db2. wow.export renders
    /// those in its map viewer but has no export path for them, so this uses Unity's
    /// built-in procedural sky instead — which also means no assets to import and it
    /// responds correctly to the sun angle.
    /// </summary>
    public class SkyboxSetup : EditorWindow
    {
        const string OutputRoot = "Assets/Settings";

        Color _skyTint = new Color(0.42f, 0.58f, 0.78f);
        Color _groundColor = new Color(0.28f, 0.26f, 0.22f);
        float _atmosphere = 1.1f;
        float _exposure = 1.15f;
        float _sunSize = 0.045f;

        float _sunPitch = 42f;   // degrees above the horizon
        float _sunYaw = 130f;
        Color _sunColor = new Color(1f, 0.96f, 0.88f);
        float _sunIntensity = 1.25f;

        [MenuItem("WoW Sandbox/Setup Sky and Sun")]
        public static void ShowWindow()
        {
            var window = GetWindow<SkyboxSetup>(true, "Sky and Sun");
            window.minSize = new Vector2(340f, 340f);
        }

        void OnGUI()
        {
            EditorGUILayout.LabelField("Sky", EditorStyles.boldLabel);
            _skyTint = EditorGUILayout.ColorField("Sky tint", _skyTint);
            _groundColor = EditorGUILayout.ColorField("Ground colour", _groundColor);
            _atmosphere = EditorGUILayout.Slider("Atmosphere", _atmosphere, 0f, 5f);
            _exposure = EditorGUILayout.Slider("Exposure", _exposure, 0f, 8f);
            _sunSize = EditorGUILayout.Slider("Sun size", _sunSize, 0f, 1f);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Sun", EditorStyles.boldLabel);
            _sunPitch = EditorGUILayout.Slider("Pitch (above horizon)", _sunPitch, -10f, 90f);
            _sunYaw = EditorGUILayout.Slider("Yaw", _sunYaw, 0f, 360f);
            _sunColor = EditorGUILayout.ColorField("Sun colour", _sunColor);
            _sunIntensity = EditorGUILayout.Slider("Intensity", _sunIntensity, 0f, 5f);

            EditorGUILayout.Space();
            if (GUILayout.Button("Apply", GUILayout.Height(30f)))
                Apply();

            EditorGUILayout.Space();
            EditorGUILayout.HelpBox(
                "A low pitch gives long shadows and a warm horizon. Drop it below ~5 degrees " +
                "for dusk. The sky reacts to the sun automatically — they're the same system.",
                MessageType.Info);
        }

        void Apply()
        {
            var shader = Shader.Find("Skybox/Procedural");
            if (shader == null)
            {
                Debug.LogError("[SkyboxSetup] Skybox/Procedural shader not found. If this is a stripped " +
                               "build, add it to Project Settings -> Graphics -> Always Included Shaders.");
                return;
            }

            Directory.CreateDirectory(OutputRoot);
            string path = $"{OutputRoot}/SandboxSky.mat";

            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(shader);
                AssetDatabase.CreateAsset(material, path);
            }

            material.shader = shader;
            material.SetColor("_SkyTint", _skyTint);
            material.SetColor("_GroundColor", _groundColor);
            material.SetFloat("_AtmosphereThickness", _atmosphere);
            material.SetFloat("_Exposure", _exposure);
            material.SetFloat("_SunSize", _sunSize);
            EditorUtility.SetDirty(material);

            RenderSettings.skybox = material;

            // Ambient light comes from the sky, so the scene tints with it rather than
            // needing a separate fill colour.
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Skybox;

            var sun = FindOrCreateSun();
            Undo.RecordObject(sun.transform, "Setup Sky and Sun");
            Undo.RecordObject(sun, "Setup Sky and Sun");
            sun.transform.rotation = Quaternion.Euler(_sunPitch, _sunYaw, 0f);
            sun.color = _sunColor;
            sun.intensity = _sunIntensity;
            sun.shadows = LightShadows.Soft;
            RenderSettings.sun = sun;

            EnsureCameraShowsSky();

            DynamicGI.UpdateEnvironment();
            AssetDatabase.SaveAssets();

            // RenderSettings and light edits don't mark the scene dirty on their own, so
            // without this they're quietly lost on reload — and discarded outright if this
            // was applied during Play mode.
            EditorSceneManager.MarkSceneDirty(sun.gameObject.scene);
            if (Application.isPlaying)
                Debug.LogWarning("[SkyboxSetup] Applied during Play mode — Unity discards these on " +
                                 "exit. Re-apply in Edit mode to make it stick.");
            Debug.Log($"[SkyboxSetup] Sky material at {path}; sun set to pitch {_sunPitch:F0}, yaw {_sunYaw:F0}.");
        }

        /// <summary>
        /// Assigning RenderSettings.skybox isn't enough on its own: a camera set to clear to a
        /// solid colour never draws the skybox at all, so a perfectly good sky material still
        /// renders as flat black behind the cloud dome. The two halves live in different
        /// places — lighting settings and the camera — so this tool owns both rather than
        /// leaving half the job to Setup Sky Dome.
        /// </summary>
        static void EnsureCameraShowsSky()
        {
            var camera = Camera.main;
            if (camera == null)
            {
                Debug.LogWarning("[SkyboxSetup] No camera tagged MainCamera, so the sky may not " +
                                 "be visible — set its Clear Flags to Skybox by hand.");
                return;
            }

            if (camera.clearFlags == CameraClearFlags.Skybox)
                return;

            var previous = camera.clearFlags;
            Undo.RecordObject(camera, "Setup Sky and Sun");
            camera.clearFlags = CameraClearFlags.Skybox;

            Debug.Log($"[SkyboxSetup] \"{camera.name}\" was clearing to {previous}, which is what " +
                      "showed as black behind the clouds. Switched it to Skybox.", camera);
        }

        static Light FindOrCreateSun()
        {
            foreach (var light in Object.FindObjectsByType<Light>(FindObjectsSortMode.None))
            {
                if (light.type == LightType.Directional)
                    return light;
            }

            var go = new GameObject("Directional Light");
            var created = go.AddComponent<Light>();
            created.type = LightType.Directional;
            Undo.RegisterCreatedObjectUndo(go, "Create Sun");
            return created;
        }
    }
}
