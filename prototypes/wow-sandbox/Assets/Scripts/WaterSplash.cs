using UnityEngine;

namespace WowSandbox
{
    /// <summary>
    /// A splash on the water: a burst of droplets thrown up and falling back, and a puff of
    /// white foam spreading on the surface. Two shared particle systems built in code the
    /// first time anything splashes — a fishing line landing, a fish biting — and fired with
    /// Emit, so any number of splashes cost nothing between them.
    /// </summary>
    public static class WaterSplash
    {
        static ParticleSystem _droplets;
        static ParticleSystem _foam;
        static Material _material;
        static Texture2D _texture;

        /// <summary>
        /// Splashes at <paramref name="position"/> (a point on the surface). Strength 1 is a
        /// fish thrashing on the line; a bobber landing is about 0.3.
        /// </summary>
        public static void Play(Vector3 position, float strength = 1f)
        {
            // Unity's null: these are scene objects, gone at the end of each Play.
            if (_droplets == null || _foam == null)
                Build();
            if (_droplets == null)
                return;

            int drops = Mathf.RoundToInt(Mathf.Lerp(6f, 40f, strength));
            for (int i = 0; i < drops; i++)
            {
                var spread = Random.insideUnitCircle * 0.6f;
                _droplets.Emit(new ParticleSystem.EmitParams
                {
                    position = position + new Vector3(spread.x, 0.05f, spread.y) * strength,
                    velocity = new Vector3(spread.x * 2.5f, Random.Range(2f, 5f) * Mathf.Lerp(0.6f, 1f, strength), spread.y * 2.5f),
                    startSize = Random.Range(0.06f, 0.16f) * Mathf.Lerp(0.7f, 1.2f, strength),
                    startLifetime = Random.Range(0.6f, 1.1f),
                    startColor = new Color(0.9f, 0.96f, 1f, 0.9f),
                }, 1);
            }

            int puffs = Mathf.RoundToInt(Mathf.Lerp(2f, 8f, strength));
            for (int i = 0; i < puffs; i++)
            {
                var spread = Random.insideUnitCircle * 0.4f * strength;
                _foam.Emit(new ParticleSystem.EmitParams
                {
                    position = position + new Vector3(spread.x, 0.08f, spread.y),
                    velocity = new Vector3(spread.x, 0.15f, spread.y),
                    startSize = Random.Range(0.6f, 1.2f) * Mathf.Lerp(0.6f, 1.8f, strength),
                    startLifetime = Random.Range(0.8f, 1.4f),
                    startColor = new Color(1f, 1f, 1f, 0.55f),
                }, 1);
            }
        }

        static void Build()
        {
            var shader = Shader.Find("WowSandbox/WeatherFX");
            if (shader == null)
            {
                Debug.LogWarning("[WaterSplash] WowSandbox/WeatherFX shader not found — no splashes.");
                return;
            }

            if (_texture == null)
                _texture = SoftDot();
            if (_material == null)
            {
                _material = new Material(shader) { name = "WaterSplash", hideFlags = HideFlags.DontSave };
                _material.SetTexture("_BaseMap", _texture);
                // Code-made materials don't get their Toggle keywords synced; see StormWeather.BuildRain.
                _material.SetFloat("_ApplyFog", 1f);
                _material.EnableKeyword("_APPLY_FOG");
            }

            // Plain scene objects, not DontSave: that would leak them past the end of Play.
            _droplets = Create("SplashDroplets", gravity: 1.2f, grow: false);
            _foam = Create("SplashFoam", gravity: 0f, grow: true);
        }

        static ParticleSystem Create(string name, float gravity, bool grow)
        {
            var system = new GameObject(name).AddComponent<ParticleSystem>();
            system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var main = system.main;
            main.loop = false;
            main.playOnAwake = false;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.gravityModifier = gravity;
            main.maxParticles = 500;

            var emission = system.emission;
            emission.enabled = false;
            var shape = system.shape;
            shape.enabled = false;

            // Fade out over their life; foam also spreads as it goes.
            var colour = system.colorOverLifetime;
            colour.enabled = true;
            var fade = new Gradient();
            fade.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                         new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(grow ? 0.7f : 1f, 0.4f), new GradientAlphaKey(0f, 1f) });
            colour.color = fade;

            if (grow)
            {
                var size = system.sizeOverLifetime;
                size.enabled = true;
                size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, 0.5f, 1f, 1.6f));
            }

            var renderer = system.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.sharedMaterial = _material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            return system;
        }

        static Texture2D SoftDot()
        {
            const int size = 32;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                name = "SplashDot",
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
                    float alpha = Mathf.Pow(Mathf.Clamp01(1f - Mathf.Sqrt(dx * dx + dy * dy)), 1.2f);
                    pixels[y * size + x] = new Color(1f, 1f, 1f, alpha);
                }
            }

            texture.SetPixels(pixels);
            texture.Apply();
            return texture;
        }
    }
}
