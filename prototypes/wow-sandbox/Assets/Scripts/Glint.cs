using UnityEngine;

namespace WowSandbox
{
    /// <summary>
    /// A twinkle of light over something worth picking up, so it can be spotted from a few
    /// strides off: a few small stars that flare and fade over it every so often. Added at Play
    /// to every GroundItem holding a quest item (Sprocket's compass parts); it goes dark with
    /// the item when it's taken, since GroundItem hides every renderer it has.
    /// </summary>
    public class Glint : MonoBehaviour
    {
        [Tooltip("Stars a second.")]
        public float rate = 2.5f;
        [Tooltip("How big each star gets, in world units.")]
        public float starSize = 0.35f;
        public Color colour = new(1f, 0.85f, 0.45f, 1f);

        static Material _material;
        static Texture2D _star;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void AddToQuestItems()
        {
            foreach (var ground in FindObjectsByType<GroundItem>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (ground.GetComponent<Glint>() == null && ground.item != null &&
                    ItemCatalog.TryFind(ground.item.Trim(), out var item) && item.kind == ItemKind.Quest)
                    ground.gameObject.AddComponent<Glint>();
        }

        void Start()
        {
            var shader = Shader.Find("WowSandbox/WeatherFX");
            if (shader == null)
            {
                Debug.LogWarning("[Glint] WowSandbox/WeatherFX shader not found — no glint.");
                return;
            }
            if (_star == null)
                _star = Star();
            if (_material == null)
            {
                _material = new Material(shader) { name = "Glint", hideFlags = HideFlags.DontSave };
                _material.SetTexture("_BaseMap", _star);
                // Additive, and through the fog, so it reads at a distance.
                _material.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
                _material.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.One);
                _material.SetFloat("_ApplyFog", 0f);
                _material.DisableKeyword("_APPLY_FOG");
            }

            // Over the middle of whatever the item looks like.
            var bounds = new Bounds(transform.position, Vector3.one * 0.3f);
            bool first = true;
            foreach (var shown in GetComponentsInChildren<Renderer>())
            {
                if (first)
                    bounds = shown.bounds;
                else
                    bounds.Encapsulate(shown.bounds);
                first = false;
            }

            var holder = new GameObject("Glint");
            holder.transform.SetParent(transform, false);
            holder.transform.position = bounds.center + Vector3.up * (bounds.extents.y * 0.5f);
            var system = holder.AddComponent<ParticleSystem>();
            system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var main = system.main;
            main.loop = true;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.5f, 0.9f);
            main.startSpeed = 0f;
            main.startSize = new ParticleSystem.MinMaxCurve(starSize * 0.6f, starSize);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 0.5f);
            main.startColor = colour;
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            main.maxParticles = 20;

            var emission = system.emission;
            emission.rateOverTime = rate;

            var shape = system.shape;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = Mathf.Max(0.1f, Mathf.Min(bounds.extents.x, bounds.extents.z));

            // Each star swells and shrinks: a twinkle, not a dot.
            var size = system.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
                new Keyframe(0f, 0f), new Keyframe(0.35f, 1f), new Keyframe(1f, 0f)));

            var renderer = holder.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.sharedMaterial = _material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;

            system.Play();
        }

        /// <summary>A soft four-pointed star.</summary>
        static Texture2D Star()
        {
            const int size = 64;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                name = "Glint Star", wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.DontSave,
            };
            var pixels = new Color[size * size];
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float u = (x + 0.5f) / size * 2f - 1f;
                float v = (y + 0.5f) / size * 2f - 1f;
                float core = Mathf.Clamp01(1f - Mathf.Sqrt(u * u + v * v) * 2.2f);
                float rays = Mathf.Clamp01(1f - Mathf.Abs(u) * 14f) * Mathf.Clamp01(1f - Mathf.Abs(v)) +
                             Mathf.Clamp01(1f - Mathf.Abs(v) * 14f) * Mathf.Clamp01(1f - Mathf.Abs(u));
                float alpha = Mathf.Clamp01(core * core + rays);
                pixels[y * size + x] = new Color(1f, 1f, 1f, alpha);
            }
            texture.SetPixels(pixels);
            texture.Apply();
            return texture;
        }
    }
}
