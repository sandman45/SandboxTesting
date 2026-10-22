using System;
using UnityEngine;

namespace WowSandbox
{
    /// <summary>
    /// The sea, as set in the Developer panel's Waves window: WaterWaves' calm and storm sea states
    /// and its wavelengths. The scene's own WaterWaves values until changed; once changed they're
    /// remembered and put on the sea every Play (StormWeather calls <see cref="ApplySaved"/>).
    /// Which state shows is the wind — the storm's, or CloudLayer's on a fine day.
    /// </summary>
    [Serializable]
    public struct WaveSettings
    {
        public WaterWaves.SeaState calm;
        public WaterWaves.SeaState storm;
        public float longestWave;
        public float shortestWave;

        public static readonly (string name, WaterWaves.SeaState sea)[] CalmPresets =
        {
            ("Glassy", new WaterWaves.SeaState { height = 0.4f, choppiness = 0.05f, chaos = 0.1f, speed = 0.7f, whitecaps = 0f, ripples = 0.3f }),
            ("Calm", new WaterWaves.SeaState { height = 1.5f, choppiness = 0.2f, chaos = 0.2f, speed = 0.9f, whitecaps = 0f, ripples = 0.8f }),
            ("Choppy", new WaterWaves.SeaState { height = 2.2f, choppiness = 0.5f, chaos = 0.4f, speed = 1f, whitecaps = 0.15f, ripples = 1.1f }),
            ("Rough", new WaterWaves.SeaState { height = 3.5f, choppiness = 0.7f, chaos = 0.55f, speed = 1.15f, whitecaps = 0.4f, ripples = 1.3f }),
        };

        const string Key = "dev.waves";
        static WaveSettings? _sceneDefaults;
        static WaterWaves _sea;

        static WaterWaves Sea => _sea != null ? _sea : _sea = UnityEngine.Object.FindAnyObjectByType<WaterWaves>();

        /// <summary>What the sea is set to now, or null without one.</summary>
        public static WaveSettings? Current
        {
            get
            {
                var sea = Sea;
                return sea != null ? Read(sea) : null;
            }
        }

        /// <summary>Puts <paramref name="settings"/> on the sea and remembers them.</summary>
        public static void Set(WaveSettings settings)
        {
            var sea = Sea;
            if (sea == null)
                return;
            Write(sea, settings);
            PlayerPrefs.SetString(Key, JsonUtility.ToJson(settings));
            PlayerPrefs.Save();
        }

        /// <summary>Back to the scene's own WaterWaves values, forgetting what was set.</summary>
        public static void ResetToDefaults()
        {
            PlayerPrefs.DeleteKey(Key);
            PlayerPrefs.Save();
            var sea = Sea;
            if (sea != null && _sceneDefaults != null)
                Write(sea, _sceneDefaults.Value);
        }

        /// <summary>As Play starts: notes the scene's values, then puts anything saved on the sea.</summary>
        public static void ApplySaved(WaterWaves sea)
        {
            if (sea == null)
                return;
            _sceneDefaults = Read(sea);
            string saved = DevOptions.Enabled ? PlayerPrefs.GetString(Key, "") : "";
            if (!string.IsNullOrEmpty(saved))
                Write(sea, JsonUtility.FromJson<WaveSettings>(saved));
        }

        static WaveSettings Read(WaterWaves sea) => new()
        {
            calm = sea.calm, storm = sea.storm, longestWave = sea.longestWave, shortestWave = sea.shortestWave,
        };

        static void Write(WaterWaves sea, WaveSettings settings)
        {
            sea.calm = settings.calm;
            sea.storm = settings.storm;
            sea.longestWave = Mathf.Max(0.5f, settings.longestWave);
            sea.shortestWave = Mathf.Clamp(settings.shortestWave, 0.25f, sea.longestWave);
        }
    }
}
