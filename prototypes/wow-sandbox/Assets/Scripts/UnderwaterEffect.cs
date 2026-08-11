using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace WowSandbox
{
    /// <summary>
    /// Makes going under the surface look like going under the surface: dense green-blue
    /// fog, a colour grade, a closing vignette, and the sky dome hidden so cloud layers
    /// don't shine through the water.
    ///
    /// The tint comes from a runtime Volume rather than a custom ScriptableRendererFeature.
    /// URP's post stack is already in this project, a Volume needs no render-pass plumbing,
    /// and its weight gives the fade in and out for free.
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public class UnderwaterEffect : MonoBehaviour
    {
        [Header("Fog")]
        public Color underwaterFog = new Color(0.06f, 0.28f, 0.32f);
        [Tooltip("Exponential-squared density. Higher is murkier.")]
        public float fogDensity = 0.06f;

        [Header("Grade")]
        public Color tint = new Color(0.55f, 0.85f, 0.95f);
        [Range(-100f, 0f)] public float saturation = -20f;
        [Range(0f, 1f)] public float vignetteIntensity = 0.45f;

        [Header("Transition")]
        [Tooltip("Seconds to fade the underwater look in or out.")]
        public float transitionTime = 0.25f;

        Volume _volume;
        VolumeProfile _profile;
        Renderer[] _skyDomeRenderers;
        float _blend;

        // Everything we overwrite on RenderSettings, captured before we touch it.
        bool _fogWasOn;
        FogMode _fogMode;
        Color _fogColor;
        float _fogDensity;
        float _fogStart;
        float _fogEnd;

        void Awake()
        {
            CacheRenderSettings();
            BuildVolume();

            var dome = FindFirstObjectByType<SkyDomeFollow>();
            if (dome != null)
                _skyDomeRenderers = dome.GetComponentsInChildren<Renderer>(true);

            // A Volume does nothing if the camera isn't running post-processing.
            var cameraData = GetComponent<UniversalAdditionalCameraData>();
            if (cameraData != null && !cameraData.renderPostProcessing)
            {
                cameraData.renderPostProcessing = true;
                Debug.Log("[UnderwaterEffect] Enabled post-processing on the camera — the " +
                          "underwater tint is a Volume override and needs it.");
            }
        }

        void LateUpdate()
        {
            bool submerged = WaterVolume.Containing(transform.position) != null;

            float step = Time.deltaTime / Mathf.Max(transitionTime, 0.0001f);
            _blend = Mathf.MoveTowards(_blend, submerged ? 1f : 0f, step);

            if (_volume != null)
                _volume.weight = _blend;

            ApplyFog(_blend);
            SetSkyDomeVisible(_blend < 0.5f);
        }

        /// <summary>
        /// Restores the scene's own fog. Without this, leaving Play mode — or just disabling
        /// the component — leaves the scene view drowned in green fog, which reads as a bug
        /// in ViewDistanceSetup rather than as leftover state from here.
        /// </summary>
        void OnDisable()
        {
            RestoreRenderSettings();
            SetSkyDomeVisible(true);

            if (_volume != null)
                _volume.weight = 0f;

            _blend = 0f;
        }

        void OnDestroy()
        {
            if (_profile != null)
                Destroy(_profile);
        }

        void CacheRenderSettings()
        {
            _fogWasOn = RenderSettings.fog;
            _fogMode = RenderSettings.fogMode;
            _fogColor = RenderSettings.fogColor;
            _fogDensity = RenderSettings.fogDensity;
            _fogStart = RenderSettings.fogStartDistance;
            _fogEnd = RenderSettings.fogEndDistance;
        }

        void RestoreRenderSettings()
        {
            RenderSettings.fog = _fogWasOn;
            RenderSettings.fogMode = _fogMode;
            RenderSettings.fogColor = _fogColor;
            RenderSettings.fogDensity = _fogDensity;
            RenderSettings.fogStartDistance = _fogStart;
            RenderSettings.fogEndDistance = _fogEnd;
        }

        /// <summary>
        /// Above water the scene keeps its own linear distance fog; below, it swaps to dense
        /// exponential-squared. The two modes can't be blended, so the swap happens as soon
        /// as we start submerging and the density ramps from there — over a quarter-second at
        /// the waterline, which is the only place you see it.
        /// </summary>
        void ApplyFog(float blend)
        {
            if (blend <= 0.001f)
            {
                RestoreRenderSettings();
                return;
            }

            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogColor = Color.Lerp(_fogColor, underwaterFog, blend);
            RenderSettings.fogDensity = Mathf.Lerp(0f, fogDensity, blend);
        }

        void SetSkyDomeVisible(bool visible)
        {
            if (_skyDomeRenderers == null)
                return;

            foreach (var renderer in _skyDomeRenderers)
            {
                if (renderer != null && renderer.enabled != visible)
                    renderer.enabled = visible;
            }
        }

        /// <summary>
        /// Builds the Volume and its profile in code so there's no asset to keep in sync —
        /// the same reasoning as the rest of the sandbox, where everything is regenerable.
        /// </summary>
        void BuildVolume()
        {
            var holder = new GameObject("UnderwaterVolume") { hideFlags = HideFlags.DontSave };
            holder.transform.SetParent(transform, false);

            _volume = holder.AddComponent<Volume>();
            _volume.isGlobal = true;
            _volume.priority = 100f;   // above the scene's own volumes
            _volume.weight = 0f;

            _profile = ScriptableObject.CreateInstance<VolumeProfile>();
            _profile.hideFlags = HideFlags.DontSave;
            _volume.sharedProfile = _profile;

            var grade = _profile.Add<ColorAdjustments>(true);
            grade.colorFilter.overrideState = true;
            grade.colorFilter.value = tint;
            grade.saturation.overrideState = true;
            grade.saturation.value = saturation;

            var vignette = _profile.Add<Vignette>(true);
            vignette.intensity.overrideState = true;
            vignette.intensity.value = vignetteIntensity;
            vignette.smoothness.overrideState = true;
            vignette.smoothness.value = 0.6f;
            vignette.color.overrideState = true;
            vignette.color.value = underwaterFog;
        }
    }
}
