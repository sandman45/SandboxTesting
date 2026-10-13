using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace WowSandbox
{
    /// <summary>
    /// The ship's wheel. Click it from close by and you take the helm: you're stood at
    /// <see cref="stand"/> behind it and held there, the camera pulls back to look at the whole
    /// ship, and W/S let out and take in sail while A/D steer (ShipSailing does the sailing).
    /// A panel at the bottom of the screen shows the sails and speed, with buttons to drop or
    /// raise the anchor and to leave the helm. Leaving keeps the sails as they are — the ship
    /// sails on, unmanned, holding its course; drop the anchor first to stop it.
    ///
    /// The wheel is part of the ship's hull mesh, so this is an invisible click box placed over
    /// it (WoW Sandbox → Add Ship Helm), facing the bow: its forward is what tells the ship
    /// which way is ahead.
    /// </summary>
    [RequireComponent(typeof(BoxCollider))]
    // After the ship (-60/-50) and the player (0) have moved, so you're placed on the deck
    // where it is this frame.
    [DefaultExecutionOrder(10)]
    public class ShipHelm : MonoBehaviour
    {
        [Tooltip("Where you stand while steering — behind the wheel.")]
        public Transform stand;
        [Tooltip("How close to the wheel you have to be to take it.")]
        public float useRange = 6f;

        [Header("Camera")]
        [Tooltip("How far back the camera starts, as a multiple of half the ship's length.")]
        public float cameraDistance = 1.6f;
        [Tooltip("Closest and furthest the camera can zoom, as multiples of half the ship's length.")]
        public Vector2 cameraZoom = new Vector2(0.6f, 3.5f);
        [Tooltip("How high above the deck at the helm the camera looks, as a multiple of the player's height.")]
        public float cameraLookHeight = 3f;

        ShipSailing _sailing;
        BoxCollider _box;
        WowCharacterController _player;
        WowOrbitCamera _camera;
        Transform _cameraAnchor;
        bool _driving;

        RectTransform _panel;
        Text _sailText;
        Text _speedText;
        Image _sailBar;
        Image _anchorBack;
        Text _anchorLabel;

        public bool IsDriving => _driving;

        void Awake()
        {
            _box = GetComponent<BoxCollider>();
            _box.isTrigger = true;  // clickable, but nothing bumps into it
            _sailing = GetComponentInParent<ShipSailing>();
            if (_sailing == null)
            {
                var ship = GetComponentInParent<ShipBuoyancy>();
                if (ship != null)
                    _sailing = ship.gameObject.AddComponent<ShipSailing>();
            }
        }

        void Start()
        {
            if (_sailing == null)
            {
                Debug.LogWarning("[ShipHelm] Not on a floating ship (no ShipBuoyancy above it), so there's nothing to steer.", this);
                enabled = false;
                return;
            }
            _sailing.SetBow(transform.forward);
        }

        void Update()
        {
            if (_driving)
            {
                Steer();
                return;
            }

            var mouse = Mouse.current;
            if (mouse != null && mouse.leftButton.wasPressedThisFrame && !Hud.PointerOverUi && ClickedOn(mouse))
                TakeHelm();
        }

        /// <summary>
        /// Did that click land on the wheel? The box sits over the wheel's own geometry, which
        /// the ray can hit first from some angles, so anything solid only counts as in the way
        /// if it's well in front of the box.
        /// </summary>
        bool ClickedOn(Mouse mouse)
        {
            var cam = Camera.main;
            if (cam == null)
                return false;

            var ray = cam.ScreenPointToRay(mouse.position.ReadValue());
            float helm = float.MaxValue, blocker = float.MaxValue;
            var player = FindPlayer();
            foreach (var hit in Physics.RaycastAll(ray, 500f, ~0, QueryTriggerInteraction.Collide))
            {
                if (hit.collider == _box)
                    helm = hit.distance;
                else if (!hit.collider.isTrigger && (player == null || !hit.collider.transform.IsChildOf(player.transform)))
                    blocker = Mathf.Min(blocker, hit.distance);
            }

            return helm < float.MaxValue && helm <= blocker + 1.5f;
        }

        WowCharacterController FindPlayer()
        {
            if (_player == null)
                _player = FindAnyObjectByType<WowCharacterController>();
            return _player;
        }

        void TakeHelm()
        {
            var player = FindPlayer();
            if (player == null || !player.enabled || player.IsSwimming || player.IsClimbing)
                return;

            var standAt = stand != null ? stand.position : transform.position;
            if (Vector3.Distance(player.transform.position, standAt) > useRange)
            {
                Hud.Error("You are too far away.");
                return;
            }

            _driving = true;
            player.SetLocked(true);
            SnapStandToDeck(player);
            PlacePlayer();

            _camera = Camera.main != null ? Camera.main.GetComponent<WowOrbitCamera>() : null;
            if (_camera != null)
            {
                _cameraAnchor = new GameObject("HelmCameraAnchor") { hideFlags = HideFlags.DontSave }.transform;
                MoveCameraAnchor();
                float half = Mathf.Max(_sailing.HalfLength, 5f);
                float height = player.GetComponent<CharacterController>() is { } capsule ? capsule.height : 2f;
                _camera.Follow(_cameraAnchor, height * cameraLookHeight, half * cameraDistance,
                               half * cameraZoom.x, half * cameraZoom.y);
            }

            ShowPanel(true);
            Hud.Info(_sailing.anchored
                ? "You take the helm. Raise the anchor, then W/S to set sail and A/D to steer."
                : "You take the helm. W/S to set sail, A/D to steer.");
        }

        public void LeaveHelm()
        {
            if (!_driving)
                return;

            _driving = false;
            _sailing.Throttle = 0f;
            _sailing.Steer = 0f;
            ShowPanel(false);

            if (_camera != null)
                _camera.Restore();
            if (_cameraAnchor != null)
                Destroy(_cameraAnchor.gameObject);

            if (_player != null)
                _player.SetLocked(false);
        }

        void ToggleAnchor()
        {
            _sailing.anchored = !_sailing.anchored;
            Hud.Info(_sailing.anchored ? "Anchor dropped." : "Anchor raised.");
        }

        void Steer()
        {
            if (_player == null || !_player.isActiveAndEnabled)
            {
                LeaveHelm();
                return;
            }

            var keyboard = Keyboard.current;
            float throttle = 0f, steer = 0f;
            if (keyboard != null)
            {
                if (keyboard.wKey.isPressed || keyboard.upArrowKey.isPressed) throttle += 1f;
                if (keyboard.sKey.isPressed || keyboard.downArrowKey.isPressed) throttle -= 1f;
                if (keyboard.dKey.isPressed || keyboard.rightArrowKey.isPressed) steer += 1f;
                if (keyboard.aKey.isPressed || keyboard.leftArrowKey.isPressed) steer -= 1f;

                if (_sailing.anchored && keyboard.wKey.wasPressedThisFrame)
                    Hud.Error("The anchor is down — raise it first.");
            }

            _sailing.Throttle = throttle;
            _sailing.Steer = steer;

            PlacePlayer();
            MoveCameraAnchor();
            UpdatePanel();
        }

        void OnDisable() => LeaveHelm();

        /// <summary>
        /// Puts the stand spot on the deck surface, so a Stand placed a little into the planks
        /// (or hovering over them) still has you standing on top. Searches from well above it
        /// down to a little below, for the ship's own solid colliders.
        /// </summary>
        void SnapStandToDeck(WowCharacterController player)
        {
            if (stand == null)
                return;

            float height = player.GetComponent<CharacterController>() is { } capsule ? capsule.height : 2f;
            var ship = _sailing.transform;
            Vector3 from = stand.position + Vector3.up * height;
            float best = float.MaxValue;
            Vector3 deck = stand.position;
            foreach (var hit in Physics.RaycastAll(from, Vector3.down, height * 1.6f, ~0, QueryTriggerInteraction.Ignore))
            {
                if (!hit.collider.transform.IsChildOf(ship) || hit.normal.y < 0.6f || hit.distance >= best)
                    continue;
                best = hit.distance;
                deck = hit.point;
            }

            if (best < float.MaxValue)
                stand.position = deck + Vector3.up * 0.05f;
        }

        void PlacePlayer()
        {
            var standAt = stand != null ? stand.position : transform.position - transform.forward;
            _player.transform.SetPositionAndRotation(standAt, Quaternion.LookRotation(_sailing.Bow, Vector3.up));
            Physics.SyncTransforms();
        }

        /// <summary>Level, over the middle of the ship at the helm's deck height, facing the bow —
        /// so the camera sits astern and swings round with the ship, without its pitch and roll.</summary>
        void MoveCameraAnchor()
        {
            if (_cameraAnchor == null)
                return;
            var centre = _sailing.Centre;
            var standAt = stand != null ? stand.position : transform.position;
            _cameraAnchor.SetPositionAndRotation(new Vector3(centre.x, standAt.y, centre.z),
                                                 Quaternion.LookRotation(_sailing.Bow, Vector3.up));
        }

        // --- Panel ---------------------------------------------------------------------------

        void ShowPanel(bool show)
        {
            if (show && _panel == null)
                BuildPanel();
            if (_panel != null)
                _panel.gameObject.SetActive(show);
            if (show)
                UpdatePanel();
        }

        void BuildPanel()
        {
            _panel = Hud.Rect("HelmPanel", Hud.Layer(HudLayer.Frames), new Vector2(0.5f, 0f), new Vector2(0f, 24f),
                              new Vector2(340f, 92f));
            Hud.Panel(_panel);

            var topLeft = new Vector2(0f, 1f);
            Hud.Label("Title", _panel, topLeft, new Vector2(14f, -8f), new Vector2(200f, 20f), 15,
                      TextAnchor.MiddleLeft, HudTheme.Heading, FontStyle.Bold).text = "Helm";
            _sailText = Hud.Label("Sails", _panel, topLeft, new Vector2(14f, -30f), new Vector2(200f, 18f), 13,
                                  TextAnchor.MiddleLeft, HudTheme.Text);
            _speedText = Hud.Label("Speed", _panel, topLeft, new Vector2(14f, -68f), new Vector2(200f, 18f), 12,
                                   TextAnchor.MiddleLeft, HudTheme.TextDim);

            var barBack = Hud.Rect("SailBar", _panel, topLeft, new Vector2(14f, -50f), new Vector2(190f, 10f));
            Hud.Rounded(barBack.gameObject, HudTheme.BarBack, 3f);
            var fill = Hud.Stretch("Fill", barBack, 1f);
            _sailBar = fill.gameObject.AddComponent<Image>();
            _sailBar.color = HudTheme.Heading;
            _sailBar.raycastTarget = false;

            (_anchorBack, _anchorLabel) = IconButton("Anchor", Hud.Anchor, new Vector2(-82f, 0f), ToggleAnchor);
            IconButton("Leave", Hud.LeaveHelm, new Vector2(-16f, 0f), LeaveHelm).label.text = "Leave";
        }

        (Image back, Text label) IconButton(string name, Sprite icon, Vector2 offsetFromRight,
                                            UnityEngine.Events.UnityAction onClick)
        {
            var rect = Hud.Rect(name, _panel, new Vector2(1f, 0.5f), offsetFromRight + new Vector2(0f, 8f), new Vector2(54f, 54f));
            var back = Hud.Rounded(rect.gameObject, new Color(0.18f, 0.15f, 0.11f, 0.95f), 6f);
            back.raycastTarget = true;
            var border = Hud.Ring(Hud.Stretch("Border", rect).gameObject, HudTheme.PanelBorder, 6f);
            border.raycastTarget = false;

            var image = Hud.Stretch("Icon", rect, 8f).gameObject.AddComponent<Image>();
            image.sprite = icon;
            image.color = HudTheme.Text;
            image.raycastTarget = false;

            var label = Hud.Label("Label", rect, new Vector2(0.5f, 0f), new Vector2(0f, -16f), new Vector2(80f, 14f), 11,
                                  TextAnchor.MiddleCenter, HudTheme.TextDim);

            var button = rect.gameObject.AddComponent<Button>();
            button.targetGraphic = back;
            button.onClick.AddListener(onClick);
            return (back, label);
        }

        void UpdatePanel()
        {
            if (_panel == null)
                return;

            float sail = _sailing.Sail;
            _sailText.text = _sailing.anchored ? "Sails: furled (at anchor)" : $"Sails: {SailName(sail)}";
            // Grown from the left edge by anchor, rather than a Filled image (which needs a sprite).
            _sailBar.rectTransform.anchorMax = new Vector2(Mathf.Clamp01(Mathf.Abs(sail)), 1f);
            _sailBar.color = sail < 0f ? HudTheme.Critical : HudTheme.Heading;

            float fraction = _sailing.Speed / Mathf.Max(_sailing.MaxSpeed, 0.01f);
            _speedText.text = Mathf.Abs(fraction) < 0.02f ? "Dead in the water"
                : fraction < 0f ? $"Going astern  {Mathf.Abs(fraction) * 100f:0}%"
                : $"Speed  {fraction * 100f:0}%";

            _anchorLabel.text = _sailing.anchored ? "Raise" : "Drop";
            _anchorBack.color = _sailing.anchored ? new Color(0.45f, 0.33f, 0.12f, 0.95f) : new Color(0.18f, 0.15f, 0.11f, 0.95f);
        }

        static string SailName(float sail) => sail switch
        {
            < -0.01f => "backed (astern)",
            < 0.05f => "furled",
            < 0.375f => "quarter sail",
            < 0.625f => "half sail",
            < 0.875f => "three-quarter sail",
            _ => "full sail",
        };

        void OnDrawGizmos()
        {
            var box = GetComponent<BoxCollider>();
            if (box == null)
                return;
            Gizmos.color = new Color(1f, 0.8f, 0.2f, 0.8f);
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.DrawWireCube(box.center, box.size);
            Gizmos.matrix = Matrix4x4.identity;
            // Which way it thinks the bow is.
            Gizmos.DrawLine(transform.position, transform.position + transform.forward * 4f);
            Gizmos.DrawWireSphere(transform.position + transform.forward * 4f, 0.25f);
            if (stand != null)
            {
                Gizmos.color = Color.cyan;
                Gizmos.DrawWireSphere(stand.position, 0.35f);
            }
        }
    }
}
