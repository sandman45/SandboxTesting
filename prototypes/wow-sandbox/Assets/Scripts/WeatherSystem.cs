using System;
using UnityEngine;
using Random = UnityEngine.Random;

namespace WowSandbox
{
    /// <summary>
    /// The weather changing on its own: every few game hours a new front picks the next sky —
    /// Clear, Few, Scattered, Broken or Overcast — and the clouds, wind (so the trees, grass and
    /// sea) blend there over most of an hour. It wanders a step or two at a time, leaning fair;
    /// only an overcast sky can turn to a storm, which then runs StormWeather's own cycle and
    /// leaves the sky overcast behind it.
    ///
    /// While it runs, its sky is what CloudLayer shows in fine weather in place of the Clouds
    /// window's settings. On/off, pace and storm chance are the Developer panel's Weather window,
    /// remembered. Added by StormWeather, like the rest of the weather.
    /// </summary>
    [RequireComponent(typeof(StormWeather))]
    public class WeatherSystem : MonoBehaviour
    {
        public enum Sky { Clear, Few, Scattered, Broken, Overcast, Storm }

        // How often each fair sky turns up, against the others.
        static readonly float[] Commonness = { 2f, 3f, 3f, 2f, 1.5f };
        // Game hours a sky holds before the next front, at pace 1; and how long a front takes.
        static readonly Vector2 HoldHours = new(2f, 4f);
        const float BlendHours = 0.75f;

        const string EnabledKey = "dev.weather.on";
        const string PaceKey = "dev.weather.pace";
        const string StormChanceKey = "dev.weather.stormChance";

        public static WeatherSystem Active { get; private set; }

        StormWeather _weather;
        Sky _from = Sky.Overcast, _to = Sky.Overcast;
        float _blend = 1f;              // 0 at _from .. 1 at _to
        float _blendRate;               // per game hour
        float _hoursLeft;               // till the next front
        bool _waitingOutStorm;

        /// <summary>The sky it's showing (or heading for, mid-front).</summary>
        public Sky Now => _blend >= 1f ? _to : _from;
        public Sky Heading => _to;
        public float BlendProgress => _blend;
        public float HoursToNextFront => _hoursLeft;

        /// <summary>Whether it's running: on (Developer → Weather), and the clouds are up.</summary>
        public static bool On
        {
            get => !DevOptions.Enabled || PlayerPrefs.GetInt(EnabledKey, 1) == 1;
            set => Save(() => PlayerPrefs.SetInt(EnabledKey, value ? 1 : 0));
        }

        /// <summary>How quickly fronts come: 1 is every 2–4 game hours, 2 twice as often.</summary>
        public static float Pace
        {
            get => DevOptions.Enabled ? PlayerPrefs.GetFloat(PaceKey, 1f) : 1f;
            set => Save(() => PlayerPrefs.SetFloat(PaceKey, Mathf.Max(0.1f, value)));
        }

        /// <summary>The chance an overcast sky turns to a storm when its front moves on.</summary>
        public static float StormChance
        {
            get => DevOptions.Enabled ? PlayerPrefs.GetFloat(StormChanceKey, 0.35f) : 0.35f;
            set => Save(() => PlayerPrefs.SetFloat(StormChanceKey, Mathf.Clamp01(value)));
        }

        static void Save(Action write)
        {
            write();
            PlayerPrefs.Save();
        }

        /// <summary>
        /// The fair-weather clouds it's showing right now, mid-front included: how much cloud, how
        /// thick, deep, soft and sunlit, and the wind. Their size, height, wind direction and
        /// silver lining stay the Clouds window's.
        /// </summary>
        public CloudSettings Clouds
        {
            get
            {
                var front = CloudSettings.Lerp(Look(_from), Look(_to), Smooth(_blend));
                var clouds = CloudSettings.Current;
                clouds.coverage = front.coverage;
                clouds.density = front.density;
                clouds.thickness = front.thickness;
                clouds.softness = front.softness;
                clouds.sunlight = front.sunlight;
                clouds.windSpeed = front.windSpeed;
                return clouds;
            }
        }

        /// <summary>Whether CloudLayer should show <see cref="Clouds"/> rather than the Clouds window's.</summary>
        public static bool Driving => Active != null && On;

        void Awake() => Active = this;

        void Start()
        {
            _weather = GetComponent<StormWeather>();
            // After the wreck's storm, or any storm Play opened with, the sky starts overcast.
            _hoursLeft = NextHold();
        }

        void OnDestroy()
        {
            if (Active == this)
                Active = null;
        }

        void Update()
        {
            if (!On || TitleScreen.Showing)
                return;
            float hours = DayNightCycle.HoursThisFrame;

            if (_blend < 1f)
                _blend = Mathf.Min(1f, _blend + _blendRate * hours);

            // Fronts wait while a storm's on: it clears in its own time, and leaves it overcast.
            if (_weather.StormRunning || ShipwreckWake.StormHeld)
            {
                if (_to == Sky.Storm || !_waitingOutStorm)
                {
                    _waitingOutStorm = true;
                    _from = _to = Sky.Overcast;
                    _blend = 1f;
                }
                return;
            }
            if (_waitingOutStorm)
            {
                _waitingOutStorm = false;
                _hoursLeft = NextHold();
            }

            _hoursLeft -= hours;
            if (_hoursLeft <= 0f)
                NextFront();
        }

        /// <summary>The front moves on: a new sky a step or two away, or from overcast maybe a storm.</summary>
        void NextFront()
        {
            var current = _to;
            if (current == Sky.Overcast && Random.value < StormChance)
            {
                GoTo(Sky.Storm);
                return;
            }

            float total = 0f;
            var weights = new float[Commonness.Length];
            for (int i = 0; i < weights.Length; i++)
            {
                int steps = Mathf.Abs(i - (int)current);
                // A step or two at a time, and never straight back to the same sky.
                weights[i] = steps == 0 || steps > 2 ? 0f : Commonness[i] / steps;
                total += weights[i];
            }
            float pick = Random.value * total;
            for (int i = 0; i < weights.Length; i++)
            {
                pick -= weights[i];
                if (pick <= 0f && weights[i] > 0f)
                {
                    GoTo((Sky)i);
                    return;
                }
            }
        }

        /// <summary>
        /// Heads for <paramref name="sky"/> over <paramref name="blendHours"/> game hours (a front's
        /// usual pace if not given), then holds it. Storm runs StormWeather's storm cycle.
        /// </summary>
        public void GoTo(Sky sky, float blendHours = BlendHours)
        {
            _hoursLeft = NextHold();
            if (sky == Sky.Storm)
            {
                _from = _to = Sky.Overcast;
                _blend = 1f;
                _waitingOutStorm = true;
                _weather.StartStormCycle();
                return;
            }
            _from = Now;
            _to = sky;
            _blend = blendHours <= 0f ? 1f : 0f;
            _blendRate = 1f / Mathf.Max(blendHours, 0.01f);
            // Sent somewhere fair mid-storm: the storm clears away (not the wreck's, which has its time).
            if (_weather.StormRunning && !ShipwreckWake.StormHeld)
            {
                _weather.ClearUp();
                _waitingOutStorm = true;
            }
        }

        float NextHold() => Random.Range(HoldHours.x, HoldHours.y) / Mathf.Max(Pace, 0.1f);

        /// <summary>Each sky's clouds, with a wind to suit it: still when it's clear, fresher as it clouds over.</summary>
        static CloudSettings Look(Sky sky)
        {
            var (clouds, wind) = sky switch
            {
                Sky.Clear => (CloudSettings.Clear, 3f),
                Sky.Few => (CloudSettings.Few, 4f),
                Sky.Scattered => (CloudSettings.Scattered, 6f),
                Sky.Broken => (CloudSettings.Broken, 10f),
                _ => (CloudSettings.Overcast, 16f),
            };
            clouds.windSpeed = wind;
            return clouds;
        }

        static float Smooth(float t) => t * t * (3f - 2f * t);

        /// <summary>A line for the Developer panel: now, where it's heading, and when it next changes.</summary>
        public string Forecast()
        {
            if (!On)
                return "Weather system off — the sky is the Clouds window's.";
            if (ShipwreckWake.StormHeld)
                return "The wreck's storm — the weather starts once it clears.";
            if (_weather.StormRunning)
                return $"Storm ({_weather.PhaseName}) — overcast after it clears.";
            string now = _blend < 1f ? $"{_from} → {_to} ({_blend * 100f:0}%)" : _to.ToString();
            string next = _to == Sky.Overcast ? $", storm chance {StormChance * 100f:0}%" : "";
            return $"Now: {now}. Next front in {_hoursLeft:0.0} game hours{next}.";
        }
    }
}
