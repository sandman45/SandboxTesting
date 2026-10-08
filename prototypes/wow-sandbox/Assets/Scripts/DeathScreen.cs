using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace WowSandbox
{
    /// <summary>
    /// Shown a moment after the player dies: the screen darkens, a line suited to whatever
    /// did it (sharks and the leviathan get their own), and two choices — Respawn at the
    /// spawn point, or Quit. The body lies where it fell until you pick one.
    /// </summary>
    [RequireComponent(typeof(HealthController))]
    public class DeathScreen : MonoBehaviour
    {
        [Tooltip("Seconds to let the death play out before the screen appears.")]
        public float delay = 1.5f;
        public float fadeSeconds = 1f;

        // (title, line) — "{0}" is the killer's name where there is one.
        static readonly (string, string)[] SharkLines =
        {
            ("Fin-ished", "You went looking for a bite to eat. The {0} found one first."),
            ("Chum's the Word", "The sea has a strict no-swimming policy. It's enforced with teeth."),
            ("Jaws-Dropping", "Somewhere out there, a {0} is telling its friends about the one that didn't get away."),
            ("Seafood Diet", "You saw food. It saw food. Only one of you was right."),
            ("Bitten Off More Than You Could Chew", "Correction: the {0} did. Several times."),
            ("Shore Thing", "Should have stayed on the shore. That was the sure thing."),
        };

        static readonly (string, string)[] LeviathanLines =
        {
            ("In Too Deep", "The Ancient Leviathan pulled you down for a deep and meaningful conversation."),
            ("Sunk Cost", "You went out too far, and the deep came up to collect."),
            ("Swallowed Whole", "Good news: you finally reached the bottom of the sea. Bad news: from the inside."),
            ("Abyssal Performance", "One star. Would not be dragged into the abyss again."),
            ("Leviathan Wanted to Hang Out", "Unfortunately, it meant hang out below."),
        };

        static readonly (string, string)[] DrownedLines =
        {
            ("Out of Your Depth", "Breathing: it's not just for dry land."),
            ("Sleeping With the Fishes", "Literally. They're being very polite about it."),
            ("In Deep Water", "Note to self: armour does not float."),
        };

        static readonly (string, string)[] OtherLines =
        {
            ("You Have Died", "Even heroes have off days. This was one of them."),
            ("Rest in Pieces", "The {0} sends its regards."),
        };

        HealthController _health;
        CanvasGroup _group;
        Text _title;
        Text _line;

        void Awake()
        {
            _health = GetComponent<HealthController>();
            Build();
        }

        void OnEnable()
        {
            _health.Died += OnDied;
            _health.Respawned += OnRespawned;
        }

        void OnDisable()
        {
            _health.Died -= OnDied;
            _health.Respawned -= OnRespawned;
        }

        void OnDied(DeathCause cause)
        {
            var lines = cause switch
            {
                DeathCause.Shark => SharkLines,
                DeathCause.Leviathan => LeviathanLines,
                DeathCause.Drowned => DrownedLines,
                _ => OtherLines,
            };
            var (title, line) = lines[Random.Range(0, lines.Length)];
            _title.text = title;
            _line.text = string.Format(line, string.IsNullOrEmpty(_health.LastKiller) ? "creature" : _health.LastKiller);

            HudTooltip.Hide();
            StopAllCoroutines();
            StartCoroutine(FadeIn());
        }

        void OnRespawned()
        {
            StopAllCoroutines();
            _group.alpha = 0f;
            _group.blocksRaycasts = false;
            _group.gameObject.SetActive(false);
        }

        IEnumerator FadeIn()
        {
            yield return new WaitForSeconds(delay);

            _group.gameObject.SetActive(true);
            _group.transform.SetAsLastSibling(); // over the leviathan's fade to black
            _group.blocksRaycasts = true;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;

            for (float t = 0f; t < fadeSeconds; t += Time.deltaTime)
            {
                _group.alpha = t / fadeSeconds;
                yield return null;
            }
            _group.alpha = 1f;
        }

        void Quit()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        void Build()
        {
            var root = Hud.Stretch("DeathScreen", Hud.Layer(HudLayer.Overlay));
            _group = root.gameObject.AddComponent<CanvasGroup>();
            _group.alpha = 0f;
            _group.blocksRaycasts = false;

            // A dark, faintly red wash over the world.
            var wash = root.gameObject.AddComponent<Image>();
            wash.color = new Color(0.12f, 0.01f, 0.01f, 0.72f);
            wash.raycastTarget = true; // nothing behind it takes clicks while you're dead

            var centre = new Vector2(0.5f, 0.5f);
            Hud.Label("Heading", root, centre, new Vector2(0f, 150f), new Vector2(900f, 30f),
                20, TextAnchor.MiddleCenter, HudTheme.Heading, FontStyle.Bold).text = "YOU HAVE DIED";

            _title = Hud.Label("Title", root, centre, new Vector2(0f, 85f), new Vector2(1200f, 70f),
                52, TextAnchor.MiddleCenter, new Color(0.9f, 0.18f, 0.12f), FontStyle.Bold);

            _line = Hud.Label("Line", root, centre, new Vector2(0f, 20f), new Vector2(760f, 60f),
                20, TextAnchor.MiddleCenter, HudTheme.Text, FontStyle.Italic);
            _line.horizontalOverflow = HorizontalWrapMode.Wrap;

            Hud.TextButton("Respawn", root, centre, new Vector2(-110f, -70f), new Vector2(190f, 46f),
                () => _health.Respawn());
            Hud.TextButton("Quit Game", root, centre, new Vector2(110f, -70f), new Vector2(190f, 46f), Quit);

            root.gameObject.SetActive(false);
        }
    }
}
