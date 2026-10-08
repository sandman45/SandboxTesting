using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace WowSandbox
{
    /// <summary>
    /// The short lines of text WoW puts near the top of the screen — red for errors
    /// ("Inventory is full."), yellow for notices (loot received). Newest on top, a few at
    /// most, each fading out after a few seconds. Repeating the newest message just
    /// refreshes it instead of stacking copies. Reached through Hud.Error and Hud.Info.
    /// </summary>
    public class HudMessages : MonoBehaviour
    {
        const int MaxLines = 3;
        const float Lifetime = 3f;
        const float FadeTime = 1f;

        class Line
        {
            public Text Text;
            public Color Color;
            public float Age;
        }

        readonly List<Line> _lines = new();

        public void Show(string message, Color color)
        {
            if (_lines.Count > 0 && _lines[0].Text.text == message)
            {
                _lines[0].Age = 0f;
                return;
            }

            Line line;
            if (_lines.Count >= MaxLines)
            {
                line = _lines[^1];
                _lines.RemoveAt(_lines.Count - 1);
            }
            else
            {
                var text = Hud.Label("Message", Hud.Layer(HudLayer.Frames), 18, TextAnchor.MiddleCenter, color,
                    FontStyle.Bold);
                var rect = text.rectTransform;
                rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 1f);
                rect.sizeDelta = new Vector2(800f, 24f);
                line = new Line { Text = text };
            }

            line.Text.text = message;
            line.Color = color;
            line.Age = 0f;
            _lines.Insert(0, line);
        }

        void Update()
        {
            for (int i = _lines.Count - 1; i >= 0; i--)
            {
                var line = _lines[i];
                line.Age += Time.deltaTime;
                if (line.Age >= Lifetime)
                {
                    Destroy(line.Text.gameObject);
                    _lines.RemoveAt(i);
                    continue;
                }

                var color = line.Color;
                color.a = Mathf.Clamp01((Lifetime - line.Age) / FadeTime);
                line.Text.color = color;
                line.Text.rectTransform.anchoredPosition = new Vector2(0f, -120f - i * 26f);
            }
        }
    }
}
