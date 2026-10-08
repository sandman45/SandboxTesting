using UnityEngine;
using UnityEngine.UI;

namespace WowSandbox
{
    /// <summary>Draw order inside the shared HUD canvas, back to front.</summary>
    public enum HudLayer
    {
        Nameplates = 0,
        CombatText = 1,
        Frames = 2,
    }

    /// <summary>
    /// Colours every HUD element pulls from, so the frames, nameplates and (later) panels
    /// read as one UI rather than each component picking its own.
    /// </summary>
    public static class HudTheme
    {
        public static readonly Color PanelBack = new(0.05f, 0.06f, 0.08f, 0.82f);
        public static readonly Color PanelBorder = new(0.62f, 0.52f, 0.32f, 0.9f);
        public static readonly Color BarBack = new(0f, 0f, 0f, 0.7f);
        public static readonly Color BarTrail = new(1f, 0.92f, 0.75f, 0.85f);

        public static readonly Color Text = new(0.96f, 0.94f, 0.88f);
        public static readonly Color TextDim = new(0.7f, 0.68f, 0.62f);
        public static readonly Color Dead = new(0.55f, 0.55f, 0.55f);
        public static readonly Color DamageFlash = new(0.95f, 0.15f, 0.1f, 1f);
        public static readonly Color DamageText = new(1f, 0.92f, 0.35f);

        public static readonly Color HealthFull = new(0.2f, 0.75f, 0.25f);
        public static readonly Color HealthLow = new(0.85f, 0.15f, 0.1f);
        public static readonly Color Breath = new(0.25f, 0.55f, 0.95f);

        public static readonly Color Hostile = new(0.92f, 0.22f, 0.16f);
        public static readonly Color Neutral = new(0.96f, 0.82f, 0.22f);
        public static readonly Color Friendly = new(0.32f, 0.85f, 0.32f);

        /// <summary>Green at full, sliding to red as it empties — kept green until half.</summary>
        public static Color HealthColor(float health01) =>
            Color.Lerp(HealthLow, HealthFull, Mathf.Clamp01(health01 * 2f));

        public static Color ReactionColor(Reaction reaction) => reaction switch
        {
            Reaction.Hostile => Hostile,
            Reaction.Friendly => Friendly,
            _ => Neutral,
        };
    }

    /// <summary>
    /// One shared screen-space canvas for every HUD element, plus the little builders they
    /// share. Still all built in code — the same no-UI-assets reasoning the bars used to
    /// carry individually — but the sprites, fonts and canvas now live in one place instead
    /// of each component making its own.
    ///
    /// The rounded-rect sprites are generated once as 9-slice textures, so frames and bars
    /// get clean corners at any size without shipping an image.
    /// </summary>
    public static class Hud
    {
        const int SpriteSize = 24;
        const float SpriteRadius = 8f;

        /// <summary>
        /// Overall HUD size. Everything is laid out in 1920x1080 units and the canvas is
        /// scaled by this on top, so frames, text, nameplates and damage numbers all grow
        /// together and stay lined up. Read when the canvas is built (start of Play).
        /// </summary>
        public static float Scale = 1.3f;

        static Canvas _canvas;
        static readonly RectTransform[] _layers = new RectTransform[3];
        static Sprite _rounded;
        static Sprite _ring;
        static Font _font;

        public static Font Font => _font != null ? _font : _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

        /// <summary>The full-screen rect for <paramref name="layer"/>, creating the canvas on first use.</summary>
        public static RectTransform Layer(HudLayer layer)
        {
            // Unity's fake-null covers the canvas having been destroyed with the play session.
            if (_canvas == null)
                BuildCanvas();
            return _layers[(int)layer];
        }

        const string CanvasName = "Hud";

        static void BuildCanvas()
        {
            DestroyStrays();

            // Deliberately no DontSave here, unlike the sprites: that flag also stops the
            // object being destroyed when Play stops, so the whole HUD would outlive the
            // session and stack up, one more copy per run.
            var go = new GameObject(CanvasName);

            _canvas = go.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 50;

            var scaler = go.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f) / Mathf.Max(Scale, 0.1f);
            scaler.matchWidthOrHeight = 0.5f;

            // Created in enum order, so sibling order is draw order.
            foreach (HudLayer layer in System.Enum.GetValues(typeof(HudLayer)))
                _layers[(int)layer] = Stretch(layer.ToString(), go.transform);
        }

        /// <summary>
        /// Removes HUD canvases left behind by an earlier session — a script reload during
        /// Play, or a build of this class that flagged the canvas DontSave. Those keep
        /// drawing their last frame forever otherwise, on top of the live one.
        /// </summary>
        static void DestroyStrays()
        {
            int removed = 0;
            foreach (var canvas in Resources.FindObjectsOfTypeAll<Canvas>())
            {
                var go = canvas.gameObject;
                if (go.name != CanvasName || go.transform.parent != null)
                    continue;

                // FindObjectsOfTypeAll also returns assets; a leaked DontSave object has no
                // valid scene either, so the flag is what tells the two apart.
                bool leaked = (go.hideFlags & HideFlags.DontSave) != 0;
                if (!go.scene.IsValid() && !leaked)
                    continue;

                Object.Destroy(go);
                removed++;
            }

            if (removed > 0)
                Debug.Log($"[Hud] Removed {removed} HUD canvas(es) left over from an earlier session.");
        }

        /// <summary>A rect pinned to <paramref name="anchor"/> (which is also its pivot).</summary>
        public static RectTransform Rect(string name, Transform parent, Vector2 anchor, Vector2 position, Vector2 size)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rect = (RectTransform)go.transform;
            rect.anchorMin = rect.anchorMax = rect.pivot = anchor;
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
            return rect;
        }

        /// <summary>A rect filling its parent, less <paramref name="inset"/> on every side.</summary>
        public static RectTransform Stretch(string name, Transform parent, float inset = 0f)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rect = (RectTransform)go.transform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(inset, inset);
            rect.offsetMax = new Vector2(-inset, -inset);
            return rect;
        }

        /// <summary>Adds a rounded-rect image with corners of roughly <paramref name="radius"/> canvas units.</summary>
        public static Image Rounded(GameObject go, Color color, float radius)
        {
            var image = go.AddComponent<Image>();
            image.sprite = RoundedSprite();
            image.type = Image.Type.Sliced;
            image.pixelsPerUnitMultiplier = SpriteRadius / Mathf.Max(radius, 0.5f);
            image.color = color;
            image.raycastTarget = false;
            return image;
        }

        /// <summary>
        /// Dark rounded backing plus a thin gilt border. Returns the border so callers can
        /// tint it — the frames flash it red when taking damage.
        /// </summary>
        public static Image Panel(RectTransform rect)
        {
            Rounded(rect.gameObject, HudTheme.PanelBack, SpriteRadius);

            var border = Stretch("Border", rect).gameObject.AddComponent<Image>();
            border.sprite = RingSprite();
            border.type = Image.Type.Sliced;
            border.color = HudTheme.PanelBorder;
            border.raycastTarget = false;
            return border;
        }

        /// <summary>Outlined text filling <paramref name="parent"/>; position it via its rectTransform.</summary>
        public static Text Label(string name, Transform parent, int fontSize, TextAnchor alignment, Color color,
            FontStyle style = FontStyle.Normal)
        {
            var text = Stretch(name, parent).gameObject.AddComponent<Text>();
            text.font = Font;
            text.fontSize = fontSize;
            text.fontStyle = style;
            text.alignment = alignment;
            text.color = color;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.raycastTarget = false;

            var outline = text.gameObject.AddComponent<Outline>();
            outline.effectColor = new Color(0f, 0f, 0f, 0.85f);
            outline.effectDistance = new Vector2(1f, -1f);
            return text;
        }

        static Sprite RoundedSprite() => _rounded != null ? _rounded : _rounded = MakeRoundedSprite(0f);
        static Sprite RingSprite() => _ring != null ? _ring : _ring = MakeRoundedSprite(1.5f);

        /// <summary>
        /// White anti-aliased rounded rect from a signed distance field — filled, or just an
        /// outline <paramref name="ringWidth"/> pixels thick. Borders are set for 9-slicing.
        /// </summary>
        static Sprite MakeRoundedSprite(float ringWidth)
        {
            var texture = new Texture2D(SpriteSize, SpriteSize, TextureFormat.RGBA32, false)
            {
                hideFlags = HideFlags.DontSave,
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
            };

            var pixels = new Color32[SpriteSize * SpriteSize];
            float half = SpriteSize * 0.5f;
            for (int y = 0; y < SpriteSize; y++)
            {
                for (int x = 0; x < SpriteSize; x++)
                {
                    float qx = Mathf.Abs(x + 0.5f - half) - (half - SpriteRadius);
                    float qy = Mathf.Abs(y + 0.5f - half) - (half - SpriteRadius);
                    float distance = new Vector2(Mathf.Max(qx, 0f), Mathf.Max(qy, 0f)).magnitude
                                     + Mathf.Min(Mathf.Max(qx, qy), 0f) - SpriteRadius;

                    float alpha = Mathf.Clamp01(0.5f - distance);
                    if (ringWidth > 0f)
                        alpha -= Mathf.Clamp01(0.5f - (distance + ringWidth));

                    pixels[y * SpriteSize + x] = new Color32(255, 255, 255, (byte)(Mathf.Clamp01(alpha) * 255f));
                }
            }

            texture.SetPixels32(pixels);
            texture.Apply();

            float border = SpriteRadius + 1f;
            var sprite = Sprite.Create(texture, new Rect(0f, 0f, SpriteSize, SpriteSize), new Vector2(0.5f, 0.5f),
                100f, 0, SpriteMeshType.FullRect, new Vector4(border, border, border, border));
            sprite.hideFlags = HideFlags.DontSave;
            return sprite;
        }
    }

    /// <summary>
    /// A rounded bar with a trailing "damage taken" segment: when the value drops, the lost
    /// chunk lingers pale for a moment and then drains, so a hit reads at a glance even when
    /// it's small. Plain class, driven by whichever component owns it calling Set each frame.
    /// </summary>
    public class HudBar
    {
        const float TrailDelay = 0.4f;
        const float TrailSpeed = 0.9f;

        public readonly RectTransform Rect;
        /// <summary>Centred text over the bar, or null if it was built without one.</summary>
        public readonly Text Label;

        readonly Image _fill;
        readonly Image _trail;
        float _value = 1f;
        float _trailValue = 1f;
        float _trailHold;

        public HudBar(string name, Transform parent, Vector2 anchor, Vector2 position, Vector2 size, int labelSize = 0)
        {
            Rect = Hud.Rect(name, parent, anchor, position, size);
            float radius = Mathf.Min(size.y * 0.5f, 5f);
            Hud.Rounded(Rect.gameObject, HudTheme.BarBack, radius);

            var inner = Hud.Stretch("Inner", Rect, 1.5f);
            _trail = Hud.Rounded(Hud.Stretch("Trail", inner).gameObject, HudTheme.BarTrail, radius - 1f);
            _fill = Hud.Rounded(Hud.Stretch("Fill", inner).gameObject, HudTheme.HealthFull, radius - 1f);

            if (labelSize > 0)
                Label = Hud.Label("Label", Rect, labelSize, TextAnchor.MiddleCenter, HudTheme.Text);
        }

        public Color FillColor
        {
            set => _fill.color = value;
        }

        /// <summary>Sets the fill and advances the trail. Call once per frame.</summary>
        public void Set(float value01)
        {
            value01 = Mathf.Clamp01(value01);
            if (value01 < _value - 0.0001f)
                _trailHold = TrailDelay;
            _value = value01;

            if (_trailHold > 0f)
                _trailHold -= Time.deltaTime;
            else
                _trailValue = Mathf.MoveTowards(_trailValue, _value, TrailSpeed * Time.deltaTime);
            // Healing jumps the trail straight up with the fill rather than leaving a gap.
            _trailValue = Mathf.Max(_trailValue, _value);

            Apply(_fill, _value);
            Apply(_trail, _trailValue);
        }

        /// <summary>Jumps to <paramref name="value01"/> with no trail — for switching what the bar shows.</summary>
        public void Snap(float value01)
        {
            _value = _trailValue = Mathf.Clamp01(value01);
            _trailHold = 0f;
            Apply(_fill, _value);
            Apply(_trail, _trailValue);
        }

        static void Apply(Image image, float value)
        {
            // A sliced sprite squeezed to zero width draws its corners anyway; hide it instead.
            image.enabled = value > 0.001f;
            image.rectTransform.anchorMax = new Vector2(value, 1f);
        }
    }
}
