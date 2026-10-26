using UnityEngine;
using UnityEngine.UI;

namespace WowSandbox
{
    /// <summary>
    /// WoW's time-of-day dial: the top half of a turning disc, the sun and moon wheeling across
    /// it (the sun at the top at noon, rising on the left and setting on the right, the moon
    /// opposite) over a sky that darkens with the night. The minimap's clock and the rest window's
    /// big one are both this, at different sizes.
    /// </summary>
    public class ClockDial
    {
        public static readonly Color DaySky = new(0.36f, 0.6f, 0.88f);
        public static readonly Color NightSky = new(0.04f, 0.06f, 0.15f);

        // The minimap's size, which the sun, moon and frame were drawn at.
        const float DrawnAt = 50f;

        readonly RectTransform _dial;
        readonly Image _sky;
        readonly Image _moonShadow;

        /// <summary>The window the dial shows through: size wide, half that tall.</summary>
        public RectTransform Window { get; }

        public ClockDial(Transform parent, Vector2 anchor, Vector2 position, float size)
        {
            float scale = size / DrawnAt;
            Window = Hud.Rect("Window", parent, anchor, position, new Vector2(size, size * 0.5f));
            Window.gameObject.AddComponent<RectMask2D>();

            var bottom = new Vector2(0.5f, 0f);
            var sky = Hud.Rect("Sky", Window, bottom, new Vector2(0f, -size * 0.5f), new Vector2(size, size));
            _sky = sky.gameObject.AddComponent<Image>();
            _sky.sprite = Hud.Circle;
            _sky.raycastTarget = false;

            // Pivot in the middle — it turns about its centre, which sits on the window's bottom edge.
            _dial = Hud.Rect("Dial", Window, bottom, Vector2.zero, new Vector2(size, size));
            _dial.pivot = new Vector2(0.5f, 0.5f);
            float orbit = size * 0.3f;
            var centre = new Vector2(0.5f, 0.5f);

            var sun = Hud.Rect("Sun", _dial, centre, new Vector2(0f, orbit), Vector2.one * 13f * scale)
                .gameObject.AddComponent<Image>();
            sun.sprite = Hud.Circle;
            sun.color = new Color(1f, 0.85f, 0.35f);
            sun.raycastTarget = false;

            var moon = Hud.Rect("Moon", _dial, centre, new Vector2(0f, -orbit), Vector2.one * 12f * scale);
            var moonImage = moon.gameObject.AddComponent<Image>();
            moonImage.sprite = Hud.Circle;
            moonImage.color = new Color(0.86f, 0.9f, 1f);
            moonImage.raycastTarget = false;
            // A sky-coloured disc over most of it leaves a crescent.
            _moonShadow = Hud.Rect("Shadow", moon, centre, new Vector2(4f, 2f) * scale, Vector2.one * 11f * scale)
                .gameObject.AddComponent<Image>();
            _moonShadow.sprite = Hud.Circle;
            _moonShadow.raycastTarget = false;

            float frame = 2f * Mathf.Max(1f, scale * 0.6f);
            var ring = Hud.Rect("Ring", Window, bottom, new Vector2(0f, -size * 0.5f - frame),
                new Vector2(size + frame * 2f, size + frame * 2f)).gameObject.AddComponent<Image>();
            ring.sprite = Hud.CircleRing;
            ring.color = HudTheme.PanelBorder;
            ring.raycastTarget = false;
            var horizon = Hud.Rect("Horizon", Window, bottom, Vector2.zero, new Vector2(size, frame))
                .gameObject.AddComponent<Image>();
            horizon.color = HudTheme.PanelBorder;
            horizon.raycastTarget = false;
        }

        /// <summary>Turns the dial to <paramref name="hour"/>, the sky as light as <paramref name="daylight"/> (0 night to 1 day).</summary>
        public void Show(float hour, float daylight)
        {
            _dial.localRotation = Quaternion.Euler(0f, 0f, (12f - hour) / 24f * 360f);
            var sky = Color.Lerp(NightSky, DaySky, daylight);
            _sky.color = sky;
            _moonShadow.color = sky;
        }

        /// <summary>An hour of the day as WoW's clock shows it: 9:30 PM.</summary>
        public static string Format(float hour)
        {
            int minutes = Mathf.FloorToInt(Mathf.Repeat(hour, 24f) * 60f);
            int h = minutes / 60;
            return $"{(h % 12 == 0 ? 12 : h % 12)}:{minutes % 60:00} {(h < 12 ? "AM" : "PM")}";
        }
    }
}
