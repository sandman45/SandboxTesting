using UnityEngine;
using UnityEngine.UI;

namespace WowSandbox
{
    /// <summary>
    /// Top-left frame showing the current target's name and health. Built in code at
    /// runtime, the same reasoning as HealthController's and BreathController's own bars:
    /// no Canvas asset to keep in sync with the rest of the sandbox. Hidden whenever
    /// TargetingController has nothing selected.
    /// </summary>
    [RequireComponent(typeof(TargetingController))]
    public class TargetFrameHud : MonoBehaviour
    {
        [Header("Frame")]
        public Vector2 barSize = new(220f, 18f);
        [Tooltip("Anchored from the top-left of the screen.")]
        public Vector2 barOffset = new(20f, -44f);
        public Color fullColor = new(0.2f, 0.75f, 0.25f);
        public Color lowColor = new(0.85f, 0.15f, 0.1f);
        public float fadeSpeed = 6f;

        TargetingController _targeting;
        CanvasGroup _canvasGroup;
        Image _fill;
        Text _nameLabel;

        static Sprite _whiteSprite;

        void Awake()
        {
            _targeting = GetComponent<TargetingController>();
            BuildFrame();
        }

        void Update()
        {
            var target = _targeting.Target;
            bool visible = target != null;

            float targetAlpha = visible ? 1f : 0f;
            _canvasGroup.alpha = Mathf.MoveTowards(_canvasGroup.alpha, targetAlpha, fadeSpeed * Time.deltaTime);

            if (!visible)
                return;

            _nameLabel.text = _targeting.TargetName;
            _fill.fillAmount = target.Health01;
            _fill.color = Color.Lerp(lowColor, fullColor, target.Health01);
        }

        void BuildFrame()
        {
            var canvasGO = new GameObject("TargetFrameCanvas") { hideFlags = HideFlags.DontSave };
            canvasGO.transform.SetParent(transform, false);

            var canvas = canvasGO.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 55;

            var scaler = canvasGO.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);

            canvasGO.AddComponent<GraphicRaycaster>();

            _canvasGroup = canvasGO.AddComponent<CanvasGroup>();
            _canvasGroup.alpha = 0f;
            _canvasGroup.interactable = false;
            _canvasGroup.blocksRaycasts = false;

            var sprite = WhiteSprite();

            var nameGO = new GameObject("Name");
            nameGO.transform.SetParent(canvasGO.transform, false);
            var nameRect = nameGO.AddComponent<RectTransform>();
            nameRect.anchorMin = nameRect.anchorMax = new Vector2(0f, 1f);
            nameRect.pivot = new Vector2(0f, 1f);
            nameRect.anchoredPosition = barOffset + new Vector2(0f, 22f);
            nameRect.sizeDelta = new Vector2(barSize.x, 20f);
            _nameLabel = nameGO.AddComponent<Text>();
            _nameLabel.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            _nameLabel.fontSize = 16;
            _nameLabel.color = Color.white;
            _nameLabel.alignment = TextAnchor.LowerLeft;

            var backGO = new GameObject("Background");
            backGO.transform.SetParent(canvasGO.transform, false);
            var backRect = backGO.AddComponent<RectTransform>();
            backRect.anchorMin = backRect.anchorMax = new Vector2(0f, 1f);
            backRect.pivot = new Vector2(0f, 1f);
            backRect.anchoredPosition = barOffset;
            backRect.sizeDelta = barSize + new Vector2(4f, 4f);
            var backImage = backGO.AddComponent<Image>();
            backImage.sprite = sprite;
            backImage.color = new Color(0f, 0f, 0f, 0.6f);

            var fillGO = new GameObject("Fill");
            fillGO.transform.SetParent(backGO.transform, false);
            var fillRect = fillGO.AddComponent<RectTransform>();
            fillRect.anchorMin = Vector2.zero;
            fillRect.anchorMax = Vector2.one;
            fillRect.offsetMin = new Vector2(2f, 2f);
            fillRect.offsetMax = new Vector2(-2f, -2f);
            _fill = fillGO.AddComponent<Image>();
            _fill.sprite = sprite;
            _fill.type = Image.Type.Filled;
            _fill.fillMethod = Image.FillMethod.Horizontal;
            _fill.fillOrigin = (int)Image.OriginHorizontal.Left;
            _fill.color = fullColor;
        }

        /// <summary>A 1x1 white pixel to tint, so the bar needs no sprite asset either.</summary>
        static Sprite WhiteSprite()
        {
            if (_whiteSprite != null)
                return _whiteSprite;

            var texture = new Texture2D(1, 1) { hideFlags = HideFlags.DontSave };
            texture.SetPixel(0, 0, Color.white);
            texture.Apply();

            _whiteSprite = Sprite.Create(texture, new Rect(0f, 0f, 1f, 1f), new Vector2(0.5f, 0.5f));
            _whiteSprite.hideFlags = HideFlags.DontSave;
            return _whiteSprite;
        }
    }
}
