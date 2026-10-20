using UnityEngine;

namespace WowSandbox
{
    /// <summary>
    /// One storm, one dial. <see cref="intensity"/> runs 0 (clear) to 1 (full storm) and
    /// everything follows it: the waves, the clouds darkening, the light failing, the fog
    /// closing in, rain, wind and lightning. Each effect has its own onset window on that
    /// dial, so a storm arrives the way they do — the sky goes first, then the rain, and the
    /// lightning only once it's properly rough.
    ///
    /// Play mode only. Everything it touches (fog, skybox, sun, ambient, shader globals) is
    /// captured on enable and put back on disable, so the scene's own clear-weather setup
    /// — the one Setup Sky and Sun and Set View Distance build — is never overwritten.
    ///
    /// It also owns the above-water fog while it's active. UnderwaterEffect asks it for that
    /// fog rather than using its own snapshot, or surfacing mid-storm would snap the
    /// visibility back to a clear day — the coupled-pair bug again.
    /// </summary>
    // Before WaterWaves (-100), so the waves see this frame's intensity.
    [DefaultExecutionOrder(-200)]
    [DisallowMultipleComponent]
    public class StormWeather : MonoBehaviour
    {
        /// <summary>The active storm, or null. There's one sky, so there's one weather.</summary>
        public static StormWeather Active { get; private set; }

        public struct FogState
        {
            public bool enabled;
            public FogMode mode;
            public Color color;
            public float density;
            public float start;
            public float end;
        }

        /// <summary>
        /// Fine weather's sun, sky, ambient and fog colour — what the storm blends from. The
        /// scene's own as Play starts; DayNightCycle turns that into this hour's.
        /// </summary>
        public struct ClearSky
        {
            public Color sunColor;
            public float sunIntensity;
            public float sunShadowStrength;
            public UnityEngine.Rendering.SphericalHarmonicsL2 ambient;
            public Color skyTint;
            public Color skyGround;
            public float skyExposure;
            public float skyAtmosphere;
            public float skySunSize;
            public Color fogColor;
            /// <summary>DayNightCycle.Tint: what the storm's own colours are multiplied by, so a storm at night is dark.</summary>
            public Color tint;
        }

        [Header("Storm")]
        [Range(0f, 1f)] public float intensity;

        [Tooltip("On entering Play mode, run the storm cycle: build from Start intensity to a " +
                 "full storm, hold it, then clear back to fine weather. Off = Play uses " +
                 "Intensity as set above.")]
        public bool buildsOnPlay = true;
        [Range(0f, 1f)] public float startIntensity = 0.1f;
        [Min(0f)] public float buildUpSeconds = 60f;
        [Tooltip("How long the storm stays at full strength before it starts to clear.")]
        [Min(0f)] public float fullStormSeconds = 120f;
        [Tooltip("Seconds from full storm back to clear weather.")]
        [Min(0f)] public float clearingSeconds = 60f;

        [Header("Wind")]
        [Tooltip("Degrees clockwise from +Z. Pushed to the waves on start, and slants the rain.")]
        public float windDirection = 30f;
        [Tooltip("Horizontal wind speed at full storm, world units per second.")]
        public float stormWindSpeed = 9f;
        [Tooltip("How much faster the clouds scroll at full storm.")]
        public float stormCloudSpeed = 6f;

        [Header("Clouds and light")]
        public Color overcastColor = new Color(0.20f, 0.22f, 0.25f);
        [Tooltip("Intensity over which the sky goes from clear to fully overcast.")]
        public Vector2 overcastOnset = new Vector2(0f, 0.6f);
        [Range(0f, 1f)] public float stormSunlight = 0.12f;
        [Range(0f, 1f)] public float stormAmbient = 0.45f;
        [Tooltip("Brightness of the overcast laid over the sky, relative to the clouds' storm " +
                 "colour. Below 1 the sky reads a shade darker than the clouds in front of it.")]
        [Range(0.3f, 1.5f)] public float overcastSkyBrightness = 0.85f;
        public Color stormSunColor = new Color(0.70f, 0.76f, 0.85f);
        [Tooltip("Procedural sky atmosphere thickness at full overcast. Keep it low — a thick " +
                 "atmosphere is how that shader draws a sunset, which shows as an orange horizon.")]
        [Range(0f, 5f)] public float stormAtmosphere = 0.35f;

        [Header("Visibility")]
        public Color stormFogColor = new Color(0.28f, 0.31f, 0.34f);
        [Tooltip("Fog fully closed in at this distance at full storm.")]
        public float stormFogEnd = 90f;
        public Vector2 fogOnset = new Vector2(0.1f, 1f);
        [Tooltip("Height above the horizon (as the sine of the view angle) where the horizon " +
                 "fog band has faded out. It hides the strip of clear sky below the cloud dome.")]
        [Range(0.05f, 0.8f)] public float horizonBandHeight = 0.28f;

        [Header("Edge of the world")]
        [Tooltip("In any weather, the fog is fully in by this fraction of the distance to the nearest " +
                 "edge of the sea, so neither the water's edge nor the seabed under it ever shows. " +
                 "Away from the edge the scene's own fog applies.")]
        [Range(0.3f, 1f)] public float edgeFogReach = 0.9f;
        [Tooltip("The fog never closes in nearer than this, even right at the edge.")]
        public float minEdgeFog = 60f;
        [Tooltip("A light haze where the sea meets the sky in clear weather, so the fogged-out sea " +
                 "blends into the horizon instead of ending in a line. 0 = none.")]
        [Range(0f, 1f)] public float clearHorizonHaze = 0.7f;
        [Tooltip("How far up the sky the clear-weather haze reaches (elevation, like Horizon Band Height).")]
        [Range(0.02f, 0.5f)] public float clearHorizonHeight = 0.08f;

        [Header("Rain")]
        public Vector2 rainOnset = new Vector2(0.25f, 0.9f);
        [Tooltip("Drops per second at full storm.")]
        public float maxRainRate = 6000f;
        [Tooltip("Width and depth of the rain box around the camera.")]
        public float rainArea = 40f;
        public float rainHeight = 18f;
        public float rainFallSpeed = 22f;
        public Color rainColor = new Color(0.75f, 0.80f, 0.88f, 0.35f);
        [Tooltip("How carefully drops test for roofs. High raycasts every drop and never leaks " +
                 "through a thin roof; Medium and Low use a cheaper voxel cache that can.")]
        public ParticleSystemCollisionQuality rainCollisionQuality = ParticleSystemCollisionQuality.High;

        [Header("Lightning")]
        [Tooltip("No strikes below this intensity.")]
        [Range(0f, 1f)] public float lightningStart = 0.6f;
        [Tooltip("Seconds between strikes: x when lightning starts, y at full storm.")]
        public Vector2 strikeInterval = new Vector2(14f, 3f);
        [Tooltip("How far from the camera strikes land.")]
        public Vector2 strikeDistance = new Vector2(80f, 350f);
        public float flashIntensity = 3f;
        [Tooltip("How far each flash throws the fog back, 0-1: lightning lights up the distance " +
                 "for a moment, which is when you catch sight of anything out there.")]
        [Range(0f, 1f)] public float flashFogReveal = 1f;
        [Tooltip("Fog end distance at the height of a flash.")]
        public float flashRevealDistance = 450f;
        [Tooltip("Seconds for the fog to close back in after a flash.")]
        public float revealFadeSeconds = 0.6f;
        [Tooltip("Share of strikes that land near StrikeFocus while it's set.")]
        [Range(0f, 1f)] public float focusedStrikeChance = 0.6f;

        float _transitionTarget = -1f;
        float _transitionRate;

        enum CyclePhase { None, Building, Holding, Clearing }
        CyclePhase _phase;
        float _holdUntil;

        // Captured scene state.
        FogState _sceneFog;
        ClearSky _scene;
        // The player's clear-weather fog (GameSettings), in place of the scene's while set.
        bool _hasClearFog;
        float _clearFogStart, _clearFogEnd;
        Material _sceneSky;
        Light _sun;

        // Runtime state.
        ClearSky _clear;        // this hour's fine weather: the scene's, through DayNightCycle
        Material _sky;
        float _cloudOffset;
        WaterWaves _waves;
        Camera _camera;
        FogState _aboveWaterFog;

        Transform _horizonBand;
        Material _horizonMaterial;
        Transform _overcastSky;
        Material _overcastMaterial;

        ParticleSystem _rain;
        Material _rainMaterial;
        WaterVolume _water;
        Rect _sea;              // XZ area all the water covers — its edge is the edge of the world
        bool _hasSea;
        Texture2D _rainTexture;

        Light _flashLight;
        LineRenderer _bolt;
        LineRenderer _branch;
        Material _boltMaterial;
        float _nextStrike;
        float _strikeTime = -100f;
        float[] _pulses = new float[0];
        float _reveal;

        static readonly int OvercastColorId = Shader.PropertyToID("_WeatherOvercastColor");
        static readonly int OvercastId = Shader.PropertyToID("_WeatherOvercast");
        static readonly int FlashId = Shader.PropertyToID("_WeatherFlash");
        static readonly int CloudOffsetId = Shader.PropertyToID("_WeatherCloudOffset");
        static readonly int DayNightTintId = Shader.PropertyToID("_DayNightTint");

        /// <summary>
        /// The fog the world should have above water right now. UnderwaterEffect blends from
        /// this when you dive and restores it when you surface.
        /// </summary>
        public FogState AboveWaterFog => _aboveWaterFog;

        /// <summary>How heavy the rain is right now, 0..1 — what StormAudio sets its volume by.</summary>
        public float RainAmount => Ramp(intensity, rainOnset);

        /// <summary>A strike: where it hit the ground, and how far that is from the camera.</summary>
        public event System.Action<Vector3, float> LightningStruck;

        /// <summary>Somewhere strikes should favour — the leviathan while it looms. Null for anywhere.</summary>
        public Vector3? StrikeFocus { get; set; }

        /// <summary>Eases intensity to a value over some seconds.</summary>
        public void TransitionTo(float target, float seconds)
        {
            target = Mathf.Clamp01(target);
            if (seconds <= 0f)
            {
                intensity = target;
                _transitionTarget = -1f;
                return;
            }

            _transitionTarget = target;
            _transitionRate = Mathf.Abs(target - intensity) / seconds;
        }

        // The manual controls take over from the automatic cycle, so it doesn't yank the
        // weather back the moment you've set it.
        [ContextMenu("Roll In Storm (20s)")]
        public void RollInStorm()
        {
            _phase = CyclePhase.None;
            TransitionTo(1f, 20f);
        }

        [ContextMenu("Clear Up (20s)")]
        public void ClearUp()
        {
            _phase = CyclePhase.None;
            TransitionTo(0f, 20f);
        }

        [ContextMenu("Strike Lightning Now")]
        public void StrikeNow() => Strike();

        /// <summary>Runs one full storm cycle from wherever the weather is now: build, hold, clear.</summary>
        public void StartStormCycle()
        {
            TransitionTo(1f, buildUpSeconds * (1f - intensity));
            _phase = CyclePhase.Building;
        }

        /// <summary>
        /// The wreck's storm starts dying down (the player has woken on the beach): the cycle's
        /// clearing stage from here, then fine weather.
        /// </summary>
        public void ClearAfterWreck()
        {
            if (intensity <= 0f)
                return;
            _phase = CyclePhase.Clearing;
            TransitionTo(0f, clearingSeconds * intensity);
        }

        /// <summary>Fine weather this instant, and no cycle running.</summary>
        public void StopStorm()
        {
            _phase = CyclePhase.None;
            TransitionTo(0f, 0f);
        }

        /// <summary>What the automatic cycle is doing, for the developer panel.</summary>
        public string PhaseName => _phase switch
        {
            CyclePhase.Building => "building",
            CyclePhase.Holding => $"full storm, {Mathf.Max(_holdUntil - Time.time, 0f):0}s left",
            CyclePhase.Clearing => "clearing",
            _ => _transitionTarget >= 0f ? "changing" : "steady",
        };

        void OnEnable()
        {
            Active = this;
            _camera = Camera.main;

            CaptureScene();

            _waves = FindFirstObjectByType<WaterWaves>();
            if (_waves != null)
            {
                // Before WaterWaves.Start, so its own build-up stands down in favour of ours.
                _waves.drivenByWeather = true;
                _waves.windDirection = windDirection;
            }

            BuildOvercastSky();
            BuildHorizonBand();
            BuildRain();
            BuildLightning();

            // Sound comes with the storm. Added here if it isn't on the object already, so an
            // existing StormWeather gets thunder without anyone re-adding anything; add Storm
            // Audio yourself in the editor to keep tuned volumes.
            if (GetComponent<StormAudio>() == null)
                gameObject.AddComponent<StormAudio>();
            // Likewise the leviathan at the storm's peak; it stands down if there's no leviathan.
            if (GetComponent<StormLeviathan>() == null)
                gameObject.AddComponent<StormLeviathan>();
            // And the time of day, which tells this what fine weather looks like at this hour.
            if (GetComponent<DayNightCycle>() == null)
                gameObject.AddComponent<DayNightCycle>();
            _aboveWaterFog = _sceneFog;
        }

        void Start()
        {
            // The developer panel's saved storm settings, if any.
            DevOptions.Configure(this);
            // Washed up on the beach: the storm that wrecked you, at its height, until you wake
            // (ShipwreckWake calls ClearAfterWreck). Otherwise fine weather behind the title
            // screen; it calls BeginPlay when you start.
            if (ShipwreckWake.Lying && buildsOnPlay)
                TransitionTo(1f, 0f);
            else if (TitleScreen.Showing)
                intensity = 0f;
            else
                BeginPlay();

            ScheduleNextStrike();
        }

        /// <summary>Starts Play's storm cycle, if it builds on Play — as Play starts, or once the title screen goes.</summary>
        public void BeginPlay()
        {
            // The wreck's storm is already raging; waking up clears it instead.
            if (!buildsOnPlay || ShipwreckWake.Lying)
                return;
            intensity = startIntensity;
            TransitionTo(1f, buildUpSeconds);
            _phase = CyclePhase.Building;
        }

        void OnDisable()
        {
            if (Active == this)
                Active = null;

            if (_waves != null)
                _waves.drivenByWeather = false;

            RestoreScene();

            Shader.SetGlobalFloat(OvercastId, 0f);
            Shader.SetGlobalFloat(FlashId, 0f);
            Shader.SetGlobalFloat(CloudOffsetId, 0f);

            DestroyRuntime(_horizonBand != null ? _horizonBand.gameObject : null);
            DestroyRuntime(_horizonMaterial);
            DestroyRuntime(_overcastSky != null ? _overcastSky.gameObject : null);
            DestroyRuntime(_overcastMaterial);
            DestroyRuntime(_rain != null ? _rain.gameObject : null);
            DestroyRuntime(_flashLight != null ? _flashLight.gameObject : null);
            DestroyRuntime(_bolt != null ? _bolt.gameObject : null);
            DestroyRuntime(_rainMaterial);
            DestroyRuntime(_rainTexture);
            DestroyRuntime(_boltMaterial);
            DestroyRuntime(_sky);
        }

        void Update()
        {
            if (_transitionTarget >= 0f)
            {
                intensity = Mathf.MoveTowards(intensity, _transitionTarget, _transitionRate * Time.deltaTime);
                if (Mathf.Approximately(intensity, _transitionTarget))
                    _transitionTarget = -1f;
            }

            UpdateCycle();

            if (_camera == null)
                _camera = Camera.main;

            if (_waves != null)
                _waves.stormIntensity = intensity;

            _clear = DayNightCycle.Active != null ? DayNightCycle.Active.Evaluate(_scene) : _scene;

            float flash = UpdateLightning();
            // The flash itself is a few milliseconds of pulses; the glimpse of the distance it
            // gives lingers a little longer, or you'd never register what you saw.
            _reveal = Mathf.Max(flash, _reveal - Time.deltaTime / Mathf.Max(revealFadeSeconds, 0.01f));
            UpdateSkyAndLight(flash);
            UpdateFog(flash);
        }

        void LateUpdate()
        {
            // After the camera has moved this frame, so the rain box and the horizon band
            // don't trail it.
            UpdateRain();
            UpdateHorizonBand();
            UpdateOvercastSky();
        }

        /// <summary>Build, hold at full strength, then clear: one storm, then fine weather.</summary>
        void UpdateCycle()
        {
            bool settled = _transitionTarget < 0f;

            switch (_phase)
            {
                case CyclePhase.Building when settled:
                    _phase = CyclePhase.Holding;
                    _holdUntil = Time.time + fullStormSeconds;
                    break;

                case CyclePhase.Holding when Time.time >= _holdUntil:
                    _phase = CyclePhase.Clearing;
                    TransitionTo(0f, clearingSeconds);
                    break;

                case CyclePhase.Clearing when settled:
                    _phase = CyclePhase.None;
                    break;
            }
        }

        // --- Sky, sun, ambient ----------------------------------------------------------

        void UpdateSkyAndLight(float flash)
        {
            float overcast = Ramp(intensity, overcastOnset);
            float wind = Ramp(intensity, rainOnset);

            Shader.SetGlobalColor(OvercastColorId, overcastColor);
            Shader.SetGlobalFloat(OvercastId, overcast);
            Shader.SetGlobalFloat(FlashId, flash);

            // Accumulated, not multiplied — see _WeatherCloudOffset in SkyDome.shader.
            _cloudOffset += (Mathf.Lerp(1f, stormCloudSpeed, wind) - 1f) * Time.deltaTime;
            Shader.SetGlobalFloat(CloudOffsetId, _cloudOffset);

            if (_sky != null)
            {
                SetIfPresent(_sky, "_SkyTint", Color.Lerp(_clear.skyTint, overcastColor, overcast));
                SetIfPresent(_sky, "_Exposure", Mathf.Lerp(_clear.skyExposure, _clear.skyExposure * 0.35f, overcast) + flash);
                // Thinner, not thicker: in Skybox/Procedural a thick atmosphere is what makes a
                // sunset, so thickening it for the storm put an orange band on the horizon.
                SetIfPresent(_sky, "_AtmosphereThickness", Mathf.Lerp(_clear.skyAtmosphere, stormAtmosphere, overcast));
                // The lower half of the sky shows past the terrain's edge; the default is a
                // warm brown that glows against storm grey.
                SetIfPresent(_sky, "_GroundColor", Color.Lerp(_clear.skyGround, stormFogColor * _clear.tint, overcast));
                // No sun disc through storm cloud.
                SetIfPresent(_sky, "_SunSize", Mathf.Lerp(_clear.skySunSize, 0f, overcast));
            }

            if (_sun != null)
            {
                _sun.intensity = Mathf.Lerp(_clear.sunIntensity, _clear.sunIntensity * stormSunlight, overcast);
                _sun.color = Color.Lerp(_clear.sunColor, stormSunColor, overcast);
                // Overcast light is diffuse, so its shadows go soft and faint.
                _sun.shadowStrength = Mathf.Lerp(_clear.sunShadowStrength, 0.2f, overcast);
            }

            // Scale the ambient probe itself. With ambient sourced from the skybox, the probe
            // is baked from the sky once and RenderSettings.ambientIntensity isn't reliably applied to it
            // at runtime — the probe is what lighting actually reads.
            RenderSettings.ambientProbe =
                _clear.ambient * (Mathf.Lerp(1f, stormAmbient, overcast) + flash * 1.2f);
        }

        // --- Fog -------------------------------------------------------------------------

        void UpdateFog(float flash)
        {
            float closing = Ramp(intensity, fogOnset);

            // The player's clear-day distances, else the scene's own, or the far plane if it
            // had no fog.
            bool sceneLinear = _sceneFog.enabled && _sceneFog.mode == FogMode.Linear;
            float clearEnd = _hasClearFog ? _clearFogEnd
                : sceneLinear ? _sceneFog.end
                : (_camera != null ? _camera.farClipPlane : 600f);
            float clearStart = _hasClearFog ? _clearFogStart
                : sceneLinear ? _sceneFog.start
                : clearEnd * 0.35f;

            // Near the edge of the sea, the fog comes in so the edge is always behind it.
            bool edgeFog = false;
            float edgeCap = float.MaxValue;
            if (_camera != null && SeaEdgeDistance(_camera.transform.position, out float edge))
            {
                float edgeEnd = Mathf.Max(edge * edgeFogReach, minEdgeFog);
                edgeCap = edgeEnd;
                if (edgeEnd < clearEnd || !_sceneFog.enabled)
                {
                    clearEnd = Mathf.Min(clearEnd, edgeEnd);
                    clearStart = Mathf.Min(clearStart, clearEnd * 0.3f);
                    edgeFog = true;
                }
            }

            var fog = new FogState
            {
                enabled = _sceneFog.enabled || _hasClearFog || closing > 0f || edgeFog,
                mode = FogMode.Linear,
                color = Color.Lerp(_clear.fogColor, stormFogColor * _clear.tint, closing) + Color.white * (flash * 0.25f),
                density = _sceneFog.density,
                start = Mathf.Lerp(clearStart, 0f, closing),
                end = Mathf.Lerp(clearEnd, stormFogEnd, closing),
            };

            float reveal = _reveal * flashFogReveal;
            fog.start = Mathf.Lerp(fog.start, clearStart, reveal);
            // Never so far that a flash shows the edge of the world.
            fog.end = Mathf.Lerp(fog.end, Mathf.Min(Mathf.Max(clearEnd, flashRevealDistance), edgeCap), reveal);

            _aboveWaterFog = fog;

            // UnderwaterEffect overrides this in LateUpdate while the camera is submerged.
            Apply(fog);
        }

        /// <summary>
        /// The fine-weather fog distances to use instead of the scene's (GameSettings' fog
        /// density). Storms and the edge of the sea still close in on top. Leaving Play puts
        /// the scene's own back, as for everything else this changes.
        /// </summary>
        public void SetClearFog(float start, float end)
        {
            _hasClearFog = true;
            _clearFogStart = start;
            _clearFogEnd = end;
        }

        public static void Apply(FogState fog)
        {
            RenderSettings.fog = fog.enabled;
            RenderSettings.fogMode = fog.mode;
            RenderSettings.fogColor = fog.color;
            RenderSettings.fogDensity = fog.density;
            RenderSettings.fogStartDistance = fog.start;
            RenderSettings.fogEndDistance = fog.end;
        }

        public static FogState CaptureFog() => new FogState
        {
            enabled = RenderSettings.fog,
            mode = RenderSettings.fogMode,
            color = RenderSettings.fogColor,
            density = RenderSettings.fogDensity,
            start = RenderSettings.fogStartDistance,
            end = RenderSettings.fogEndDistance,
        };

        // --- Horizon band ----------------------------------------------------------------

        /// <summary>A camera-pinned, inside-out sphere carrying one of the sky overlays.</summary>
        internal static Transform BuildSkySphere(string name, Material material)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = name;
            go.hideFlags = HideFlags.DontSave;
            Destroy(go.GetComponent<Collider>());

            var renderer = go.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            return go.transform;
        }

        void BuildOvercastSky()
        {
            var shader = Shader.Find("WowSandbox/OvercastSky");
            if (shader == null)
            {
                Debug.LogWarning("[StormWeather] WowSandbox/OvercastSky shader not found — the sky will " +
                                 "stay blue behind the storm clouds.");
                return;
            }

            _overcastMaterial = new Material(shader) { name = "OvercastSky", hideFlags = HideFlags.DontSave };
            _overcastSky = BuildSkySphere("StormOvercastSky", _overcastMaterial);
        }

        void BuildHorizonBand()
        {
            var shader = Shader.Find("WowSandbox/HorizonFog");
            if (shader == null)
            {
                Debug.LogWarning("[StormWeather] WowSandbox/HorizonFog shader not found — the horizon " +
                                 "will stay bright under storm fog.");
                return;
            }

            _horizonMaterial = new Material(shader) { name = "HorizonFog", hideFlags = HideFlags.DontSave };
            _horizonBand = BuildSkySphere("StormHorizonFog", _horizonMaterial);
        }

        void UpdateHorizonBand()
        {
            if (_horizonBand == null || _camera == null)
                return;

            // Just inside the far clip: everything beyond the storm fog's end distance is
            // already fog-coloured, so the band and the terrain meet with no visible seam.
            // CreatePrimitive's sphere has radius 0.5, hence the doubling.
            float radius = _camera.farClipPlane * 0.9f;
            _horizonBand.position = _camera.transform.position;
            _horizonBand.localScale = Vector3.one * (radius * 2f);

            // The storm's tall grey band, or in fine weather a low haze along the horizon.
            float closing = Ramp(intensity, fogOnset);
            float strength = Mathf.Max(closing, clearHorizonHaze);
            _horizonMaterial.SetFloat("_Strength", strength);
            _horizonMaterial.SetFloat("_FadeEnd", Mathf.Lerp(clearHorizonHeight, horizonBandHeight, closing));
            _horizonBand.gameObject.SetActive(strength > 0.001f);
        }

        void UpdateOvercastSky()
        {
            if (_overcastSky == null || _camera == null)
                return;

            // Same radius as the horizon band, for the same reason.
            float radius = _camera.farClipPlane * 0.9f;
            _overcastSky.position = _camera.transform.position;
            _overcastSky.localScale = Vector3.one * (radius * 2f);

            // Its strength is the _WeatherOvercast global the clouds use, so the two can't
            // drift apart; this only decides whether to draw it at all.
            _overcastMaterial.SetFloat("_Brightness", overcastSkyBrightness);
            _overcastSky.gameObject.SetActive(Ramp(intensity, overcastOnset) > 0.001f);
        }

        // --- Rain ------------------------------------------------------------------------

        void BuildRain()
        {
            var shader = Shader.Find("WowSandbox/WeatherFX");
            if (shader == null)
            {
                Debug.LogWarning("[StormWeather] WowSandbox/WeatherFX shader not found — no rain or bolts.");
                return;
            }

            _rainTexture = BuildDropTexture();
            _rainMaterial = new Material(shader) { name = "StormRain", hideFlags = HideFlags.DontSave };
            _rainMaterial.SetTexture("_BaseMap", _rainTexture);
            // A material made in code doesn't get its Toggle keywords synced the way the
            // inspector would, whatever the property's default says.
            _rainMaterial.SetFloat("_ApplyFog", 1f);
            _rainMaterial.EnableKeyword("_APPLY_FOG");

            var go = new GameObject("StormRain") { hideFlags = HideFlags.DontSave };
            _rain = go.AddComponent<ParticleSystem>();
            _rain.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var main = _rain.main;
            main.loop = true;
            main.duration = 5f;
            main.playOnAwake = false;
            main.startSpeed = 0f;
            main.startSize = 0.035f;
            main.startColor = rainColor;
            // Headroom for long-lived drops when you're high above the water (see UpdateRain).
            main.maxParticles = Mathf.CeilToInt(maxRainRate * 3f);
            // World space: drops already falling stay put when you move, rather than the
            // whole sheet of rain sliding along with the camera.
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startLifetime = (rainHeight + 6f) / rainFallSpeed;

            var emission = _rain.emission;
            emission.rateOverTime = 0f;

            var shape = _rain.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(rainArea, 1f, rainArea);

            var velocity = _rain.velocityOverLifetime;
            velocity.enabled = true;
            velocity.space = ParticleSystemSimulationSpace.World;
            // All three axes must use the same curve mode or Unity refuses them.
            velocity.x = new ParticleSystem.MinMaxCurve(0f);
            velocity.y = new ParticleSystem.MinMaxCurve(-rainFallSpeed);
            velocity.z = new ParticleSystem.MinMaxCurve(0f);

            // Drops die on whatever they hit — roofs, the ship's deck, the ground — so it
            // doesn't rain inside the hut or down in the hold. World collision rather than
            // planes: it's the only mode that sees arbitrary geometry, and it includes the
            // ship's colliders as they move. The water has no collider, so drops over the lake
            // are stopped at the surface by their lifetime instead (see UpdateRain).
            var collision = _rain.collision;
            collision.enabled = true;
            collision.type = ParticleSystemCollisionType.World;
            collision.mode = ParticleSystemCollisionMode.Collision3D;
            collision.quality = rainCollisionQuality;
            collision.enableDynamicColliders = true;
            collision.lifetimeLoss = 1f;
            collision.bounce = 0f;
            collision.radiusScale = 0.5f;
            // The player's own capsule is a collider too; a drop that hits your head and
            // stops is exactly right.
            collision.collidesWith = ~0;

            _water = FindFirstObjectByType<WaterVolume>();

            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Stretch;
            // Streak length comes from the fall speed: ~22 m/s * 0.04 ≈ 0.9 m.
            renderer.velocityScale = 0.04f;
            renderer.lengthScale = 1f;
            renderer.sharedMaterial = _rainMaterial;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;

            _rain.Play();
        }

        void UpdateRain()
        {
            if (_rain == null || _camera == null)
                return;

            float rain = Ramp(intensity, rainOnset);
            Vector3 wind = WindVector() * rain;

            Vector3 cameraPosition = _camera.transform.position;
            bool submerged = WaterVolume.Containing(cameraPosition) != null;

            // Live exactly long enough to fall from the spawn height to the water surface, so
            // drops over the lake vanish at it instead of streaking on down past the swimmer.
            // Over land the ground is higher than the water, so they hit it first anyway.
            float spawnY = cameraPosition.y + rainHeight;
            float floorY = _water != null ? _water.SurfaceY : cameraPosition.y - 6f;
            float lifetime = Mathf.Max(spawnY - floorY, 1f) / rainFallSpeed;
            var main = _rain.main;
            main.startLifetime = lifetime;
            // Nothing lights the drops at night but the lightning.
            var dropColor = rainColor * Color.Lerp(_clear.tint, Color.white, 0.15f);
            dropColor.a = rainColor.a;
            main.startColor = dropColor;

            // Spawn upwind, so drops blown sideways over their fall still land around you.
            _rain.transform.position = cameraPosition + Vector3.up * rainHeight - wind * (lifetime * 0.5f);

            var emission = _rain.emission;
            emission.rateOverTime = submerged ? 0f : maxRainRate * rain;

            var velocity = _rain.velocityOverLifetime;
            velocity.x = new ParticleSystem.MinMaxCurve(wind.x);
            velocity.y = new ParticleSystem.MinMaxCurve(-rainFallSpeed);
            velocity.z = new ParticleSystem.MinMaxCurve(wind.z);
        }

        Vector3 WindVector()
        {
            float radians = windDirection * Mathf.Deg2Rad;
            return new Vector3(Mathf.Sin(radians), 0f, Mathf.Cos(radians)) * stormWindSpeed;
        }

        /// <summary>A soft round blob; the stretched-billboard renderer turns it into a streak.</summary>
        static Texture2D BuildDropTexture()
        {
            const int size = 32;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                name = "RainDrop",
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.DontSave
            };

            var pixels = new Color[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = (x + 0.5f) / size * 2f - 1f;
                    float dy = (y + 0.5f) / size * 2f - 1f;
                    float alpha = Mathf.Pow(Mathf.Clamp01(1f - Mathf.Sqrt(dx * dx + dy * dy)), 1.5f);
                    pixels[y * size + x] = new Color(1f, 1f, 1f, alpha);
                }
            }

            texture.SetPixels(pixels);
            texture.Apply();
            return texture;
        }

        // --- Lightning -------------------------------------------------------------------

        void BuildLightning()
        {
            var lightGo = new GameObject("LightningFlash") { hideFlags = HideFlags.DontSave };
            _flashLight = lightGo.AddComponent<Light>();
            _flashLight.type = LightType.Directional;
            _flashLight.color = new Color(0.82f, 0.87f, 1f);
            _flashLight.intensity = 0f;
            _flashLight.shadows = LightShadows.None;
            _flashLight.enabled = false;

            var shader = Shader.Find("WowSandbox/WeatherFX");
            if (shader == null)
                return;

            _boltMaterial = new Material(shader) { name = "LightningBolt", hideFlags = HideFlags.DontSave };
            // Additive and unfogged: a strike shows through rain that hides the hills behind it.
            _boltMaterial.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            _boltMaterial.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.One);
            _boltMaterial.SetFloat("_ApplyFog", 0f);
            _boltMaterial.DisableKeyword("_APPLY_FOG");

            var boltGo = new GameObject("LightningBolt") { hideFlags = HideFlags.DontSave };
            _bolt = MakeBoltLine(boltGo, 2.2f);

            var branchGo = new GameObject("Branch") { hideFlags = HideFlags.DontSave };
            branchGo.transform.SetParent(boltGo.transform, false);
            _branch = MakeBoltLine(branchGo, 1.1f);
        }

        LineRenderer MakeBoltLine(GameObject go, float width)
        {
            var line = go.AddComponent<LineRenderer>();
            line.sharedMaterial = _boltMaterial;
            line.useWorldSpace = true;
            line.widthCurve = new AnimationCurve(new Keyframe(0f, width), new Keyframe(1f, width * 0.35f));
            line.startColor = new Color(0.85f, 0.9f, 1f, 1f);
            line.endColor = new Color(0.85f, 0.9f, 1f, 0.8f);
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            line.receiveShadows = false;
            line.enabled = false;
            return line;
        }

        void ScheduleNextStrike()
        {
            float storm = Mathf.InverseLerp(lightningStart, 1f, intensity);
            float interval = Mathf.Lerp(strikeInterval.x, strikeInterval.y, storm);
            _nextStrike = Time.time + interval * Random.Range(0.5f, 1.5f);
        }

        /// <summary>Returns this frame's flash brightness, 0..1.</summary>
        float UpdateLightning()
        {
            if (intensity >= lightningStart && Time.time >= _nextStrike)
            {
                Strike();
                ScheduleNextStrike();
            }
            else if (intensity < lightningStart)
            {
                // Keep pushing the schedule out, so the first strike after crossing the
                // threshold doesn't fire instantly from a long-expired timer.
                if (Time.time >= _nextStrike)
                    ScheduleNextStrike();
            }

            // A strike is a few quick pulses — return strokes — not one smooth flash.
            float t = Time.time - _strikeTime;
            float flash = 0f;
            foreach (float pulse in _pulses)
            {
                if (t >= pulse)
                    flash += Mathf.Exp(-(t - pulse) * 22f);
            }

            flash = Mathf.Clamp01(flash);

            if (_flashLight != null)
            {
                _flashLight.enabled = flash > 0.01f;
                _flashLight.intensity = flash * flashIntensity;
            }

            bool boltVisible = flash > 0.15f;
            if (_bolt != null)
            {
                _bolt.enabled = boltVisible;
                _branch.enabled = boltVisible;
                var color = new Color(0.85f, 0.9f, 1f, flash);
                _bolt.startColor = _bolt.endColor = color;
                _branch.startColor = _branch.endColor = color * new Color(1f, 1f, 1f, 0.7f);
            }

            return flash;
        }

        void Strike()
        {
            if (_camera == null)
                return;

            _strikeTime = Time.time;
            int count = Random.Range(1, 4);
            _pulses = new float[count];
            for (int i = 0; i < count; i++)
                _pulses[i] = i == 0 ? 0f : _pulses[i - 1] + Random.Range(0.05f, 0.14f);

            Vector3 origin = _camera.transform.position;
            Vector3 ground;
            if (StrikeFocus.HasValue && Random.value < focusedStrikeChance)
            {
                var scatter = Random.insideUnitCircle * 60f;
                ground = StrikeFocus.Value + new Vector3(scatter.x, 0f, scatter.y);
            }
            else
            {
                float angle = Random.Range(0f, Mathf.PI * 2f);
                float distance = Random.Range(strikeDistance.x, strikeDistance.y);
                ground = origin + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * distance;
            }
            ground.y = GroundHeight(ground);
            LightningStruck?.Invoke(ground, Vector3.Distance(origin, ground));
            Vector3 top = new Vector3(ground.x + Random.Range(-30f, 30f), origin.y + 160f,
                                      ground.z + Random.Range(-30f, 30f));

            if (_bolt != null)
            {
                SetJagged(_bolt, top, ground, 16, 9f);
                int fork = Random.Range(4, 9);
                Vector3 forkStart = _bolt.GetPosition(fork);
                Vector3 forkEnd = Vector3.Lerp(forkStart, ground, 0.55f)
                                + new Vector3(Random.Range(-40f, 40f), 0f, Random.Range(-40f, 40f));
                SetJagged(_branch, forkStart, forkEnd, 8, 6f);
            }

            if (_flashLight != null)
            {
                // Light from the strike toward you, steeply downward.
                Vector3 direction = (origin - top).normalized;
                direction.y = Mathf.Min(direction.y, -0.6f);
                _flashLight.transform.rotation = Quaternion.LookRotation(direction.normalized);
            }
        }

        static void SetJagged(LineRenderer line, Vector3 from, Vector3 to, int segments, float jitter)
        {
            line.positionCount = segments + 1;
            for (int i = 0; i <= segments; i++)
            {
                float t = i / (float)segments;
                Vector3 point = Vector3.Lerp(from, to, t);
                // Pinned at both ends, wildest in the middle.
                if (i > 0 && i < segments)
                    point += new Vector3(Random.Range(-jitter, jitter), Random.Range(-jitter, jitter) * 0.3f,
                                         Random.Range(-jitter, jitter));
                line.SetPosition(i, point);
            }
        }

        /// <summary>How far <paramref name="point"/> is from the nearest edge of the sea, flat;
        /// 0 if it's already past it. False if there's no water to have an edge.</summary>
        bool SeaEdgeDistance(Vector3 point, out float distance)
        {
            distance = 0f;
            if (!_hasSea)
            {
                var volumes = FindObjectsByType<WaterVolume>();
                if (volumes.Length == 0)
                    return false;
                _hasSea = true;
                bool first = true;
                foreach (var water in volumes)
                {
                    var centre = new Vector2(water.transform.position.x, water.transform.position.z);
                    var area = new Rect(centre - water.extents, water.extents * 2f);
                    _sea = first ? area : Rect.MinMaxRect(Mathf.Min(_sea.xMin, area.xMin), Mathf.Min(_sea.yMin, area.yMin),
                                                          Mathf.Max(_sea.xMax, area.xMax), Mathf.Max(_sea.yMax, area.yMax));
                    first = false;
                }
            }

            distance = Mathf.Max(0f, Mathf.Min(Mathf.Min(point.x - _sea.xMin, _sea.xMax - point.x),
                                               Mathf.Min(point.z - _sea.yMin, _sea.yMax - point.z)));
            return true;
        }

        static float GroundHeight(Vector3 position)
        {
            float height = float.MinValue;

            var terrain = Terrain.activeTerrain;
            if (terrain != null)
                height = terrain.SampleHeight(position) + terrain.transform.position.y;

            var water = WaterVolume.Containing(new Vector3(position.x, height, position.z));
            if (water != null)
                height = Mathf.Max(height, water.SurfaceY);

            return height == float.MinValue ? 0f : height;
        }

        // --- Capture / restore -----------------------------------------------------------

        void CaptureScene()
        {
            _sceneFog = CaptureFog();
            _scene = new ClearSky
            {
                ambient = RenderSettings.ambientProbe,
                fogColor = _sceneFog.color,
                skyTint = Color.white,
                skyGround = Color.gray,
                skyExposure = 1f,
                skyAtmosphere = 1f,
                tint = Color.white,
            };

            _sun = FindSun();
            if (_sun != null)
            {
                _scene.sunColor = _sun.color;
                _scene.sunIntensity = _sun.intensity;
                _scene.sunShadowStrength = _sun.shadowStrength;
            }

            // Work on a copy of the sky material, so the saved asset is never touched.
            _sceneSky = RenderSettings.skybox;
            if (_sceneSky != null)
            {
                _sky = new Material(_sceneSky) { name = _sceneSky.name + " (Storm)", hideFlags = HideFlags.DontSave };
                if (_sky.HasProperty("_SkyTint")) _scene.skyTint = _sky.GetColor("_SkyTint");
                if (_sky.HasProperty("_GroundColor")) _scene.skyGround = _sky.GetColor("_GroundColor");
                if (_sky.HasProperty("_Exposure")) _scene.skyExposure = _sky.GetFloat("_Exposure");
                if (_sky.HasProperty("_AtmosphereThickness")) _scene.skyAtmosphere = _sky.GetFloat("_AtmosphereThickness");
                if (_sky.HasProperty("_SunSize")) _scene.skySunSize = _sky.GetFloat("_SunSize");
                RenderSettings.skybox = _sky;
            }
            _clear = _scene;
        }

        /// <summary>The scene's sun: the lighting settings' Sun Source, else the first directional light.</summary>
        public static Light FindSun()
        {
            if (RenderSettings.sun != null)
                return RenderSettings.sun;
            foreach (var light in FindObjectsByType<Light>(FindObjectsSortMode.None))
            {
                if (light.type == LightType.Directional)
                    return light;
            }
            return null;
        }

        /// <summary>
        /// Runs <paramref name="render"/> under the scene's own clear noon — no storm, no night —
        /// then puts this moment's light back. For the world map's snapshot, which is taken
        /// once and kept, so it mustn't catch the island at midnight or in a squall.
        /// </summary>
        public static void InDaylight(System.Action render)
        {
            var weather = Active;
            if (weather == null)
            {
                render();
                return;
            }

            var sun = weather._sun;
            var probe = RenderSettings.ambientProbe;
            var tint = Shader.GetGlobalColor(DayNightTintId);
            float overcast = Shader.GetGlobalFloat(OvercastId);
            float flash = Shader.GetGlobalFloat(FlashId);
            Quaternion rotation = default;
            Color color = default;
            float strength = 0f;
            if (sun != null)
            {
                rotation = sun.transform.rotation;
                color = sun.color;
                strength = sun.intensity;
                sun.color = weather._scene.sunColor;
                sun.intensity = weather._scene.sunIntensity;
                if (DayNightCycle.Active != null)
                    sun.transform.rotation = DayNightCycle.Active.NoonRotation;
            }
            RenderSettings.ambientProbe = weather._scene.ambient;
            Shader.SetGlobalColor(DayNightTintId, Color.clear);
            Shader.SetGlobalFloat(OvercastId, 0f);
            Shader.SetGlobalFloat(FlashId, 0f);
            try
            {
                render();
            }
            finally
            {
                if (sun != null)
                {
                    sun.transform.rotation = rotation;
                    sun.color = color;
                    sun.intensity = strength;
                }
                RenderSettings.ambientProbe = probe;
                Shader.SetGlobalColor(DayNightTintId, tint);
                Shader.SetGlobalFloat(OvercastId, overcast);
                Shader.SetGlobalFloat(FlashId, flash);
            }
        }

        void RestoreScene()
        {
            Apply(_sceneFog);
            RenderSettings.ambientProbe = _scene.ambient;

            if (_sun != null)
            {
                _sun.color = _scene.sunColor;
                _sun.intensity = _scene.sunIntensity;
                _sun.shadowStrength = _scene.sunShadowStrength;
            }

            if (_sceneSky != null)
                RenderSettings.skybox = _sceneSky;
        }

        // --- Helpers ---------------------------------------------------------------------

        /// <summary>0 below window.x, 1 above window.y, linear between.</summary>
        static float Ramp(float value, Vector2 window) =>
            Mathf.Clamp01((value - window.x) / Mathf.Max(window.y - window.x, 0.0001f));

        static void SetIfPresent(Material material, string name, float value)
        {
            if (material.HasProperty(name))
                material.SetFloat(name, value);
        }

        static void SetIfPresent(Material material, string name, Color value)
        {
            if (material.HasProperty(name))
                material.SetColor(name, value);
        }

        static void DestroyRuntime(Object target)
        {
            if (target != null)
                Destroy(target);
        }
    }
}
