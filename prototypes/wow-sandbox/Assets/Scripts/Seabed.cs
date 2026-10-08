using UnityEngine;

namespace WowSandbox
{
    /// <summary>
    /// The sea floor beyond the terrain's edge. The terrain only covers the island, but the
    /// sea runs much further, so WoW Sandbox → Generate Seabed builds a skirt mesh from the
    /// terrain border out to the edge of the water — a shallow shelf, then a drop into the
    /// abyss — and stores its heights here.
    ///
    /// HeightAt is the one question everything asks about the bottom of the sea: the
    /// terrain where there is terrain, this skirt beyond it, the water volume's floor past
    /// that.
    /// </summary>
    public class Seabed : MonoBehaviour
    {
        [HideInInspector] public float[] heights;
        [HideInInspector] public int columns;
        [HideInInspector] public int rows;
        [HideInInspector] public Vector2 origin;
        [HideInInspector] public float step;

        static Seabed _instance;

        void OnEnable() => _instance = this;

        void OnDisable()
        {
            if (_instance == this)
                _instance = null;
        }

        /// <summary>World Y of the sea floor (or ground) below <paramref name="point"/>.</summary>
        public static float HeightAt(Vector3 point, WaterVolume water)
        {
            var terrain = Terrain.activeTerrain;
            if (terrain != null)
            {
                var terrainOrigin = terrain.GetPosition();
                var size = terrain.terrainData.size;
                if (point.x >= terrainOrigin.x && point.x <= terrainOrigin.x + size.x &&
                    point.z >= terrainOrigin.z && point.z <= terrainOrigin.z + size.z)
                    return terrainOrigin.y + terrain.SampleHeight(point);
            }

            if (_instance != null && _instance.TrySample(point, out float height))
                return height;

            return water != null ? water.SurfaceY - water.depth : point.y - 1000f;
        }

        bool TrySample(Vector3 point, out float height)
        {
            height = 0f;
            if (heights == null || heights.Length != columns * rows || step <= 0f)
                return false;

            float gx = (point.x - origin.x) / step;
            float gz = (point.z - origin.y) / step;
            if (gx < 0f || gz < 0f || gx > columns - 1 || gz > rows - 1)
                return false;

            int x0 = Mathf.Min((int)gx, columns - 2), z0 = Mathf.Min((int)gz, rows - 2);
            float tx = gx - x0, tz = gz - z0;
            float a = Mathf.Lerp(heights[z0 * columns + x0], heights[z0 * columns + x0 + 1], tx);
            float b = Mathf.Lerp(heights[(z0 + 1) * columns + x0], heights[(z0 + 1) * columns + x0 + 1], tx);
            height = Mathf.Lerp(a, b, tz);
            return true;
        }
    }
}
