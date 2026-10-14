using UnityEngine;
using UnityEngine.InputSystem;

namespace WowSandbox
{
    /// <summary>
    /// WoW-style third-person camera. Left-drag orbits the camera alone; right-drag
    /// steers the character and the camera rides along behind it.
    ///
    /// The camera's yaw is stored as an offset from the character's facing rather than
    /// as a world angle, which is what makes both behaviours fall out naturally: turning
    /// the character with A/D or right-drag moves the camera with it, while left-drag
    /// changes only the offset.
    /// </summary>
    public class WowOrbitCamera : MonoBehaviour
    {
        [Header("Target")]
        public Transform target;
        [Tooltip("Height above the target's pivot to look at, in world units.")]
        public float targetHeight = 1.6f;

        [Header("Distance")]
        public float distance = 6f;
        public float minDistance = 1.5f;
        public float maxDistance = 15f;
        public float zoomSpeed = 1.5f;

        [Header("Rotation")]
        public float mouseSensitivity = 0.18f;
        public float minPitch = -20f;
        public float maxPitch = 75f;
        [Tooltip("Starting pitch, in degrees below the horizon.")]
        public float startPitch = 15f;
        [Tooltip("Pixels the left button has to move before it counts as an orbit drag " +
                 "rather than a click. Below this the cursor stays put, so left-clicking to " +
                 "target or attack doesn't warp it to the centre of the screen.")]
        public float leftClickDragThreshold = 4f;

        [Header("Collision")]
        public bool avoidGeometry = true;
        public LayerMask collisionMask = ~0;
        [Tooltip("Keep the camera this far off any surface it bumps into.")]
        public float collisionPadding = 0.25f;

        float _yawOffset;   // degrees, relative to the target's forward
        float _pitch;
        float _currentDistance;

        // Tracks whether the current left-button hold has moved past the click threshold
        // yet, so a plain click never engages the cursor lock a drag needs.
        bool _leftDragConfirmed;
        bool _leftPressOnUi;
        bool _rightPressOnUi;
        float _leftDragDistance;

        WowCharacterController _character;

        // What Follow replaced, for Restore to put back. _saved is false while following the player.
        bool _saved;
        Transform _savedTarget;
        float _savedHeight, _savedDistance, _savedMin, _savedMax, _savedZoomSpeed, _savedPitch;
        bool _savedAvoid;
        bool _freeOrbit;    // right-drag orbits too, rather than turning the target

        /// <summary>
        /// Looks at something other than the player for a while — the ship, from the helm — at
        /// its own distances. Right-drag orbits rather than turning what it follows, and it
        /// doesn't pull in for geometry, or the masts and sails would yank it about.
        /// </summary>
        public void Follow(Transform newTarget, float height, float newDistance, float min, float max)
        {
            if (!_saved)
            {
                _saved = true;
                _savedTarget = target;
                _savedHeight = targetHeight;
                _savedDistance = distance;
                _savedMin = minDistance;
                _savedMax = maxDistance;
                _savedZoomSpeed = zoomSpeed;
                _savedPitch = _pitch;
                _savedAvoid = avoidGeometry;
            }

            target = newTarget;
            targetHeight = height;
            minDistance = min;
            maxDistance = max;
            distance = Mathf.Clamp(newDistance, min, max);
            // Scroll steps that suit the bigger range.
            zoomSpeed = Mathf.Max(_savedZoomSpeed, (max - min) / 12f);
            avoidGeometry = false;
            _freeOrbit = true;
            _yawOffset = 0f;
            _pitch = Mathf.Clamp(20f, minPitch, maxPitch);
        }

        /// <summary>
        /// The zoom-out limit and scroll step for following the player (Settings). At the helm
        /// they're kept for when you leave it, so the ship's own range isn't disturbed.
        /// </summary>
        public void SetPlayerZoom(float max, float speed)
        {
            // A limit below the closest zoom pulls that in too (down to 0, the developer
            // panel's floor), and raising it again gives the original closest zoom back.
            if (_playerMinDistance < 0f)
                _playerMinDistance = _saved ? _savedMin : minDistance;
            max = Mathf.Max(max, 0f);
            float min = Mathf.Min(_playerMinDistance, max);

            if (_saved)
            {
                _savedMin = min;
                _savedMax = max;
                _savedDistance = Mathf.Clamp(_savedDistance, min, max);
                _savedZoomSpeed = speed;
                return;
            }

            minDistance = min;
            maxDistance = max;
            distance = Mathf.Clamp(distance, min, max);
            zoomSpeed = speed;
        }

        float _playerMinDistance = -1f;

        /// <summary>Back to following the player as before Follow.</summary>
        public void Restore()
        {
            if (!_saved)
                return;

            _saved = false;
            target = _savedTarget;
            targetHeight = _savedHeight;
            distance = _savedDistance;
            minDistance = _savedMin;
            maxDistance = _savedMax;
            zoomSpeed = _savedZoomSpeed;
            avoidGeometry = _savedAvoid;
            _freeOrbit = false;
            _yawOffset = 0f;
            _pitch = _savedPitch;
            _currentDistance = distance;
        }

        void Start()
        {
            _pitch = startPitch;
            _currentDistance = distance;
            if (target != null)
                _character = target.GetComponent<WowCharacterController>();
        }

        void LateUpdate()
        {
            if (target == null)
                return;

            var mouse = Mouse.current;
            if (mouse != null)
            {
                // A press that starts on a HUD panel belongs to the panel for as long as it's
                // held, even if the mouse then wanders off it mid-drag.
                if (mouse.leftButton.wasPressedThisFrame)
                    _leftPressOnUi = Hud.PointerOverUi;
                if (mouse.rightButton.wasPressedThisFrame)
                    _rightPressOnUi = Hud.PointerOverUi;

                bool leftHeld = mouse.leftButton.isPressed && !_leftPressOnUi;
                bool steering = mouse.rightButton.isPressed && !_rightPressOnUi;
                Vector2 rawDelta = mouse.delta.ReadValue();

                if (mouse.leftButton.wasPressedThisFrame)
                {
                    _leftDragConfirmed = false;
                    _leftDragDistance = 0f;
                }

                if (leftHeld && !_leftDragConfirmed)
                {
                    _leftDragDistance += rawDelta.magnitude;
                    if (_leftDragDistance >= leftClickDragThreshold)
                        _leftDragConfirmed = true;
                }

                // Left only orbits once the hold has proven itself a drag; right always does
                // — steering has no click-only meaning of its own to protect.
                bool orbiting = leftHeld && _leftDragConfirmed;

                if (orbiting || steering)
                {
                    Vector2 delta = rawDelta * mouseSensitivity;

                    if (steering && !_freeOrbit)
                    {
                        // Right-drag turns the character; the offset stays put so the
                        // camera keeps its position behind them.
                        target.Rotate(Vector3.up, delta.x, Space.World);
                    }
                    else
                    {
                        _yawOffset += delta.x;
                    }

                    _pitch = Mathf.Clamp(_pitch - delta.y, minPitch, maxPitch);
                }

                Cursor.lockState = (orbiting || steering) ? CursorLockMode.Locked : CursorLockMode.None;
                Cursor.visible = !(orbiting || steering);

                // Over a panel the wheel is the panel's — the world map zooms with it.
                float scroll = Hud.PointerOverUi ? 0f : mouse.scroll.ReadValue().y;
                if (Mathf.Abs(scroll) > 0.01f)
                    distance = Mathf.Clamp(distance - Mathf.Sign(scroll) * zoomSpeed, minDistance, maxDistance);
            }

            Vector3 pivot = target.position + Vector3.up * targetHeight;
            Quaternion rotation = Quaternion.Euler(_pitch, target.eulerAngles.y + _yawOffset, 0f);
            Vector3 desired = pivot - rotation * Vector3.forward * distance;

            float wanted = distance;
            if (avoidGeometry)
            {
                Vector3 dir = (desired - pivot).normalized;
                if (Physics.SphereCast(pivot, collisionPadding, dir, out RaycastHit hit, distance, collisionMask, QueryTriggerInteraction.Ignore))
                    wanted = Mathf.Max(hit.distance - collisionPadding, minDistance);
            }

            // Pull in instantly so geometry never clips, but ease back out.
            _currentDistance = wanted < _currentDistance
                ? wanted
                : Mathf.Lerp(_currentDistance, wanted, Time.deltaTime * 4f);

            transform.position = pivot - rotation * Vector3.forward * _currentDistance;
            transform.rotation = rotation;
        }
    }
}
