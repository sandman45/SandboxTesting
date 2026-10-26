using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace WowSandbox
{
    /// <summary>
    /// Resting by the fire: sitting down (X) beside a lit campfire opens this — the time of day
    /// on a big copy of the minimap's dial, a choice of how long to rest, and when he'd wake.
    /// Rest dims the screen and the dial spins the sun and moon round to the waking hour while
    /// the hours really pass (DayNightCycle.Rush): the fire burns down, and goes out if he
    /// sleeps past it, hunger grows, the weather turns. A blow, death or Esc wakes him early.
    /// Waking heals him to full (not when a blow woke him); an hour or more also gets his strength
    /// back, once he's eaten (Hunger.Rest).
    ///
    /// Standing up closes it. Added to the player the first time he sits by a fire.
    /// </summary>
    public class RestPanel : HudPanel
    {
        static readonly int[] Lengths = { 1, 2, 5, 8, 10, 12 };

        const float Width = 320f;
        const float Pad = 14f;
        const float DialSize = 140f;
        const float BigDialSize = 280f;
        const float Reach = 4.5f;
        const float FadeSeconds = 0.6f;

        static readonly Color Chosen = new(0.62f, 0.47f, 0.18f, 1f);
        static readonly Color NotChosen = new(0.36f, 0.27f, 0.12f, 0.95f);

        enum Phase { Awake, FallingAsleep, Sleeping, Waking }

        /// <summary>Whether he's asleep by the fire (or nodding off, or waking) — PlayerSit keeps him sat.</summary>
        public static bool Resting { get; private set; }

        Campfire _fire;
        PlayerSit _sit;
        HealthController _health;

        ClockDial _dial;
        Text _status, _now, _wake;
        readonly Button[] _lengths = new Button[Lengths.Length];
        int _chosen;

        // The dimmed screen while he sleeps, with the big dial.
        CanvasGroup _sleep;
        ClockDial _bigDial;
        Text _bigTime;

        Phase _phase;
        float _phaseStarted;
        int _hours;
        float _fellAsleepAt;

        protected override Vector2 PanelSize => new(Width, 284f);
        protected override Vector2 PanelAnchor => new(0.5f, 0.5f);
        protected override Vector2 PanelPosition => new(280f, 40f);

        /// <summary>The window for <paramref name="fire"/> — on him sitting down by it, adding the window if it's new.</summary>
        public static void OpenAt(Campfire fire, GameObject player)
        {
            var panel = player.GetComponent<RestPanel>();
            if (panel == null)
                panel = player.AddComponent<RestPanel>();
            panel._fire = fire;
            panel.Open();
        }

        protected override void Awake()
        {
            toggleKey = Key.None;
            altToggleKey = Key.None;
            _sit = GetComponent<PlayerSit>();
            _health = GetComponent<HealthController>();
            base.Awake();
        }

        void OnEnable()
        {
            Combat.PlayerAttacked += OnAttacked;
            if (_health != null)
                _health.Died += OnDied;
        }

        protected override void OnDisable()
        {
            Combat.PlayerAttacked -= OnAttacked;
            if (_health != null)
                _health.Died -= OnDied;
            if (_phase != Phase.Awake)
                EndSleep();
            base.OnDisable();
        }

        void OnAttacked(HealthController target, AttackResult result)
        {
            if (result.Hit && _phase is Phase.FallingAsleep or Phase.Sleeping)
            {
                Hud.Error("Your rest is interrupted!");
                WakeUp(attacked: true);
            }
        }

        void OnDied(DeathCause cause)
        {
            if (_phase != Phase.Awake)
                EndSleep();
        }

        protected override void Build()
        {
            var topLeft = new Vector2(0f, 1f);
            var top = new Vector2(0.5f, 1f);
            Hud.Label("Title", Root, topLeft, new Vector2(Pad, -12f), new Vector2(Width - 60f, 24f),
                18, TextAnchor.MiddleLeft, HudTheme.Text, FontStyle.Bold).text = "Rest";
            _status = Hud.Label("Status", Root, topLeft, new Vector2(Pad, -38f), new Vector2(Width - Pad * 2f, 20f),
                13, TextAnchor.MiddleLeft, HudTheme.TextDim);

            _dial = new ClockDial(Root, top, new Vector2(0f, -66f), DialSize);
            _now = Hud.Label("Now", Root, top, new Vector2(0f, -66f - DialSize * 0.5f - 6f), new Vector2(Width, 24f),
                18, TextAnchor.MiddleCenter, HudTheme.Text, FontStyle.Bold);
            _wake = Hud.Label("Wake", Root, top, new Vector2(0f, -66f - DialSize * 0.5f - 30f), new Vector2(Width, 20f),
                14, TextAnchor.MiddleCenter, HudTheme.TextDim);

            const float buttonWidth = 42f;
            float gap = (Width - Pad * 2f - buttonWidth * Lengths.Length) / (Lengths.Length - 1);
            for (int i = 0; i < Lengths.Length; i++)
            {
                int index = i;
                _lengths[i] = Hud.TextButton($"{Lengths[i]}h", Root, topLeft,
                    new Vector2(Pad + i * (buttonWidth + gap), -200f), new Vector2(buttonWidth, 28f), () => Choose(index));
            }
            Hud.TextButton("Rest", Root, new Vector2(0.5f, 0f), new Vector2(0f, 16f), new Vector2(120f, 30f), FallAsleep);
            Choose(0);
        }

        void Choose(int index)
        {
            _chosen = index;
            for (int i = 0; i < _lengths.Length; i++)
            {
                var colors = _lengths[i].colors;
                colors.normalColor = i == index ? Chosen : NotChosen;
                _lengths[i].colors = colors;
            }
        }

        protected override void Refresh()
        {
            var cycle = DayNightCycle.Active;
            if (_phase == Phase.Awake && (_fire == null || !_fire.IsLit || Distance() > Reach ||
                                          _sit == null || !_sit.IsSitting))
            {
                Close();
                return;
            }

            if (cycle != null)
            {
                _dial.Show(cycle.hour, cycle.Daylight);
                _now.text = ClockDial.Format(cycle.hour);
                _wake.text = $"Wake at {ClockDial.Format(cycle.hour + Lengths[_chosen])}";
            }
            else
            {
                _now.text = "";
                _wake.text = "";
            }

            float fireLeft = _fire != null ? Mathf.Max(0f, _fire.HoursLeft) : 0f;
            _status.text = fireLeft < Lengths[_chosen]
                ? $"The fire has {fireLeft:0.0} hours left — it will burn out."
                : $"The fire has {fireLeft:0.0} hours left in it.";
        }

        // Esc or the X while asleep: he wakes.
        protected override void Closed()
        {
            if (_phase is Phase.FallingAsleep or Phase.Sleeping)
                WakeUp(attacked: false);
        }

        void FallAsleep()
        {
            var cycle = DayNightCycle.Active;
            if (_phase != Phase.Awake || cycle == null || _sit == null || !_sit.IsSitting)
                return;
            _hours = Lengths[_chosen];
            _fellAsleepAt = cycle.hour;
            Resting = true;
            Begin(Phase.FallingAsleep);
            ShowSleep(true);
        }

        protected override void Update()
        {
            base.Update();
            if (_phase == Phase.Awake)
                return;

            var cycle = DayNightCycle.Active;
            float t = Time.time - _phaseStarted;
            switch (_phase)
            {
                case Phase.FallingAsleep:
                    _sleep.alpha = Mathf.Clamp01(t / FadeSeconds);
                    if (t >= FadeSeconds)
                    {
                        // A dozen hours go by in under five seconds.
                        cycle?.Rush(_hours, 1.5f + _hours * 0.25f);
                        Begin(Phase.Sleeping);
                    }
                    break;
                case Phase.Sleeping:
                    if (cycle == null || !cycle.Rushing)
                    {
                        Rested(attacked: false);
                        Begin(Phase.Waking);
                    }
                    break;
                case Phase.Waking:
                    _sleep.alpha = 1f - Mathf.Clamp01(t / FadeSeconds);
                    if (t >= FadeSeconds)
                    {
                        EndSleep();
                        Close();
                    }
                    break;
            }

            if (cycle != null)
            {
                _bigDial.Show(cycle.hour, cycle.Daylight);
                _bigTime.text = ClockDial.Format(cycle.hour);
            }
        }

        /// <summary>Early: stops the hours and fades back in.</summary>
        void WakeUp(bool attacked)
        {
            if (_phase == Phase.Sleeping)
            {
                DayNightCycle.Active?.StopRush();
                Rested(attacked);
            }
            if (_phase is Phase.FallingAsleep or Phase.Sleeping)
            {
                // Fade back from however dark it got.
                _phaseStarted = Time.time - (1f - _sleep.alpha) * FadeSeconds;
                _phase = Phase.Waking;
            }
        }

        /// <summary>
        /// What the sleep did for him, once it's over: back to full health — unless something woke
        /// him with a blow — and his strength, after an hour of it.
        /// </summary>
        void Rested(bool attacked)
        {
            var cycle = DayNightCycle.Active;
            float slept = cycle != null ? Mathf.Repeat(cycle.hour - _fellAsleepAt, 24f) : 0f;
            if (slept < 0.25f)
                return;
            int whole = Mathf.Max(1, Mathf.RoundToInt(slept));
            Hud.Info($"You rest for {whole} hour{(whole == 1 ? "" : "s")}.");
            if (!attacked && _health != null)
                _health.Heal(_health.maxHealth);
            // An hour's sleep by the fire is all the rest he needs to get his strength back.
            if (slept >= 1f - 0.05f && Hunger.Active != null)
                Hunger.Active.Rest(Hunger.Active.restSeconds);
        }

        void Begin(Phase phase)
        {
            _phase = phase;
            _phaseStarted = Time.time;
        }

        void EndSleep()
        {
            DayNightCycle.Active?.StopRush();
            _phase = Phase.Awake;
            Resting = false;
            ShowSleep(false);
        }

        void ShowSleep(bool shown)
        {
            if (shown && _sleep == null)
                BuildSleep();
            if (_sleep == null)
                return;
            _sleep.gameObject.SetActive(shown);
            if (shown)
            {
                _sleep.alpha = 0f;
                _sleep.transform.SetAsLastSibling();
            }
        }

        void BuildSleep()
        {
            var root = Hud.Stretch("Resting", Hud.Layer(HudLayer.Overlay));
            var shade = root.gameObject.AddComponent<Image>();
            shade.color = new Color(0f, 0f, 0f, 0.88f);
            shade.raycastTarget = true;   // nothing behind it clickable while he sleeps
            _sleep = root.gameObject.AddComponent<CanvasGroup>();

            var centre = new Vector2(0.5f, 0.5f);
            _bigDial = new ClockDial(root, centre, new Vector2(0f, 60f), BigDialSize);
            _bigTime = Hud.Label("Time", root, centre, new Vector2(0f, -36f), new Vector2(300f, 34f),
                28, TextAnchor.MiddleCenter, HudTheme.Text, FontStyle.Bold);
            Hud.Label("Hint", root, centre, new Vector2(0f, -70f), new Vector2(400f, 22f),
                15, TextAnchor.MiddleCenter, HudTheme.TextDim).text = "Resting by the fire...  (Esc to wake)";
            root.gameObject.SetActive(false);
        }

        float Distance()
        {
            var offset = transform.position - _fire.transform.position;
            offset.y = 0f;
            return offset.magnitude;
        }
    }
}
