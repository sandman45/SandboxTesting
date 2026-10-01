using UnityEngine;
using UnityEngine.UI;

namespace WowSandbox
{
    /// <summary>
    /// Held breath while the head is underwater. Drains while submerged, refills at the
    /// surface, and once it runs out feeds damage into HealthController for as long as
    /// you stay under — dying, and the respawn that follows, is that component's call.
    ///
    /// Submersion is measured independently of WowCharacterController's swim state —
    /// swimming starts at the waist so you can wade and swim with your head in the air.
    /// This asks WaterVolume the same "how far under" question at head height instead,
    /// the same pattern WaterVolume's own doc comment describes the controller and camera
    /// already using.
    ///
    /// The bar is built in code rather than as a Canvas prefab, so there's no UI asset to
    /// keep in sync with the rest of the sandbox — the same reasoning as UnderwaterEffect
    /// building its own Volume at runtime.
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public class BreathController : MonoBehaviour
    {
        [Header("Breath")]
        [Tooltip("Seconds of held breath before you start drowning.")]
        public float maxBreath = 25f;
        [Tooltip("Seconds to fully refill breath once your head clears the surface.")]
        public float refillTime = 5f;
        [Tooltip("Fraction of the capsule's height the head sits at, for the submersion check.")]
        [Range(0.5f, 1f)] public float headHeightFraction = 0.92f;
        [Tooltip("Health lost per second once breath hits zero and you're still under. Only " +
                 "applies if a HealthController is present; otherwise drowning just respawns " +
                 "you at the last breath, same as before health existed.")]
        public float drowningDamagePerSecond = 12f;

        [Header("Bar")]
        public Vector2 barSize = new(220f, 18f);
        [Tooltip("Anchored from bottom-centre of the screen.")]
        public Vector2 barOffset = new(0f, 70f);
        public Color fullColor = new(0.25f, 0.55f, 0.95f);
        public Color lowColor = new(0.85f, 0.15f, 0.1f);
        public float fadeSpeed = 4f;

        CharacterController _controller;
        HealthController _health;
        float _breath;
        // Fallback only: used to respawn directly if there's no HealthController to hand
        // drowning damage to. Kept updated regardless, so it's ready if that ever happens.
        Vector3 _lastSafePosition;
        Quaternion _lastSafeRotation;

        CanvasGroup _canvasGroup;
        Image _fill;

        static Sprite _whiteSprite;

        /// <summary>Current breath as a 0-1 fraction of <see cref="maxBreath"/>.</summary>
        public float Breath01 => maxBreath > 0f ? Mathf.Clamp01(_breath / maxBreath) : 1f;

        void Awake()
        {
            _controller = GetComponent<CharacterController>();
            _health = GetComponent<HealthController>();
            _breath = maxBreath;
            _lastSafePosition = transform.position;
            _lastSafeRotation = transform.rotation;
            BuildBar();
        }

        void Update()
        {
            bool submerged = IsHeadSubmerged();

            if (submerged)
            {
                _breath -= Time.deltaTime;
                if (_breath <= 0f)
                {
                    _breath = 0f;
                    Drown();
                }
            }
            else
            {
                _lastSafePosition = transform.position;
                _lastSafeRotation = transform.rotation;
                _breath = Mathf.Min(maxBreath, _breath + maxBreath / Mathf.Max(refillTime, 0.01f) * Time.deltaTime);
            }

            UpdateBar(submerged);
        }

        /// <summary>Tops breath back up. Called by HealthController after it respawns you.</summary>
        public void Refill() => _breath = maxBreath;

        bool IsHeadSubmerged()
        {
            var water = WaterVolume.Containing(transform.position);
            if (water == null)
                return false;

            // transform.position is at the feet (see WowCharacterController), so head height
            // is a fraction of the capsule up from there.
            float headY = transform.position.y + _controller.height * headHeightFraction;
            return headY < water.SurfaceHeightAt(transform.position);
        }

        /// <summary>
        /// Out of air. Feeds damage into HealthController for as long as you stay under with
        /// no breath left — dying is its call to make, and it refills breath via Refill()
        /// once it respawns you. Without a HealthController this falls back to the old
        /// behaviour: straight back to the last spot you had your head above water.
        /// </summary>
        void Drown()
        {
            if (_health != null)
            {
                _health.TakeDamage(drowningDamagePerSecond * Time.deltaTime);
                return;
            }

            Debug.Log("[BreathController] Ran out of air — respawning at the last breath.", this);

            _controller.enabled = false;
            transform.SetPositionAndRotation(_lastSafePosition, _lastSafeRotation);
            _controller.enabled = true;

            _breath = maxBreath;
        }

        void UpdateBar(bool submerged)
        {
            bool visible = submerged || Breath01 < 0.999f;
            float targetAlpha = visible ? 1f : 0f;
            _canvasGroup.alpha = Mathf.MoveTowards(_canvasGroup.alpha, targetAlpha, fadeSpeed * Time.deltaTime);

            _fill.fillAmount = Breath01;
            _fill.color = Color.Lerp(lowColor, fullColor, Breath01);
        }

        void BuildBar()
        {
            var canvasGO = new GameObject("BreathBarCanvas") { hideFlags = HideFlags.DontSave };
            canvasGO.transform.SetParent(transform, false);

            var canvas = canvasGO.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 50;

            var scaler = canvasGO.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);

            canvasGO.AddComponent<GraphicRaycaster>();

            _canvasGroup = canvasGO.AddComponent<CanvasGroup>();
            _canvasGroup.alpha = 0f;
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
