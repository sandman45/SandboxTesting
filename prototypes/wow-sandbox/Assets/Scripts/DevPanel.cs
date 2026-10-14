using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace WowSandbox
{
    /// <summary>
    /// Developer options, opened from Settings (hidden entirely when DevOptions.Enabled is
    /// false): drive the storm by hand and set how long its stages last, summon the sea's
    /// dangers, spawn enemies and chickens beside you, and cheat — god mode, healing, gold,
    /// magic items, game speed.
    ///
    /// The storm switches and lengths are remembered (DevOptions); the rest last until Play
    /// ends.
    /// </summary>
    public class DevPanel : HudPanel
    {
        const float Width = 470f;
        const float Pad = 20f;
        const float ButtonWidth = 101f;
        const float ButtonGap = 7f;

        Text _stormStatus;
        Text _stormOnPlay;
        Text _godMode;
        Text _skipTitle;
        Action _refreshSliders;

        // Inactive copies of one living enemy per kind and one chicken per colour, taken as
        // Play starts, so spawning still works once you've killed every original.
        readonly List<GameObject> _enemies = new();
        readonly List<GameObject> _chickens = new();

        protected override Vector2 PanelSize => new(Width, 662f);
        protected override Vector2 PanelAnchor => new(0.5f, 0.5f);
        protected override Vector2 PanelPosition => Vector2.zero;

        protected override void Awake()
        {
            toggleKey = Key.None;
            base.Awake();
        }

        void Start()
        {
            var stash = new GameObject("DevSpawnTemplates");
            var enemyKinds = new HashSet<string>();
            var chickenKinds = new HashSet<string>();
            foreach (var wanderer in FindObjectsByType<WanderingNpc>())
            {
                var health = wanderer.GetComponent<Health>();
                if (health == null || health.IsDead)
                    continue;

                if (wanderer is ChickenWanderer)
                {
                    // One per colour: the model's texture tells them apart.
                    var look = wanderer.GetComponentInChildren<Renderer>();
                    string kind = look != null && look.sharedMaterial != null && look.sharedMaterial.mainTexture != null
                        ? look.sharedMaterial.mainTexture.name : "chicken";
                    if (chickenKinds.Add(kind))
                        _chickens.Add(Stash(wanderer.gameObject, stash.transform));
                }
                else if (wanderer.GetComponent<NpcCombat>() != null && health.reaction == Reaction.Hostile)
                {
                    var stats = wanderer.GetComponent<CharacterStats>();
                    if (enemyKinds.Add(stats != null ? stats.characterName : wanderer.name))
                        _enemies.Add(Stash(wanderer.gameObject, stash.transform));
                }
            }
        }

        /// <summary>An inactive copy — it doesn't wake up (Awake, Start) until it's spawned.</summary>
        static GameObject Stash(GameObject original, Transform stash)
        {
            original.SetActive(false);
            var copy = Instantiate(original, stash);
            original.SetActive(true);
            copy.name = original.name;
            return copy;
        }

        /// <summary>A random one of <paramref name="templates"/>, on the ground in front of you, facing you.</summary>
        void Spawn(List<GameObject> templates, string what, float distance)
        {
            var player = FindAnyObjectByType<WowCharacterController>();
            if (player == null)
                return;
            if (templates.Count == 0)
            {
                Hud.Error($"There were no living {what} in the scene to copy when Play started.");
                return;
            }

            var ahead = player.transform.position + player.transform.forward * distance;
            if (!NavMesh.SamplePosition(ahead, out var hit, 6f, NavMesh.AllAreas))
            {
                Hud.Error($"There's no ground in front of you for a {what.TrimEnd('s')} to stand on.");
                return;
            }

            var toPlayer = player.transform.position - hit.position;
            toPlayer.y = 0f;
            var rotation = toPlayer.sqrMagnitude > 0.01f ? Quaternion.LookRotation(toPlayer) : Quaternion.identity;
            var template = templates[UnityEngine.Random.Range(0, templates.Count)];
            var spawned = Instantiate(template, hit.position, rotation);
            spawned.name = template.name;
            spawned.SetActive(true);
        }

        // Cheats last until Play ends — even if the editor skips the domain reload.
        void OnDestroy()
        {
            Time.timeScale = 1f;
            HealthController.GodMode = false;
        }

        protected override void Build()
        {
            Hud.Label("Title", Root, new Vector2(0.5f, 1f), new Vector2(0f, -14f), new Vector2(Width, 28f),
                22, TextAnchor.MiddleCenter, HudTheme.Heading, FontStyle.Bold).text = "Developer";

            float y = -50f;
            Heading("Weather", ref y);
            _stormStatus = Hud.Label("Status", Root, new Vector2(0f, 1f), new Vector2(Pad + 8f, y), new Vector2(Width - Pad * 2f, 20f),
                13, TextAnchor.MiddleLeft, HudTheme.TextDim);
            y -= 24f;
            Buttons(ref y,
                ("Start Storm", () => Weather(w => w.StartStormCycle())),
                ("Roll In", () => Weather(w => w.RollInStorm())),
                ("Clear Up", () => Weather(w => w.ClearUp())),
                ("Stop Storm", () => Weather(w => w.StopStorm())));
            var row = Buttons(ref y,
                ("Lightning", () => Weather(w => w.StrikeNow())),
                ("Storm on Play", () => DevOptions.StormOnPlay = !DevOptions.StormOnPlay),
                ("Skip Title", () => DevOptions.SkipTitle = !DevOptions.SkipTitle));
            _stormOnPlay = row[1].GetComponentInChildren<Text>();
            _skipTitle = row[2].GetComponentInChildren<Text>();

            StormLength("Build-up", ref y, DevOptions.BuildSeconds, DevOptions.SetBuildSeconds, 300f);
            StormLength("Full storm", ref y, DevOptions.HoldSeconds, DevOptions.SetHoldSeconds, 600f);
            StormLength("Clearing", ref y, DevOptions.ClearSeconds, DevOptions.SetClearSeconds, 300f);

            Heading("Sea", ref y);
            Buttons(ref y,
                ("Sharks", () => Player<DeepSeaDanger>(d => d.DebugSharks())),
                ("Leviathan Grab", () => Player<DeepSeaDanger>(d => d.DebugLeviathan())),
                ("Storm Leviathan", () => Weather(w => w.GetComponent<StormLeviathan>()?.RaiseNow())));

            Heading("Spawn", ref y);
            Buttons(ref y,
                ("Enemy", () => Spawn(_enemies, "enemies", 10f)),
                ("Chicken", () => Spawn(_chickens, "chickens", 4f)));

            Heading("Player", ref y);
            row = Buttons(ref y,
                ("God Mode", () => HealthController.GodMode = !HealthController.GodMode),
                ("Heal", () => Player<HealthController>(h => h.Heal(h.maxHealth))),
                ("+10 Gold", () => Player<Inventory>(i => i.AddCopper(10 * Money.Gold))));
            _godMode = row[0].GetComponentInChildren<Text>();
            Buttons(ref y,
                ("Give Rare", () => Player<Inventory>(i => i.GiveRare())),
                ("Very Rare", () => Player<Inventory>(i => i.GiveVeryRare())),
                ("Legendaries", () => Player<Inventory>(i => i.GiveLegendaries())));
            Buttons(ref y,
                ("Pirate Hat", () => Player<Inventory>(i => i.Give("Pirate Hat"))),
                ("Linen Outfit", () => Player<Inventory>(i =>
                {
                    i.Give("Linen Shirt");
                    i.Give("Cloth Shorts");
                    i.Give("Worn Sandals");
                })));

            y -= 4f;
            SliderRow("Max zoom out", ref y, GameSettings.MinZoom, GameSettings.MaxZoom,
                () => GameSettings.MaxZoomDistance, v => GameSettings.MaxZoomDistance = v, v => $"{v:0}");
            SliderRow("Game speed", ref y, 0.25f, 4f, () => Time.timeScale, v => Time.timeScale = v, v => $"{v:0.00}×");

            var bottom = new Vector2(0.5f, 0f);
            Hud.TextButton("Back", Root, bottom, new Vector2(-65f, 18f), new Vector2(110f, 30f), () =>
            {
                Close();
                GetComponent<SettingsPanel>()?.Open();
            });
            Hud.TextButton("Close", Root, bottom, new Vector2(65f, 18f), new Vector2(110f, 30f), Close);
        }

        protected override void Refresh()
        {
            var weather = StormWeather.Active;
            _stormStatus.text = weather != null
                ? $"Storm {weather.intensity * 100f:0}% — {weather.PhaseName}"
                : "No StormWeather in the scene.";
            _stormOnPlay.text = $"Storm on Play: {(DevOptions.StormOnPlay ? "On" : "Off")}";
            _godMode.text = $"God Mode: {(HealthController.GodMode ? "On" : "Off")}";
            _skipTitle.text = $"Skip Title: {(DevOptions.SkipTitle ? "On" : "Off")}";
            _refreshSliders?.Invoke();
        }

        static void Weather(Action<StormWeather> act)
        {
            if (StormWeather.Active != null)
                act(StormWeather.Active);
            else
                Hud.Error("There's no StormWeather in the scene.");
        }

        static void Player<T>(Action<T> act) where T : Component
        {
            var component = FindAnyObjectByType<T>();
            if (component != null)
                act(component);
            else
                Hud.Error($"There's no {typeof(T).Name} in the scene.");
        }

        void StormLength(string label, ref float y, Func<StormWeather, float> get,
                         Action<StormWeather, float> set, float max)
        {
            SliderRow(label, ref y, 0f, max,
                () => StormWeather.Active != null ? get(StormWeather.Active) : 0f,
                v => Weather(w => set(w, Mathf.Round(v / 5f) * 5f)),
                v => $"{Mathf.Round(v / 5f) * 5f:0}s");
        }

        void Heading(string text, ref float y)
        {
            y -= 4f;
            Hud.Label(text, Root, new Vector2(0f, 1f), new Vector2(Pad, y), new Vector2(Width - Pad * 2f, 22f),
                15, TextAnchor.MiddleLeft, HudTheme.Heading, FontStyle.Bold).text = text;
            y -= 24f;
        }

        /// <summary>A row of up to four buttons, left to right.</summary>
        Button[] Buttons(ref float y, params (string label, Action onClick)[] buttons)
        {
            var made = new Button[buttons.Length];
            for (int i = 0; i < buttons.Length; i++)
            {
                var (label, onClick) = buttons[i];
                made[i] = Hud.TextButton(label, Root, new Vector2(0f, 1f),
                    new Vector2(Pad + i * (ButtonWidth + ButtonGap), y), new Vector2(ButtonWidth, 26f), () => onClick());
                // Longer labels ("Storm on Play: Off") need the smaller type.
                made[i].GetComponentInChildren<Text>().fontSize = 12;
            }
            y -= 32f;
            return made;
        }

        void SliderRow(string label, ref float y, float min, float max, Func<float> get, Action<float> set,
                       Func<float, string> format)
        {
            var topLeft = new Vector2(0f, 1f);
            Hud.Label(label, Root, topLeft, new Vector2(Pad + 8f, y), new Vector2(130f, 24f),
                14, TextAnchor.MiddleLeft, HudTheme.Text).text = label;
            var slider = Hud.Slider(Root, topLeft, new Vector2(Pad + 140f, y - 6f), new Vector2(220f, 12f), min, max);
            var value = Hud.Label("Value", Root, topLeft, new Vector2(Width - Pad - 60f, y), new Vector2(60f, 24f),
                14, TextAnchor.MiddleRight, HudTheme.TextDim);

            slider.onValueChanged.AddListener(v =>
            {
                set(v);
                value.text = format(v);
            });
            _refreshSliders += () =>
            {
                slider.SetValueWithoutNotify(get());
                value.text = format(get());
            };
            y -= 30f;
        }
    }
}
