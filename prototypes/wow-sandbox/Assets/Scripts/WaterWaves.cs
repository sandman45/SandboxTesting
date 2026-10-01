using System;
using UnityEngine;

namespace WowSandbox
{
    /// <summary>
    /// Drives the water's waves: how big, how sharp, how confused, how fast — blended between
    /// a calm and a storm sea state by a single intensity value, so weather can roll in.
    ///
    /// This component is the single source of truth for the wave shape. It pushes the wave
    /// set to the shader every frame AND answers <see cref="HeightAt"/> on the CPU with the
    /// exact same maths, so WaterVolume (swimming, breath, the underwater camera) agrees with
    /// the surface you see. The old shader-only waves had to stay tiny because gameplay
    /// assumed a flat plane; that assumption is gone, which is what makes storm-sized waves
    /// possible at all.
    ///
    /// Displacement is still vertical only — no Gerstner pinching — because a vertical-only
    /// surface can be queried at a point directly. Sharp crests come from reshaping each sine
    /// instead (see <see cref="Sharpness"/>).
    /// </summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(MeshRenderer))]
    // Advance the wave clock before anything samples the surface this frame, so the player's
    // swim check and the rendered waves use the same time.
    [DefaultExecutionOrder(-100)]
    public class WaterWaves : MonoBehaviour
    {
        public const int MaxWaves = 16;
        const float Gravity = 9.81f;

        [Serializable]
        public struct SeaState
        {
            [Tooltip("Largest crest-to-trough height in world units, reached only when every " +
                     "wave lines up. Typical waves are around half of this.")]
            [Min(0f)] public float height;

            [Tooltip("0 = rolling sine swells. 1 = sharp peaks with wide flat troughs.")]
            [Range(0f, 1f)] public float choppiness;

            [Tooltip("0 = every wave travels with the wind (orderly swell). 1 = waves from all " +
                     "directions at once (a confused storm sea).")]
            [Range(0f, 1f)] public float chaos;

            [Tooltip("Multiplier on the physically-based wave speed.")]
            [Range(0f, 3f)] public float speed;

            [Tooltip("How much foam breaks out on the crests.")]
            [Range(0f, 1f)] public float whitecaps;

            [Tooltip("Strength of the small scrolling ripples. Overrides the material's " +
                     "Normal strength while this component is active.")]
            [Range(0f, 2f)] public float ripples;

            public static SeaState Lerp(SeaState a, SeaState b, float t) => new SeaState
            {
                height = Mathf.Lerp(a.height, b.height, t),
                choppiness = Mathf.Lerp(a.choppiness, b.choppiness, t),
                chaos = Mathf.Lerp(a.chaos, b.chaos, t),
                speed = Mathf.Lerp(a.speed, b.speed, t),
                whitecaps = Mathf.Lerp(a.whitecaps, b.whitecaps, t),
                ripples = Mathf.Lerp(a.ripples, b.ripples, t),
            };
        }

        [Header("Weather")]
        [Tooltip("Blends from the calm sea state (0) to the storm sea state (1). Safe to " +
                 "animate — every setting it blends changes smoothly.")]
        [Range(0f, 1f)] public float stormIntensity;

        /// <summary>
        /// Set by StormWeather while it's active: it writes stormIntensity every frame and
        /// owns the build-up, so the three build-up settings below are ignored.
        /// </summary>
        [NonSerialized] public bool drivenByWeather;

        [Tooltip("On entering Play mode, start at Start intensity and build to a full storm " +
                 "over Build-up time. Off = Play uses Storm intensity as set above. Ignored " +
                 "when a StormWeather is in the scene — it runs the build-up instead.")]
        public bool stormBuildsOnPlay = true;

        [Tooltip("Intensity the storm starts at when Play begins.")]
        [Range(0f, 1f)] public float startIntensity = 0.1f;

        [Tooltip("Seconds from Start intensity to a full storm.")]
        [Min(0f)] public float buildUpSeconds = 60f;

        public SeaState calm = new SeaState
        {
            height = 1.5f, choppiness = 0.2f, chaos = 0.2f, speed = 0.9f, whitecaps = 0f, ripples = 0.8f
        };

        public SeaState storm = new SeaState
        {
            height = 5f, choppiness = 0.8f, chaos = 0.7f, speed = 1.3f, whitecaps = 0.7f, ripples = 1.4f
        };

        // The wave set itself. These are structural: changing them reshuffles the waves, so
        // they jump rather than blend. Tune them in the editor, not mid-storm.
        [Header("Wave set (changes jump, don't animate)")]
        [Tooltip("Direction the waves travel, in degrees clockwise from +Z.")]
        public float windDirection = 30f;

        [Tooltip("Wavelength of the biggest swell, in world units.")]
        [Min(0.5f)] public float longestWave = 40f;

        [Tooltip("Wavelength of the smallest wave. Waves too short for the mesh to show are " +
                 "still drawn as lighting detail, just not as moving geometry.")]
        [Min(0.25f)] public float shortestWave = 4f;

        [Range(4, MaxWaves)] public int waveCount = 12;

        public int seed = 1234;

        /// <summary>The blended sea state actually in effect this frame.</summary>
        public SeaState Current { get; private set; }

        // Per wave: xy = direction, z = wavenumber k, w = angular frequency.
        readonly Vector4[] _waveA = new Vector4[MaxWaves];
        // Per wave: x = amplitude, y = phase offset, z = 1 if the mesh is fine enough to
        // displace it, else 0.
        readonly Vector4[] _waveB = new Vector4[MaxWaves];

        // Bank, rebuilt only when the structural settings change.
        readonly float[] _length = new float[MaxWaves];
        readonly float[] _angleOffset = new float[MaxWaves];
        readonly float[] _phase = new float[MaxWaves];
        int _bankHash;

        float _clock;
        float _lastRealtime = -1f;
        float _sharpness = 1f;
        float _mean = 0.5f;
        float _minDisplacedLength;

        float _transitionTarget = -1f;
        float _transitionRate;

        MeshRenderer _renderer;
        MaterialPropertyBlock _block;

        static readonly int WaveAId = Shader.PropertyToID("_WaveA");
        static readonly int WaveBId = Shader.PropertyToID("_WaveB");
        static readonly int WaveCountId = Shader.PropertyToID("_WaveCount");
        static readonly int WaveTimeId = Shader.PropertyToID("_WaveTime");
        static readonly int WaveSharpnessId = Shader.PropertyToID("_WaveSharpness");
        static readonly int WaveMeanId = Shader.PropertyToID("_WaveMean");
        static readonly int WaveHeightId = Shader.PropertyToID("_WaveHeight");
        static readonly int WhitecapsId = Shader.PropertyToID("_Whitecaps");
        static readonly int NormalStrengthId = Shader.PropertyToID("_NormalStrength");

        /// <summary>
        /// Eases <see cref="stormIntensity"/> to <paramref name="intensity"/> over
        /// <paramref name="seconds"/>. Runs in Play mode; in Edit mode it jumps straight there.
        /// </summary>
        public void TransitionTo(float intensity, float seconds)
        {
            intensity = Mathf.Clamp01(intensity);
            if (!Application.isPlaying || seconds <= 0f)
            {
                stormIntensity = intensity;
                _transitionTarget = -1f;
                return;
            }

            _transitionTarget = intensity;
            _transitionRate = Mathf.Abs(intensity - stormIntensity) / seconds;
        }

        [ContextMenu("Roll In Storm (20s)")]
        void RollInStorm() => TransitionTo(1f, 20f);

        [ContextMenu("Calm Down (20s)")]
        void CalmDown() => TransitionTo(0f, 20f);

        /// <summary>
        /// Height of the visible surface above (or below) the transform at this world XZ, right
        /// now. Matches the vertex shader exactly, including leaving out waves the mesh is too
        /// coarse to show — so this is where the drawn surface is, not where an ideal one is.
        /// </summary>
        public float HeightAt(float x, float z)
        {
            float h = 0f;
            int count = Mathf.Min(waveCount, MaxWaves);

            for (int i = 0; i < count; i++)
            {
                Vector4 a = _waveA[i];
                Vector4 b = _waveB[i];
                if (b.z < 0.5f)
                    continue;

                float theta = (a.x * x + a.y * z) * a.z - a.w * _clock + b.y;
                h += 2f * b.x * (Shape(Mathf.Sin(theta)) - _mean);
            }

            return h;
        }

        float Shape(float sine)
        {
            float u = Mathf.Max(sine * 0.5f + 0.5f, 1e-4f);
            return Mathf.Pow(u, _sharpness);
        }

        void OnEnable()
        {
            _renderer = GetComponent<MeshRenderer>();
            _block ??= new MaterialPropertyBlock();
            _bankHash = 0;
            _lastRealtime = -1f;
            Refresh(0f);
        }

        void Start()
        {
            // ExecuteAlways calls Start in Edit mode too; the build-up is a Play-mode thing.
            if (!Application.isPlaying || !stormBuildsOnPlay || drivenByWeather)
                return;

            stormIntensity = startIntensity;
            TransitionTo(1f, buildUpSeconds);
        }

        void OnDisable()
        {
            if (_renderer != null)
            {
                _renderer.SetPropertyBlock(null);
                _renderer.ResetLocalBounds();
            }
        }

        void OnValidate()
        {
            if (shortestWave > longestWave)
                shortestWave = longestWave;

            if (isActiveAndEnabled)
            {
                Refresh(0f);
                UpdateBounds(); // the storm height may have changed without a bank rebuild
            }
        }

        void Update()
        {
            float dt;
            if (Application.isPlaying)
            {
                dt = Time.deltaTime;
            }
            else
            {
                // Edit-mode Update only fires on repaint, so Time.deltaTime means nothing here.
                float now = Time.realtimeSinceStartup;
                dt = _lastRealtime < 0f ? 0f : Mathf.Min(now - _lastRealtime, 0.1f);
                _lastRealtime = now;
            }

            if (_transitionTarget >= 0f)
            {
                stormIntensity = Mathf.MoveTowards(stormIntensity, _transitionTarget, _transitionRate * dt);
                if (Mathf.Approximately(stormIntensity, _transitionTarget))
                    _transitionTarget = -1f;
            }

            Refresh(dt);
        }

        void Refresh(float dt)
        {
            if (_renderer == null)
                return;

            RebuildBankIfChanged();

            var state = SeaState.Lerp(calm, storm, stormIntensity);
            Current = state;

            // A clock that runs at the blended speed, rather than time * speed: scaling the
            // whole elapsed time would make every wave lurch to a new phase the moment the
            // speed changed, which is exactly what a storm transition does continuously.
            _clock += dt * state.speed;

            // Peakiness: 1 is a plain sine, higher raises the crests and flattens the troughs.
            _sharpness = 1f + state.choppiness * 4f;
            _mean = MeanOfShape(_sharpness);

            ComputeAmplitudes(state);
            Upload(state);
        }

        void RebuildBankIfChanged()
        {
            float vertexSpacing = MeshVertexSpacing();
            int hash = HashCode.Combine(windDirection, longestWave, shortestWave, waveCount, seed, vertexSpacing);
            if (hash == _bankHash)
                return;

            _bankHash = hash;

            // A wave needs a few vertices per wavelength to read as geometry; anything shorter
            // aliases into a crawling mess, so it's kept for lighting only.
            _minDisplacedLength = vertexSpacing * 2.5f;

            var random = new System.Random(seed);
            int count = Mathf.Min(waveCount, MaxWaves);

            for (int i = 0; i < count; i++)
            {
                // Geometric spread of wavelengths, longest first.
                float t = count > 1 ? i / (float)(count - 1) : 0f;
                _length[i] = longestWave * Mathf.Pow(shortestWave / longestWave, t);

                // The biggest swell always runs with the wind. The rest scatter, mostly close
                // to it but with a few running across — those are what chaos turns up.
                float spread = (float)random.NextDouble();
                float sign = random.NextDouble() < 0.5 ? -1f : 1f;
                _angleOffset[i] = i == 0 ? 0f : sign * Mathf.Pow(spread, 1.5f) * 170f * Mathf.Deg2Rad;

                _phase[i] = (float)(random.NextDouble() * Math.PI * 2.0);
            }

            UpdateBounds();
        }

        void ComputeAmplitudes(SeaState state)
        {
            int count = Mathf.Min(waveCount, MaxWaves);
            float windRadians = windDirection * Mathf.Deg2Rad;
            float total = 0f;

            for (int i = 0; i < count; i++)
            {
                // With no chaos only waves near the wind survive; full chaos weights every
                // direction equally. Continuous in chaos, so a storm can build smoothly.
                float alignment = Mathf.Pow(Mathf.Max(Mathf.Cos(_angleOffset[i]), 0f), 6f);
                float directional = Mathf.Lerp(alignment, 1f, state.chaos);

                // Longer waves are taller, like real ones — keeps steepness roughly constant.
                float weight = directional * (_length[i] / longestWave);
                _waveB[i].x = weight;
                total += weight;
            }

            // Normalise so the amplitudes sum to half the stated height, whatever chaos does to
            // the weights — chaos changes the character of the sea, not its size.
            float scale = total > 0f ? state.height * 0.5f / total : 0f;

            for (int i = 0; i < count; i++)
            {
                float angle = windRadians + _angleOffset[i];
                float k = 2f * Mathf.PI / _length[i];

                _waveA[i] = new Vector4(Mathf.Sin(angle), Mathf.Cos(angle), k, Mathf.Sqrt(Gravity * k));
                _waveB[i] = new Vector4(
                    _waveB[i].x * scale,
                    _phase[i],
                    _length[i] >= _minDisplacedLength ? 1f : 0f,
                    0f);
            }

            for (int i = count; i < MaxWaves; i++)
            {
                _waveA[i] = Vector4.zero;
                _waveB[i] = Vector4.zero;
            }
        }

        void Upload(SeaState state)
        {
            // OnValidate can run after a script reload before OnEnable has: Unity restores
            // _renderer across the reload (it serializes private fields for that) but not
            // the property block, which isn't serializable.
            _block ??= new MaterialPropertyBlock();
            _renderer.GetPropertyBlock(_block);
            _block.SetVectorArray(WaveAId, _waveA);
            _block.SetVectorArray(WaveBId, _waveB);
            _block.SetFloat(WaveCountId, Mathf.Min(waveCount, MaxWaves));
            _block.SetFloat(WaveTimeId, _clock);
            _block.SetFloat(WaveSharpnessId, _sharpness);
            _block.SetFloat(WaveMeanId, _mean);
            _block.SetFloat(WaveHeightId, state.height);
            _block.SetFloat(WhitecapsId, state.whitecaps);
            _block.SetFloat(NormalStrengthId, state.ripples);
            _renderer.SetPropertyBlock(_block);
        }

        /// <summary>
        /// The mean of the reshaped wave over a full cycle. Subtracting it keeps the average
        /// water level at the transform's height however sharp the crests get — otherwise
        /// choppier water would sit visibly lower, and the swim check with it.
        /// </summary>
        static float MeanOfShape(float sharpness)
        {
            const int samples = 64;
            float sum = 0f;
            for (int i = 0; i < samples; i++)
            {
                float u = Mathf.Sin((i + 0.5f) / samples * 2f * Mathf.PI) * 0.5f + 0.5f;
                sum += Mathf.Pow(Mathf.Max(u, 1e-4f), sharpness);
            }

            return sum / samples;
        }

        float MeshVertexSpacing()
        {
            var filter = GetComponent<MeshFilter>();
            var mesh = filter != null ? filter.sharedMesh : null;
            if (mesh == null)
                return 1f;

            // WaterSetup builds a square grid, so the side count falls out of the vertex count.
            int perSide = Mathf.Max(Mathf.RoundToInt(Mathf.Sqrt(mesh.vertexCount)) - 1, 1);
            return mesh.bounds.size.x / perSide;
        }

        /// <summary>
        /// The mesh is a flat plane with zero-height bounds; the waves lift it well clear of
        /// that. Without widening the bounds, Unity culls the water whenever the flat plane
        /// itself is out of view even though the crests are on screen.
        /// </summary>
        void UpdateBounds()
        {
            var filter = GetComponent<MeshFilter>();
            if (_renderer == null || filter == null || filter.sharedMesh == null)
                return;

            var bounds = filter.sharedMesh.bounds;
            float reach = Mathf.Max(calm.height, storm.height) + 1f;
            bounds.Expand(new Vector3(0f, reach * 2f, 0f));
            _renderer.localBounds = bounds;
        }
    }
}
