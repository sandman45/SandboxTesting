using System;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace WowSandbox
{
    /// <summary>
    /// The Developer panel's Waves window: presets and sliders for the calm sea (WaterWaves.SeaState),
    /// and the wavelengths — which reshuffle the waves, so they jump rather than blend. The storm
    /// sea stays as the scene has it, as the clouds' storm sky does; the wind blends toward it.
    /// Every change shows at once and is remembered (WaveSettings); Defaults forgets them, back to
    /// the scene's own sea.
    /// </summary>
    public class WavePanel : HudPanel
    {
        const float Width = 470f;
        const float Pad = 20f;
        const float ButtonWidth = 101f;
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
                22, TextAnchor.MiddleCenter, HudTheme.Heading, FontStyle.Bold).text = "Waves";

            float y = -50f;
            _status = Hud.Label("Status", Root, topLeft, new Vector2(Pad + 8f, y), new Vector2(Width - Pad * 2f, 20f),
                13, TextAnchor.MiddleLeft, HudTheme.TextDim);
            y -= 26f;

            Hud.Label("Calm sea", Root, topLeft, new Vector2(Pad + 8f, y), new Vector2(200f, 20f),
                14, TextAnchor.MiddleLeft, HudTheme.Heading, FontStyle.Bold).text = "Calm sea";
            y -= 24f;
            var presets = WaveSettings.CalmPresets;
            for (int i = 0; i < presets.Length; i++)
            {
                var (name, sea) = presets[i];
                Hud.TextButton(name, Root, topLeft, new Vector2(Pad + i * (ButtonWidth + ButtonGap), y),
                    new Vector2(ButtonWidth, 26f), () => Change(w => { w.calm = sea; return w; }));
            }
            y -= 36f;

            SeaRow("Height", ref y, 0f, 8f, s => s.height, (s, v) => { s.height = v; return s; }, v => $"{v:0.00}");
            SeaRow("Choppiness", ref y, 0f, 1f, s => s.choppiness, (s, v) => { s.choppiness = v; return s; }, Percent);
            SeaRow("Chaos", ref y, 0f, 1f, s => s.chaos, (s, v) => { s.chaos = v; return s; }, Percent);
            SeaRow("Speed", ref y, 0f, 3f, s => s.speed, (s, v) => { s.speed = v; return s; }, v => $"{v:0.00}×");
            SeaRow("Whitecaps", ref y, 0f, 1f, s => s.whitecaps, (s, v) => { s.whitecaps = v; return s; }, Percent);
            SeaRow("Ripples", ref y, 0f, 2f, s => s.ripples, (s, v) => { s.ripples = v; return s; }, v => $"{v:0.00}");

            y -= 6f;
            Hud.Label("Wavelengths", Root, topLeft, new Vector2(Pad + 8f, y), new Vector2(Width - Pad * 2f, 20f),
                14, TextAnchor.MiddleLeft, HudTheme.Heading, FontStyle.Bold).text = "Wavelengths (these reshuffle the waves)";
            y -= 24f;
            Row("Longest", ref y, 5f, 120f, w => w.longestWave, (w, v) => { w.longestWave = v; return w; }, v => $"{v:0}");
            Row("Shortest", ref y, 1f, 20f, w => w.shortestWave, (w, v) => { w.shortestWave = v; return w; }, v => $"{v:0.0}");

            Root.sizeDelta = new Vector2(Width, -y + 56f);

            var bottom = new Vector2(0.5f, 0f);
            Hud.TextButton("Defaults", Root, bottom, new Vector2(-120f, 18f), new Vector2(110f, 30f), WaveSettings.ResetToDefaults);
            Hud.TextButton("Back", Root, bottom, new Vector2(0f, 18f), new Vector2(110f, 30f), () =>
            {
                Close();
                GetComponent<DevPanel>()?.Open();
            });
            Hud.TextButton("Close", Root, bottom, new Vector2(120f, 18f), new Vector2(110f, 30f), Close);
        }

        protected override void Refresh()
        {
            var sea = FindAnyObjectByType<WaterWaves>();
            _status.text = sea == null ? "No sea: there's no WaterWaves in the scene."
                         : $"Wind {CloudLayer.WindStrength * 100f:0}% — the sea is {sea.stormIntensity * 100f:0}% of the way to its storm state.";
            _refreshSliders?.Invoke();
        }

        static string Percent(float v) => $"{v * 100f:0}%";

        static void Change(Func<WaveSettings, WaveSettings> change)
        {
            var current = WaveSettings.Current;
            if (current != null)
                WaveSettings.Set(change(current.Value));
        }

        /// <summary>A slider on the calm sea.</summary>
        void SeaRow(string label, ref float y, float min, float max, Func<WaterWaves.SeaState, float> get,
                    Func<WaterWaves.SeaState, float, WaterWaves.SeaState> set, Func<float, string> format)
        {
            Row(label, ref y, min, max, w => get(w.calm), (w, v) => { w.calm = set(w.calm, v); return w; }, format);
        }

        void Row(string label, ref float y, float min, float max, Func<WaveSettings, float> get,
                 Func<WaveSettings, float, WaveSettings> set, Func<float, string> format)
        {
            var topLeft = new Vector2(0f, 1f);
            Hud.Label(label, Root, topLeft, new Vector2(Pad + 8f, y), new Vector2(130f, 24f),
                14, TextAnchor.MiddleLeft, HudTheme.Text).text = label;
            var slider = Hud.Slider(Root, topLeft, new Vector2(Pad + 140f, y - 6f), new Vector2(220f, 12f), min, max);
            var value = Hud.Label("Value", Root, topLeft, new Vector2(Width - Pad - 60f, y), new Vector2(60f, 24f),
                14, TextAnchor.MiddleRight, HudTheme.TextDim);

            slider.onValueChanged.AddListener(v =>
            {
                Change(w => set(w, v));
                value.text = format(v);
            });
            _refreshSliders += () =>
            {
                var current = WaveSettings.Current;
                if (current == null)
                    return;
                float shown = get(current.Value);
                slider.SetValueWithoutNotify(shown);
                value.text = format(shown);
            };
            y -= 30f;
        }
    }
}
