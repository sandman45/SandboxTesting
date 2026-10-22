using System;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace WowSandbox
{
    /// <summary>
    /// The Developer panel's Weather window: the weather system on or off, buttons to send it to
    /// any sky now (Storm starts one), how quickly fronts come and how often an overcast sky turns
    /// to a storm, and a forecast — what it is, where it's heading, when it next changes.
    /// </summary>
    public class WeatherPanel : HudPanel
    {
        const float Width = 470f;
        const float Pad = 20f;
        const float ButtonWidth = 136f;
        const float ButtonGap = 7f;

        Action _refreshSliders;
        Text _forecast;
        Text _onOff;

        protected override Vector2 PanelSize => new(Width, 400f);
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
                22, TextAnchor.MiddleCenter, HudTheme.Heading, FontStyle.Bold).text = "Weather";

            float y = -50f;
            _forecast = Hud.Label("Forecast", Root, topLeft, new Vector2(Pad + 8f, y), new Vector2(Width - Pad * 2f, 36f),
                13, TextAnchor.UpperLeft, HudTheme.TextDim);
            _forecast.horizontalOverflow = HorizontalWrapMode.Wrap;
            y -= 42f;

            var toggle = Hud.TextButton("On", Root, topLeft, new Vector2(Pad, y),
                new Vector2(ButtonWidth * 3f + ButtonGap * 2f, 26f), () => WeatherSystem.On = !WeatherSystem.On);
            _onOff = toggle.GetComponentInChildren<Text>();
            y -= 36f;

            Hud.Label("Go to", Root, topLeft, new Vector2(Pad + 8f, y), new Vector2(200f, 20f),
                14, TextAnchor.MiddleLeft, HudTheme.Heading, FontStyle.Bold).text = "Send the weather to";
            y -= 24f;
            var skies = (WeatherSystem.Sky[])Enum.GetValues(typeof(WeatherSystem.Sky));
            for (int i = 0; i < skies.Length; i++)
            {
                var sky = skies[i];
                int column = i % 3;
                Hud.TextButton(sky.ToString(), Root, topLeft,
                    new Vector2(Pad + column * (ButtonWidth + ButtonGap), y), new Vector2(ButtonWidth, 26f),
                    () => WeatherSystem.Active?.GoTo(sky, 0.15f));
                if (column == 2)
                    y -= 32f;
            }
            y -= 6f;

            Row("Pace", ref y, 0.25f, 4f, () => WeatherSystem.Pace, v => WeatherSystem.Pace = v, v => $"{v:0.00}×");
            Row("Storm chance", ref y, 0f, 1f, () => WeatherSystem.StormChance, v => WeatherSystem.StormChance = v,
                v => $"{v * 100f:0}%");

            Root.sizeDelta = new Vector2(Width, -y + 56f);

            var bottom = new Vector2(0.5f, 0f);
            Hud.TextButton("Back", Root, bottom, new Vector2(-65f, 18f), new Vector2(110f, 30f), () =>
            {
                Close();
                GetComponent<DevPanel>()?.Open();
            });
            Hud.TextButton("Close", Root, bottom, new Vector2(65f, 18f), new Vector2(110f, 30f), Close);
        }

        protected override void Refresh()
        {
            _forecast.text = WeatherSystem.Active != null ? WeatherSystem.Active.Forecast() : "No weather system in the scene.";
            _onOff.text = WeatherSystem.On ? "Weather system: On (changes on its own)" : "Weather system: Off (sky set by hand)";
            _refreshSliders?.Invoke();
        }

        void Row(string label, ref float y, float min, float max, Func<float> get, Action<float> set,
                 Func<float, string> format)
        {
            var topLeft = new Vector2(0f, 1f);
            Hud.Label(label, Root, topLeft, new Vector2(Pad + 8f, y), new Vector2(130f, 24f),
                14, TextAnchor.MiddleLeft, HudTheme.Text).text = label;
            var slider = Hud.Slider(Root, topLeft, new Vector2(Pad + 140f, y - 6f), new Vector2(220f, 12f), min, max);
            var value = Hud.Label("Value", Root, topLeft, new Vector2(Width - Pad - 60f, y), new Vector2(60f, 24f),
                14, TextAnchor.MiddleRight, HudTheme.TextDim);

            slider.onValueChanged.AddListener(v =>
            {
                set(v);
                value.text = format(v);
            });
            _refreshSliders += () =>
            {
                slider.SetValueWithoutNotify(get());
                value.text = format(get());
            };
            y -= 30f;
        }
    }
}
