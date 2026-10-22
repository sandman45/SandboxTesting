using System;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace WowSandbox
{
    /// <summary>
    /// The Developer panel's Clouds window: presets from a clear sky to a storm, and sliders for
    /// fine weather's clouds (CloudSettings.Current) — coverage, density, size, height, depth,
    /// softness, wind and light. Every change shows at once and is remembered. A storm still
    /// blends the sky toward Storm on top of whatever's set here.
    /// </summary>
    public class CloudPanel : HudPanel
    {
        const float Width = 470f;
        const float Pad = 20f;
        const float ButtonWidth = 136f;
        const float ButtonGap = 7f;

        Action _refreshSliders;
        Text _status;

        protected override Vector2 PanelSize => new(Width, 600f);
        protected override Vector2 PanelAnchor => new(0.5f, 0.5f);
        protected override Vector2 PanelPosition => Vector2.zero;

        protected override void Awake()
        {
            toggleKey = Key.None;
            base.Awake();
        }

        protected override void Build()
        {
            var topLeft = new Vector2(0f, 1f);
            Hud.Label("Title", Root, new Vector2(0.5f, 1f), new Vector2(0f, -14f), new Vector2(Width, 28f),
                22, TextAnchor.MiddleCenter, HudTheme.Heading, FontStyle.Bold).text = "Clouds";

            float y = -50f;
            _status = Hud.Label("Status", Root, topLeft, new Vector2(Pad + 8f, y), new Vector2(Width - Pad * 2f, 20f),
                13, TextAnchor.MiddleLeft, HudTheme.TextDim);
            y -= 26f;

            var presets = CloudSettings.Presets;
            for (int i = 0; i < presets.Length; i++)
            {
                var (name, settings) = presets[i];
                int column = i % 3;
                // Presets run in the same order as the weather system's skies.
                var sky = (WeatherSystem.Sky)i;
                Hud.TextButton(name, Root, topLeft,
                    new Vector2(Pad + column * (ButtonWidth + ButtonGap), y), new Vector2(ButtonWidth, 26f),
                    () =>
                    {
                        // With the weather running, a preset sends the weather there instead.
                        if (WeatherSystem.Driving)
                            WeatherSystem.Active.GoTo(sky, 0.1f);
                        else
                            CloudSettings.Current = settings;
                    });
                if (column == 2)
                    y -= 32f;
            }
            y -= 6f;

            Row("Coverage", ref y, 0f, 1f, s => s.coverage, (b, v) => b.settings.coverage = v, Percent);
            Row("Density", ref y, 0.1f, 3f, s => s.density, (b, v) => b.settings.density = v, v => $"{v:0.00}");
            Row("Cloud size", ref y, 60f, 1200f, s => s.size, (b, v) => b.settings.size = v, v => $"{v:0}");
            Row("Base height", ref y, 100f, 1500f, s => s.height, (b, v) => b.settings.height = v, v => $"{v:0}");
            Row("Depth", ref y, 50f, 1200f, s => s.thickness, (b, v) => b.settings.thickness = v, v => $"{v:0}");
            Row("Softness", ref y, 0f, 1f, s => s.softness, (b, v) => b.settings.softness = v, Percent);
            Row("Wind speed", ref y, 0f, 80f, s => s.windSpeed, (b, v) => b.settings.windSpeed = v, v => $"{v:0.0}");
            Row("Wind direction", ref y, 0f, 360f, s => s.windDirection, (b, v) => b.settings.windDirection = v, v => $"{v:0}°");
            Row("Sunlight", ref y, 0f, 2f, s => s.sunlight, (b, v) => b.settings.sunlight = v, Percent);
            Row("Silver lining", ref y, 0f, 1f, s => s.silverLining, (b, v) => b.settings.silverLining = v, Percent);

            Root.sizeDelta = new Vector2(Width, -y + 56f);

            var bottom = new Vector2(0.5f, 0f);
            Hud.TextButton("Defaults", Root, bottom, new Vector2(-120f, 18f), new Vector2(110f, 30f), CloudSettings.ResetToDefaults);
            Hud.TextButton("Back", Root, bottom, new Vector2(0f, 18f), new Vector2(110f, 30f), () =>
            {
                Close();
                GetComponent<DevPanel>()?.Open();
            });
            Hud.TextButton("Close", Root, bottom, new Vector2(120f, 18f), new Vector2(110f, 30f), Close);
        }

        protected override void Refresh()
        {
            var weather = StormWeather.Active;
            float storm = weather != null ? weather.Overcast : 0f;
            _status.text = CloudLayer.Active == null ? "No clouds: CloudLayer isn't running."
                         : DevOptions.HideClouds ? "Hidden (Developer → Clouds: Hidden)."
                         : WeatherSystem.Driving ? "Weather system on: it sets cover, density, depth, softness, sun and wind."
                         : storm > 0.01f ? $"Fine weather below — the storm has it {storm * 100f:0}% of the way to Storm."
                         : "Fine weather's clouds. Storms blend these toward Storm.";
            _refreshSliders?.Invoke();
        }

        static string Percent(float v) => $"{v * 100f:0}%";

        void Row(string label, ref float y, float min, float max, Func<CloudSettings, float> get,
                 Action<Box, float> set, Func<float, string> format)
        {
            var topLeft = new Vector2(0f, 1f);
            Hud.Label(label, Root, topLeft, new Vector2(Pad + 8f, y), new Vector2(130f, 24f),
                14, TextAnchor.MiddleLeft, HudTheme.Text).text = label;
            var slider = Hud.Slider(Root, topLeft, new Vector2(Pad + 140f, y - 6f), new Vector2(220f, 12f), min, max);
            var value = Hud.Label("Value", Root, topLeft, new Vector2(Width - Pad - 60f, y), new Vector2(60f, 24f),
                14, TextAnchor.MiddleRight, HudTheme.TextDim);

            slider.onValueChanged.AddListener(v =>
            {
                var box = new Box { settings = CloudSettings.Current };
                set(box, v);
                CloudSettings.Current = box.settings;
                value.text = format(v);
            });
            _refreshSliders += () =>
            {
                float current = get(CloudSettings.Current);
                slider.SetValueWithoutNotify(current);
                value.text = format(current);
            };
            y -= 30f;
        }

        /// <summary>CloudSettings is a struct; the sliders' setters change it through this.</summary>
        class Box
        {
            public CloudSettings settings;
        }
    }
}
