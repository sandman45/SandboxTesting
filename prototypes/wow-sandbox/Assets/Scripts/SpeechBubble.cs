using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace WowSandbox
{
    /// <summary>
    /// A line said out loud, in a pale bubble over someone's head for a few seconds, as in WoW.
    /// One per speaker: a new line replaces the last. It follows them, fades out at the end,
    /// and hides when they're behind the camera or far away.
    ///
    /// Runs after the orbit camera's LateUpdate, like the nameplates, so it tracks this frame's view.
    /// </summary>
    [DefaultExecutionOrder(1000)]
    public class SpeechBubble : MonoBehaviour
    {
        const float MaxWidth = 260f;
        const float Pad = 10f;
        const float FadeSeconds = 0.4f;
        const float MaxDistance = 60f;

        static readonly Color Back = new(0.95f, 0.93f, 0.86f, 0.94f);
        static readonly Color Ink = new(0.12f, 0.1f, 0.08f);
        static readonly Dictionary<Transform, SpeechBubble> _bubbles = new();

        Transform _speaker;
        float _height;
        float _timeLeft;
        RectTransform _rect;
        CanvasGroup _group;

        /// <summary>
        /// Says <paramref name="line"/> over <paramref name="speaker"/>, whose head is
        /// <paramref name="height"/> above its root. Longer lines stay up longer.
        /// </summary>
        public static SpeechBubble Say(Transform speaker, float height, string line, float seconds = 0f)
        {
            if (speaker == null || string.IsNullOrEmpty(line))
                return null;
            if (_bubbles.TryGetValue(speaker, out var old) && old != null)
                Destroy(old.gameObject);

            var layer = Hud.Layer(HudLayer.Nameplates);
            var go = new GameObject("Speech Bubble", typeof(RectTransform));
            go.transform.SetParent(layer, false);
            var bubble = go.AddComponent<SpeechBubble>();
            bubble.Build(line);
            bubble._speaker = speaker;
            bubble._height = height;
            bubble._timeLeft = seconds > 0f ? seconds : Mathf.Clamp(2.5f + line.Length * 0.05f, 3f, 8f);
            _bubbles[speaker] = bubble;
            bubble.LateUpdate();
            return bubble;
        }

        void Build(string line)
        {
            _rect = (RectTransform)transform;
            _rect.anchorMin = _rect.anchorMax = new Vector2(0.5f, 0.5f);
            _rect.pivot = new Vector2(0.5f, 0f);
            _group = gameObject.AddComponent<CanvasGroup>();
            _group.blocksRaycasts = false;
            Hud.Rounded(gameObject, Back, 8f);

            var text = Hud.Stretch("Text", _rect, Pad).gameObject.AddComponent<Text>();
            text.font = Hud.Font;
            text.fontSize = 14;
            text.color = Ink;
            text.alignment = TextAnchor.MiddleCenter;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.raycastTarget = false;
            text.text = line;

            // Fit the bubble to the line: one line as wide as it needs, wrapping past MaxWidth.
            var single = text.GetGenerationSettings(Vector2.zero);
            float width = Mathf.Min(text.cachedTextGeneratorForLayout.GetPreferredWidth(line, single) / text.pixelsPerUnit, MaxWidth);
            var wrapped = text.GetGenerationSettings(new Vector2(width, 0f));
            float height = text.cachedTextGeneratorForLayout.GetPreferredHeight(line, wrapped) / text.pixelsPerUnit;
            _rect.sizeDelta = new Vector2(width + Pad * 2f + 2f, height + Pad * 2f);
        }

        void LateUpdate()
        {
            var camera = Camera.main;
            if (_speaker == null || camera == null)
            {
                Destroy(gameObject);
                return;
            }

            _timeLeft -= Time.deltaTime;
            if (_timeLeft <= 0f)
            {
                Destroy(gameObject);
                return;
            }

            Vector3 head = _speaker.position + Vector3.up * (_height * 1.15f + 0.3f);
            Vector3 screen = camera.WorldToScreenPoint(head);
            bool visible = screen.z > 0f && Vector3.Distance(camera.transform.position, head) < MaxDistance;
            _group.alpha = visible ? Mathf.Clamp01(_timeLeft / FadeSeconds) : 0f;
            if (!visible)
                return;
            RectTransformUtility.ScreenPointToLocalPointInRectangle((RectTransform)_rect.parent, screen, null, out var local);
            _rect.anchoredPosition = local;
        }

        void OnDestroy()
        {
            if (_speaker != null && _bubbles.TryGetValue(_speaker, out var current) && current == this)
                _bubbles.Remove(_speaker);
        }
    }
}
