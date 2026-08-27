using UnityEngine;
using UnityEngine.UI;

namespace WowSandbox
{
    /// <summary>
    /// Hit points. Nothing deals damage yet except BreathController once you run out of
    /// air underwater — TakeDamage is public so combat, fall damage, etc. can hang off it
    /// later without this needing to know about them.
    ///
    /// The bar is built in code at runtime rather than as a Canvas prefab, the same
    /// reasoning as BreathController's bar and UnderwaterEffect's Volume: no UI asset to
    /// keep in sync with the rest of the sandbox. It sits directly above the breath bar.
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public class HealthController : MonoBehaviour
    {
        [Header("Health")]
        public float maxHealth = 100f;

        [Header("Bar")]
        public Vector2 barSize = new(220f, 18f);
        [Tooltip("Anchored from bottom-centre of the screen. Sits above the breath bar.")]
        public Vector2 barOffset = new(0f, 98f);
        public Color fullColor = new(0.2f, 0.75f, 0.25f);
        public Color lowColor = new(0.85f, 0.15f, 0.1f);
        public float fadeSpeed = 4f;

        CharacterController _controller;
        BreathController _breath;
        float _health;
        Vector3 _spawnPosition;
        Quaternion _spawnRotation;

        CanvasGroup _canvasGroup;
        Image _fill;

        static Sprite _whiteSprite;

        /// <summary>Current health as a 0-1 fraction of <see cref="maxHealth"/>.</summary>
        public float Health01 => maxHealth > 0f ? Mathf.Clamp01(_health / maxHealth) : 1f;

        /// <summary>True once health has hit zero and Die() has fired for it.</summary>
        public bool IsDead { get; private set; }

        void Awake()
        {
            _controller = GetComponent<CharacterController>();
            _breath = GetComponent<BreathController>();
            _health = maxHealth;
            _spawnPosition = transform.position;
            _spawnRotation = transform.rotation;
            BuildBar();
        }

        void Update()
        {
            UpdateBar();
        }

        /// <summary>Reduces health by <paramref name="amount"/>, dying at zero.</summary>
        public void TakeDamage(float amount)
        {
            if (IsDead || amount <= 0f)
                return;

            _health = Mathf.Max(0f, _health - amount);
            if (_health <= 0f)
                Die();
        }

        /// <summary>Restores health, capped at <see cref="maxHealth"/>.</summary>
        public void Heal(float amount)
        {
            if (IsDead || amount <= 0f)
                return;

            _health = Mathf.Min(maxHealth, _health + amount);
        }

        /// <summary>
        /// No death state to show yet, so dying just sends you back to spawn at full health —
        /// same "you died, try again" the breath meter used before it started feeding damage
        /// through here instead.
        /// </summary>
        void Die()
        {
            IsDead = true;
            Debug.Log("[HealthController] Died — respawning.", this);

            _controller.enabled = false;
            transform.SetPositionAndRotation(_spawnPosition, _spawnRotation);
            _controller.enabled = true;

            _health = maxHealth;
            if (_breath != null)
                _breath.Refill();

            IsDead = false;
        }

        void UpdateBar()
        {
            float targetAlpha = Health01 < 0.999f ? 1f : 0.6f;
            _canvasGroup.alpha = Mathf.MoveTowards(_canvasGroup.alpha, targetAlpha, fadeSpeed * Time.deltaTime);

            _fill.fillAmount = Health01;
            _fill.color = Color.Lerp(lowColor, fullColor, Health01);
        }

        void BuildBar()
        {
            var canvasGO = new GameObject("HealthBarCanvas") { hideFlags = HideFlags.DontSave };
            canvasGO.transform.SetParent(transform, false);

            var canvas = canvasGO.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 50;

            var scaler = canvasGO.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);

            canvasGO.AddComponent<GraphicRaycaster>();

            _canvasGroup = canvasGO.AddComponent<CanvasGroup>();
            _canvasGroup.alpha = 0.6f;
            _canvasGroup.interactable = false;
            _canvasGroup.blocksRaycasts = false;

            var sprite = WhiteSprite();

            var backGO = new GameObject("Background");
            backGO.transform.SetParent(canvasGO.transform, false);
            var backRect = backGO.AddComponent<RectTransform>();
            backRect.anchorMin = backRect.anchorMax = new Vector2(0.5f, 0f);
            backRect.pivot = new Vector2(0.5f, 0f);
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
