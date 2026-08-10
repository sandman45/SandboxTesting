using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace WowSandbox.EditorTools
{
    /// <summary>
    /// Sets the camera's view distance, matching fog, and the sky dome's scale together.
    ///
    /// These three can't be set independently. The sky dome has to sit inside the far
    /// clip plane or it gets clipped away, and shortening the view distance without fog
    /// just gives you a hard edge where the terrain stops. WoW leans on fog heavily for
    /// exactly this reason.
    /// </summary>
    public class ViewDistanceSetup : EditorWindow
    {
        /// <summary>Radius of the sky dome models at scale 1, from their glTF bounds.</summary>
        const float DomeRadiusAtUnitScale = 43f;

        float _viewDistance = 600f;
        bool _enableFog = true;
        Color _fogColor = new Color(0.63f, 0.72f, 0.84f);
        [Range(0f, 1f)] float _fogStart = 0.35f;
        [Range(0f, 1f)] float _fogEnd = 0.95f;

        [MenuItem("WoW Sandbox/Set View Distance")]
        public static void ShowWindow()
        {
            var window = GetWindow<ViewDistanceSetup>(true, "View Distance");
            window.minSize = new Vector2(360f, 260f);
        }

        void OnGUI()
        {
            _viewDistance = EditorGUILayout.Slider("View distance", _viewDistance, 50f, 1500f);

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
                "Also rescales the sky dome to sit just inside the far plane. Match the fog " +
                "colour to your sky near the horizon or the join will show.",
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

            RescaleSkyDome();

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

        /// <summary>
        /// Keeps the dome comfortably inside the far plane. Its apparent size doesn't change
        /// with scale — it's centred on the camera — so this is purely about not being clipped.
        /// </summary>
        void RescaleSkyDome()
        {
            var dome = Object.FindFirstObjectByType<SkyDomeFollow>();
            if (dome == null)
                return;

            float scale = _viewDistance * 0.85f / DomeRadiusAtUnitScale;
            Undo.RecordObject(dome.transform, "Rescale Sky Dome");
            dome.transform.localScale = Vector3.one * scale;

            // The dome's height offset is in world units, so it has to track the scale or
            // the horizon drifts every time the view distance changes.
            Debug.Log($"[ViewDistanceSetup] Sky dome rescaled to {scale:F1}. " +
                      "Re-check its Height Offset — that value is in world units.");
        }
    }
}
