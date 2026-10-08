using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace WowSandbox
{
    /// <summary>
    /// The map image both maps draw from: one top-down orthographic snapshot of the
    /// island, taken the first time anything asks for it in a Play session. The area
    /// covered is the active Terrain plus a margin of sea, squared up, north (+Z) at the top.
    ///
    /// For that one frame the capture hides what would get in the way from above: the
    /// sky dome and the storm's overcast and horizon spheres (they follow the main camera,
    /// so they sit over the island), particles (rain), lightning, skinned characters, and
    /// fog itself. Everything is restored straight after.
    /// </summary>
    public static class WorldMap
    {
        const int Resolution = 1024;
        const float Margin = 0.06f;

        static RenderTexture _texture;
        static Rect _bounds;

        public static Texture Texture
        {
            get
            {
                Ensure();
                return _texture;
            }
        }

        /// <summary>World XZ area the map covers.</summary>
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

        /// <summary>WoW-style coordinates, 0-100 left to right and top to bottom.</summary>
        public static Vector2 ToCoordinates(Vector3 world)
        {
            var uv = ToUv(world);
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
            try
            {
                var request = new UniversalRenderPipeline.SingleCameraRequest { destination = _texture };
                if (RenderPipeline.SupportsRenderRequest(camera, request))
                    RenderPipeline.SubmitRenderRequest(camera, request);
                else
                    camera.Render();
            }
            finally
            {
                RenderSettings.fog = fog;
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
            || renderer.name.Contains("Sky")
            || renderer.name.Contains("Horizon");
    }
}
