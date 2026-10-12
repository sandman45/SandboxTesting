using UnityEngine;

namespace WowSandbox
{
    /// <summary>
    /// At each storm's peak the Ancient Leviathan rises far out to sea — seaward of the
    /// player, past the island's shelf — and looms there, swaying and roaring, until the
    /// storm starts to clear and it sinks away. The fog hides it; lightning (which favours
    /// the spot while it's up) throws the fog back for a moment and shows its silhouette.
    ///
    /// A spectacle only: it never comes closer and can't be targeted. Uses the same hidden
    /// Leviathan that DeepSeaDanger sends, and waits its turn if that one's already out.
    /// StormWeather adds this to itself; with no leviathan in the scene it stands down.
    /// </summary>
    [DisallowMultipleComponent]
    public class StormLeviathan : MonoBehaviour
    {
        [Tooltip("Storm intensity at which it rises.")]
        [Range(0f, 1f)] public float riseAtIntensity = 0.95f;
        [Tooltip("It sinks once the storm drops below this, and can rise again at the next peak.")]
        [Range(0f, 1f)] public float sinkBelowIntensity = 0.85f;
        [Tooltip("How far from the player it rises.")]
        public float distance = 170f;
        [Tooltip("And at least this far past the island's edge, so it's always in open water.")]
        public float minShoreGap = 60f;

        StormWeather _weather;
        Leviathan _leviathan;
        bool _armed = true;
        bool _up;
        float _forcedUntil = -1f;

        void Start()
        {
            _weather = GetComponent<StormWeather>();
            _leviathan = FindAnyObjectByType<Leviathan>(FindObjectsInactive.Include);
            if (_weather == null || _leviathan == null)
            {
                Debug.Log("[StormLeviathan] No Leviathan in the scene, so nothing rises at the storm's peak. " +
                          "Run WoW Sandbox → Setup Deep Sea Danger to place one.", this);
                enabled = false;
            }
        }

        void Update()
        {
            if (_weather.intensity < sinkBelowIntensity)
                _armed = true;

            if (_up || !_armed || _weather.intensity < riseAtIntensity || _leviathan.InUse)
                return;

            _armed = false;
            StartCoroutine(Appear());
        }

        System.Collections.IEnumerator Appear()
        {
            Vector3 watcher = WatcherPosition();
            Vector3 spot = SpotOffshore(watcher);

            _up = true;
            _weather.StrikeFocus = spot;
            Hud.Info("Something vast stirs out in the storm...");

            yield return _leviathan.Loom(spot, watcher, 
                () => _weather.intensity >= sinkBelowIntensity || Time.time < _forcedUntil);

            _weather.StrikeFocus = null;
            _up = false;
        }

        void OnDisable()
        {
            if (_weather != null)
                _weather.StrikeFocus = null;
            if (_up && _leviathan != null)
                _leviathan.Hide();
            _up = false;
        }

        static Vector3 WatcherPosition()
        {
            var player = FindAnyObjectByType<HealthController>();
            if (player != null)
                return player.transform.position;
            return Camera.main != null ? Camera.main.transform.position : Vector3.zero;
        }

        /// <summary>
        /// Out along the line from the island's centre through the watcher — whichever way is
        /// "out to sea" from where they stand — then on until it's clear of the shelf.
        /// </summary>
        Vector3 SpotOffshore(Vector3 watcher)
        {
            var terrain = Terrain.activeTerrain;
            Rect island = default;
            Vector3 seaward = Vector3.zero;
            if (terrain != null)
            {
                var origin = terrain.GetPosition();
                var size = terrain.terrainData.size;
                island = new Rect(origin.x, origin.z, size.x, size.z);
                seaward = watcher - new Vector3(island.center.x, watcher.y, island.center.y);
            }

            seaward.y = 0f;
            if (seaward.sqrMagnitude < 1f && Camera.main != null)
                seaward = Camera.main.transform.forward;
            seaward.y = 0f;
            seaward = seaward.sqrMagnitude > 0.001f ? seaward.normalized : Vector3.forward;

            Vector3 spot = watcher + seaward * distance;
            for (int i = 0; terrain != null && i < 100 && GapToIsland(spot, island) < minShoreGap; i++)
                spot += seaward * 10f;

            var water = FindAnyObjectByType<WaterVolume>();
            spot.y = water != null ? water.SurfaceY : watcher.y;
            return spot;
        }

        static float GapToIsland(Vector3 point, Rect island)
        {
            var flat = new Vector2(point.x, point.z);
            var nearest = new Vector2(Mathf.Clamp(flat.x, island.xMin, island.xMax),
                                      Mathf.Clamp(flat.y, island.yMin, island.yMax));
            return Vector2.Distance(flat, nearest);
        }

        [ContextMenu("Raise Leviathan Now (60s)")]
        void RaiseNow()
        {
            if (!Application.isPlaying || !enabled || _up || _leviathan.InUse)
                return;
            _forcedUntil = Time.time + 60f;
            StartCoroutine(Appear());
        }
    }
}
