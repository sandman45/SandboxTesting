using System;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace WowSandbox
{
    /// <summary>
    /// The Developer panel's Fog window: presets (None, Light Mist, Sea Fog, Pea Soup, Auto), the
    /// Auto schedule's dawn and night mists, and sliders for how the sea fog looks
    /// (FogSettings.Current). Every change shows at once and is remembered.
    /// </summary>
    public class FogPanel : HudPanel
    {
        const float Width = 470f;
        const float Pad = 20f;
        const float ButtonWidth = 136f;
        const float ButtonGap = 7f;

        Action _refreshSliders;
        Text _status;
        Text _mode;
        Text _dawn;
        Text _night;

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
                22, TextAnchor.MiddleCenter, HudTheme.Heading, FontStyle.Bold).text = "Sea Fog";

            float y = -50f;
            _status = Hud.Label("Status", Root, topLeft, new Vector2(Pad + 8f, y), new Vector2(Width - Pad * 2f, 20f),
                13, TextAnchor.MiddleLeft, HudTheme.TextDim);
            y -= 26f;

            var presets = FogSettings.Presets;
            for (int i = 0; i < presets.Length; i++)
            {
                var (name, settings) = presets[i];
                int column = i % 3;
                Hud.TextButton(name, Root, topLeft,
                    new Vector2(Pad + column * (ButtonWidth + ButtonGap), y), new Vector2(ButtonWidth, 26f),
                    () => Change(f =>
                    {
                        // A preset sets the look and when; the schedule's switches stay as you left them.
                        settings.dawnMist = f.dawnMist;
                        settings.nightMist = f.nightMist;
                        return settings;
                    }));
                if (column == 2 || i == presets.Length - 1)
                    y -= 32f;
            }

            _mode = Toggle(0, y, () => Change(f =>
            {
                f.mode = (FogSettings.Mode)(((int)f.mode + 1) % 3);
                return f;
            }));
            _dawn = Toggle(1, y, () => Change(f => { f.dawnMist = !f.dawnMist; return f; }));
            _night = Toggle(2, y, () => Change(f => { f.nightMist = !f.nightMist; return f; }));
            y -= 38f;

            Row("Bank distance", ref y, 0f, 600f, f => f.bankDistance, (f, v) => { f.bankDistance = v; return f; }, v => $"{v:0}");
            Row("Bank width", ref y, 40f, 800f, f => f.bankWidth, (f, v) => { f.bankWidth = v; return f; }, v => $"{v:0}");
            Row("Bank height", ref y, 2f, 80f, f => f.bankHeight, (f, v) => { f.bankHeight = v; return f; }, v => $"{v:0}");
            Row("Bank density", ref y, 0f, 3f, f => f.bankDensity, (f, v) => { f.bankDensity = v; return f; }, v => $"{v:0.00}");
            Row("Mist density", ref y, 0f, 2f, f => f.mistDensity, (f, v) => { f.mistDensity = v; return f; }, v => $"{v:0.00}");
            Row("Mist height", ref y, 0.5f, 20f, f => f.mistHeight, (f, v) => { f.mistHeight = v; return f; }, v => $"{v:0.0}");
            Row("Visibility cut", ref y, 0.005f, 0.2f, f => f.visibility, (f, v) => { f.visibility = v; return f; }, v => $"{v:0.000}");
            Row("Drift", ref y, 0f, 20f, f => f.drift, (f, v) => { f.drift = v; return f; }, v => $"{v:0.0}");

            Root.sizeDelta = new Vector2(Width, -y + 56f);

            var bottom = new Vector2(0.5f, 0f);
            Hud.TextButton("Defaults", Root, bottom, new Vector2(-120f, 18f), new Vector2(110f, 30f), FogSettings.ResetToDefaults);
            Hud.TextButton("Back", Root, bottom, new Vector2(0f, 18f), new Vector2(110f, 30f), () =>
            {
                Close();
                GetComponent<DevPanel>()?.Open();
            });
            Hud.TextButton("Close", Root, bottom, new Vector2(120f, 18f), new Vector2(110f, 30f), Close);
        }

        protected override void Refresh()
        {
            var fog = FogSettings.Current;
            var sea = SeaFog.Active;
            _status.text = sea == null ? "No sea fog: SeaFog isn't running."
                         : $"{sea.Reason} — showing {sea.Amount * 100f:0}%.";
            _mode.text = $"When: {fog.mode}";
            _dawn.text = $"Dawn mist: {(fog.dawnMist ? "On" : "Off")}";
            _night.text = $"Night mist: {(fog.nightMist ? "On" : "Off")}";
            _refreshSliders?.Invoke();
        }

        static void Change(Func<FogSettings, FogSettings> change) => FogSettings.Current = change(FogSettings.Current);

        Text Toggle(int column, float y, UnityEngine.Events.UnityAction onClick)
        {
            var button = Hud.TextButton("Toggle", Root, new Vector2(0f, 1f),
                new Vector2(Pad + column * (ButtonWidth + ButtonGap), y), new Vector2(ButtonWidth, 26f), onClick);
            var label = button.GetComponentInChildren<Text>();
            label.fontSize = 12;
            return label;
        }

        void Row(string label, ref float y, float min, float max, Func<FogSettings, float> get,
                 Func<FogSettings, float, FogSettings> set, Func<float, string> format)
        {
            var topLeft = new Vector2(0f, 1f);
            Hud.Label(label, Root, topLeft, new Vector2(Pad + 8f, y), new Vector2(130f, 24f),
                14, TextAnchor.MiddleLeft, HudTheme.Text).text = label;
            var slider = Hud.Slider(Root, topLeft, new Vector2(Pad + 140f, y - 6f), new Vector2(220f, 12f), min, max);
            var value = Hud.Label("Value", Root, topLeft, new Vector2(Width - Pad - 60f, y), new Vector2(60f, 24f),
                14, TextAnchor.MiddleRight, HudTheme.TextDim);

            slider.onValueChanged.AddListener(v =>
            {
                Change(f => set(f, v));
                value.text = format(v);
            });
            _refreshSliders += () =>
            {
                float current = get(FogSettings.Current);
                slider.SetValueWithoutNotify(current);
                value.text = format(current);
            };
            y -= 30f;
        }
    }
}
