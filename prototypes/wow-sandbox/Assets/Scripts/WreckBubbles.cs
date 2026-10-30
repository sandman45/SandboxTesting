using UnityEngine;

namespace WowSandbox
{
    /// <summary>
    /// A thin stream of bubbles rising from a sunk wreck to the surface, so it can be spotted from
    /// above. The particle system is built in code at Play (like WaterSplash's) on the WeatherFX
    /// shader; each bubble lives just long enough to reach the surface from here, wobbling as it goes.
    /// Add Shipwreck puts one on a Sunk wreck, over the hull.
    /// </summary>
    public class WreckBubbles : MonoBehaviour
    {
        [Tooltip("Bubbles a second.")]
        public float rate = 6f;
        public float riseSpeed = 1.2f;
        [Tooltip("How far round this point they start.")]
        public float spread = 0.6f;
        public Vector2 size = new(0.05f, 0.14f);

        static Material _material;
        static Texture2D _texture;

        ParticleSystem _system;

        void Start()
        {
            var water = FindAnyObjectByType<WaterVolume>();
            var shader = Shader.Find("WowSandbox/WeatherFX");
            if (water == null || shader == null)
            {
                Debug.LogWarning("[WreckBubbles] Needs the water and the WowSandbox/WeatherFX shader — no bubbles.", this);
                return;
            }

            float depth = water.SurfaceY - transform.position.y;
            if (depth <= 0.2f)
                return;

            if (_texture == null)
                _texture = Ring();
            if (_material == null)
            {
                _material = new Material(shader) { name = "WreckBubbles", hideFlags = HideFlags.DontSave };
                _material.SetTexture("_BaseMap", _texture);
                // Code-made materials don't get their Toggle keywords synced; see StormWeather.BuildRain.
                _material.SetFloat("_ApplyFog", 1f);
                _material.EnableKeyword("_APPLY_FOG");
            }

            // A child, so it goes with the wreck; not DontSave, which would leak it past the end of Play.
            _system = new GameObject("Bubbles").AddComponent<ParticleSystem>();
            _system.transform.SetParent(transform, false);
            // Straight up, however the wreck it's on lies.
            _system.transform.rotation = Quaternion.identity;
            _system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var main = _system.main;
            main.loop = true;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startSpeed = riseSpeed;
            // Gone as they reach the surface (a little short, as the wobble slows them).
            main.startLifetime = depth / riseSpeed * 0.95f;
            main.startSize = new ParticleSystem.MinMaxCurve(size.x, size.y);
            main.startColor = new Color(0.85f, 0.95f, 1f, 0.8f);
            main.maxParticles = Mathf.CeilToInt(rate * depth / riseSpeed) + 20;

            var emission = _system.emission;
            emission.rateOverTime = rate;

            var shape = _system.shape;
            // A near-straight cone, turned from +Z to point up.
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 3f;
            shape.radius = spread;
            shape.rotation = new Vector3(-90f, 0f, 0f);

            var wobble = _system.noise;
            wobble.enabled = true;
            wobble.strength = 0.3f;
            wobble.frequency = 0.8f;

            // Grow a little as they rise, and fade in and out.
            var grow = _system.sizeOverLifetime;
            grow.enabled = true;
            grow.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.8f, 1f, 1.4f));
            var colour = _system.colorOverLifetime;
            colour.enabled = true;
            var fade = new Gradient();
            fade.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                         new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.1f), new GradientAlphaKey(1f, 0.85f), new GradientAlphaKey(0f, 1f) });
            colour.color = fade;

            var renderer = _system.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.sharedMaterial = _material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;

            _system.Play();
        }

        /// <summary>A soft ring: a bubble's bright rim round a faint middle.</summary>
        static Texture2D Ring()
        {
            const int size = 32;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                name = "BubbleRing",
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.DontSave,
            };

            var pixels = new Color[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = (x + 0.5f) / size * 2f - 1f;
                    float dy = (y + 0.5f) / size * 2f - 1f;
                    float r = Mathf.Sqrt(dx * dx + dy * dy);
                    float rim = Mathf.Clamp01(1f - Mathf.Abs(r - 0.8f) / 0.2f);
                    float alpha = r > 1f ? 0f : Mathf.Max(rim, 0.15f);
                    pixels[y * size + x] = new Color(1f, 1f, 1f, alpha);
                }
            }

            texture.SetPixels(pixels);
            texture.Apply();
            return texture;
        }
    }
}
