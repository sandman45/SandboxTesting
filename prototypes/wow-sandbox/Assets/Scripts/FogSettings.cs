using System;
using UnityEngine;

namespace WowSandbox
{
    /// <summary>
    /// How the sea fog looks and when it comes (SeaFog): the bank standing on the water round the
    /// island — how far out, how wide and tall, how thick — the low mist over the water and the
    /// beach, how much it cuts what you can see, and how it drifts. <see cref="mode"/> says when:
    /// on its own schedule (dawn mist, night mist), always, or never. Set with the Developer
    /// panel's Fog window and remembered.
    /// </summary>
    [Serializable]
    public struct FogSettings
    {
        public enum Mode
        {
            /// <summary>Comes and goes with the time of day: <see cref="dawnMist"/>, <see cref="nightMist"/>.</summary>
            Auto,
            Always,
            Off,
        }

        public Mode mode;
        [Tooltip("Auto: a fog that rolls in before sunrise and burns off by mid-morning.")]
        public bool dawnMist;
        [Tooltip("Auto: a thin mist over the water after dark, thicker past midnight.")]
        public bool nightMist;

        [Tooltip("How far out past the island's edge the bank begins, world units.")]
        public float bankDistance;
        [Tooltip("How deep the bank is, from its inner edge outward.")]
        public float bankWidth;
        [Tooltip("How tall the bank stands off the water (it thins upward).")]
        public float bankHeight;
        [Range(0f, 3f)] public float bankDensity;
        [Tooltip("The low mist over the water and beach everywhere.")]
        [Range(0f, 2f)] public float mistDensity;
        public float mistHeight;
        [Tooltip("How much a given thickness of fog hides: low and you see far into it.")]
        public float visibility;
        [Tooltip("How fast it drifts and churns, world units per second (along the clouds' wind).")]
        public float drift;

        public static readonly FogSettings None = Look(Mode.Off, 1f, 0.4f, 0.03f);
        public static readonly FogSettings LightMist = Look(Mode.Always, 0.4f, 0.25f, 0.02f);
        public static readonly FogSettings SeaFog = Look(Mode.Always, 1f, 0.5f, 0.035f);
        public static readonly FogSettings PeaSoup = Look(Mode.Always, 2f, 1.6f, 0.08f, distance: 0f, height: 45f);
        /// <summary>The sea fog, coming and going with the time of day.</summary>
        public static readonly FogSettings Auto = Look(Mode.Auto, 1f, 0.5f, 0.035f);

        public static readonly (string name, FogSettings settings)[] Presets =
        {
            ("None", None), ("Light Mist", LightMist), ("Sea Fog", SeaFog), ("Pea Soup", PeaSoup), ("Auto", Auto),
        };

        static FogSettings Look(Mode mode, float bank, float mist, float visibility, float distance = 80f, float height = 22f) => new()
        {
            mode = mode, dawnMist = true, nightMist = true,
            bankDistance = distance, bankWidth = 260f, bankHeight = height, bankDensity = bank,
            mistDensity = mist, mistHeight = 4f, visibility = visibility, drift = 2f,
        };

        const string Key = "dev.fog";
        static FogSettings? _current;

        /// <summary>What was last set in the Fog window, or Auto. Changing it shows at once and is saved.</summary>
        public static FogSettings Current
        {
            get
            {
                if (_current == null)
                {
                    string saved = DevOptions.Enabled ? PlayerPrefs.GetString(Key, "") : "";
                    _current = string.IsNullOrEmpty(saved) ? Auto : JsonUtility.FromJson<FogSettings>(saved);
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

        /// <summary>Forgets what was set: back to Auto, dawn and night mists on.</summary>
        public static void ResetToDefaults()
        {
            PlayerPrefs.DeleteKey(Key);
            PlayerPrefs.Save();
            _current = Auto;
        }
    }
}
