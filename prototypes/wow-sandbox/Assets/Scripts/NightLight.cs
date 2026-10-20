using UnityEngine;

namespace WowSandbox
{
    /// <summary>
    /// A lantern, torch, brazier or campfire that lights up as it gets dark: a warm point light
    /// with a flicker, and for open fires a flame. WoW's flames are particle emitters, which
    /// the glTF/OBJ exports don't carry, so the flame is built here in code.
    ///
    /// Added at Play to every doodad named like one (the pirate ship's lanterns among them),
    /// so exported light sources need no setup; WoW Sandbox → Place Light Source places more.
    /// HeldLight uses one for the torch or lantern in the player's hand, lit by hand
    /// (<see cref="held"/>) rather than by the hour.
    /// </summary>
    [DisallowMultipleComponent]
    public class NightLight : MonoBehaviour
    {
        public enum Kind { Lantern, Torch, Brazier, Campfire }

        static readonly string[] LanternNames = { "lantern" };
        static readonly string[] TorchNames = { "torch" };
        static readonly string[] BrazierNames = { "brazier" };
        static readonly string[] CampfireNames = { "campfire", "bonfire" };

        public Kind kind = Kind.Lantern;
        [Tooltip("Where the light (and flame) sits, from this object. Zero = worked out from the model.")]
        public Vector3 lightOffset;
        public Color color = new Color(1f, 0.62f, 0.3f);
        [Tooltip("Zero = this kind's usual brightness and reach.")]
        public float intensity;
        public float range;
        [Tooltip("Shadows from every lantern add up quickly; on for one or two that matter.")]
        public bool castShadows;

        /// <summary>Carried: lit when <see cref="Lit"/> says, not by the time of day.</summary>
        [System.NonSerialized] public bool held;
        [System.NonSerialized] public bool Lit;

        Light _light;
        ParticleSystem _flame;
        float _seed;
        float _baseIntensity;
        float _baseRange;
        Vector3 _flamePoint;

        /// <summary>
        /// How far up a lantern its light sits, as a share of its height from the bottom: around
        /// the glass, below the hook, chain or handle that makes up the top of its bounds.
        /// </summary>
        public const float LanternLightHeight = 0.35f;

        /// <summary>
        /// Particular models that want more (or less) than their kind's light: brightness and
        /// reach, by a word in the model's name. The dwarven new_small_lantern02 on the ship is
        /// the bigger of its two lanterns, whatever its name says.
        /// </summary>
        static readonly (string model, float intensity, float range)[] ModelLights =
        {
            ("new_small_lantern", 2.6f, 9f),
        };

        /// <summary>
        /// A lantern's glass, by material name. WoW marks it unlit and opaque, so a light inside
        /// can't brighten it and it reads as a solid pane in front of the light; it's made to
        /// glow instead, warm and flickering with the flame.
        /// </summary>
        static readonly string[] GlassMaterials = { "hanginglantern", "elwynnlantern" };
        static readonly Color GlassGlow = new Color(2.4f, 1.7f, 0.95f);

        readonly System.Collections.Generic.List<(Material material, Color color)> _glass = new();

        static Material _flameMaterial;
        static Texture2D _flameTexture;

        /// <summary>
        /// How lit the lights should be, 0 by day to 1 at night — and partly in a heavy storm,
        /// when it's dark enough to want them. Full on without a day and night.
        /// </summary>
        public static float Darkness
        {
            get
            {
                var cycle = DayNightCycle.Active;
                float night = cycle != null ? 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.3f, 0.75f, cycle.Daylight)) : 1f;
                float storm = StormWeather.Active != null ? Mathf.InverseLerp(0.5f, 1f, StormWeather.Active.intensity) * 0.8f : 0f;
                return Mathf.Max(night, storm);
            }
        }

        /// <summary>Which kind a model is, from its name; null if it isn't a light source.</summary>
        public static Kind? KindFromName(string name)
        {
            name = name.ToLowerInvariant();
            if (Contains(name, CampfireNames)) return Kind.Campfire;
            if (Contains(name, BrazierNames)) return Kind.Brazier;
            if (Contains(name, TorchNames)) return Kind.Torch;
            if (Contains(name, LanternNames)) return Kind.Lantern;
            return null;
        }

        static bool Contains(string name, string[] words)
        {
            foreach (var word in words)
            {
                if (name.Contains(word))
                    return true;
            }
            return false;
        }

        /// <summary>
        /// Every light source in the scene that isn't in someone's hand: anything with a
        /// NightLight already (Place Light Source), and every doodad named like one — the topmost
        /// match, so a lantern's parts don't each get a light, and nothing lying on the ground to
        /// pick up. Shared with the editor's Preview Night Lights.
        /// </summary>
        public static System.Collections.Generic.List<(Transform source, Kind kind)> FindSources()
        {
            var found = new System.Collections.Generic.List<(Transform, Kind)>();
            foreach (var t in FindObjectsByType<Transform>(FindObjectsSortMode.None))
            {
                if (t.TryGetComponent<NightLight>(out var existing))
                {
                    if (!existing.held)
                        found.Add((t, existing.kind));
                    continue;
                }
                var kind = KindFromName(t.name);
                if (kind == null || t.GetComponentInChildren<Renderer>() == null)
                    continue;
                // A torch or lantern lying on the ground to pick up isn't lit.
                if (t.GetComponentInParent<Interactable>() != null)
                    continue;
                // The doodad, not each of its parts: skip anything under one already matched.
                bool nested = false;
                for (var parent = t.parent; parent != null && !nested; parent = parent.parent)
                    nested = KindFromName(parent.name) != null || parent.GetComponent<NightLight>() != null;
                if (!nested)
                    found.Add((t, kind.Value));
            }
            return found;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void AttachAll()
        {
            int added = 0;
            foreach (var (source, kind) in FindSources())
            {
                if (source.GetComponent<NightLight>() != null)
                    continue;
                source.gameObject.AddComponent<NightLight>().kind = kind;
                added++;
            }

            if (added > 0)
                Debug.Log($"[NightLight] Lit {added} lantern(s) and torch(es) for the night.");
        }

        /// <summary>This one's brightness and reach: its own settings, else its model's, else its kind's.</summary>
        public (float intensity, float range) Brightness()
        {
            var (baseIntensity, baseRange) = Defaults(kind, name);
            return (intensity > 0f ? intensity : baseIntensity, range > 0f ? range : baseRange);
        }

        /// <summary>A kind's usual brightness and reach, or a particular model's (ModelLights).</summary>
        public static (float intensity, float range) Defaults(Kind kind, string objectName)
        {
            var result = kind switch
            {
                Kind.Torch => (2.2f, 9f),
                Kind.Brazier => (2.8f, 11f),
                Kind.Campfire => (3.2f, 13f),
                _ => (1.8f, 7f),
            };
            string lowerName = objectName.ToLowerInvariant();
            foreach (var (model, modelIntensity, modelRange) in ModelLights)
            {
                if (lowerName.Contains(model))
                    result = (modelIntensity, modelRange);
            }
            return result;
        }

        /// <summary>Where this one's light goes: its Light Offset if set, else worked out from the model.</summary>
        public Vector3 LightPoint() =>
            lightOffset != Vector3.zero ? transform.TransformPoint(lightOffset) : ModelLightPoint(transform, kind);

        void Start()
        {
            _seed = Random.value * 100f;
            (_baseIntensity, _baseRange) = Brightness();

            var point = LightPoint();
            var go = new GameObject("Light");
            go.transform.SetParent(transform, false);
            go.transform.position = point;
            _flamePoint = go.transform.localPosition;

            _light = go.AddComponent<Light>();
            _light.type = LightType.Point;
            _light.color = color;
            _light.range = _baseRange;
            _light.shadows = castShadows ? LightShadows.Soft : LightShadows.None;
            _light.renderMode = LightRenderMode.ForcePixel;
            _light.enabled = false;

            if (kind is Kind.Torch or Kind.Brazier or Kind.Campfire)
                _flame = BuildFlame(go.transform);
            if (kind == Kind.Lantern)
                FindGlass();
        }

        void OnDestroy()
        {
            foreach (var (material, _) in _glass)
                if (material != null)
                    Destroy(material);
        }

        /// <summary>Gives each glass renderer its own copy of its material, so this lantern's glow is its own.</summary>
        void FindGlass()
        {
            foreach (var renderer in GetComponentsInChildren<Renderer>())
            {
                var materials = renderer.sharedMaterials;
                bool changed = false;
                for (int i = 0; i < materials.Length; i++)
                {
                    var material = materials[i];
                    if (material == null || !material.HasProperty("_BaseColor") || !Contains(material.name.ToLowerInvariant(), GlassMaterials))
                        continue;
                    var copy = new Material(material) { name = material.name + " (lit)" };
                    _glass.Add((copy, copy.GetColor("_BaseColor")));
                    materials[i] = copy;
                    changed = true;
                }
                if (changed)
                    renderer.sharedMaterials = materials;
            }
        }

        void SetGlass(float glow)
        {
            foreach (var (material, color) in _glass)
            {
                var lit = color * Color.Lerp(Color.white, GlassGlow, glow);
                lit.a = color.a;
                material.SetColor("_BaseColor", lit);
            }
        }

        /// <summary>A lantern's light low in it, at the glass; a fire's at the top, where it burns.</summary>
        public static Vector3 ModelLightPoint(Transform transform, Kind kind)
        {
            var renderers = transform.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0)
                return transform.position;
            var bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++)
                bounds.Encapsulate(renderers[i].bounds);

            return kind switch
            {
                Kind.Lantern => new Vector3(bounds.center.x, bounds.min.y + bounds.size.y * LanternLightHeight, bounds.center.z),
                Kind.Torch => new Vector3(bounds.center.x, bounds.max.y - bounds.size.y * 0.08f, bounds.center.z),
                _ => new Vector3(bounds.center.x, bounds.max.y, bounds.center.z),
            };
        }

        void Update()
        {
            if (_light == null)
                return;

            float level = held ? (Lit ? 1f : 0f) : Darkness;
            bool lit = level > 0.01f;
            if (_light.enabled != lit)
                _light.enabled = lit;
            if (!lit && _glass.Count > 0)
                SetGlass(0f);

            if (_flame != null)
            {
                var emission = _flame.emission;
                emission.rateOverTime = lit ? FlameRate * level : 0f;
            }

            if (!lit)
                return;

            // Open flames flicker hard and dance about; a lantern behind glass barely does.
            float t = Time.time * (kind == Kind.Lantern ? 3f : 9f) + _seed;
            float noise = Mathf.PerlinNoise(t, _seed) - 0.5f;
            float flicker = kind switch
            {
                Kind.Lantern => noise * 0.12f,
                _ => noise * 0.45f,
            };
            _light.intensity = _baseIntensity * level * (1f + flicker);
            if (_glass.Count > 0)
                SetGlass(Mathf.Clamp01(level * (1f + flicker * 1.5f)));
            if (_flame != null)
                _light.transform.localPosition = _flamePoint + new Vector3(noise, Mathf.Abs(noise), Mathf.PerlinNoise(_seed, t) - 0.5f) * 0.06f;
        }

        // --- Flame -------------------------------------------------------------------------

        float FlameRate => kind switch
        {
            Kind.Campfire => 60f,
            Kind.Brazier => 45f,
            _ => 25f,
        };

        float FlameSize => kind switch
        {
            Kind.Campfire => 0.6f,
            Kind.Brazier => 0.45f,
            _ => 0.25f,
        };

        ParticleSystem BuildFlame(Transform at)
        {
            if (!EnsureFlameMaterial())
                return null;

            var go = new GameObject("Flame");
            go.transform.SetParent(at, false);
            var system = go.AddComponent<ParticleSystem>();
            system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            float size = FlameSize * Mathf.Max(transform.lossyScale.y, 0.2f);
            var main = system.main;
            main.loop = true;
            main.playOnAwake = false;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.35f, 0.7f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.4f * size / 0.25f, 0.9f * size / 0.25f);
            main.startSize = new ParticleSystem.MinMaxCurve(size * 0.6f, size);
            main.startColor = Color.white;
            main.maxParticles = 200;
            // World space: a torch on the moving ship leaves its flame trailing, as it would.
            main.simulationSpace = ParticleSystemSimulationSpace.World;

            var emission = system.emission;
            emission.rateOverTime = 0f;

            var shape = system.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 8f;
            shape.radius = size * 0.25f;
            // Upward, whatever way the model points.
            go.transform.rotation = Quaternion.LookRotation(Vector3.up, Vector3.forward);

            // Yellow at the root, deep orange, then out.
            var colour = system.colorOverLifetime;
            colour.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(
                new[] { new GradientColorKey(new Color(1f, 0.85f, 0.45f), 0f), new GradientColorKey(new Color(1f, 0.45f, 0.12f), 0.5f),
                        new GradientColorKey(new Color(0.6f, 0.12f, 0.04f), 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(0.9f, 0.15f), new GradientAlphaKey(0.5f, 0.6f), new GradientAlphaKey(0f, 1f) });
            colour.color = gradient;

            var shrink = system.sizeOverLifetime;
            shrink.enabled = true;
            shrink.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, 1f, 1f, 0.3f));

            var renderer = system.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.sharedMaterial = _flameMaterial;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;

            system.Play();
            return system;
        }

        static bool EnsureFlameMaterial()
        {
            if (_flameMaterial != null)
                return true;
            var shader = Shader.Find("WowSandbox/WeatherFX");
            if (shader == null)
            {
                Debug.LogWarning("[NightLight] WowSandbox/WeatherFX shader not found — lights without flames.");
                return false;
            }

            if (_flameTexture == null)
                _flameTexture = SoftDot();
            _flameMaterial = new Material(shader) { name = "Flame", hideFlags = HideFlags.DontSave };
            _flameMaterial.SetTexture("_BaseMap", _flameTexture);
            // Additive and unfogged, like the lightning: a fire shows through the dark.
            _flameMaterial.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            _flameMaterial.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.One);
            _flameMaterial.SetFloat("_ApplyFog", 0f);
            _flameMaterial.DisableKeyword("_APPLY_FOG");
            return true;
        }

        static Texture2D SoftDot()
        {
            const int size = 32;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                name = "FlameDot",
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
                    float alpha = Mathf.Pow(Mathf.Clamp01(1f - Mathf.Sqrt(dx * dx + dy * dy)), 1.6f);
                    pixels[y * size + x] = new Color(1f, 1f, 1f, alpha);
                }
            }

            texture.SetPixels(pixels);
            texture.Apply();
            return texture;
        }
    }
}
