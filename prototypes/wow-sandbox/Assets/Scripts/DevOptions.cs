using UnityEngine;

namespace WowSandbox
{
    /// <summary>
    /// Developer options — DevPanel's switches that last between sessions: whether a storm
    /// rolls in at Play, and how long its stages take. Saved in PlayerPrefs, like
    /// GameSettings; anything never set follows StormWeather's own Inspector values.
    /// </summary>
    public static class DevOptions
    {
        /// <summary>Shows the Developer button in Settings. Set false to hide it for players.</summary>
        public const bool Enabled = true;

        const string StormOnPlayKey = "dev.stormOnPlay";
        const string SkipTitleKey = "dev.skipTitle";
        const string ShowMinimapKey = "dev.showMinimap";
        const string EnableMapKey = "dev.enableMap";
        const string BuildKey = "dev.stormBuildSeconds";
        const string HoldKey = "dev.stormHoldSeconds";
        const string ClearKey = "dev.stormClearSeconds";

        public static bool StormOnPlay
        {
            get => PlayerPrefs.GetInt(StormOnPlayKey, 1) == 1;
            set => Save(() => PlayerPrefs.SetInt(StormOnPlayKey, value ? 1 : 0));
        }

        /// <summary>Straight into the game at Play, without the title screen.</summary>
        public static bool SkipTitle
        {
            get => PlayerPrefs.GetInt(SkipTitleKey, 0) == 1;
            set => Save(() => PlayerPrefs.SetInt(SkipTitleKey, value ? 1 : 0));
        }

        /// <summary>The minimap shows without a map found (WorldMap.Found).</summary>
        public static bool ShowMinimap
        {
            get => PlayerPrefs.GetInt(ShowMinimapKey, 0) == 1;
            set => Save(() => PlayerPrefs.SetInt(ShowMinimapKey, value ? 1 : 0));
        }

        /// <summary>M opens the world map without a map found (WorldMap.Found).</summary>
        public static bool EnableMap
        {
            get => PlayerPrefs.GetInt(EnableMapKey, 0) == 1;
            set => Save(() => PlayerPrefs.SetInt(EnableMapKey, value ? 1 : 0));
        }

        /// <summary>Stage lengths in seconds; the weather's Inspector values until set.</summary>
        public static float BuildSeconds(StormWeather weather) => PlayerPrefs.GetFloat(BuildKey, weather.buildUpSeconds);
        public static float HoldSeconds(StormWeather weather) => PlayerPrefs.GetFloat(HoldKey, weather.fullStormSeconds);
        public static float ClearSeconds(StormWeather weather) => PlayerPrefs.GetFloat(ClearKey, weather.clearingSeconds);

        /// <summary>Sets a stage length and applies it to the running weather straight away.</summary>
        public static void SetBuildSeconds(StormWeather weather, float seconds) =>
            Save(() => PlayerPrefs.SetFloat(BuildKey, weather.buildUpSeconds = seconds));

        public static void SetHoldSeconds(StormWeather weather, float seconds) =>
            Save(() => PlayerPrefs.SetFloat(HoldKey, weather.fullStormSeconds = seconds));

        public static void SetClearSeconds(StormWeather weather, float seconds) =>
            Save(() => PlayerPrefs.SetFloat(ClearKey, weather.clearingSeconds = seconds));

        /// <summary>Called by StormWeather as Play starts, before it decides whether to build a storm.</summary>
        public static void Configure(StormWeather weather)
        {
            weather.buildUpSeconds = BuildSeconds(weather);
            weather.fullStormSeconds = HoldSeconds(weather);
            weather.clearingSeconds = ClearSeconds(weather);
            if (!StormOnPlay)
            {
                weather.buildsOnPlay = false;
                weather.intensity = 0f;
            }
        }

        static void Save(System.Action write)
        {
            write();
            PlayerPrefs.Save();
        }
    }
}
