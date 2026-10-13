using System;
using UnityEngine;

namespace WowSandbox
{
    /// <summary>
    /// The player's settings — view distance, fog, volumes, mouse sensitivity — saved in
    /// PlayerPrefs so they carry over between sessions, and applied to the running game.
    /// SettingsPanel edits them; MusicPlayer and StormAudio read the volumes as they play.
    ///
    /// Anything never changed follows the scene: the view distance and fog it opened with,
    /// so a fresh install looks exactly as the scene was tuned. Nothing here touches the
    /// scene file — it's all applied at runtime, and Unity drops it when Play ends.
    /// </summary>
    public static class GameSettings
    {
        const string ViewKey = "settings.viewDistance";
        const string FogKey = "settings.fogDensity";
        const string MasterKey = "settings.masterVolume";
        const string MusicKey = "settings.musicVolume";
        const string EffectsKey = "settings.effectsVolume";
        const string MouseKey = "settings.mouseSensitivity";

        public const float MinView = 250f;
        public const float MaxView = 1500f;

        // Fog density 0..1 sets how far into the view the clear-day fog is total: from the
        // very edge of the view (0) to under a third of it (1). It starts a quarter of the
        // way to that. Storms and the edge of the sea still close in on top.
        const float ThinFogEnd = 1f;
        const float ThickFogEnd = 0.3f;
        const float FogStartFraction = 0.25f;

        /// <summary>Raised whenever a setting changes, or the scene defaults are captured.</summary>
        public static event Action Changed;

        static float _sceneView = 900f;
        static float _sceneFog = 0.3f;
        static float _sceneMouse = 0.18f;
        static bool _captured;

        public static float ViewDistance
        {
            get => PlayerPrefs.GetFloat(ViewKey, _sceneView);
            set => Set(ViewKey, Mathf.Clamp(value, MinView, MaxView));
        }

        public static float FogDensity
        {
            get => PlayerPrefs.GetFloat(FogKey, _sceneFog);
            set => Set(FogKey, Mathf.Clamp01(value));
        }

        public static float MasterVolume
        {
            get => PlayerPrefs.GetFloat(MasterKey, 1f);
            set => Set(MasterKey, Mathf.Clamp01(value));
        }

        public static float MusicVolume
        {
            get => PlayerPrefs.GetFloat(MusicKey, 1f);
            set => Set(MusicKey, Mathf.Clamp01(value));
        }

        public static float EffectsVolume
        {
            get => PlayerPrefs.GetFloat(EffectsKey, 1f);
            set => Set(EffectsKey, Mathf.Clamp01(value));
        }

        /// <summary>The camera's mouse sensitivity (WowOrbitCamera's default is 0.18).</summary>
        public static float MouseSensitivity
        {
            get => PlayerPrefs.GetFloat(MouseKey, _sceneMouse);
            set => Set(MouseKey, Mathf.Clamp(value, 0.05f, 0.6f));
        }

        static void Set(string key, float value)
        {
            PlayerPrefs.SetFloat(key, value);
            PlayerPrefs.Save();
            Apply();
            Changed?.Invoke();
        }

        /// <summary>
        /// Remembers what the scene opened with, as the defaults for anything not yet set.
        /// Call once at the start of Play, before anything changes the camera or fog.
        /// </summary>
        public static void CaptureSceneDefaults()
        {
            if (_captured)
                return;
            _captured = true;

            var camera = Camera.main;
            if (camera != null)
                _sceneView = Mathf.Clamp(camera.farClipPlane, MinView, MaxView);
            if (RenderSettings.fog && RenderSettings.fogMode == FogMode.Linear && _sceneView > 0f)
                _sceneFog = Mathf.Clamp01(Mathf.InverseLerp(ThinFogEnd, ThickFogEnd, RenderSettings.fogEndDistance / _sceneView));
            var orbit = camera != null ? camera.GetComponent<WowOrbitCamera>() : null;
            if (orbit != null)
                _sceneMouse = orbit.mouseSensitivity;

            Apply();
            Changed?.Invoke();
        }

        /// <summary>Forgets every saved setting, back to the scene's own values.</summary>
        public static void ResetToDefaults()
        {
            foreach (string key in new[] { ViewKey, FogKey, MasterKey, MusicKey, EffectsKey, MouseKey })
                PlayerPrefs.DeleteKey(key);
            PlayerPrefs.Save();
            Apply();
            Changed?.Invoke();
        }

        /// <summary>Pushes every setting into the running game.</summary>
        public static void Apply()
        {
            AudioListener.volume = MasterVolume;

            var camera = Camera.main;
            if (camera == null)
                return;

            var orbit = camera.GetComponent<WowOrbitCamera>();
            if (orbit != null)
                orbit.mouseSensitivity = MouseSensitivity;

            ApplyViewDistance(camera, ViewDistance);

            float fogEnd = ViewDistance * Mathf.Lerp(ThinFogEnd, ThickFogEnd, FogDensity);
            float fogStart = fogEnd * FogStartFraction;
            if (StormWeather.Active != null)
            {
                StormWeather.Active.SetClearFog(fogStart, fogEnd);
            }
            else
            {
                RenderSettings.fog = true;
                RenderSettings.fogMode = FogMode.Linear;
                RenderSettings.fogStartDistance = fogStart;
                RenderSettings.fogEndDistance = fogEnd;
            }
        }

        /// <summary>
        /// Moves the far plane, and scales the sky dome with it — along with its height offset,
        /// so the sky looks exactly the same (it's centred on the camera; see ViewDistanceSetup).
        /// </summary>
        static void ApplyViewDistance(Camera camera, float distance)
        {
            float ratio = distance / Mathf.Max(camera.farClipPlane, 1f);
            if (Mathf.Abs(ratio - 1f) < 0.001f)
                return;

            camera.farClipPlane = distance;
            var dome = UnityEngine.Object.FindAnyObjectByType<SkyDomeFollow>();
            if (dome != null)
            {
                dome.transform.localScale *= ratio;
                dome.heightOffset *= ratio;
            }
        }
    }
}
