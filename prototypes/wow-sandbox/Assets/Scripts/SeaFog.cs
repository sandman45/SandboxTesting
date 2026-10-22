using UnityEngine;

namespace WowSandbox
{
    /// <summary>
    /// Sea fog: a bank of it standing on the water in a ring round the island, and a low mist over
    /// the water and the beach (WowSandbox/SeaFog), on an inside-out sphere pinned to the camera.
    /// When it comes is FogSettings.Current's mode: on Auto, a dawn mist rolls in before sunrise
    /// and burns off by mid-morning, and a thin night mist lies on the water after dark. The story
    /// can take it over with <see cref="Hold"/> and hand it back with <see cref="Release"/>.
    ///
    /// It shapes and lights itself with CloudLayer's noise and light, so it needs the clouds
    /// running. Looks only: nothing sees less for it. Added by StormWeather, like the rest of the
    /// weather.
    /// </summary>
    [RequireComponent(typeof(StormWeather))]
    public class SeaFog : MonoBehaviour
    {
        public static SeaFog Active { get; private set; }

        Camera _camera;
        Transform _sphere;
        Material _material;
        WaterVolume _water;
        Rect _island;
        Vector2 _driftOffset, _detailOffset;

        float _held = -1f;          // the story's fog amount, or -1 for the schedule's
        float _holdRate;
        float _amount;

        static readonly int IslandId = Shader.PropertyToID("_FogIsland");
        static readonly int BankId = Shader.PropertyToID("_FogBank");
        static readonly int MistId = Shader.PropertyToID("_FogMist");
        static readonly int ShapeId = Shader.PropertyToID("_FogShape");
        static readonly int DriftId = Shader.PropertyToID("_FogDrift");

        // The fog's noise repeats every this many world units.
        const float NoiseTile = 900f;

        /// <summary>How thick the fog is right now, 0 none to 1 its full look.</summary>
        public float Amount => _amount;

        /// <summary>Why it's as thick as it is — for the Developer panel.</summary>
        public string Reason { get; private set; } = "";

        void Start()
        {
            var shader = Shader.Find("WowSandbox/SeaFog");
            if (shader == null)
            {
                Debug.LogWarning("[SeaFog] WowSandbox/SeaFog shader not found — no sea fog.", this);
                enabled = false;
                return;
            }
            _material = new Material(shader) { name = "SeaFog", hideFlags = HideFlags.DontSave };
            _sphere = StormWeather.BuildSkySphere("SeaFogSky", _material);
            _water = FindAnyObjectByType<WaterVolume>();

            var terrain = Terrain.activeTerrain;
            if (terrain != null)
            {
                var origin = terrain.GetPosition();
                var size = terrain.terrainData.size;
                _island = new Rect(origin.x, origin.z, size.x, size.z);
            }
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
        }

        /// <summary>
        /// The story takes the fog over: it moves to <paramref name="amount"/> (0–1, or more for a
        /// thicker fog than the look) over <paramref name="seconds"/> and stays there till Release.
        /// </summary>
        public void Hold(float amount, float seconds = 10f)
        {
            _held = Mathf.Max(0f, amount);
            _holdRate = Mathf.Abs(_held - _amount) / Mathf.Max(seconds, 0.01f);
        }

        /// <summary>Hands the fog back to its schedule, easing there over <paramref name="seconds"/>.</summary>
        public void Release(float seconds = 10f)
        {
            _held = -1f;
            _holdRate = 1f / Mathf.Max(seconds, 0.01f);
        }

        void LateUpdate()
        {
            var fog = FogSettings.Current;
            float target = Target(fog);
            // The schedule moves with the clock; the story's fog eases at its own pace.
            float rate = _holdRate > 0f ? _holdRate : 0.5f;
            _amount = Mathf.MoveTowards(_amount, target, rate * Time.deltaTime);
            if (_held < 0f && Mathf.Approximately(_amount, target))
                _holdRate = 0f;

            if (_camera == null)
                _camera = Camera.main;
            if (_camera == null || _sphere == null)
                return;
            Vector3 cameraPosition = _camera.transform.position;
            bool show = _amount > 0.005f && CloudLayer.Active != null && WaterVolume.Containing(cameraPosition) == null;
            _sphere.gameObject.SetActive(show);
            if (!show)
                return;
            _sphere.position = cameraPosition;
            _sphere.localScale = Vector3.one * (_camera.farClipPlane * 0.9f * 2f);
            Apply(fog);
        }

        float Target(FogSettings fog)
        {
            if (_held >= 0f)
            {
                Reason = "Held by the story";
                return _held;
            }
            switch (fog.mode)
            {
                case FogSettings.Mode.Off:
                    Reason = "Off";
                    return 0f;
                case FogSettings.Mode.Always:
                    Reason = "Always on";
                    return 1f;
            }

            float hour = DayNightCycle.Active != null ? DayNightCycle.Active.hour : 12f;
            float dawn = fog.dawnMist ? DawnMist(hour) : 0f;
            float night = fog.nightMist ? NightMist(hour) : 0f;
            Reason = dawn <= 0.001f && night <= 0.001f ? "Auto: clear at this hour"
                   : dawn >= night ? $"Auto: dawn mist {dawn * 100f:0}%"
                   : $"Auto: night mist {night * 100f:0}%";
            return Mathf.Max(dawn, night);
        }

        /// <summary>Rolls in from 3:30, full from 5 to 7, burned off by half past 9.</summary>
        static float DawnMist(float hour) =>
            Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(3.5f, 5f, hour)) *
            (1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(7f, 9.5f, hour)));

        /// <summary>Thin from dusk, thicker past midnight, till the dawn mist takes over.</summary>
        static float NightMist(float hour)
        {
            if (hour >= 20f)
                return Mathf.SmoothStep(0f, 0.4f, Mathf.InverseLerp(20f, 22.5f, hour));
            if (hour < 5f)
                return Mathf.Lerp(0.4f, 0.6f, Mathf.InverseLerp(0f, 2f, hour));
            return 0f;
        }

        void Apply(FogSettings fog)
        {
            float scale = 1f / NoiseTile;
            float direction = CloudLayer.Active != null ? CloudLayer.Active.Showing.windDirection : 30f;
            float angle = direction * Mathf.Deg2Rad;
            var drift = new Vector2(Mathf.Sin(angle), Mathf.Cos(angle)) * fog.drift * Time.deltaTime;
            _driftOffset = Wrap(_driftOffset - drift * scale);
            _detailOffset = Wrap(_detailOffset - drift * scale * 6f * 1.3f);

            float waterLevel = _water != null ? _water.SurfaceY : 0f;
            Shader.SetGlobalVector(IslandId, new Vector4(_island.xMin, _island.yMin, _island.xMax, _island.yMax));
            Shader.SetGlobalVector(BankId, new Vector4(fog.bankDistance, fog.bankWidth, fog.bankHeight, fog.bankDensity * _amount));
            Shader.SetGlobalVector(MistId, new Vector4(fog.mistDensity * _amount, fog.mistHeight, 0f, 0f));
            Shader.SetGlobalVector(ShapeId, new Vector4(waterLevel, _camera.farClipPlane * 0.9f, fog.visibility, scale));
            Shader.SetGlobalVector(DriftId, new Vector4(_driftOffset.x, _driftOffset.y, _detailOffset.x, _detailOffset.y));
        }

        static Vector2 Wrap(Vector2 v) => new(Mathf.Repeat(v.x, 1f), Mathf.Repeat(v.y, 1f));
    }
}
