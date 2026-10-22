using System;
using UnityEngine;

namespace WowSandbox
{
    /// <summary>
    /// How the sky's clouds look (CloudLayer): how much of the sky they cover, how thick, how big
    /// and high, how soft-edged, how they drift and how the sun lights them. Fine weather's look
    /// is <see cref="Current"/>, set with the Developer panel's Clouds window and remembered;
    /// a storm blends it toward <see cref="Storm"/>.
    /// </summary>
    [Serializable]
    public struct CloudSettings
    {
        [Range(0f, 1f)] public float coverage;
        [Tooltip("Thickness: thin and bright to dense and dark-bellied.")]
        [Range(0.1f, 3f)] public float density;
        [Tooltip("Roughly how wide one cloud is, in world units.")]
        public float size;
        [Tooltip("Height of the cloud base above sea level.")]
        public float height;
        [Tooltip("How deep the layer is, base to tops.")]
        public float thickness;
        [Tooltip("0 crisp, billowing edges; 1 soft and wispy.")]
        [Range(0f, 1f)] public float softness;
        [Tooltip("World units per second.")]
        public float windSpeed;
        [Tooltip("Degrees clockwise from +Z the clouds drift toward.")]
        public float windDirection;
        [Tooltip("How much of the sun's (or moon's) light the clouds take.")]
        [Range(0f, 2f)] public float sunlight;
        [Tooltip("The bright rim on clouds near the sun.")]
        [Range(0f, 1f)] public float silverLining;

        // Fair-weather clouds are shallow; the layer deepens as it closes over.
        public static readonly CloudSettings Clear = Preset(0f, 0.6f, 0.6f, depth: 140f);
        public static readonly CloudSettings Few = Preset(0.22f, 0.6f, 0.35f, depth: 140f);
        public static readonly CloudSettings Scattered = Preset(0.42f, 0.8f, 0.35f, depth: 160f);
        public static readonly CloudSettings Broken = Preset(0.65f, 1f, 0.4f, depth: 260f);
        public static readonly CloudSettings Overcast = Preset(0.92f, 1.4f, 0.7f, depth: 380f, sunlight: 0.8f);
        /// <summary>What a storm blends the sky toward: solid, dark, low and fast.</summary>
        public static readonly CloudSettings Storm = new()
        {
            coverage = 1f, density = 2.4f, size = 450f, height = 300f, thickness = 500f, softness = 0.75f,
            windSpeed = 45f, windDirection = 30f, sunlight = 0.5f, silverLining = 0.1f,
        };

        public static readonly (string name, CloudSettings settings)[] Presets =
        {
            ("Clear", Clear), ("Few", Few), ("Scattered", Scattered),
            ("Broken", Broken), ("Overcast", Overcast), ("Storm", Storm),
        };

        static CloudSettings Preset(float coverage, float density, float softness, float depth, float sunlight = 1f) => new()
        {
            coverage = coverage, density = density, size = 260f, height = 420f, thickness = depth,
            softness = softness, windSpeed = 6f, windDirection = 30f, sunlight = sunlight, silverLining = 0.6f,
        };

        public static CloudSettings Lerp(CloudSettings a, CloudSettings b, float t) => new()
        {
            coverage = Mathf.Lerp(a.coverage, b.coverage, t),
            density = Mathf.Lerp(a.density, b.density, t),
            size = Mathf.Lerp(a.size, b.size, t),
            height = Mathf.Lerp(a.height, b.height, t),
            thickness = Mathf.Lerp(a.thickness, b.thickness, t),
            softness = Mathf.Lerp(a.softness, b.softness, t),
            windSpeed = Mathf.Lerp(a.windSpeed, b.windSpeed, t),
            windDirection = Mathf.LerpAngle(a.windDirection, b.windDirection, t),
            sunlight = Mathf.Lerp(a.sunlight, b.sunlight, t),
            silverLining = Mathf.Lerp(a.silverLining, b.silverLining, t),
        };

        const string Key = "dev.clouds";
        static CloudSettings? _current;

        /// <summary>
        /// Fine weather's clouds: what was last set in the Clouds window, or Scattered. Changing it
        /// shows at once and is saved.
        /// </summary>
        public static CloudSettings Current
        {
            get
            {
                if (_current == null)
                {
                    string saved = DevOptions.Enabled ? PlayerPrefs.GetString(Key, "") : "";
                    _current = string.IsNullOrEmpty(saved) ? Scattered : JsonUtility.FromJson<CloudSettings>(saved);
                }
                return _current.Value;
            }
            set
            {
                _current = value;
                PlayerPrefs.SetString(Key, JsonUtility.ToJson(value));
                PlayerPrefs.Save();
            }
        }

        /// <summary>Forgets what was set: back to Scattered.</summary>
        public static void ResetToDefaults()
        {
            PlayerPrefs.DeleteKey(Key);
            PlayerPrefs.Save();
            _current = Scattered;
        }
    }
}
