using UnityEngine;

namespace WowSandbox
{
    /// <summary>
    /// The terrain's grass waves harder as the wind gets up: from the terrain's own waving (a still
    /// day) to full strength, speed and amount in a storm's wind — the stronger of StormWeather's
    /// intensity and CloudLayer's wind. The waving lives on the TerrainData asset, which keeps
    /// changes made in Play, so the terrain's own values are put back when Play ends.
    ///
    /// Added by StormWeather, like the rest of the weather.
    /// </summary>
    [RequireComponent(typeof(StormWeather))]
    public class GrassWind : MonoBehaviour
    {
        StormWeather _weather;
        TerrainData _terrain;
        float _strength, _speed, _amount;
        float _applied = -1f;

        void Start()
        {
            _weather = GetComponent<StormWeather>();
            var terrain = Terrain.activeTerrain;
            _terrain = terrain != null ? terrain.terrainData : null;
            if (_terrain == null)
            {
                enabled = false;
                return;
            }
            _strength = _terrain.wavingGrassStrength;
            _speed = _terrain.wavingGrassSpeed;
            _amount = _terrain.wavingGrassAmount;
        }

        void LateUpdate()
        {
            float wind = Mathf.Max(_weather.intensity, CloudLayer.WindStrength);
            // Only when it's really changed: each set rebuilds the grass's wind on the terrain.
            if (Mathf.Abs(wind - _applied) < 0.01f)
                return;
            _applied = wind;
            _terrain.wavingGrassStrength = Mathf.Lerp(_strength, 1f, wind);
            _terrain.wavingGrassSpeed = Mathf.Lerp(_speed, 1f, wind);
            _terrain.wavingGrassAmount = Mathf.Lerp(_amount, 1f, wind);
        }

        void OnDisable()
        {
            if (_terrain == null || _applied < 0f)
                return;
            _terrain.wavingGrassStrength = _strength;
            _terrain.wavingGrassSpeed = _speed;
            _terrain.wavingGrassAmount = _amount;
            _applied = -1f;
        }
    }
}
