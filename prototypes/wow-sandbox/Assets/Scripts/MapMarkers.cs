using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace WowSandbox
{
    /// <summary>
    /// The player's arrow and a dot per living NPC (in its reaction colour, the target's
    /// drawn larger) over a map image. Shared by the minimap and the full map; each passes
    /// its own world-to-map mapping, returning null for anything that's off its edge.
    /// </summary>
    public class MapMarkers
    {
        readonly RectTransform _parent;
        readonly Image _arrow;
        readonly List<Image> _dots = new();

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

        public void Update(Transform player, Health target, Func<Vector3, Vector2?> toMap, float dotSize)
        {
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
