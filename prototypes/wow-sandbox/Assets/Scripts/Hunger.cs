using UnityEngine;
using UnityEngine.UI;

namespace WowSandbox
{
    /// <summary>
    /// The Captain's stomach. He washes up Starving and weak: he can only walk, he's too tired to
    /// swim (his breath runs out at the surface, and he drowns), and he can't get further than
    /// beachRadius from where he woke — he staggers as he nears the edge and can't go past it.
    /// It takes a cooked meal (story flag "survival.fed") and then a rest sat by a campfire (Rest,
    /// restSeconds of it — "survival.strong") to get his strength back, for good.
    ///
    /// After that it's a gentle hunger bar that empties over hoursToEmpty game hours: Hungry
    /// below a quarter (a little slower and weaker), Starving again when it's empty (slower and
    /// weaker still, but no wall and no drowning). Cooked food fills it and leaves him Well Fed
    /// (+1 damage) for a while; raw food fills it a little and makes him Sick.
    ///
    /// Food is eaten from the bags (Inventory.Use). The bar and his state hang under the player
    /// frame. Added to the player at Play; SaveGame keeps the bar and the timers.
    /// </summary>
    [RequireComponent(typeof(WowCharacterController))]
    public class Hunger : MonoBehaviour
    {
        [Tooltip("How far from where he woke he can get while starving, in metres.")]
        public float beachRadius = 25f;
        [Tooltip("How far inside that edge he starts to stagger.")]
        public float staggerBand = 5f;
        [Tooltip("Game hours for a full stomach to empty (a game hour is a minute at normal speed).")]
        public float hoursToEmpty = 48f;
        [Tooltip("Game hours of Well Fed after a cooked meal.")]
        public float wellFedHours = 2f;
        [Tooltip("Game hours of Sick after eating something raw.")]
        public float sickHours = 1f;
        [Tooltip("Seconds resting by a fire, after his first cooked meal, to get his strength back.")]
        public float restSeconds = 20f;

        public const string FedFlag = "survival.fed";
        public const string StrongFlag = "survival.strong";
        const float HungryBelow = 0.25f;
        const float WarnCooldown = 25f;

        public static Hunger Active { get; private set; }

        public enum State { Starving, Hungry, Fed, WellFed }

        WowCharacterController _player;
        CharacterController _body;
        CharacterStats _stats;
        BreathController _breath;
        Vector3 _wokeAt;
        float _fullness;
        float _wellFedLeft, _sickLeft;
        float _awakeSince = -1f;
        float _nextWarn;
        bool _wasHungry;
        float _rested;
        // The cast bar showing the rest, and when it was last added to.
        bool _restBar;
        float _restedAt;

        Text _label;
        HudBar _bar;
        CanvasGroup _barGroup;

        /// <summary>Whether he's still the starving castaway of the opening — before his first cooked meal.</summary>
        public static bool StillStarving => !StoryState.Has(FedFlag);

        /// <summary>
        /// Whether he's still too weak for more than the beach — walking only, no swimming — until
        /// he's eaten and rested by a fire.
        /// </summary>
        public static bool Weak => !StoryState.Has(StrongFlag);

        /// <summary>How full he is, 0-1.</summary>
        public float Fullness => _fullness;
        public float WellFedHoursLeft => _wellFedLeft;
        public float SickHoursLeft => _sickLeft;
        public bool IsSick => _sickLeft > 0f;

        public State Now => StillStarving || _fullness <= 0f ? State.Starving
                          : _wellFedLeft > 0f ? State.WellFed
                          : _fullness < HungryBelow ? State.Hungry
                          : State.Fed;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void AddToPlayer()
        {
            var player = FindAnyObjectByType<WowCharacterController>();
            if (player != null && player.GetComponent<Hunger>() == null)
                player.gameObject.AddComponent<Hunger>();
        }

        void Awake()
        {
            Active = this;
            _player = GetComponent<WowCharacterController>();
            _body = GetComponent<CharacterController>();
            _stats = GetComponent<CharacterStats>();
            _breath = GetComponent<BreathController>();
            // Where the scene stands him: on the beach by the wreck, before any save moves him.
            _wokeAt = transform.position;
            Build();
        }

        void Start()
        {
            // For tuning beachRadius: what a starving castaway can reach.
            int Near<T>() where T : Component
            {
                int count = 0;
                foreach (var thing in FindObjectsByType<T>(FindObjectsSortMode.None))
                {
                    var offset = thing.transform.position - _wokeAt;
                    offset.y = 0f;
                    if (offset.magnitude <= beachRadius)
                        count++;
                }
                return count;
            }
            Debug.Log($"[Hunger] Within {beachRadius:0} m of where he wakes: {Near<FishingSpot>()} fishing spots, " +
                      $"{Near<TreasureChest>()} chests, {Near<GroundItem>()} ground items, " +
                      $"{Near<ChickenWanderer>()} chickens.", this);
        }

        void OnDestroy()
        {
            if (Active == this)
                Active = null;
        }

        void Update()
        {
            bool playing = !TitleScreen.Showing && !ShipwreckWake.Lying;
            if (playing && _awakeSince < 0f)
                _awakeSince = Time.time;

            float hours = DayNightCycle.HoursThisFrame;
            if (!StillStarving)
                _fullness = Mathf.Max(0f, _fullness - hours / Mathf.Max(hoursToEmpty, 0.1f));
            _wellFedLeft = Mathf.Max(0f, _wellFedLeft - hours);
            _sickLeft = Mathf.Max(0f, _sickLeft - hours);

            var state = Now;
            // Weak, he's walking (below) — slow enough without slowing the walk too.
            float speed = Weak ? 1f : state switch
            {
                State.Starving => 0.85f,
                State.Hungry => 0.93f,
                _ => 1f,
            };
            int damage = Weak ? (StillStarving ? -2 : -1) : state switch
            {
                State.Starving => -2,
                State.Hungry => -1,
                State.WellFed => 1,
                _ => 0,
            };
            if (IsSick)
            {
                speed *= 0.85f;
                damage -= 1;
            }
            speed *= Stagger(playing);

            if (_stats != null)
            {
                _stats.conditionSpeed = speed;
                _stats.conditionDamage = damage;
            }

            _player.ForceWalk = Weak && playing;

            bool tooWeakToSwim = Weak && playing && _player.IsSwimming;
            if (_breath != null)
                _breath.Exhausted = tooWeakToSwim;
            if (tooWeakToSwim)
                Warn("swim", "I can't... I'm too weak to swim!", "Too weak... get out of the water!");

            if (playing)
                Think(state);
            Show(state);

            // He got up (or walked off) mid-rest: the bar goes, what he's rested is kept.
            if (_restBar && Time.time - _restedAt > 0.2f)
            {
                _restBar = false;
                CastBarHud.Fade();
            }
        }

        // After the controller's moved him: not an inch past the edge while he's starving.
        void LateUpdate()
        {
            if (!Weak || TitleScreen.Showing || ShipwreckWake.Lying || _body == null || !_body.enabled)
                return;
            var offset = transform.position - _wokeAt;
            offset.y = 0f;
            float distance = offset.magnitude;
            if (distance <= beachRadius)
                return;
            _body.Move(-offset / distance * (distance - beachRadius));
        }

        /// <summary>Slows him to a stagger as he nears the edge of how far he can get, weak.</summary>
        float Stagger(bool playing)
        {
            if (!Weak)
                return 1f;
            var offset = transform.position - _wokeAt;
            offset.y = 0f;
            float into = offset.magnitude - (beachRadius - staggerBand);
            if (into <= 0f)
                return 1f;
            if (playing && StillStarving)
                Warn("inland", "I'm too weak to go any further...", "I can't... not without food in me.",
                    "I need to eat. And a fire, to cook something on.");
            else if (playing)
                Warn("inland.fed", "Not yet... I need to rest first.", "My legs won't carry me. I need to rest by the fire a while.");
            return Mathf.Lerp(1f, 0.35f, Mathf.Clamp01(into / Mathf.Max(staggerBand, 0.1f)));
        }

        /// <summary>A thought the first time, then a shorter one now and then while it keeps happening.</summary>
        void Warn(string id, string again, params string[] first)
        {
            if (Time.time < _nextWarn)
                return;
            _nextWarn = Time.time + WarnCooldown;
            if (!Thoughts.Once("weak." + id, first.Length > 0 ? first : new[] { again }))
                Thoughts.Say(again);
        }

        void Think(State state)
        {
            if (StillStarving && Time.time - _awakeSince > 8f)
                Thoughts.Once("hunger", "My stomach's knotted tight... when did I last eat?",
                    "I'm weak as a kitten. I need food, and a fire to cook it on.");

            bool hungry = state is State.Hungry or State.Starving && !StillStarving;
            if (hungry && !_wasHungry)
            {
                if (!Thoughts.Once("hungry", "I should find something to eat soon."))
                    Thoughts.Say(state == State.Starving ? "I'm starving..." : "I could do with a meal.");
            }
            _wasHungry = hungry;
        }

        /// <summary>
        /// Eats <paramref name="food"/> (one of it — the caller takes it from the bags). False, with
        /// why, if it isn't food or he's too full for it.
        /// </summary>
        public bool Eat(Item food, out string error)
        {
            error = null;
            if (food == null || !food.IsFood)
            {
                error = "You can't eat that.";
                return false;
            }
            if (!StillStarving && _fullness >= 0.98f)
            {
                error = "You're too full to eat any more.";
                return false;
            }

            if (food.cooked)
            {
                bool first = StillStarving;
                if (first)
                    StoryState.Set(FedFlag);
                _fullness = Mathf.Min(1f, (first ? 0.5f : _fullness) + food.nourish);
                _wellFedLeft = wellFedHours;
                Hud.Info($"You eat the {food.name}. You feel well fed.");
                if (first)
                    Thoughts.Once("fed", "That's better...", "Now I need to rest a while by the fire, and get my strength back.");
            }
            else
            {
                if (!StillStarving)
                    _fullness = Mathf.Min(1f, _fullness + food.nourish);
                _sickLeft = sickHours;
                Hud.Info($"You eat the {food.name} raw. Your stomach heaves.");
                if (!Thoughts.Once("raw", "Ugh... that was a mistake. I need to cook it first."))
                    Thoughts.Say("I really should cook things first.");
            }
            return true;
        }

        /// <summary>Back to how it was saved — SaveGame. Whether he's still starving is a story flag, saved with the story.</summary>
        public void Restore(float fullness, float wellFedLeft, float sickLeft)
        {
            _fullness = Mathf.Clamp01(fullness);
            _wellFedLeft = Mathf.Max(0f, wellFedLeft);
            _sickLeft = Mathf.Max(0f, sickLeft);
            _wasHungry = Now is State.Hungry or State.Starving && !StillStarving;
        }

        /// <summary>
        /// Resting by a fire for <paramref name="seconds"/> — the campfire calls it each frame he's
        /// beside it. Once he's eaten, restSeconds of it gets his strength back for good.
        /// </summary>
        public void Rest(float seconds)
        {
            if (!Weak)
                return;
            if (StillStarving)
            {
                Thoughts.Once("rest.hungry", "The warmth helps... but I need food in me before I'll get my strength back.");
                return;
            }
            _rested += seconds;
            _restedAt = Time.time;
            if (_rested < restSeconds)
            {
                _restBar = true;
                CastBarHud.Show("Resting", _rested / restSeconds, restSeconds - _rested);
                return;
            }
            _restBar = false;
            CastBarHud.Finish();
            StoryState.Set(StrongFlag);
            Hud.Info("You feel your strength return.");
            Thoughts.Once("strong", "I can feel my strength coming back.", "Now. Let's see what this island's hiding.");
        }

        /// <summary>The opening's starving again, from scratch — the Developer panel's Starve.</summary>
        public void StarveAgain()
        {
            StoryState.Clear(FedFlag);
            StoryState.Clear(StrongFlag);
            _fullness = 0f;
            _wellFedLeft = _sickLeft = 0f;
            _rested = 0f;
        }

        // --- Under the player frame ------------------------------------------------------------

        void Build()
        {
            var frame = GetComponent<PlayerFrameHud>();
            var corner = frame != null ? frame.position : new Vector2(20f, -20f);
            var topLeft = new Vector2(0f, 1f);
            // Below the breath bar's spot under the frame.
            var row = Hud.Rect("HungerRow", Hud.Layer(HudLayer.Frames), topLeft, corner + new Vector2(12f, -98f),
                new Vector2(226f, 18f));
            _label = Hud.Label("State", row, topLeft, Vector2.zero, new Vector2(84f, 18f),
                13, TextAnchor.MiddleLeft, HudTheme.Text, FontStyle.Bold);
            var barRow = Hud.Rect("Bar", row, topLeft, new Vector2(86f, -5f), new Vector2(140f, 8f));
            _barGroup = barRow.gameObject.AddComponent<CanvasGroup>();
            _bar = new HudBar("Food", barRow, topLeft, Vector2.zero, barRow.sizeDelta);
            _bar.Snap(0f);
        }

        static readonly Color StarvingColor = new(0.95f, 0.3f, 0.2f);
        static readonly Color HungryColor = new(0.95f, 0.65f, 0.25f);
        static readonly Color FedColor = new(0.85f, 0.75f, 0.45f);
        static readonly Color WellFedColor = new(0.45f, 0.9f, 0.4f);
        static readonly Color SickColor = new(0.6f, 0.75f, 0.3f);

        void Show(State state)
        {
            if (_label == null)
                return;
            // Eaten, but not yet rested by a fire.
            bool weakOnly = Weak && !StillStarving && !IsSick;
            _label.text = IsSick ? "Sick" : weakOnly ? "Weak" : state switch
            {
                State.Starving => "Starving",
                State.Hungry => "Hungry",
                State.WellFed => "Well Fed",
                _ => "Fed",
            };
            _label.color = IsSick ? SickColor : weakOnly ? HungryColor : state switch
            {
                State.Starving => StarvingColor,
                State.Hungry => HungryColor,
                State.WellFed => WellFedColor,
                _ => FedColor,
            };
            // No bar till there's been a meal to fill it.
            _barGroup.alpha = StillStarving ? 0f : 1f;
            _bar.Set(_fullness);
            _bar.FillColor = _fullness < HungryBelow ? HungryColor : FedColor;
        }
    }
}
