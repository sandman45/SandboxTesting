using UnityEngine;
using UnityEngine.UI;

namespace WowSandbox
{
    /// <summary>
    /// The WoW cast bar: a bar low in the middle of the screen that fills while you're busy
    /// doing something that takes time — fishing, for now — with its name on the left and
    /// the seconds left on the right. Interrupt turns it red with "Interrupted" and lets it
    /// fade. One shared bar, built on the HUD the first time anything casts.
    /// </summary>
    public class CastBarHud : MonoBehaviour
    {
        static readonly Color CastColor = new(1f, 0.75f, 0.2f);
        static readonly Color DoneColor = new(0.3f, 0.85f, 0.3f);

        static CastBarHud _instance;

        CanvasGroup _group;
        HudBar _bar;
        Text _name;
        Text _time;
        bool _showing;
        float _hideAt;

        /// <summary>Shows the bar at <paramref name="progress01"/>, with <paramref name="secondsLeft"/> on the right.</summary>
        public static void Show(string label, float progress01, float secondsLeft)
        {
            var bar = Get();
            bar._showing = true;
            bar._group.alpha = 1f;
            bar._name.text = label;
            bar._time.text = secondsLeft > 0f ? $"{secondsLeft:0.0}" : "";
            bar._bar.FillColor = CastColor;
            bar._bar.Snap(progress01);
        }

        /// <summary>Full and green for a moment, then fades — the cast went off.</summary>
        public static void Finish() => End(null, DoneColor);

        /// <summary>Red with "Interrupted", then fades.</summary>
        public static void Interrupt() => End("Interrupted", HudTheme.HealthLow);

        static void End(string label, Color color)
        {
            var bar = Get();
            if (!bar._showing)
                return;
            bar._showing = false;
            bar._hideAt = Time.time + 0.4f;
            if (label != null)
                bar._name.text = label;
            bar._time.text = "";
            bar._bar.FillColor = color;
            bar._bar.Snap(1f);
        }

        static CastBarHud Get()
        {
            // Unity's null: the HUD canvas, and this with it, goes away at the end of Play.
            if (_instance != null)
                return _instance;

            var root = Hud.Rect("CastBar", Hud.Layer(HudLayer.Frames), new Vector2(0.5f, 0f),
                new Vector2(0f, 190f), new Vector2(260f, 20f));
            _instance = root.gameObject.AddComponent<CastBarHud>();
            _instance._group = root.gameObject.AddComponent<CanvasGroup>();
            _instance._group.alpha = 0f;
            _instance._group.blocksRaycasts = false;

            _instance._bar = new HudBar("Bar", root, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(260f, 20f));
            _instance._name = Hud.Label("Name", root, new Vector2(0f, 0.5f), new Vector2(10f, 0f), new Vector2(180f, 20f),
                13, TextAnchor.MiddleLeft, HudTheme.Text, FontStyle.Bold);
            _instance._time = Hud.Label("Time", root, new Vector2(1f, 0.5f), new Vector2(-10f, 0f), new Vector2(60f, 20f),
                13, TextAnchor.MiddleRight, HudTheme.Text, FontStyle.Bold);
            return _instance;
        }

        void Update()
        {
            if (!_showing && Time.time >= _hideAt)
                _group.alpha = Mathf.MoveTowards(_group.alpha, 0f, 3f * Time.deltaTime);
        }
    }
}
