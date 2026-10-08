using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace WowSandbox
{
    /// <summary>
    /// WoW's floating combat text, fed by Combat's attack rolls: damage numbers pop over the
    /// target and drift up, crits come bigger and orange with a "!", misses say "Miss", and
    /// a kill adds the XP it earned. Each number stays pinned to the spot it was dealt
    /// rather than to the unit, so a fleeing target leaves its numbers behind like in WoW.
    ///
    /// Levelling up (from CharacterStats on the same object) gets a banner mid-screen, and
    /// hits taken by the player show in red over their own head.
    /// </summary>
    [DefaultExecutionOrder(1000)]
    public class CombatTextHud : MonoBehaviour
    {
        public float duration = 1.1f;
        [Tooltip("How far a number drifts upward over its lifetime, in HUD pixels.")]
        public float rise = 70f;
        public int fontSize = 28;
        public float levelUpSeconds = 2.5f;

        class Popup
        {
            public Text Text;
            /// <summary>Where it's pinned, or null for a fixed spot on screen.</summary>
            public Vector3? World;
            public Vector2 Offset;
            public Color Color;
            public float Scale;
            public float Age;
            public float Duration;
        }

        RectTransform _layer;
        CharacterStats _stats;
        readonly List<Popup> _live = new();
        readonly Stack<Popup> _free = new();

        void Awake()
        {
            _layer = Hud.Layer(HudLayer.CombatText);
            _stats = GetComponent<CharacterStats>();
        }

        void OnEnable()
        {
            Combat.AttackResolved += OnAttack;
            Combat.PlayerAttacked += OnPlayerAttacked;
            if (_stats != null)
                _stats.LeveledUp += OnLeveledUp;
        }

        void OnDisable()
        {
            Combat.AttackResolved -= OnAttack;
            Combat.PlayerAttacked -= OnPlayerAttacked;
            if (_stats != null)
                _stats.LeveledUp -= OnLeveledUp;
        }

        void OnAttack(Health target, AttackResult result)
        {
            Vector3 world = target.transform.position + Vector3.up * target.Height;
            var jitter = new Vector2(Random.Range(-24f, 24f), 0f);

            if (!result.Hit)
                Show("Miss", world, jitter, HudTheme.Miss, 0.85f, duration);
            else if (result.Critical)
                Show($"{result.Damage}!", world, jitter, HudTheme.Critical, 1.4f, duration * 1.2f);
            else
                Show(result.Damage.ToString(), world, jitter, HudTheme.DamageText, 1f, duration);

            if (result.ExperienceAwarded > 0)
                Show($"+{result.ExperienceAwarded} XP", world, jitter + new Vector2(0f, -34f),
                    HudTheme.Experience, 0.8f, duration * 1.6f);
        }

        void OnPlayerAttacked(HealthController player, AttackResult result)
        {
            var body = player.GetComponent<CharacterController>();
            Vector3 head = player.transform.position + Vector3.up * (body != null ? body.height : 2f);
            var jitter = new Vector2(Random.Range(-30f, 30f), 0f);

            if (!result.Hit)
                Show("Miss", head, jitter, HudTheme.Miss, 0.8f, duration);
            else
                Show($"-{result.Damage}{(result.Critical ? "!" : "")}", head, jitter, HudTheme.DamageFlash,
                    result.Critical ? 1.3f : 1f, duration);
        }

        void OnLeveledUp(int level) =>
            Show($"Level {level}!", null, new Vector2(0f, 180f), HudTheme.Heading, 1.8f, levelUpSeconds);

        void Show(string text, Vector3? world, Vector2 offset, Color color, float scale, float seconds)
        {
            var popup = _free.Count > 0 ? _free.Pop() : CreatePopup();
            popup.World = world;
            popup.Offset = offset;
            popup.Color = color;
            popup.Scale = scale;
            popup.Age = 0f;
            popup.Duration = Mathf.Max(seconds, 0.01f);
            popup.Text.text = text;
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
                float t = popup.Age / popup.Duration;
                if (t >= 1f || (popup.World.HasValue && camera == null))
                {
                    popup.Text.gameObject.SetActive(false);
                    _live.RemoveAt(i);
                    _free.Push(popup);
                    continue;
                }

                Vector2 anchor = Vector2.zero;
                if (popup.World.HasValue)
                {
                    Vector3 screen = camera.WorldToScreenPoint(popup.World.Value);
                    bool onScreen = screen.z > 0f;
                    popup.Text.enabled = onScreen;
                    if (!onScreen)
                        continue;
                    RectTransformUtility.ScreenPointToLocalPointInRectangle(_layer, screen, null, out anchor);
                }
                else
                {
                    popup.Text.enabled = true;
                }

                float eased = 1f - (1f - t) * (1f - t);
                float drift = popup.World.HasValue ? rise * eased : rise * 0.3f * eased;
                popup.Text.rectTransform.anchoredPosition = anchor + popup.Offset + Vector2.up * drift;

                // A quick overshoot on spawn, then hold, then fade out over the last 40%.
                float pop = t < 0.12f ? Mathf.Lerp(1.7f, 1f, t / 0.12f) : 1f;
                popup.Text.rectTransform.localScale = Vector3.one * (popup.Scale * pop);
                var color = popup.Color;
                color.a = t < 0.6f ? 1f : 1f - (t - 0.6f) / 0.4f;
                popup.Text.color = color;
            }
        }

        Popup CreatePopup()
        {
            var text = Hud.Label("CombatText", _layer, fontSize, TextAnchor.MiddleCenter, HudTheme.DamageText, FontStyle.Bold);
            var rect = text.rectTransform;
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(240f, 40f);
            return new Popup { Text = text };
        }
    }
}
