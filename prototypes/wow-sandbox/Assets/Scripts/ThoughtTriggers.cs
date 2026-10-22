using UnityEngine;

namespace WowSandbox
{
    /// <summary>
    /// When the Captain thinks aloud (Thoughts): waking on the beach, finding the map and the
    /// first page in each hand, the first dusk, storm and sight of the Leviathan, a first fight,
    /// and — again now and then, on a long cooldown — swimming too far out, running out of air
    /// and getting badly hurt. Each moment's first line plays once (StoryState).
    ///
    /// It watches the systems from outside rather than being called by them, so none of them
    /// needs to know about it. Added beside Thoughts at Play.
    /// </summary>
    public class ThoughtTriggers : MonoBehaviour
    {
        const float DeepSeaCooldown = 180f;
        const float BreathCooldown = 90f;
        const float HurtCooldown = 120f;
        // Seconds after waking to raise the Leviathan in the wreck's storm, if it isn't up.
        const float RaiseAfter = 4f;

        WowCharacterController _player;
        HealthController _health;
        BreathController _breath;
        DeepSeaDanger _sea;

        bool _wasLying;
        bool _awake;
        bool _stormWasCalm;
        float _wokeAt;
        bool _raised;
        float _nextCombatCheck;
        float _deepSeaReady, _breathReady, _hurtReady;

        void Start()
        {
            _player = FindAnyObjectByType<WowCharacterController>();
            if (_player != null)
            {
                _health = _player.GetComponent<HealthController>();
                _breath = _player.GetComponent<BreathController>();
                _sea = _player.GetComponent<DeepSeaDanger>();
            }
            _wasLying = ShipwreckWake.Lying;
            _awake = !_wasLying;
            StoryState.PageFound += OnPageFound;
        }

        void OnDestroy() => StoryState.PageFound -= OnPageFound;

        void OnPageFound(int number)
        {
            if (!JournalPages.TryFind(number, out var page))
                return;
            switch (page.hand)
            {
                case JournalHand.Log:
                    Thoughts.Once("page.log", "This is my hand. My log.", "I was... the captain of that ship?");
                    break;
                case JournalHand.Kell:
                    Thoughts.Once("page.kell", "Kell's journal. The one I was sent to find.",
                        "Strange... the way Kell shapes the letters. It's almost like mine.");
                    break;
                default:
                    Thoughts.Once("page.castaway", "Someone else was stranded here. Before us.");
                    break;
            }
        }

        void Update()
        {
            if (TitleScreen.Showing || _player == null)
                return;

            // Waking: the first key press sits them up in the sand.
            if (_wasLying && !ShipwreckWake.Lying)
            {
                Thoughts.Once("wake", "Where... where am I?", "What happened? I don't remember a thing...",
                    "That wreck... that was a ship. Was it mine?");
                _awake = true;
                _wokeAt = Time.time;
            }
            _wasLying = ShipwreckWake.Lying;
            if (!_awake)
                return;

            if (WorldMap.Found)
                Thoughts.Once("map", "A chart of this island... in my own hand?", "I must have drawn this. Before.");

            WatchDay();
            WatchStorm();
            WatchCombat();
            WatchDanger();
        }

        void WatchDay()
        {
            var cycle = DayNightCycle.Active;
            if (cycle != null && cycle.hour > 12f && cycle.Daylight < 0.4f)
                Thoughts.Once("dusk", HeldLight.IsLit ? "Getting dark. Good thing I've a light." : "Getting dark. I'll need a light.");
        }

        void WatchStorm()
        {
            var weather = StormWeather.Active;
            if (weather == null)
                return;
            // Not the storm we washed up in: only one that builds after it's cleared.
            if (weather.intensity < 0.1f)
                _stormWasCalm = true;
            if (_stormWasCalm && weather.intensity > 0.4f)
                Thoughts.Once("storm", "The wind's turning.", "Another storm's coming in. Like the one that wrecked us.");

            var leviathan = weather.GetComponent<StormLeviathan>();
            bool canRise = leviathan != null && leviathan.enabled;
            if (canRise && leviathan.IsUp)
                Thoughts.Once("leviathan", "Light preserve me... what IS that?", "It's not moving. It's... watching me.");

            // The wreck's storm (ShipwreckWake times it): if the Leviathan isn't up out in it by
            // the time they're on their feet, raise it, so there's something to see.
            if (ShipwreckWake.StormHeld && canRise && !leviathan.IsUp && !_raised &&
                weather.intensity >= leviathan.sinkBelowIntensity && Time.time - _wokeAt > RaiseAfter)
            {
                _raised = true;
                leviathan.RaiseNow();
            }
        }

        void WatchCombat()
        {
            if (Time.time < _nextCombatCheck || StoryState.Has("thought.fight"))
                return;
            _nextCombatCheck = Time.time + 0.5f;
            foreach (var npc in FindObjectsByType<NpcCombat>())
                if (npc.InCombat)
                {
                    Thoughts.Once("fight", "They mean to kill me. Steady, now...");
                    return;
                }
        }

        void WatchDanger()
        {
            if (_sea != null && _sea.InDanger && Time.time >= _deepSeaReady)
            {
                _deepSeaReady = Time.time + DeepSeaCooldown;
                if (!Thoughts.Once("deepsea", "Too far out. Turn back, before something finds me!"))
                    Thoughts.Say("Too far from shore...");
            }

            if (_breath != null && _breath.Breath01 < 0.3f && Time.time >= _breathReady)
            {
                _breathReady = Time.time + BreathCooldown;
                if (!Thoughts.Once("breath", "Air... I need air!"))
                    Thoughts.Say("Air!");
            }

            if (_health != null && _health.Current > 0f && _health.Health01 < 0.3f && Time.time >= _hurtReady)
            {
                _hurtReady = Time.time + HurtCooldown;
                if (!Thoughts.Once("hurt", "I can't take much more of this."))
                    Thoughts.Say("Not like this...");
            }
        }
    }
}
