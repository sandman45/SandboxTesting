using UnityEngine;

namespace WowSandbox
{
    /// <summary>
    /// The sky's clouds: a raymarched layer (WowSandbox/Clouds) on an inside-out sphere pinned to
    /// the camera. Fine
    /// weather looks like CloudSettings.Current (the Developer panel's Clouds window); as a storm
    /// gathers, the sky blends toward CloudSettings.Storm — and greys over behind the clouds.
    ///
    /// The noise the clouds are shaped from is built here at Play, tileable, so there's no
    /// texture asset. The light on them is the scene's own sun or moon and ambient, read each
    /// frame, so the time of day and the storm reach them without colours of their own.
    ///
    /// Added by StormWeather, like the rest of the weather.
    /// </summary>
    [RequireComponent(typeof(StormWeather))]
    public class CloudLayer : MonoBehaviour
    {
        const int MapSize = 256;
        const int DetailSize = 32;
        // Curves the layer like a planet's so it meets the horizon; and how far off it fades.
        const float PlanetRadius = 20000f;

        public static CloudLayer Active { get; private set; }

        StormWeather _weather;
        Camera _camera;
        Transform _sphere;
        Material _material;
        Texture2D _map;
        Texture3D _detail;
        Vector2 _mapOffset, _detailOffset;
        readonly Vector3[] _ambientDirections = { Vector3.up, Vector3.down };
        readonly Color[] _ambient = new Color[2];

        static readonly int WeatherMapId = Shader.PropertyToID("_WeatherMap");
        static readonly int DetailNoiseId = Shader.PropertyToID("_DetailNoise");
        static readonly int CoverageId = Shader.PropertyToID("_CloudCoverage");
        static readonly int DensityId = Shader.PropertyToID("_CloudDensity");
        static readonly int ScaleId = Shader.PropertyToID("_CloudScale");
        static readonly int HeightId = Shader.PropertyToID("_CloudHeight");
        static readonly int SoftnessId = Shader.PropertyToID("_CloudSoftness");
        static readonly int WindId = Shader.PropertyToID("_CloudWind");
        static readonly int SunDirectionId = Shader.PropertyToID("_CloudSunDirection");
        static readonly int SunColorId = Shader.PropertyToID("_CloudSunColor");
        static readonly int AmbientTopId = Shader.PropertyToID("_CloudAmbientTop");
        static readonly int AmbientBottomId = Shader.PropertyToID("_CloudAmbientBottom");
        static readonly int SunlightId = Shader.PropertyToID("_CloudSunlight");
        static readonly int SilverLiningId = Shader.PropertyToID("_CloudSilverLining");
        static readonly int OvercastBrightnessId = Shader.PropertyToID("_CloudOvercastBrightness");

        /// <summary>What's in the sky this moment: fine weather's clouds, as far toward the storm's as it's come.</summary>
        public CloudSettings Showing { get; private set; }

        /// <summary>
        /// The wind, 0 still to 1 a storm's: the clouds' wind speed against Storm's. The trees, the
        /// grass and the sea answer to this (or the storm's intensity, whichever is more), so the
        /// Clouds window's wind speed blows through them too. 0 without the clouds.
        /// </summary>
        public static float WindStrength =>
            Active != null ? Mathf.Clamp01(Active.Showing.windSpeed / Mathf.Max(CloudSettings.Storm.windSpeed, 0.01f)) : 0f;

        /// <summary>Degrees clockwise from +Z the wind blows toward: the clouds' drift.</summary>
        public static float WindDirection => Active != null ? Active.Showing.windDirection : CloudSettings.Current.windDirection;

        void Start()
        {
            _weather = GetComponent<StormWeather>();
            var shader = Shader.Find("WowSandbox/Clouds");
            if (shader == null)
            {
                Debug.LogWarning("[CloudLayer] WowSandbox/Clouds shader not found — no clouds.", this);
                enabled = false;
                return;
            }

            _map = BuildCoverageMap();
            _detail = BuildDetailNoise();
            Shader.SetGlobalTexture(WeatherMapId, _map);
            Shader.SetGlobalTexture(DetailNoiseId, _detail);

            _material = new Material(shader) { name = "Clouds", hideFlags = HideFlags.DontSave };
            _sphere = StormWeather.BuildSkySphere("CloudSky", _material);
            _sphere.GetComponent<MeshRenderer>().lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            Active = this;
        }

        void OnDestroy()
        {
            if (Active == this)
                Active = null;
            if (_sphere != null)
                Destroy(_sphere.gameObject);
            if (_material != null)
                Destroy(_material);
            if (_map != null)
                Destroy(_map);
            if (_detail != null)
                Destroy(_detail);
        }

        // After the camera's moved, like the other sky spheres.
        void LateUpdate()
        {
            if (_camera == null)
                _camera = Camera.main;
            if (_camera == null || _sphere == null)
                return;

            Vector3 cameraPosition = _camera.transform.position;
            bool show = !DevOptions.HideClouds && WaterVolume.Containing(cameraPosition) == null;
            _sphere.gameObject.SetActive(show);
            if (!show)
                return;
            _sphere.position = cameraPosition;
            _sphere.localScale = Vector3.one * (_camera.farClipPlane * 0.9f * 2f);

            // Fine weather's sky is the weather system's while it runs, else the Clouds window's.
            var fair = WeatherSystem.Driving ? WeatherSystem.Active.Clouds : CloudSettings.Current;
            var clouds = CloudSettings.Lerp(fair, CloudSettings.Storm, _weather.Overcast);
            Showing = clouds;
            Apply(clouds);
        }

        void Apply(CloudSettings clouds)
        {
            // The coverage map repeats every 4 clouds or so, its biggest shapes a quarter of it.
            float scale = 1f / Mathf.Max(clouds.size * 4f, 1f);
            float angle = clouds.windDirection * Mathf.Deg2Rad;
            var wind = new Vector2(Mathf.Sin(angle), Mathf.Cos(angle)) * clouds.windSpeed * Time.deltaTime;
            // In map units, wrapped (both textures tile), so the offsets never lose precision.
            _mapOffset = Wrap(_mapOffset - wind * scale);
            // The detail drifts a touch faster than the shapes, so the clouds churn as they go.
            _detailOffset = Wrap(_detailOffset - wind * scale * 9f * 1.15f);

            Shader.SetGlobalFloat(CoverageId, clouds.coverage);
            Shader.SetGlobalFloat(DensityId, clouds.density);
            Shader.SetGlobalFloat(ScaleId, scale);
            Shader.SetGlobalVector(HeightId, new Vector4(clouds.height, clouds.height + clouds.thickness, PlanetRadius, 0f));
            Shader.SetGlobalFloat(SoftnessId, clouds.softness);
            Shader.SetGlobalVector(WindId, new Vector4(_mapOffset.x, _mapOffset.y, _detailOffset.x, _detailOffset.y));
            Shader.SetGlobalFloat(SunlightId, clouds.sunlight);
            Shader.SetGlobalFloat(SilverLiningId, clouds.silverLining);
            Shader.SetGlobalFloat(OvercastBrightnessId, _weather.overcastSkyBrightness);

            // The sun by day, the moon by night (DayNightCycle swaps one light round).
            var sun = _weather.Sun != null ? _weather.Sun : RenderSettings.sun;
            if (sun != null)
            {
                Shader.SetGlobalVector(SunDirectionId, -sun.transform.forward);
                Shader.SetGlobalColor(SunColorId, sun.color * (sun.enabled ? sun.intensity : 0f));
            }

            // The ambient the storm and the night have already dimmed.
            RenderSettings.ambientProbe.Evaluate(_ambientDirections, _ambient);
            Shader.SetGlobalColor(AmbientTopId, _ambient[0]);
            Shader.SetGlobalColor(AmbientBottomId, _ambient[1]);
        }

        static Vector2 Wrap(Vector2 v) => new(Mathf.Repeat(v.x, 1f), Mathf.Repeat(v.y, 1f));

        // --- Noise ----------------------------------------------------------------------

        /// <summary>
        /// Where clouds are: tileable fractal noise, big shapes and small (R), and a slower
        /// variation in how tall they grow (G).
        /// </summary>
        static Texture2D BuildCoverageMap()
        {
            var texture = new Texture2D(MapSize, MapSize, TextureFormat.RGBA32, false, true)
            {
                name = "Cloud Coverage", hideFlags = HideFlags.DontSave,
                wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Bilinear,
            };
            var shapes = new float[MapSize * MapSize];
            var heights = new float[MapSize * MapSize];
            for (int y = 0; y < MapSize; y++)
            {
                for (int x = 0; x < MapSize; x++)
                {
                    float u = (float)x / MapSize, v = (float)y / MapSize;
                    shapes[y * MapSize + x] = Fractal(u, v, 4, 5, 11);
                    heights[y * MapSize + x] = Fractal(u, v, 3, 2, 47);
                }
            }

            // Fractal noise bunches up round the middle, so a threshold barely moved the cover at
            // the ends and jumped in between ("Few" showed nothing). Spread evenly by rank, the
            // same shapes cover about as much of the sky as the coverage asks.
            var sorted = (float[])shapes.Clone();
            System.Array.Sort(sorted);
            var pixels = new Color32[MapSize * MapSize];
            for (int i = 0; i < pixels.Length; i++)
            {
                int rank = System.Array.BinarySearch(sorted, shapes[i]);
                if (rank < 0)
                    rank = ~rank;
                pixels[i] = new Color32(ToByte(rank / (float)(sorted.Length - 1)), ToByte(heights[i]), 0, 255);
            }
            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            return texture;
        }

        /// <summary>The billowing edges: tileable 3D cellular noise, inverted, three octaves.</summary>
        static Texture3D BuildDetailNoise()
        {
            var texture = new Texture3D(DetailSize, DetailSize, DetailSize, TextureFormat.R8, false)
            {
                name = "Cloud Detail", hideFlags = HideFlags.DontSave,
                wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Bilinear,
            };
            var pixels = new byte[DetailSize * DetailSize * DetailSize];
            for (int z = 0; z < DetailSize; z++)
                for (int y = 0; y < DetailSize; y++)
                    for (int x = 0; x < DetailSize; x++)
                    {
                        var p = new Vector3(x, y, z) / DetailSize;
                        float billow = 1f - (Cellular(p, 4, 3) * 0.625f + Cellular(p, 8, 5) * 0.25f + Cellular(p, 16, 9) * 0.125f);
                        pixels[(z * DetailSize + y) * DetailSize + x] = ToByte(billow);
                    }
            texture.SetPixelData(pixels, 0);
            texture.Apply(false, true);
            return texture;
        }

        static byte ToByte(float value) => (byte)Mathf.RoundToInt(Mathf.Clamp01(value) * 255f);

        /// <summary>Gradient noise in 0..1 summed over octaves, tiling over the unit square.</summary>
        static float Fractal(float u, float v, int period, int octaves, int seed)
        {
            float sum = 0f, amplitude = 0.5f, total = 0f;
            for (int i = 0; i < octaves; i++)
            {
                int cells = period << i;
                sum += Gradient(u * cells, v * cells, cells, seed + i * 31) * amplitude;
                total += amplitude;
                amplitude *= 0.5f;
            }
            // Gradient noise sits in about -0.7..0.7; spread it over 0..1.
            return Mathf.Clamp01(sum / total * 0.75f + 0.5f);
        }

        static float Gradient(float x, float y, int period, int seed)
        {
            int x0 = Mathf.FloorToInt(x), y0 = Mathf.FloorToInt(y);
            float fx = x - x0, fy = y - y0;
            float a = Dot(x0, y0, fx, fy, period, seed);
            float b = Dot(x0 + 1, y0, fx - 1f, fy, period, seed);
            float c = Dot(x0, y0 + 1, fx, fy - 1f, period, seed);
            float d = Dot(x0 + 1, y0 + 1, fx - 1f, fy - 1f, period, seed);
            float sx = fx * fx * fx * (fx * (fx * 6f - 15f) + 10f);
            float sy = fy * fy * fy * (fy * (fy * 6f - 15f) + 10f);
            return Mathf.Lerp(Mathf.Lerp(a, b, sx), Mathf.Lerp(c, d, sx), sy);
        }

        static float Dot(int cx, int cy, float dx, float dy, int period, int seed)
        {
            float angle = Hash(Mod(cx, period), Mod(cy, period), 0, seed) * Mathf.PI * 2f;
            return Mathf.Cos(angle) * dx + Mathf.Sin(angle) * dy;
        }

        /// <summary>Distance to the nearest of one random point per cell, 0..1, tiling over the unit cube.</summary>
        static float Cellular(Vector3 p, int cells, int seed)
        {
            var scaled = p * cells;
            int cx = Mathf.FloorToInt(scaled.x), cy = Mathf.FloorToInt(scaled.y), cz = Mathf.FloorToInt(scaled.z);
            float nearest = float.MaxValue;
            for (int k = -1; k <= 1; k++)
                for (int j = -1; j <= 1; j++)
                    for (int i = -1; i <= 1; i++)
                    {
                        int x = cx + i, y = cy + j, z = cz + k;
                        int wx = Mod(x, cells), wy = Mod(y, cells), wz = Mod(z, cells);
                        var point = new Vector3(x + Hash(wx, wy, wz, seed), y + Hash(wx, wy, wz, seed + 1),
                                                z + Hash(wx, wy, wz, seed + 2));
                        nearest = Mathf.Min(nearest, (point - scaled).sqrMagnitude);
                    }
            return Mathf.Clamp01(Mathf.Sqrt(nearest));
        }

        static int Mod(int value, int period) => ((value % period) + period) % period;

        static float Hash(int x, int y, int z, int seed)
        {
            unchecked
            {
                uint h = (uint)(x * 374761393 + y * 668265263 + z * 2147483647 + seed * 144665);
                h = (h ^ (h >> 13)) * 1274126177u;
                h ^= h >> 16;
                return (h & 0xFFFFFF) / 16777216f;
            }
        }
    }
}
