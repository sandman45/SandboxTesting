using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.UI;

namespace WowSandbox
{
    /// <summary>
    /// Name and a small health bar floating over NPCs, WoW-style. By default only your
    /// current target gets one — select something else and the plate moves to it. Turn off
    /// targetOnly to show plates on every living NPC within range instead.
    ///
    /// The name takes its reaction colour; the bar shows on anything hostile, hurt or
    /// targeted, and stays hidden on a neutral chicken at full health so the screen isn't
    /// all bars. Your current target's plate is drawn larger and on top, and stays up past
    /// the range cut-off. Corpses lose their plate unless they're still targeted.
    ///
    /// Runs after the orbit camera's LateUpdate, so plates track this frame's view rather
    /// than lagging a frame behind it.
    /// </summary>
    [DefaultExecutionOrder(1000)]
    [RequireComponent(typeof(TargetingController))]
    public class NameplateHud : MonoBehaviour
    {
        [Tooltip("Only the selected target gets a plate. Off shows every NPC within maxDistance.")]
        public bool targetOnly = true;
        [Tooltip("Plates beyond this distance from the player are hidden (except the target's). " +
                 "Spawn Warrior Player scales it with the model.")]
        public float maxDistance = 40f;
        [Tooltip("Gap between the top of the unit and the bottom of its plate, as a fraction of its height.")]
        public float heightPadding = 0.15f;
        public Vector2 barSize = new(100f, 8f);
        public float targetScale = 1.15f;

        class Plate
        {
            public RectTransform Rect;
            public CanvasGroup Group;
            public Text Name;
            public HudBar Bar;
            public Health Owner;
        }

        TargetingController _targeting;
        RectTransform _layer;
        readonly Dictionary<Health, Plate> _active = new();
        readonly Stack<Plate> _free = new();
        readonly List<(Health health, float distance)> _visible = new();
        readonly HashSet<Health> _seen = new();
        readonly List<Health> _released = new();

        void Awake()
        {
            _targeting = GetComponent<TargetingController>();
            _layer = Hud.Layer(HudLayer.Nameplates);
        }

        void OnDisable()
        {
            foreach (var plate in _active.Values)
                Release(plate);
            _active.Clear();
        }

        void LateUpdate()
        {
            var camera = Camera.main;
            if (camera == null)
                return;

            var target = _targeting.Target;
            CollectVisible(camera, target);

            // Farthest first, so nearer plates end up later in sibling order and draw on top;
            // the target (listed at distance -1) goes last of all.
            _visible.Sort((a, b) => b.distance.CompareTo(a.distance));

            foreach (var (health, distance) in _visible)
            {
                if (!_active.TryGetValue(health, out var plate))
                {
                    plate = _free.Count > 0 ? _free.Pop() : CreatePlate();
                    plate.Owner = health;
                    plate.Rect.gameObject.SetActive(true);
                    plate.Name.text = CleanName(health.name);
                    plate.Bar.Snap(health.Health01);
                    _active.Add(health, plate);
                }

                UpdatePlate(plate, camera, health == target, distance);
            }

            // Anything not seen this frame goes back to the pool.
            _released.Clear();
            foreach (var pair in _active)
            {
                if (pair.Key == null || !_seen.Contains(pair.Key))
                    _released.Add(pair.Key);
            }
            foreach (var health in _released)
            {
                Release(_active[health]);
                _active.Remove(health);
            }
        }

        void CollectVisible(Camera camera, Health target)
        {
            _visible.Clear();
            _seen.Clear();
            Vector3 camPos = camera.transform.position;
            Vector3 camForward = camera.transform.forward;

            foreach (var health in Health.All)
            {
                bool isTarget = health == target;
                if (targetOnly && !isTarget)
                    continue;
                if (health.IsDead && !isTarget)
                    continue;

                float distance = Vector3.Distance(transform.position, health.transform.position);
                if (distance > maxDistance && !isTarget)
                    continue;

                // Behind the camera — WorldToScreenPoint would mirror it onto the screen.
                if (Vector3.Dot(health.transform.position - camPos, camForward) <= 0f)
                    continue;

                _visible.Add((health, isTarget ? -1f : distance));
                _seen.Add(health);
            }
        }

        void UpdatePlate(Plate plate, Camera camera, bool isTarget, float distance)
        {
            var health = plate.Owner;
            Vector3 world = health.transform.position + Vector3.up * health.Height * (1f + heightPadding);
            Vector3 screen = camera.WorldToScreenPoint(world);
            RectTransformUtility.ScreenPointToLocalPointInRectangle(_layer, screen, null, out var local);
            plate.Rect.anchoredPosition = local;
            plate.Rect.SetAsLastSibling();

            float near = Mathf.Max(distance, 0f) / Mathf.Max(maxDistance, 0.01f);
            float fade = isTarget ? 1f : 0.85f * (1f - Mathf.InverseLerp(0.75f, 1f, near));
            plate.Group.alpha = fade;
            plate.Rect.localScale = Vector3.one * (isTarget ? targetScale : Mathf.Lerp(1f, 0.8f, near));

            plate.Name.color = health.IsDead ? HudTheme.Dead : HudTheme.ReactionColor(health.reaction);

            bool showBar = !health.IsDead &&
                           (isTarget || health.reaction == Reaction.Hostile || health.Health01 < 0.999f);
            plate.Bar.Rect.gameObject.SetActive(showBar);
            plate.Bar.Set(health.Health01);
            plate.Bar.FillColor = HudTheme.HealthColor(health.Health01);
        }

        Plate CreatePlate()
        {
            var bottom = new Vector2(0.5f, 0f);
            var rect = Hud.Rect("Nameplate", _layer, new Vector2(0.5f, 0.5f), Vector2.zero,
                new Vector2(barSize.x, barSize.y + 20f));
            rect.pivot = bottom;

            var plate = new Plate
            {
                Rect = rect,
                Group = rect.gameObject.AddComponent<CanvasGroup>(),
                Bar = new HudBar("Health", rect, bottom, Vector2.zero, barSize),
            };

            plate.Name = Hud.Label("Name", rect, 14, TextAnchor.LowerCenter, HudTheme.Text, FontStyle.Bold);
            plate.Name.rectTransform.offsetMin = new Vector2(0f, barSize.y + 2f);
            return plate;
        }

        void Release(Plate plate)
        {
            plate.Owner = null;
            if (plate.Rect == null)
                return;
            plate.Rect.gameObject.SetActive(false);
            _free.Push(plate);
        }

        /// <summary>Strips the "_00" spawn index, same as TargetingController's target name.</summary>
        static string CleanName(string rawName) => Regex.Replace(rawName, @"_\d+$", "");
    }
}
