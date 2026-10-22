using UnityEngine;
using UnityEngine.Rendering;

namespace WowSandbox
{
    /// <summary>
    /// The time of day. A clock runs 0-24 (a day lasts <see cref="dayMinutes"/> real minutes),
    /// the sun rises in the east, crosses through the scene's own noon position and sets in the
    /// west, and at night the same light turns round to be the moon, opposite it.
    ///
    /// It doesn't write fog, sky or ambient itself. StormWeather already owns those, blending
    /// from the clear-weather sky toward a storm, so this only says what the clear-weather sky
    /// is at this hour (<see cref="Evaluate"/>) and StormWeather blends from that — two scripts
    /// writing the same RenderSettings would fight. What this does write is the light's
    /// direction, which nothing else touches, and the _DayNightTint global for the unlit things
    /// (cloud dome, overcast sky, water reflection) that no light or fog would ever darken.
    ///
    /// Added by StormWeather at Play, like StormAudio, so there's nothing to set up — and so
    /// its defaults are always the code's.
    /// </summary>
    // Before StormWeather (-200), so it blends from this frame's sky.
    [DefaultExecutionOrder(-250)]
    [DisallowMultipleComponent]
    public class DayNightCycle : MonoBehaviour
    {
        /// <summary>The running cycle, or null — then it's always the scene's own daylight.</summary>
        public static DayNightCycle Active { get; private set; }

        public const float Sunrise = 6f;
        public const float Sunset = 18f;

        [Header("Clock")]
        [Range(0f, 24f)] public float hour = 8f;
        [Tooltip("Real minutes for a whole day, midnight to midnight.")]
        [Min(0.1f)] public float dayMinutes = 24f;
        [Tooltip("Multiplies the clock — for the developer panel. 0 stops time.")]
        [Min(0f)] public float timeSpeed = 1f;
        [Tooltip("Where Play starts when you don't open washed up on the beach.")]
        [Range(0f, 24f)] public float startHour = 8f;
        [Tooltip("The night of the wreck: the clock waits here until you wake, then runs on as usual.")]
        [Range(0f, 24f)] public float wreckHour = 4f;

        [Header("Dusk")]
        public Color duskSunColor = new Color(1f, 0.5f, 0.25f);
        [Tooltip("What the world's light (fog, clouds, ambient) is multiplied by at sunset.")]
        public Color duskTint = new Color(1f, 0.7f, 0.55f);

        [Header("Night")]
        [Tooltip("What the world's light is multiplied by in the middle of the night. Low = properly dark.")]
        public Color nightTint = new Color(0.05f, 0.065f, 0.11f);
        public Color moonColor = new Color(0.6f, 0.7f, 1f);
        [Tooltip("Moonlight, as a share of the scene's sunlight.")]
        [Range(0f, 1f)] public float moonlight = 0.12f;
        [Tooltip("Sky brightness at night, as a share of the day's.")]
        [Range(0f, 1f)] public float nightSkyExposure = 0.06f;
        [Tooltip("The procedural sky's tint at night. Its daytime horizon is a pale warm band; " +
                 "only dimmed, that reads as an orange glow round a black sky.")]
        public Color nightSkyTint = new Color(0.15f, 0.22f, 0.45f);
        [Tooltip("Procedural sky atmosphere at night. Thin = less horizon glow (thick is how that " +
                 "shader draws a sunset).")]
        [Range(0f, 5f)] public float nightAtmosphere = 0.35f;

        [Header("Stars and moon")]
        [Tooltip("How far below the horizon (sine of the elevation) the sun is when the stars are all out.")]
        [Range(0.05f, 0.6f)] public float starsOutBy = 0.25f;
        public Color moonDiscColor = new Color(0.85f, 0.9f, 1f);

        Light _sun;
        Quaternion _sceneRotation;
        // A sun to the south if the scene has no light to take noon from.
        Vector3 _east = Vector3.right;                          // where the sun rises from, flat
        Vector3 _noon = new Vector3(0f, 0.7f, -0.7f).normalized; // toward the sun at noon (up and out)

        float _target = -1f;    // AdvanceTo
        float _advanceRate;     // hours per second

        Transform _nightSky;
        Material _nightSkyMaterial;
        Camera _camera;

        static readonly int TintId = Shader.PropertyToID("_DayNightTint");
        static readonly int StarRotationId = Shader.PropertyToID("_StarRotation");
        static readonly int MoonDirectionId = Shader.PropertyToID("_MoonDirection");
        static readonly int MoonColorId = Shader.PropertyToID("_MoonColor");
        static readonly int StarVisibilityId = Shader.PropertyToID("_StarVisibility");
        static readonly int MoonVisibilityId = Shader.PropertyToID("_MoonVisibility");

        /// <summary>
        /// What the world's light is multiplied by right now: white by day, warm at dusk, deep
        /// blue at night. White when there's no cycle. Things with a fixed colour of their own
        /// (storm fog, underwater fog, rain) scale by it so they don't glow in the dark.
        /// </summary>
        public static Color Tint => Active != null ? Active.TintAt(Active.SunHeight) : Color.white;

        /// <summary>The sun's height in the sky as the sine of its elevation: 1 overhead, 0 on the horizon, negative below it.</summary>
        public float SunHeight => SunDirection(hour).y;

        /// <summary>0 in the dark of night to 1 in full day; in between through dawn and dusk.</summary>
        public float Daylight => DaylightAt(SunHeight);

        public bool IsNight => SunHeight < -0.05f;

        /// <summary>Game hours passing per real second right now — 0 while the clock waits (the wreck) or is paused.</summary>
        public float HoursPerSecond => ShipwreckWake.Lying || _target >= 0f ? 0f : 24f / (dayMinutes * 60f) * timeSpeed;

        /// <summary>Game hours gone by this frame; at the default 24-minute day without a cycle.</summary>
        public static float HoursThisFrame => (Active != null ? Active.HoursPerSecond : 1f / 60f) * Time.deltaTime;

        /// <summary>The clock as 06:30.</summary>
        public string ClockText
        {
            get
            {
                int minutes = Mathf.FloorToInt(Mathf.Repeat(hour, 24f) * 60f);
                return $"{minutes / 60:00}:{minutes % 60:00}";
            }
        }

        /// <summary>The light at noon, for things that want to see the island in plain daylight (the map).</summary>
        public Quaternion NoonRotation => _sceneRotation;

        /// <summary>Runs the clock forward (never back) to <paramref name="target"/> over some seconds, then on at its usual pace.</summary>
        public void AdvanceTo(float target, float seconds)
        {
            target = Mathf.Repeat(target, 24f);
            float hours = Mathf.Repeat(target - hour, 24f);
            if (seconds <= 0f || hours <= 0f)
            {
                hour = target;
                _target = -1f;
                return;
            }
            _target = target;
            _advanceRate = hours / seconds;
        }

        /// <summary>Straight to an hour, for the developer panel.</summary>
        public void SetHour(float value)
        {
            hour = Mathf.Repeat(value, 24f);
            _target = -1f;
        }

        void OnEnable()
        {
            Active = this;
            BuildNightSky();
            _sun = StormWeather.FindSun();
            if (_sun == null)
            {
                Debug.LogWarning("[DayNightCycle] No directional light to be the sun — run WoW Sandbox → " +
                                 "Setup Sky and Sun. The time of day will only tint the sky.", this);
                return;
            }

            // The scene's sun is noon: the day turns about it, so midday looks as it was set up.
            _sceneRotation = _sun.transform.rotation;
            Vector3 toSun = -_sun.transform.forward;
            if (toSun.y < 0.2f)
                toSun = new Vector3(toSun.x, 0.75f, toSun.z);
            _noon = toSun.normalized;
            var flat = new Vector3(_noon.x, 0f, _noon.z);
            if (flat.sqrMagnitude < 0.0001f)
                flat = Vector3.back;
            // The sun is due south at noon in the north; it rose a quarter turn left of that.
            _east = Vector3.Cross(flat.normalized, Vector3.up);
        }

        void Start()
        {
            // Washed up: the storm night, held until you wake.
            hour = ShipwreckWake.Lying ? wreckHour : startHour;
        }

        void OnDisable()
        {
            if (Active == this)
                Active = null;
            if (_sun != null)
                _sun.transform.rotation = _sceneRotation;
            Shader.SetGlobalColor(TintId, Color.clear);
            if (_nightSky != null)
                Destroy(_nightSky.gameObject);
            if (_nightSkyMaterial != null)
                Destroy(_nightSkyMaterial);
        }

        void Update()
        {
            if (_target >= 0f)
            {
                float step = _advanceRate * Time.deltaTime;
                float left = Mathf.Repeat(_target - hour, 24f);
                if (step >= left)
                {
                    hour = _target;
                    _target = -1f;
                }
                else
                    hour += step;
            }
            else
                hour += HoursPerSecond * Time.deltaTime;
            hour = Mathf.Repeat(hour, 24f);

            Vector3 sun = SunDirection(hour);
            if (_sun != null)
            {
                // One light: the sun by day, the moon (opposite it) by night. They swap at the
                // horizon, where Evaluate has both at zero, so the turn is never seen.
                Vector3 toLight = sun.y >= 0f ? sun : -sun;
                _sun.transform.rotation = Quaternion.LookRotation(-toLight, Vector3.up);
            }

            // Alpha 1 says the tint is live; a scene without the cycle leaves it at zero and the
            // shaders treat that as white.
            Color tint = TintAt(sun.y);
            tint.a = 1f;
            Shader.SetGlobalColor(TintId, tint);
        }

        /// <summary>
        /// The clear-weather sky at this hour, from the scene's own (its noon). StormWeather
        /// blends its storm from this.
        /// </summary>
        public StormWeather.ClearSky Evaluate(StormWeather.ClearSky scene)
        {
            float s = SunHeight;
            float day = DaylightAt(s);
            Color tint = TintAt(s);

            var sky = scene;
            if (s >= 0f)
            {
                // Low sun: weak and orange; up by mid-morning it's the scene's own.
                sky.sunIntensity = scene.sunIntensity * Smooth(0f, 0.15f, s);
                sky.sunColor = Color.Lerp(duskSunColor, scene.sunColor, Smooth(0.02f, 0.35f, s));
            }
            else
            {
                sky.sunIntensity = scene.sunIntensity * moonlight * NightBrightness * Smooth(0f, 0.15f, -s);
                sky.sunColor = moonColor;
                sky.sunShadowStrength = scene.sunShadowStrength * 0.6f;
            }

            sky.ambient = Scale(scene.ambient, tint);
            // The sky reflection can't be tinted, only dimmed: by the tint's brightness.
            sky.reflections = scene.reflections * tint.grayscale;
            sky.fogColor = scene.fogColor * tint;
            // The procedural sky draws its own sunrise and sunset from where the light is. Once
            // the sun's down (the light has become the moon) it's recoloured as well as dimmed,
            // or its daytime horizon shows as a warm glow; and the half below the horizon
            // goes to the night fog, so the sea fades into it rather than into brown.
            float night = Smooth(0f, 0.15f, -s);
            sky.skyExposure = scene.skyExposure * Mathf.Lerp(nightSkyExposure * NightBrightness, 1f, day);
            sky.skyTint = Color.Lerp(scene.skyTint, nightSkyTint, night);
            sky.skyAtmosphere = Mathf.Lerp(scene.skyAtmosphere, nightAtmosphere, night);
            sky.skyGround = Color.Lerp(scene.skyGround * tint, sky.fogColor, night);
            // The sky's sun disc would sit on the moon as a second, dimmer one.
            sky.skySunSize = Mathf.Lerp(scene.skySunSize, 0f, night);
            sky.tint = tint;
            return sky;
        }

        void BuildNightSky()
        {
            var shader = Shader.Find("WowSandbox/NightSky");
            if (shader == null)
            {
                Debug.LogWarning("[DayNightCycle] WowSandbox/NightSky shader not found — no stars or moon.", this);
                return;
            }

            _nightSkyMaterial = new Material(shader) { name = "NightSky", hideFlags = HideFlags.DontSave };
            _nightSky = StormWeather.BuildSkySphere("NightSky", _nightSkyMaterial);
        }

        void LateUpdate()
        {
            // After the camera has moved, like StormWeather's sky spheres.
            if (_nightSky == null)
                return;
            if (_camera == null)
                _camera = Camera.main;
            if (_camera == null)
                return;

            Vector3 cameraPosition = _camera.transform.position;
            float s = SunHeight;
            float stars = Smooth(0.02f, starsOutBy, -s);
            float moon = Smooth(0f, 0.08f, -s);
            // No sky to see from under the sea.
            bool show = stars + moon > 0.001f && WaterVolume.Containing(cameraPosition) == null;
            _nightSky.gameObject.SetActive(show);
            if (!show)
                return;

            // Same radius as StormWeather's spheres; the queue decides which draws over which.
            _nightSky.position = cameraPosition;
            _nightSky.localScale = Vector3.one * (_camera.farClipPlane * 0.9f * 2f);

            // The stars wheel round the same axis as the sun, a turn a day.
            Vector3 pole = Vector3.Cross(_east, _noon).normalized;
            var turn = Quaternion.Inverse(Quaternion.AngleAxis(hour / 24f * 360f, pole));
            _nightSkyMaterial.SetMatrix(StarRotationId, Matrix4x4.Rotate(turn));
            _nightSkyMaterial.SetVector(MoonDirectionId, -SunDirection(hour));
            _nightSkyMaterial.SetColor(MoonColorId, moonDiscColor);
            _nightSkyMaterial.SetFloat(StarVisibilityId, stars);
            _nightSkyMaterial.SetFloat(MoonVisibilityId, moon);
        }

        /// <summary>Toward the sun at <paramref name="atHour"/>: on the eastern horizon at sunrise, at the scene's noon at midday.</summary>
        Vector3 SunDirection(float atHour)
        {
            float angle = (atHour - Sunrise) / (Sunset - Sunrise) * Mathf.PI;
            return (_east * Mathf.Cos(angle) + _noon * Mathf.Sin(angle)).normalized;
        }

        float DaylightAt(float s) => Smooth(-0.12f, 0.2f, s);

        Color TintAt(float s)
        {
            // Warmest with the sun just on the horizon; gone a little way up, and once it's
            // well down.
            float dusk = s >= 0f ? 1f - Smooth(0.02f, 0.3f, s) : Smooth(-0.14f, 0f, s);
            return Color.Lerp(nightTint * NightBrightness, Color.white, DaylightAt(s)) * Color.Lerp(Color.white, duskTint, dusk);
        }

        /// <summary>
        /// How much of the night colours above the night really gets: 10%, which is properly dark
        /// once the sky's reflection is dimmed too. The Developer panel's Night brightness
        /// overrides it.
        /// </summary>
        public const float DefaultNightBrightness = 0.1f;

        static float NightBrightness => DevOptions.Enabled ? DevOptions.NightBrightness : DefaultNightBrightness;

        static float Smooth(float from, float to, float value) =>
            Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(from, to, value));

        /// <summary>The ambient probe with each colour channel scaled — the probe is what lighting reads.</summary>
        static SphericalHarmonicsL2 Scale(SphericalHarmonicsL2 probe, Color by)
        {
            for (int coefficient = 0; coefficient < 9; coefficient++)
            {
                probe[0, coefficient] *= by.r;
                probe[1, coefficient] *= by.g;
                probe[2, coefficient] *= by.b;
            }
            return probe;
        }
    }
}
