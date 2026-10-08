using UnityEngine;
using UnityEngine.UI;

namespace WowSandbox
{
    /// <summary>
    /// Top-left unit frame for the player: name, health bar with numbers, and the breath
    /// bar tucked underneath it while you're underwater or still catching your breath. The
    /// border flashes red whenever you take damage.
    ///
    /// HealthController and BreathController only hold the numbers; this is the one place
    /// they get drawn.
    /// </summary>
    [RequireComponent(typeof(HealthController))]
    public class PlayerFrameHud : MonoBehaviour
    {
        public string displayName = "Warrior";
        [Tooltip("Anchored from the top-left of the screen.")]
        public Vector2 position = new(20f, -20f);
        public float damageFlashSeconds = 0.35f;
        public float breathFadeSpeed = 4f;

        static readonly Vector2 FrameSize = new(250f, 62f);

        HealthController _health;
        BreathController _breath;

        HudBar _healthBar;
        HudBar _breathBar;
        CanvasGroup _breathGroup;
        Image _border;
        float _lastHealth;
        float _flash;

        void Awake()
        {
            _health = GetComponent<HealthController>();
            _breath = GetComponent<BreathController>();
            Build();
            _lastHealth = _health.Current;
        }

        void Update()
        {
            float current = _health.Current;
            if (current < _lastHealth - 0.001f)
                _flash = damageFlashSeconds;
            _lastHealth = current;

            _healthBar.Set(_health.Health01);
            _healthBar.FillColor = HudTheme.HealthColor(_health.Health01);
            _healthBar.Label.text = $"{Mathf.CeilToInt(current)} / {Mathf.CeilToInt(_health.maxHealth)}";

            _flash = Mathf.Max(0f, _flash - Time.deltaTime);
            float flash01 = damageFlashSeconds > 0f ? _flash / damageFlashSeconds : 0f;
            _border.color = Color.Lerp(HudTheme.PanelBorder, HudTheme.DamageFlash, flash01);

            if (_breath != null)
            {
                bool showBreath = _breath.IsSubmerged || _breath.Breath01 < 0.999f;
                _breathGroup.alpha = Mathf.MoveTowards(_breathGroup.alpha, showBreath ? 1f : 0f,
                    breathFadeSpeed * Time.deltaTime);
                _breathBar.Set(_breath.Breath01);
                _breathBar.FillColor = Color.Lerp(HudTheme.HealthLow, HudTheme.Breath, _breath.Breath01 * 2f);
            }
        }

        void Build()
        {
            var layer = Hud.Layer(HudLayer.Frames);
            var topLeft = new Vector2(0f, 1f);

            var frame = Hud.Rect("PlayerFrame", layer, topLeft, position, FrameSize);
            _border = Hud.Panel(frame);

            var name = Hud.Label("Name", frame, 16, TextAnchor.MiddleLeft, HudTheme.Text, FontStyle.Bold);
            var nameRect = name.rectTransform;
            nameRect.anchorMin = new Vector2(0f, 1f);
            nameRect.anchorMax = new Vector2(1f, 1f);
            nameRect.pivot = new Vector2(0.5f, 1f);
            nameRect.offsetMin = new Vector2(12f, -26f);
            nameRect.offsetMax = new Vector2(-12f, -6f);
            name.text = displayName;

            _healthBar = new HudBar("Health", frame, topLeft, new Vector2(12f, -30f),
                new Vector2(FrameSize.x - 24f, 20f), labelSize: 13);
            _healthBar.Snap(_health.Health01);

            // Hangs just below the frame rather than inside it, so the frame doesn't carry an
            // empty gap for the 99% of the time you're on dry land.
            var breathRow = Hud.Rect("BreathRow", frame, topLeft, new Vector2(12f, -FrameSize.y - 6f),
                new Vector2(FrameSize.x - 24f, 10f));
            _breathGroup = breathRow.gameObject.AddComponent<CanvasGroup>();
            _breathGroup.alpha = 0f;
            _breathBar = new HudBar("Breath", breathRow, topLeft, Vector2.zero, breathRow.sizeDelta);
            _breathBar.Snap(1f);
        }
    }
}
