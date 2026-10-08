using UnityEngine;
using UnityEngine.UI;

namespace WowSandbox
{
    /// <summary>
    /// Unit frame for the current target, top-right — across the screen from
    /// PlayerFrameHud's, so the two never crowd each other at larger HUD scales: name
    /// coloured by how it regards you (red hostile, yellow neutral, green friendly), health
    /// with numbers and a percentage, and "Dead" once it's a corpse. Fades out whenever
    /// TargetingController has nothing selected.
    /// </summary>
    [RequireComponent(typeof(TargetingController))]
    public class TargetFrameHud : MonoBehaviour
    {
        [Tooltip("Anchored from the top-right of the screen.")]
        public Vector2 topRightOffset = new(-20f, -20f);
        public float fadeSpeed = 8f;

        static readonly Vector2 FrameSize = new(250f, 62f);

        TargetingController _targeting;
        CanvasGroup _group;
        Text _name;
        Text _percent;
        HudBar _healthBar;
        Health _shown;

        void Awake()
        {
            _targeting = GetComponent<TargetingController>();
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
                _healthBar.Snap(target.Health01);
            }

            _name.text = _targeting.TargetName;
            _healthBar.Set(target.Health01);

            if (target.IsDead)
            {
                _name.color = HudTheme.Dead;
                _percent.text = "";
                _healthBar.Label.text = "Dead";
                return;
            }

            _name.color = HudTheme.ReactionColor(target.reaction);
            _percent.text = $"{Mathf.CeilToInt(target.Health01 * 100f)}%";
            _healthBar.FillColor = HudTheme.HealthColor(target.Health01);
            _healthBar.Label.text = $"{Mathf.CeilToInt(target.Current)} / {Mathf.CeilToInt(target.maxHealth)}";
        }

        void Build()
        {
            var topLeft = new Vector2(0f, 1f);

            var frame = Hud.Rect("TargetFrame", Hud.Layer(HudLayer.Frames), Vector2.one, topRightOffset, FrameSize);
            Hud.Panel(frame);
            _group = frame.gameObject.AddComponent<CanvasGroup>();
            _group.alpha = 0f;

            _name = Hud.Label("Name", frame, 16, TextAnchor.MiddleLeft, HudTheme.Text, FontStyle.Bold);
            _percent = Hud.Label("Percent", frame, 13, TextAnchor.MiddleRight, HudTheme.TextDim);
            foreach (var label in new[] { _name, _percent })
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
