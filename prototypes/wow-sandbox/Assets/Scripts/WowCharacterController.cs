using UnityEngine;
using UnityEngine.InputSystem;

namespace WowSandbox
{
    /// <summary>
    /// WoW-style character movement: W/S drive forward/back along the character's own
    /// facing, A/D turn in place (or strafe while the right mouse button steers), Q/E
    /// always strafe. Movement is character-relative, never camera-relative.
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public class WowCharacterController : MonoBehaviour
    {
        [Header("Movement")]
        [Tooltip("Default movement speed. WoW runs by default; the walk modifier slows you down.")]
        public float runSpeed = 7f;
        public float walkSpeed = 2.5f;
        [Tooltip("Strafe speed as a fraction of forward speed.")]
        [Range(0.1f, 1f)] public float strafeFactor = 0.8f;
        [Tooltip("Backpedal speed as a fraction of forward speed. WoW backpedals slowly.")]
        [Range(0.1f, 1f)] public float backpedalFactor = 0.45f;

        [Header("Turning")]
        [Tooltip("Degrees per second when turning with A/D.")]
        public float turnSpeed = 180f;

        [Header("Jump / Gravity")]
        public float jumpHeight = 1.4f;
        public float gravity = -25f;

        [Header("Animation")]
        [Tooltip("Blend tree thresholds are 0 = idle, 0.5 = walk, 1 = run.")]
        public float animationDamping = 0.12f;

        CharacterController _controller;
        Animator _animator;
        float _verticalVelocity;
        float _animSpeed;

        // Cached animator parameter hashes.
        static readonly int SpeedHash = Animator.StringToHash("Speed");
        static readonly int GroundedHash = Animator.StringToHash("Grounded");
        static readonly int JumpHash = Animator.StringToHash("Jump");
        static readonly int AttackHash = Animator.StringToHash("Attack");

        /// <summary>True while the right mouse button is steering the character.</summary>
        public bool IsSteering { get; private set; }

        void Awake()
        {
            _controller = GetComponent<CharacterController>();
            // The Animator usually lives on the imported glTF child, not the root.
            _animator = GetComponentInChildren<Animator>();
        }

        void Update()
        {
            var keyboard = Keyboard.current;
            var mouse = Mouse.current;
            if (keyboard == null)
                return;

            IsSteering = mouse != null && mouse.rightButton.isPressed;

            // --- Read input -------------------------------------------------
            float forward = 0f;
            if (keyboard.wKey.isPressed || keyboard.upArrowKey.isPressed) forward += 1f;
            if (keyboard.sKey.isPressed || keyboard.downArrowKey.isPressed) forward -= 1f;

            float strafe = 0f;
            if (keyboard.eKey.isPressed) strafe += 1f;
            if (keyboard.qKey.isPressed) strafe -= 1f;

            // A/D turn the character, but become strafe keys while right-mouse steering.
            float turn = 0f;
            float adAxis = 0f;
            if (keyboard.dKey.isPressed || keyboard.rightArrowKey.isPressed) adAxis += 1f;
            if (keyboard.aKey.isPressed || keyboard.leftArrowKey.isPressed) adAxis -= 1f;
            if (IsSteering)
                strafe += adAxis;
            else
                turn = adAxis;

            strafe = Mathf.Clamp(strafe, -1f, 1f);

            bool walking = keyboard.leftShiftKey.isPressed || keyboard.rightShiftKey.isPressed;

            // --- Turn -------------------------------------------------------
            if (Mathf.Abs(turn) > 0.01f)
                transform.Rotate(Vector3.up, turn * turnSpeed * Time.deltaTime, Space.World);

            // --- Horizontal movement ---------------------------------------
            float baseSpeed = walking ? walkSpeed : runSpeed;
            // Backpedalling is slower than moving forward.
            float forwardSpeed = forward >= 0f ? baseSpeed : baseSpeed * backpedalFactor;

            Vector3 move = transform.forward * (forward * forwardSpeed)
                         + transform.right * (strafe * baseSpeed * strafeFactor);

            // Diagonal input shouldn't outrun a straight line.
            float maxSpeed = Mathf.Max(Mathf.Abs(forward * forwardSpeed), Mathf.Abs(strafe * baseSpeed * strafeFactor));
            if (move.sqrMagnitude > maxSpeed * maxSpeed && move.sqrMagnitude > 0.0001f)
                move = move.normalized * maxSpeed;

            // --- Gravity and jump ------------------------------------------
            bool grounded = _controller.isGrounded;
            if (grounded && _verticalVelocity < 0f)
                _verticalVelocity = -2f; // keep it pinned to the ground

            if (grounded && keyboard.spaceKey.wasPressedThisFrame)
            {
                _verticalVelocity = Mathf.Sqrt(jumpHeight * -2f * gravity);
                if (_animator != null)
                    _animator.SetTrigger(JumpHash);
            }

            _verticalVelocity += gravity * Time.deltaTime;
            move.y = _verticalVelocity;

            _controller.Move(move * Time.deltaTime);

            // --- Attack -----------------------------------------------------
            // Left click only attacks when it isn't being used to orbit the camera.
            if (mouse != null && mouse.leftButton.wasPressedThisFrame && !IsSteering && _animator != null)
                _animator.SetTrigger(AttackHash);

            UpdateAnimator(grounded, walking, forward, strafe);
        }

        void UpdateAnimator(bool grounded, bool walking, float forward, float strafe)
        {
            if (_animator == null)
                return;

            // Map to the blend tree's 0 / 0.5 / 1 thresholds.
            bool moving = Mathf.Abs(forward) > 0.01f || Mathf.Abs(strafe) > 0.01f;
            float target = moving ? (walking ? 0.5f : 1f) : 0f;

            _animSpeed = Mathf.MoveTowards(_animSpeed, target, Time.deltaTime / Mathf.Max(animationDamping, 0.0001f));
            _animator.SetFloat(SpeedHash, _animSpeed);
            _animator.SetBool(GroundedHash, grounded);
        }
    }
}
