using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace WowSandbox.EditorTools
{
    /// <summary>
    /// Sets the camera's view distance and matching fog together.
    ///
    /// Shortening the view distance without fog just gives you a hard edge where the terrain
    /// stops. WoW leans on fog heavily for exactly this reason. (The weather's sky spheres size
    /// themselves to the far plane.)
    /// </summary>
    public class ViewDistanceSetup : EditorWindow
    {
        // Defaults for a scene with no camera or fog yet; otherwise the window opens on what
        // the scene has now. The edge of the sea is hidden by StormWeather's edge fog, which
        // closes in only as you near it, so the clear-day view can reach a long way.
        float _viewDistance = 900f;
        bool _enableFog = true;
        Color _fogColor = new Color(0.63f, 0.72f, 0.84f);
        [Range(0f, 1f)] float _fogStart = 0.2f;
        [Range(0f, 1f)] float _fogEnd = 0.8f;

        [MenuItem("WoW Sandbox/Set View Distance")]
        public static void ShowWindow()
        {
            var window = GetWindow<ViewDistanceSetup>(true, "View Distance");
            window.minSize = new Vector2(360f, 260f);
        }

        void OnEnable()
        {
            var camera = Camera.main;
            if (camera != null)
                _viewDistance = camera.farClipPlane;

            _enableFog = RenderSettings.fog;
            if (RenderSettings.fog && RenderSettings.fogMode == FogMode.Linear && _viewDistance > 0f)
            {
                _fogColor = RenderSettings.fogColor;
                _fogStart = Mathf.Clamp01(RenderSettings.fogStartDistance / _viewDistance);
                _fogEnd = Mathf.Clamp01(RenderSettings.fogEndDistance / _viewDistance);
            }
        }

        void OnGUI()
        {
            _viewDistance = EditorGUILayout.Slider("View distance", _viewDistance, 50f, 2000f);

            EditorGUILayout.Space();
            _enableFog = EditorGUILayout.Toggle("Fog", _enableFog);
            using (new EditorGUI.DisabledScope(!_enableFog))
            {
                _fogColor = EditorGUILayout.ColorField("Fog colour", _fogColor);
                _fogStart = EditorGUILayout.Slider("Fog starts at", _fogStart, 0f, 1f);
                _fogEnd = EditorGUILayout.Slider("Fog full at", _fogEnd, 0f, 1f);
            }

            EditorGUILayout.Space();
            if (GUILayout.Button("Apply", GUILayout.Height(30f)))
                Apply();

            EditorGUILayout.Space();
            EditorGUILayout.HelpBox(
                "Match the fog colour to your sky near the horizon or the join will show.",
                MessageType.Info);
        }

        void Apply()
        {
            var camera = Camera.main;
            if (camera == null)
            {
                Debug.LogError("[ViewDistanceSetup] No camera tagged MainCamera.");
                return;
            }

            Undo.RecordObject(camera, "Set View Distance");
            camera.farClipPlane = _viewDistance;

            if (_enableFog)
            {
                RenderSettings.fog = true;
                RenderSettings.fogMode = FogMode.Linear;
                RenderSettings.fogColor = _fogColor;
                RenderSettings.fogStartDistance = _viewDistance * Mathf.Min(_fogStart, _fogEnd);
                RenderSettings.fogEndDistance = _viewDistance * Mathf.Max(_fogStart, _fogEnd);
            }
            else
            {
                RenderSettings.fog = false;
            }

            // Camera and RenderSettings edits don't mark the scene dirty by themselves, so
            // without this they're quietly lost when the scene reloads. (They also revert
            // outright if applied during Play mode — apply this in Edit mode.)
            EditorUtility.SetDirty(camera);
            EditorSceneManager.MarkSceneDirty(camera.gameObject.scene);

            if (Application.isPlaying)
                Debug.LogWarning("[ViewDistanceSetup] Applied during Play mode — Unity discards these " +
                                 "on exit. Re-apply in Edit mode to make it stick.");

            Debug.Log($"[ViewDistanceSetup] View distance {_viewDistance:F0}" +
                      (_enableFog
                          ? $", fog {RenderSettings.fogStartDistance:F0}-{RenderSettings.fogEndDistance:F0}."
                          : ", fog off."));
        }
    }
}
