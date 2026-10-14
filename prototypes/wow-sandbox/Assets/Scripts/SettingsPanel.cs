using System;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace WowSandbox
{
    /// <summary>
    /// The settings menu, on Esc like WoW's game menu: Esc closes whatever window is open
    /// first, and opens this once there's nothing left to close. Sliders for view distance,
    /// fog, master/music/effects volume, mouse sensitivity and the camera's zoom and field of
    /// view, each applied the moment it
    /// moves and remembered between sessions (GameSettings), plus Defaults, Quit Game and —
    /// while DevOptions.Enabled — Developer (DevPanel).
    ///
    /// It needs no setup: one is created in every scene at Play, and it applies the saved
    /// settings as the game starts.
    /// </summary>
    public class SettingsPanel : HudPanel
    {
        const float Width = 420f;
        const float Pad = 22f;
        const float RowHeight = 34f;

        Action _refreshValues;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void CreateInScene()
        {
            if (FindAnyObjectByType<SettingsPanel>() != null)
                return;
            var go = new GameObject("Settings");
            go.AddComponent<SettingsPanel>();
            if (DevOptions.Enabled)
                go.AddComponent<DevPanel>();
        }

        protected override Vector2 PanelSize => new(Width, 554f);
        protected override Vector2 PanelAnchor => new(0.5f, 0.5f);
        protected override Vector2 PanelPosition => Vector2.zero;

        protected override void Awake()
        {
            toggleKey = Key.None;
            base.Awake();
        }

        void Start()
        {
            // Before anything's changed, so "never set" means "as the scene has it".
            GameSettings.CaptureSceneDefaults();
        }

        protected override void Update()
        {
            // Esc opens it once no other window is left for Esc to close.
            var keyboard = Keyboard.current;
            if (!IsOpen && keyboard != null && keyboard.escapeKey.wasPressedThisFrame &&
                !EscapeUsedThisFrame && !AnyOpen)
            {
                UseEscape();
                Open();
                return;
            }

            base.Update();
        }

        protected override void Build()
        {
            var top = new Vector2(0.5f, 1f);
            Hud.Label("Title", Root, top, new Vector2(0f, -14f), new Vector2(Width, 28f),
                22, TextAnchor.MiddleCenter, HudTheme.Heading, FontStyle.Bold).text = "Settings";

            float y = -54f;
            Heading("Graphics", ref y);
            Row("View distance", ref y, GameSettings.MinView, GameSettings.MaxView,
                () => GameSettings.ViewDistance, v => GameSettings.ViewDistance = v, v => $"{v:0}");
            Row("Fog density", ref y, 0f, 1f,
                () => GameSettings.FogDensity, v => GameSettings.FogDensity = v, Percent);

            Heading("Sound", ref y);
            Row("Master volume", ref y, 0f, 1f,
                () => GameSettings.MasterVolume, v => GameSettings.MasterVolume = v, Percent);
            Row("Music", ref y, 0f, 1f,
                () => GameSettings.MusicVolume, v => GameSettings.MusicVolume = v, Percent);
            Row("Sound effects", ref y, 0f, 1f,
                () => GameSettings.EffectsVolume, v => GameSettings.EffectsVolume = v, Percent);

            Heading("Controls", ref y);
            Row("Mouse sensitivity", ref y, 0.05f, 0.6f,
                () => GameSettings.MouseSensitivity, v => GameSettings.MouseSensitivity = v,
                v => $"{v / 0.18f:0.0}×");

            Heading("Camera", ref y);
            Row("Max zoom out", ref y, GameSettings.SettingsMinZoom, GameSettings.SettingsMaxZoom,
                () => GameSettings.MaxZoomDistance, v => GameSettings.MaxZoomDistance = v, v => $"{v:0}");
            Row("Zoom speed", ref y, GameSettings.MinZoomSpeed, GameSettings.MaxZoomSpeed,
                () => GameSettings.ZoomSpeed, v => GameSettings.ZoomSpeed = v, v => $"{v:0.0}");
            Row("Field of view", ref y, GameSettings.MinFov, GameSettings.MaxFov,
                () => GameSettings.FieldOfView, v => GameSettings.FieldOfView = v, v => $"{v:0}°");

            var bottom = new Vector2(0.5f, 0f);
            var size = new Vector2(88f, 30f);
            Hud.TextButton("Defaults", Root, bottom, new Vector2(-144f, 20f), size, GameSettings.ResetToDefaults);
            Hud.TextButton("Quit Game", Root, bottom, new Vector2(-48f, 20f), size, Quit);
            if (DevOptions.Enabled)
            {
                Hud.TextButton("Developer", Root, bottom, new Vector2(48f, 20f), size, () =>
                {
                    Close();
                    GetComponent<DevPanel>()?.Open();
                });
            }
            Hud.TextButton("Close", Root, bottom, new Vector2(144f, 20f), size, Close);

            GameSettings.Changed += RefreshValues;
        }

        void OnDestroy() => GameSettings.Changed -= RefreshValues;

        // Values only change through GameSettings, which tells us; nothing to poll.
        protected override void Refresh() { }

        void RefreshValues() => _refreshValues?.Invoke();

        static string Percent(float value) => $"{value * 100f:0}%";

        void Heading(string text, ref float y)
        {
            y -= 6f;
            Hud.Label(text, Root, new Vector2(0f, 1f), new Vector2(Pad, y), new Vector2(Width - Pad * 2f, 22f),
                15, TextAnchor.MiddleLeft, HudTheme.Heading, FontStyle.Bold).text = text;
            y -= 26f;
        }

        /// <summary>A label, a slider and its value, bound to one setting.</summary>
        void Row(string label, ref float y, float min, float max, Func<float> get, Action<float> set,
                 Func<float, string> format)
        {
            var topLeft = new Vector2(0f, 1f);
            Hud.Label(label, Root, topLeft, new Vector2(Pad + 8f, y), new Vector2(150f, RowHeight - 6f),
                14, TextAnchor.MiddleLeft, HudTheme.Text).text = label;

            var slider = Hud.Slider(Root, topLeft, new Vector2(Pad + 160f, y - 8f), new Vector2(170f, 12f), min, max);
            var value = Hud.Label("Value", Root, topLeft, new Vector2(Width - Pad - 52f, y), new Vector2(52f, RowHeight - 6f),
                14, TextAnchor.MiddleRight, HudTheme.TextDim);

            void Show()
            {
                slider.SetValueWithoutNotify(get());
                value.text = format(get());
            }

            Show();
            slider.onValueChanged.AddListener(v =>
            {
                set(v);
                value.text = format(v);
            });
            _refreshValues += Show;
            y -= RowHeight;
        }

        static void Quit()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }
    }
}
