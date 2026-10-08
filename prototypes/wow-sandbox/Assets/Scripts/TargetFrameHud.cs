using UnityEngine;
using UnityEngine.UI;

namespace WowSandbox
{
    /// <summary>
    /// Unit frame for the current target, bottom-right — the minimap has the top-right
    /// corner, and the player frame the top-left: name
    /// coloured by how it regards you (red hostile, yellow neutral, green friendly), its
    /// level coloured by difficulty relative to yours (WoW's grey-green-yellow-orange-red),
    /// health with numbers and a percentage, and "Dead" once it's a corpse. Fades out
    /// whenever TargetingController has nothing selected.
    /// </summary>
    [RequireComponent(typeof(TargetingController))]
    public class TargetFrameHud : MonoBehaviour
    {
        [Tooltip("Anchored from the bottom-right of the screen.")]
        public Vector2 bottomRightOffset = new(-20f, 20f);
        public float fadeSpeed = 8f;

        static readonly Vector2 FrameSize = new(250f, 62f);

        TargetingController _targeting;
        CanvasGroup _group;
        Text _name;
        Text _level;
        HudBar _healthBar;
        Health _shown;
        CharacterStats _shownStats;
        LootDrop _shownLoot;
        CharacterStats _playerStats;

        void Awake()
        {
            _targeting = GetComponent<TargetingController>();
            _playerStats = GetComponent<CharacterStats>();
            Build();
        }

        void Update()
        {
            var target = _targeting.Target;
            _group.alpha = Mathf.MoveTowards(_group.alpha, target != null ? 1f : 0f, fadeSpeed * Time.deltaTime);
            if (target == null)
                return;

            // A new target shouldn't inherit the old one's draining damage trail.
            if (target != _shown)
            {
                _shown = target;
                _shownStats = target.GetComponent<CharacterStats>();
                _shownLoot = target.GetComponent<LootDrop>();
                _healthBar.Snap(target.Health01);
            }

            if (_shownStats != null)
            {
                _level.text = $"Lv {_shownStats.level}";
                _level.color = target.IsDead || _playerStats == null
                    ? HudTheme.TextDim
                    : HudTheme.LevelColor(_shownStats.level, _playerStats.level);
            }
            else
            {
                _level.text = "";
            }

            _name.text = _targeting.TargetName;
            _healthBar.Set(target.Health01);

            if (target.IsDead)
            {
                _name.color = HudTheme.Dead;
                _healthBar.Label.text = _shownLoot != null && _shownLoot.HasLoot ? "Dead  ·  click to loot" : "Dead";
                return;
            }

            _name.color = HudTheme.ReactionColor(target.reaction);
            _healthBar.FillColor = HudTheme.HealthColor(target.Health01);
            _healthBar.Label.text = $"{Mathf.CeilToInt(target.Current)} / {Mathf.CeilToInt(target.maxHealth)}" +
                                    $"  ({Mathf.CeilToInt(target.Health01 * 100f)}%)";
        }

        void Build()
        {
            var topLeft = new Vector2(0f, 1f);

            var frame = Hud.Rect("TargetFrame", Hud.Layer(HudLayer.Frames), new Vector2(1f, 0f), bottomRightOffset, FrameSize);
            Hud.Panel(frame);
            _group = frame.gameObject.AddComponent<CanvasGroup>();
            _group.alpha = 0f;

            _name = Hud.Label("Name", frame, 16, TextAnchor.MiddleLeft, HudTheme.Text, FontStyle.Bold);
            _level = Hud.Label("Level", frame, 14, TextAnchor.MiddleRight, HudTheme.TextDim, FontStyle.Bold);
            foreach (var label in new[] { _name, _level })
            {
                var rect = label.rectTransform;
                rect.anchorMin = new Vector2(0f, 1f);
                rect.anchorMax = new Vector2(1f, 1f);
                rect.pivot = new Vector2(0.5f, 1f);
                rect.offsetMin = new Vector2(12f, -26f);
                rect.offsetMax = new Vector2(-12f, -6f);
            }

            _healthBar = new HudBar("Health", frame, topLeft, new Vector2(12f, -30f),
                new Vector2(FrameSize.x - 24f, 20f), labelSize: 13);
        }
    }
}
