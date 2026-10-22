using System;
using System.Collections.Generic;
using System.Linq;
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
        // Two columns of this width, side by side, so the panel fits on a laptop screen.
        const float Width = 470f;
        const float Pad = 20f;
        const float ButtonWidth = 101f;
        const float ButtonGap = 7f;

        Text _stormStatus;
        Text _stormOnPlay;
        Text _godMode;
        Text _skipTitle;
        Text _hideClouds;
        Text _showClock;
        Text _showMinimap;
        Text _enableMap;
        Action _refreshSliders;
        float _x;               // left edge of the column being built

        // Inactive copies of one living enemy per kind and one chicken per colour, taken as
        // Play starts, so spawning still works once you've killed every original.
        readonly List<GameObject> _enemies = new();
        readonly List<GameObject> _chickens = new();

        // A first guess; Build sizes it to the taller column.
        protected override Vector2 PanelSize => new(Width * 2f, 600f);

        // The Spawn section's chest buttons: one per exported chest model, four to a row.
        static GameObject[] _chests;
        static GameObject[] Chests => _chests ??= DevChests.Load();
        static int ChestRows => (Chests.Length + 3) / 4;
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
            Hud.Label("Title", Root, new Vector2(0.5f, 1f), new Vector2(0f, -14f), new Vector2(Width * 2f, 28f),
                22, TextAnchor.MiddleCenter, HudTheme.Heading, FontStyle.Bold).text = "Developer";

            const float top = -50f;
            float y = top;
            _x = 0f;
            Heading("Weather", ref y);
            _stormStatus = Hud.Label("Status", Root, new Vector2(0f, 1f), new Vector2(_x + Pad + 8f, y), new Vector2(Width - Pad * 2f, 20f),
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
                ("Skip Title", () => DevOptions.SkipTitle = !DevOptions.SkipTitle),
                ("Clouds", () => DevOptions.HideClouds = !DevOptions.HideClouds));
            _stormOnPlay = row[1].GetComponentInChildren<Text>();
            _skipTitle = row[2].GetComponentInChildren<Text>();
            _hideClouds = row[3].GetComponentInChildren<Text>();
            Buttons(ref y, ("Weather", () =>
            {
                Close();
                GetComponent<WeatherPanel>()?.Open();
            }), ("Cloud Settings", () =>
            {
                Close();
                GetComponent<CloudPanel>()?.Open();
            }), ("Fog Settings", () =>
            {
                Close();
                GetComponent<FogPanel>()?.Open();
            }), ("Wave Settings", () =>
            {
                Close();
                GetComponent<WavePanel>()?.Open();
            }));

            StormLength("Build-up", ref y, DevOptions.BuildSeconds, DevOptions.SetBuildSeconds, 300f);
            StormLength("Full storm", ref y, DevOptions.HoldSeconds, DevOptions.SetHoldSeconds, 600f);
            StormLength("Clearing", ref y, DevOptions.ClearSeconds, DevOptions.SetClearSeconds, 300f);

            Heading("Time of day", ref y);
            SliderRow("Time", ref y, 0f, 24f, () => DayNightCycle.Active != null ? DayNightCycle.Active.hour : 0f,
                v => Clock(c => c.SetHour(v)), v => DayNightCycle.Active != null ? DayNightCycle.Active.ClockText : "—");
            SliderRow("Time speed", ref y, 0f, 30f, () => DayNightCycle.Active != null ? DayNightCycle.Active.timeSpeed : 0f,
                v => Clock(c => c.timeSpeed = v), v => $"{v:0.0}×");
            // Saved, unlike the clock: for finding how dark the night should be.
            SliderRow("Night brightness", ref y, 0f, 2f, () => DevOptions.NightBrightness,
                v => DevOptions.NightBrightness = Mathf.Round(v * 20f) / 20f, v => $"{Mathf.Round(v * 20f) * 5f:0}%");
            Buttons(ref y,
                ("Dawn", () => Clock(c => c.SetHour(DayNightCycle.Sunrise + 0.25f))),
                ("Noon", () => Clock(c => c.SetHour(12f))),
                ("Dusk", () => Clock(c => c.SetHour(DayNightCycle.Sunset - 0.25f))),
                ("Midnight", () => Clock(c => c.SetHour(0f))));
            _showClock = Buttons(ref y, ("Clock", () => DevOptions.ShowClock = !DevOptions.ShowClock))[0]
                .GetComponentInChildren<Text>();

            Heading("Sea", ref y);
            Buttons(ref y,
                ("Sharks", () => Player<DeepSeaDanger>(d => d.DebugSharks())),
                ("Leviathan Grab", () => Player<DeepSeaDanger>(d => d.DebugLeviathan())),
                ("Storm Leviathan", () => Weather(w => w.GetComponent<StormLeviathan>()?.RaiseNow())));

            float left = y;
            y = top;
            _x = Width;
            Heading("Spawn", ref y);
            Buttons(ref y,
                ("Enemy", () => Spawn(_enemies, "enemies", 10f)),
                ("Chicken", () => Spawn(_chickens, "chickens", 4f)));
            if (Chests.Length > 0)
            {
                Heading("Chests (random test loot)", ref y);
                for (int start = 0; start < Chests.Length; start += 4)
                    Buttons(ref y, Chests.Skip(start).Take(4)
                        .Select(chest => (DevChests.Label(chest), (Action)(() => DevChests.Spawn(chest)))).ToArray());
            }

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
                })),
                ("Light Kit", () => Player<Inventory>(i =>
                {
                    i.Add(ItemCatalog.Create("Torch", 3));
                    i.Give("Hooded Lantern");
                    i.Give("Bullseye Lantern");
                    i.Add(ItemCatalog.Create("Flask of Oil", 2));
                })));

            row = Buttons(ref y,
                ("Show Minimap", () => DevOptions.ShowMinimap = !DevOptions.ShowMinimap),
                ("Enable Map", () => DevOptions.EnableMap = !DevOptions.EnableMap),
                ("All Pages", StoryState.FindAllPages));
            _showMinimap = row[0].GetComponentInChildren<Text>();
            _enableMap = row[1].GetComponentInChildren<Text>();
            Buttons(ref y,
                ("Save Now", SaveGame.SaveNow),
                ("Load Save", () =>
                {
                    if (!SaveGame.Load())
                        Hud.Error("There's no saved game.");
                }),
                ("Delete Save", () =>
                {
                    SaveGame.Delete();
                    Hud.Info("Saved game deleted.");
                }));

            y -= 4f;
            SliderRow("Max zoom out", ref y, GameSettings.MinZoom, GameSettings.MaxZoom,
                () => GameSettings.MaxZoomDistance, v => GameSettings.MaxZoomDistance = v, v => $"{v:0}");
            SliderRow("Game speed", ref y, 0.25f, 4f, () => Time.timeScale, v => Time.timeScale = v, v => $"{v:0.00}×");
            // Live on the player, not saved: for finding the numbers to bake into the defaults.
            SliderRow("Swim float depth", ref y, 0.4f, 0.95f, () => Swimmer()?.floatDepth ?? 0f,
                v => { if (Swimmer() != null) Swimmer().floatDepth = v; }, v => $"{v:0.00}");
            SliderRow("Swim stroke lift", ref y, -0.5f, 1f, () => Swimmer()?.swimStrokeLift ?? 0f,
                v => { if (Swimmer() != null) Swimmer().swimStrokeLift = v; }, v => $"{v:0.00}");

            // Tall enough for the longer column and the Back/Close row under it.
            Root.sizeDelta = new Vector2(Width * 2f, -Mathf.Min(left, y) + 56f);

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
            _hideClouds.text = $"Clouds: {(DevOptions.HideClouds ? "Hidden" : "Shown")}";
            _showClock.text = $"Clock: {(DevOptions.ShowClock ? "Shown" : "Hidden")}";
            _showMinimap.text = $"Show Minimap: {(DevOptions.ShowMinimap ? "On" : "Off")}";
            _enableMap.text = $"Enable Map: {(DevOptions.EnableMap ? "On" : "Off")}";
            _refreshSliders?.Invoke();
        }

        static void Weather(Action<StormWeather> act)
        {
            if (StormWeather.Active != null)
                act(StormWeather.Active);
            else
                Hud.Error("There's no StormWeather in the scene.");
        }

        static void Clock(Action<DayNightCycle> act)
        {
            if (DayNightCycle.Active != null)
                act(DayNightCycle.Active);
            else
                Hud.Error("There's no day and night — it comes with StormWeather.");
        }

        static WowCharacterController Swimmer() => FindAnyObjectByType<WowCharacterController>();

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
            Hud.Label(text, Root, new Vector2(0f, 1f), new Vector2(_x + Pad, y), new Vector2(Width - Pad * 2f, 22f),
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
                    new Vector2(_x + Pad + i * (ButtonWidth + ButtonGap), y), new Vector2(ButtonWidth, 26f), () => onClick());
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
            Hud.Label(label, Root, topLeft, new Vector2(_x + Pad + 8f, y), new Vector2(130f, 24f),
                14, TextAnchor.MiddleLeft, HudTheme.Text).text = label;
            var slider = Hud.Slider(Root, topLeft, new Vector2(_x + Pad + 140f, y - 6f), new Vector2(220f, 12f), min, max);
            var value = Hud.Label("Value", Root, topLeft, new Vector2(_x + Width - Pad - 60f, y), new Vector2(60f, 24f),
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
