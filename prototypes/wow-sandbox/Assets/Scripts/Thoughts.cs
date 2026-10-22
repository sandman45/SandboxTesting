using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace WowSandbox
{
    /// <summary>
    /// The Captain's own voice: thoughts and mutterings as an italic subtitle low in the middle of
    /// the screen, typed out, held a moment and faded, one line after another. It never stops
    /// play. Lines wait while a conversation is open. ThoughtTriggers decides what's said when.
    ///
    /// It needs no setup: one is created in every scene at Play.
    /// </summary>
    public class Thoughts : MonoBehaviour
    {
        const float LettersPerSecond = 40f;
        const float FadeSeconds = 0.5f;
        const float GapSeconds = 0.35f;

        static Thoughts _active;
        static readonly Queue<(string line, Action shown)> _queue = new();

        CanvasGroup _group;
        Text _text;
        string _line;
        float _letters;
        float _hold;
        float _gap;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void CreateInScene()
        {
            _queue.Clear();
            if (FindAnyObjectByType<Thoughts>() == null)
            {
                var go = new GameObject("Thoughts");
                go.AddComponent<Thoughts>();
                go.AddComponent<ThoughtTriggers>();
            }
        }

        /// <summary>Thinks <paramref name="lines"/>, one after another, after anything already waiting.</summary>
        public static void Say(params string[] lines)
        {
            foreach (var line in lines)
                if (!string.IsNullOrEmpty(line))
                    _queue.Enqueue((line, null));
        }

        /// <summary>Thinks <paramref name="line"/> after anything waiting, and runs <paramref name="shown"/> as it starts to show.</summary>
        public static void Say(string line, Action shown)
        {
            if (!string.IsNullOrEmpty(line))
                _queue.Enqueue((line, shown));
        }

        /// <summary>
        /// Thinks <paramref name="lines"/> the first time moment <paramref name="id"/> comes round,
        /// and never again (StoryState flag "thought.id"). False if it's had its say already.
        /// </summary>
        public static bool Once(string id, params string[] lines)
        {
            string flag = "thought." + id;
            if (StoryState.Has(flag))
                return false;
            StoryState.Set(flag);
            Say(lines);
            return true;
        }

        void Awake()
        {
            _active = this;
            var rect = Hud.Rect("Thoughts", Hud.Layer(HudLayer.Frames), new Vector2(0.5f, 0f), new Vector2(0f, 236f),
                new Vector2(900f, 60f));
            _group = rect.gameObject.AddComponent<CanvasGroup>();
            _group.blocksRaycasts = false;
            _group.alpha = 0f;
            _text = Hud.Label("Line", rect, 19, TextAnchor.LowerCenter, new Color(0.98f, 0.95f, 0.86f), FontStyle.Italic);
            _text.horizontalOverflow = HorizontalWrapMode.Wrap;
            _text.supportRichText = false;
            var shadow = _text.gameObject.AddComponent<Shadow>();
            shadow.effectColor = new Color(0f, 0f, 0f, 0.6f);
            shadow.effectDistance = new Vector2(2f, -2f);
        }

        void OnDestroy()
        {
            if (_active == this)
                _active = null;
        }

        void Update()
        {
            // Not over the title, and not while someone's talking to you.
            bool waiting = TitleScreen.Showing || DialoguePanel.Talking;
            if (waiting)
            {
                _group.alpha = 0f;
                return;
            }

            if (_line == null)
            {
                if (_gap > 0f)
                {
                    _gap -= Time.deltaTime;
                    return;
                }
                if (_queue.Count == 0)
                    return;
                var (next, shown) = _queue.Dequeue();
                _line = "“" + next + "”";
                shown?.Invoke();
                _letters = 0f;
                _hold = 1.6f + _line.Length * 0.045f;
            }

            if (_letters < _line.Length)
            {
                _letters += LettersPerSecond * Time.deltaTime;
                _text.text = _line.Substring(0, Mathf.Min(_line.Length, Mathf.FloorToInt(_letters)));
                _group.alpha = 1f;
                return;
            }

            _text.text = _line;
            _hold -= Time.deltaTime;
            _group.alpha = Mathf.Clamp01((_hold + FadeSeconds) / FadeSeconds);
            if (_hold <= -FadeSeconds)
            {
                _line = null;
                _group.alpha = 0f;
                _gap = GapSeconds;
            }
        }
    }
}
