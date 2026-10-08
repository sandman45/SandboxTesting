using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace WowSandbox
{
    /// <summary>
    /// Damage numbers that pop over an NPC when it's hit, drift up and fade — WoW's
    /// floating combat text. Listens to Health.AnyDamaged, so anything that damages an NPC
    /// gets numbers for free. Each number stays pinned to the spot it was dealt, rather than
    /// to the unit, so a fleeing target leaves its numbers behind like in WoW.
    /// </summary>
    [DefaultExecutionOrder(1000)]
    public class CombatTextHud : MonoBehaviour
    {
        public float duration = 1.1f;
        [Tooltip("How far a number drifts upward over its lifetime, in HUD pixels.")]
        public float rise = 70f;
        public int fontSize = 28;

        class Popup
        {
            public Text Text;
            public Vector3 World;
            public Vector2 Jitter;
            public float Age;
        }

        RectTransform _layer;
        readonly List<Popup> _live = new();
        readonly Stack<Popup> _free = new();

        void Awake() => _layer = Hud.Layer(HudLayer.CombatText);
        void OnEnable() => Health.AnyDamaged += OnDamaged;
        void OnDisable() => Health.AnyDamaged -= OnDamaged;

        void OnDamaged(Health health, float amount)
        {
            var popup = _free.Count > 0 ? _free.Pop() : CreatePopup();
            popup.World = health.transform.position + Vector3.up * health.Height;
            popup.Jitter = new Vector2(Random.Range(-24f, 24f), 0f);
            popup.Age = 0f;
            popup.Text.text = Mathf.Max(1, Mathf.RoundToInt(amount)).ToString();
            popup.Text.gameObject.SetActive(true);
            _live.Add(popup);
        }

        void LateUpdate()
        {
            var camera = Camera.main;
            for (int i = _live.Count - 1; i >= 0; i--)
            {
                var popup = _live[i];
                popup.Age += Time.deltaTime;
                float t = popup.Age / Mathf.Max(duration, 0.01f);
                if (t >= 1f || camera == null)
                {
                    popup.Text.gameObject.SetActive(false);
                    _live.RemoveAt(i);
                    _free.Push(popup);
                    continue;
                }

                Vector3 screen = camera.WorldToScreenPoint(popup.World);
                bool onScreen = screen.z > 0f;
                popup.Text.enabled = onScreen;
                if (!onScreen)
                    continue;

                RectTransformUtility.ScreenPointToLocalPointInRectangle(_layer, screen, null, out var local);
                float eased = 1f - (1f - t) * (1f - t);
                popup.Text.rectTransform.anchoredPosition = local + popup.Jitter + Vector2.up * (rise * eased);

                // A quick overshoot on spawn, then hold, then fade out over the last 40%.
                float scale = t < 0.12f ? Mathf.Lerp(1.7f, 1f, t / 0.12f) : 1f;
                popup.Text.rectTransform.localScale = Vector3.one * scale;
                var color = HudTheme.DamageText;
                color.a = t < 0.6f ? 1f : 1f - (t - 0.6f) / 0.4f;
                popup.Text.color = color;
            }
        }

        Popup CreatePopup()
        {
            var text = Hud.Label("Damage", _layer, fontSize, TextAnchor.MiddleCenter, HudTheme.DamageText, FontStyle.Bold);
            var rect = text.rectTransform;
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(120f, 40f);
            return new Popup { Text = text };
        }
    }
}
