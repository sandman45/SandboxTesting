using UnityEngine;
using UnityEngine.UI;

namespace WowSandbox
{
    /// <summary>
    /// Top-left unit frame for the player: name and level, health bar with numbers, a thin
    /// experience bar, and the breath bar tucked underneath while you're underwater or still
    /// catching your breath. The border flashes red whenever you take damage.
    ///
    /// HealthController, BreathController and CharacterStats only hold the numbers; this is
    /// the one place they get drawn. Without CharacterStats the level and XP bar are hidden
    /// and displayName is used for the name.
    /// </summary>
    [RequireComponent(typeof(HealthController))]
    public class PlayerFrameHud : MonoBehaviour
    {
        public string displayName = "Warrior";
        [Tooltip("Anchored from the top-left of the screen.")]
        public Vector2 position = new(20f, -20f);
        public float damageFlashSeconds = 0.35f;
        public float breathFadeSpeed = 4f;

        static readonly Vector2 FrameSize = new(250f, 72f);

        HealthController _health;
        BreathController _breath;
        CharacterStats _stats;

        Text _name;
        Text _level;
        HudBar _healthBar;
        HudBar _experienceBar;
        HudBar _breathBar;
        CanvasGroup _breathGroup;
        Image _border;
        float _lastHealth;
        float _flash;

        void Awake()
        {
            _health = GetComponent<HealthController>();
            _breath = GetComponent<BreathController>();
            _stats = GetComponent<CharacterStats>();
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

            if (_stats != null)
            {
                _name.text = _stats.characterName;
                _level.text = $"Lv {_stats.level}";
                _experienceBar.Set(_stats.LevelProgress01);
            }

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
            float inner = FrameSize.x - 24f;

            var frame = Hud.Rect("PlayerFrame", layer, topLeft, position, FrameSize);
            _border = Hud.Panel(frame);

            _name = Hud.Label("Name", frame, topLeft, new Vector2(12f, -6f), new Vector2(inner, 20f),
                16, TextAnchor.MiddleLeft, HudTheme.Text, FontStyle.Bold);
            _name.text = displayName;
            _level = Hud.Label("Level", frame, topLeft, new Vector2(12f, -6f), new Vector2(inner, 20f),
                14, TextAnchor.MiddleRight, HudTheme.Heading, FontStyle.Bold);

            _healthBar = new HudBar("Health", frame, topLeft, new Vector2(12f, -30f),
                new Vector2(inner, 20f), labelSize: 13);
            _healthBar.Snap(_health.Health01);

            _experienceBar = new HudBar("Experience", frame, topLeft, new Vector2(12f, -56f), new Vector2(inner, 7f));
            _experienceBar.FillColor = HudTheme.Experience;
            _experienceBar.Snap(_stats != null ? _stats.LevelProgress01 : 0f);
            _experienceBar.Rect.gameObject.SetActive(_stats != null);

            // Hangs just below the frame rather than inside it, so the frame doesn't carry an
            // empty gap for the 99% of the time you're on dry land.
            var breathRow = Hud.Rect("BreathRow", frame, topLeft, new Vector2(12f, -FrameSize.y - 6f),
                new Vector2(inner, 10f));
            _breathGroup = breathRow.gameObject.AddComponent<CanvasGroup>();
            _breathGroup.alpha = 0f;
            _breathBar = new HudBar("Breath", breathRow, topLeft, Vector2.zero, breathRow.sizeDelta);
            _breathBar.Snap(1f);
        }
    }
}
