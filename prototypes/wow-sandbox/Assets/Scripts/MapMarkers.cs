using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace WowSandbox
{
    /// <summary>
    /// The player's arrow and a dot per living NPC (in its reaction colour, the target's
    /// drawn larger) over a map image. Shared by the minimap and the full map; each passes
    /// its own world-to-map mapping, returning null for anything that's off its edge. The
    /// leviathan, while it looms out at sea, gets a big pulsing mark of its own — pinned to
    /// the map's rim when it's off the edge, so it still shows which way it is.
    /// </summary>
    public class MapMarkers
    {
        readonly RectTransform _parent;
        readonly Image _arrow;
        readonly List<Image> _dots = new();
        Image _leviathan;
        Image _ship;

        /// <param name="parent">Rect the markers live in; positions are relative to its centre.</param>
        public MapMarkers(RectTransform parent, float arrowSize)
        {
            _parent = parent;
            var arrow = Hud.Rect("PlayerArrow", parent, new Vector2(0.5f, 0.5f), Vector2.zero,
                new Vector2(arrowSize, arrowSize));
            _arrow = arrow.gameObject.AddComponent<Image>();
            _arrow.sprite = Hud.Arrow;
            _arrow.color = new Color(1f, 0.92f, 0.4f);
            _arrow.raycastTarget = false;
            var outline = arrow.gameObject.AddComponent<Outline>();
            outline.effectColor = new Color(0f, 0f, 0f, 0.8f);
        }

        /// <param name="pinToEdge">Where a point off the map's edge sits on its rim.</param>
        public void Update(Transform player, Health target, Func<Vector3, Vector2?> toMap, float dotSize,
                           Func<Vector3, Vector2> pinToEdge)
        {
            UpdateLeviathan(toMap, pinToEdge, dotSize);
            UpdateShip(toMap, dotSize);

            int used = 0;
            foreach (var health in Health.All)
            {
                if (health.IsDead)
                    continue;
                var position = toMap(health.transform.position);
                if (position == null)
                    continue;

                var dot = Dot(used++);
                dot.rectTransform.anchoredPosition = position.Value;
                float size = health == target ? dotSize * 1.6f : dotSize;
                dot.rectTransform.sizeDelta = new Vector2(size, size);
                dot.color = HudTheme.ReactionColor(health.reaction);
            }

            for (int i = used; i < _dots.Count; i++)
                _dots[i].enabled = false;

            var playerPosition = player != null ? toMap(player.position) : null;
            _arrow.enabled = playerPosition != null;
            if (playerPosition == null)
                return;

            // Yaw is clockwise from north (+Z); UI rotation is anticlockwise, hence the minus.
            _arrow.rectTransform.anchoredPosition = playerPosition.Value;
            _arrow.rectTransform.localEulerAngles = new Vector3(0f, 0f, -player.eulerAngles.y);
            _arrow.rectTransform.SetAsLastSibling();
        }

        /// <summary>The ship as a wooden silhouette to scale, turned the way its bow points — the
        /// map snapshot leaves the ship out, since it doesn't stay where the snapshot saw it.</summary>
        void UpdateShip(Func<Vector3, Vector2?> toMap, float dotSize)
        {
            var ship = ShipSailing.Active;
            var position = ship != null ? toMap(ship.Centre) : null;
            if (position == null)
            {
                if (_ship != null)
                    _ship.enabled = false;
                return;
            }

            if (_ship == null)
            {
                var rect = Hud.Rect("Ship", _parent, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.one * 8f);
                _ship = rect.gameObject.AddComponent<Image>();
                _ship.sprite = Hud.ShipSilhouette;
                _ship.color = new Color(0.72f, 0.52f, 0.28f);
                _ship.raycastTarget = false;
                var outline = rect.gameObject.AddComponent<Outline>();
                outline.effectColor = new Color(0f, 0f, 0f, 0.9f);
            }

            Vector3 bow = ship.Bow;
            _ship.enabled = true;
            _ship.rectTransform.anchoredPosition = position.Value;
            // Its real length on this map, from the map's scale right at the ship's middle — the
            // bow and stern can be off the edge of a zoomed-in minimap. The minimap's round mask
            // trims a ship bigger than its window. Never smaller than a big dot.
            var nearby = toMap(ship.Centre + bow);
            float length = nearby != null
                ? Vector2.Distance(nearby.Value, position.Value) * ship.HalfLength * 2f : 0f;
            _ship.rectTransform.sizeDelta = Vector2.one * Mathf.Max(length, dotSize * 2.6f);
            _ship.rectTransform.localEulerAngles = new Vector3(0f, 0f, -Mathf.Atan2(bow.x, bow.z) * Mathf.Rad2Deg);
        }

        void UpdateLeviathan(Func<Vector3, Vector2?> toMap, Func<Vector3, Vector2> pinToEdge, float dotSize)
        {
            var leviathan = Leviathan.Looming;
            if (leviathan == null)
            {
                if (_leviathan != null)
                    _leviathan.enabled = false;
                return;
            }

            if (_leviathan == null)
            {
                var rect = Hud.Rect("Leviathan", _parent, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.one * 8f);
                _leviathan = rect.gameObject.AddComponent<Image>();
                _leviathan.sprite = Hud.Circle;
                _leviathan.raycastTarget = false;
                var outline = rect.gameObject.AddComponent<Outline>();
                outline.effectColor = new Color(0f, 0f, 0f, 0.9f);
                outline.effectDistance = new Vector2(2f, -2f);
            }

            Vector3 position = leviathan.transform.position;
            _leviathan.enabled = true;
            _leviathan.rectTransform.anchoredPosition = toMap(position) ?? pinToEdge(position);
            float pulse = 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 3f);
            _leviathan.rectTransform.sizeDelta = Vector2.one * dotSize * Mathf.Lerp(2.6f, 3.2f, pulse);
            _leviathan.color = Color.Lerp(new Color(0.45f, 0.05f, 0.35f), new Color(0.85f, 0.15f, 0.6f), pulse);
            _leviathan.rectTransform.SetAsLastSibling();
        }

        Image Dot(int index)
        {
            if (index < _dots.Count)
            {
                _dots[index].enabled = true;
                return _dots[index];
            }

            var rect = Hud.Rect("Dot", _parent, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.one * 8f);
            var dot = rect.gameObject.AddComponent<Image>();
            dot.sprite = Hud.Circle;
            dot.raycastTarget = false;
            var outline = rect.gameObject.AddComponent<Outline>();
            outline.effectColor = new Color(0f, 0f, 0f, 0.7f);
            _dots.Add(dot);
            return dot;
        }
    }
}
