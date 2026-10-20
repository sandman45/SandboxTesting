using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace WowSandbox
{
    /// <summary>
    /// The map image both maps draw from: one top-down orthographic snapshot, taken the
    /// first time anything asks for it in a Play session, north (+Z) at the top. It covers
    /// the whole sea out to the water's edge, so wherever you sail you're on it; IslandBounds
    /// is the island plus a margin of sea, which the world map opens on and the coordinates
    /// are measured across.
    ///
    /// For that one frame the capture hides what would get in the way from above: the
    /// sky dome and the storm's overcast and horizon spheres (they follow the main camera,
    /// so they sit over the island), particles (rain), lightning, skinned characters, and
    /// fog itself. Everything is restored straight after.
    /// </summary>
    public static class WorldMap
    {
        // The island is only part of the picture now, so more pixels to keep it sharp.
        const int Resolution = 2048;
        const float Margin = 0.06f;

        static RenderTexture _texture;
        static Rect _bounds;
        static Rect _island;

        /// <summary>
        /// Whether the player has found a map: until then there's no minimap and M does nothing.
        /// A MapPickup in the world sets it; the Developer panel's Show Minimap and Enable Map
        /// stand in for it (<see cref="MinimapShown"/>, <see cref="MapUsable"/>).
        /// </summary>
        public static bool Found { get; set; }

        public static bool MinimapShown => Found || (DevOptions.Enabled && DevOptions.ShowMinimap);
        public static bool MapUsable => Found || (DevOptions.Enabled && DevOptions.EnableMap);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetFound() => Found = false;

        public static Texture Texture
        {
            get
            {
                Ensure();
                return _texture;
            }
        }

        /// <summary>The island and a little sea around it, squared up — what the world map opens on.</summary>
        public static Rect IslandBounds
        {
            get
            {
                Ensure();
                return _island;
            }
        }

        /// <summary>World XZ area the map covers: the whole sea.</summary>
        public static Rect Bounds
        {
            get
            {
                Ensure();
                return _bounds;
            }
        }

        /// <summary>Where <paramref name="world"/> falls on the map, 0-1 on each axis.</summary>
        public static Vector2 ToUv(Vector3 world)
        {
            var bounds = Bounds;
            return new Vector2((world.x - bounds.xMin) / bounds.width, (world.z - bounds.yMin) / bounds.height);
        }

        /// <summary>WoW-style coordinates, 0-100 left to right and top to bottom across the
        /// island's area (out at sea they run past 0 and 100).</summary>
        public static Vector2 ToCoordinates(Vector3 world)
        {
            var island = IslandBounds;
            var uv = new Vector2((world.x - island.xMin) / island.width, (world.z - island.yMin) / island.height);
            return new Vector2(uv.x * 100f, (1f - uv.y) * 100f);
        }

        static void Ensure()
        {
            if (_texture != null)
                return;

            float top;
            float bottom;
            var terrain = Terrain.activeTerrain;
            if (terrain != null)
            {
                var origin = terrain.GetPosition();
                var size = terrain.terrainData.size;
                _bounds = new Rect(origin.x, origin.z, size.x, size.z);
                bottom = origin.y;
                top = origin.y + size.y;
            }
            else
            {
                _bounds = new Rect(-100f, -100f, 200f, 200f);
                bottom = -50f;
                top = 100f;
            }

            // Square it up around the centre, with some sea showing past the shore.
            float side = Mathf.Max(_bounds.width, _bounds.height) * (1f + Margin * 2f);
            _bounds = new Rect(_bounds.center - new Vector2(side, side) * 0.5f, new Vector2(side, side));
            _island = _bounds;

            // Then out to take in every bit of sea, still square.
            foreach (var water in Object.FindObjectsByType<WaterVolume>())
            {
                var centre = new Vector2(water.transform.position.x, water.transform.position.z);
                var min = Vector2.Min(_bounds.min, centre - water.extents);
                var max = Vector2.Max(_bounds.max, centre + water.extents);
                _bounds = Rect.MinMaxRect(min.x, min.y, max.x, max.y);
            }
            side = Mathf.Max(_bounds.width, _bounds.height);
            _bounds = new Rect(_bounds.center - new Vector2(side, side) * 0.5f, new Vector2(side, side));

            // Clamped, so a zoomed-out minimap past the edge shows open sea rather than the
            // island repeating.
            _texture = new RenderTexture(Resolution, Resolution, 24)
            {
                name = "WorldMap",
                wrapMode = TextureWrapMode.Clamp,
            };
            Capture(top, bottom);
        }

        static void Capture(float top, float bottom)
        {
            var go = new GameObject("WorldMapCapture");
            var camera = go.AddComponent<Camera>();
            camera.enabled = false;
            camera.orthographic = true;
            camera.orthographicSize = _bounds.height * 0.5f;
            camera.aspect = 1f;
            // Looking straight down with +Z at the top of the image.
            go.transform.SetPositionAndRotation(
                new Vector3(_bounds.center.x, top + 50f, _bounds.center.y), Quaternion.Euler(90f, 0f, 0f));
            camera.nearClipPlane = 1f;
            camera.farClipPlane = top - bottom + 250f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.1f, 0.22f, 0.32f);
            camera.targetTexture = _texture;

            var hidden = new List<Renderer>();
            foreach (var renderer in Object.FindObjectsByType<Renderer>())
            {
                if (!renderer.enabled || !InTheWay(renderer))
                    continue;
                renderer.enabled = false;
                hidden.Add(renderer);
            }

            bool fog = RenderSettings.fog;
            RenderSettings.fog = false;
            WaterDryZone.Suspend(true);
            try
            {
                // Taken once and kept, so in plain noon light whatever the hour or weather.
                StormWeather.InDaylight(() =>
                {
                    var request = new UniversalRenderPipeline.SingleCameraRequest { destination = _texture };
                    if (RenderPipeline.SupportsRenderRequest(camera, request))
                        RenderPipeline.SubmitRenderRequest(camera, request);
                    else
                        camera.Render();
                });
            }
            finally
            {
                RenderSettings.fog = fog;
                WaterDryZone.Suspend(false);
                foreach (var renderer in hidden)
                    renderer.enabled = true;
                camera.targetTexture = null;
                Object.Destroy(go);
            }
        }

        // StormWeather's camera-centred spheres are "StormOvercastSky" and "StormHorizonFog".
        static bool InTheWay(Renderer renderer) =>
            renderer is ParticleSystemRenderer or SkinnedMeshRenderer or LineRenderer
            || renderer.GetComponentInParent<SkyDomeFollow>() != null
            // It sails off; MapMarkers draws it where it is instead.
            || renderer.GetComponentInParent<ShipSailing>() != null
            || renderer.name.Contains("Sky")
            || renderer.name.Contains("Horizon");
    }
}
