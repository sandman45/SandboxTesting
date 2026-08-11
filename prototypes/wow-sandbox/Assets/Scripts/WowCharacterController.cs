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

        [Header("Swimming")]
        [Tooltip("Forward speed while swimming. WoW swims slower than it runs.")]
        public float swimSpeed = 4f;
        [Tooltip("Vertical speed from the ascend/descend keys, as a fraction of swim speed.")]
        [Range(0.1f, 1.5f)] public float verticalSwimFactor = 0.7f;
        [Tooltip("Start swimming once the water is this far up the capsule, measured from the " +
                 "feet. 0.45 is roughly waist height. Select the player to see both thresholds " +
                 "drawn as rings in the scene view.")]
        [Range(0.3f, 1f)] public float swimEnterHeight = 0.45f;
        [Tooltip("Stop swimming once the water drops to this fraction. Lower than the enter " +
                 "threshold on purpose — without the gap you flicker in and out at the waterline.")]
        [Range(0.2f, 0.9f)] public float swimExitHeight = 0.35f;
        [Tooltip("How fast you drop onto the surface when you're above it — after jumping in " +
                 "from a ledge, say. Below the surface you hold depth instead; Space is the " +
                 "only thing that raises you.")]
        public float settleSpeed = 3f;
        [Tooltip("Degrees the model tips forward while swimming. Purely cosmetic.")]
        public float swimPitch = 45f;

        [Header("Animation")]
        [Tooltip("Blend tree thresholds are 0 = idle, 0.5 = walk, 1 = run.")]
        public float animationDamping = 0.12f;
        [Tooltip("The imported model child. Carries the facing offset and the swim pitch, so " +
                 "the logic root stays upright and the movement maths needs no fudge.")]
        public Transform modelRoot;

        CharacterController _controller;
        Animator _animator;
        float _verticalVelocity;
        float _animSpeed;
        bool _swimming;
        bool _hasSwimmingParameter;
        float _modelPitch;
        Quaternion _modelRestRotation;

        // Cached animator parameter hashes.
        static readonly int SpeedHash = Animator.StringToHash("Speed");
        static readonly int GroundedHash = Animator.StringToHash("Grounded");
        static readonly int JumpHash = Animator.StringToHash("Jump");
        static readonly int AttackHash = Animator.StringToHash("Attack");
        static readonly int SwimmingHash = Animator.StringToHash("Swimming");

        /// <summary>True while the right mouse button is steering the character.</summary>
        public bool IsSteering { get; private set; }

        /// <summary>True while the character is in deep enough water to swim.</summary>
        public bool IsSwimming => _swimming;

        void Awake()
        {
            _controller = GetComponent<CharacterController>();
            // The Animator usually lives on the imported glTF child, not the root.
            _animator = GetComponentInChildren<Animator>();

            if (modelRoot == null && transform.childCount > 0)
                modelRoot = transform.GetChild(0);
            if (modelRoot != null)
                _modelRestRotation = modelRoot.localRotation;

            // "Swimming" only exists in controllers built after swimming was added. A player
            // already in the scene keeps the controller it was spawned with — rebuilding the
            // asset can't reach it, because WarriorSetup deletes and recreates it, which mints
            // a new GUID. Without this check that mismatch is one console error per frame.
            _hasSwimmingParameter = HasParameter(_animator, SwimmingHash);

            if (_animator != null && _animator.runtimeAnimatorController != null && !_hasSwimmingParameter)
            {
                Debug.LogWarning($"[WowCharacterController] \"{_animator.runtimeAnimatorController.name}\" " +
                                 "has no Swimming parameter, so the swim animation won't play — " +
                                 "swimming itself still works. Re-run WoW Sandbox → Spawn Warrior " +
                                 "Player to rebuild the controller.", this);
            }
        }

        /// <summary>
        /// Draws the two swim thresholds as rings around the capsule, so "where does swimming
        /// start" is a thing you can see against the water plane rather than a number you have
        /// to infer. Cyan is the entry height, dimmer blue is the exit height below it.
        /// </summary>
        void OnDrawGizmosSelected()
        {
            var controller = GetComponent<CharacterController>();
            if (controller == null)
                return;

            DrawThresholdRing(controller, swimEnterHeight, Color.cyan);
            DrawThresholdRing(controller, Mathf.Min(swimExitHeight, swimEnterHeight),
                              new Color(0.2f, 0.5f, 0.7f));
        }

        void DrawThresholdRing(CharacterController controller, float fraction, Color color)
        {
            Gizmos.color = color;

            Vector3 centre = transform.position + Vector3.up * (controller.height * fraction);
            float radius = Mathf.Max(controller.radius * 1.6f, 0.2f);

            const int segments = 24;
            Vector3 previous = centre + new Vector3(radius, 0f, 0f);

            for (int i = 1; i <= segments; i++)
            {
                float angle = i / (float)segments * Mathf.PI * 2f;
                Vector3 next = centre + new Vector3(Mathf.Cos(angle) * radius, 0f,
                                                    Mathf.Sin(angle) * radius);
                Gizmos.DrawLine(previous, next);
                previous = next;
            }
        }

        static bool HasParameter(Animator animator, int hash)
        {
            if (animator == null || animator.runtimeAnimatorController == null)
                return false;

            foreach (var parameter in animator.parameters)
            {
                if (parameter.nameHash == hash)
                    return true;
            }

            return false;
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

            // --- Water ------------------------------------------------------
            var water = WaterVolume.Containing(transform.position);
            UpdateSwimState(water);

            // --- Horizontal movement ---------------------------------------
            float baseSpeed = _swimming ? swimSpeed : (walking ? walkSpeed : runSpeed);
            // Backpedalling is slower than moving forward.
            float forwardSpeed = forward >= 0f ? baseSpeed : baseSpeed * backpedalFactor;

            Vector3 move = transform.forward * (forward * forwardSpeed)
                         + transform.right * (strafe * baseSpeed * strafeFactor);

            // Diagonal input shouldn't outrun a straight line.
            float maxSpeed = Mathf.Max(Mathf.Abs(forward * forwardSpeed), Mathf.Abs(strafe * baseSpeed * strafeFactor));
            if (move.sqrMagnitude > maxSpeed * maxSpeed && move.sqrMagnitude > 0.0001f)
                move = move.normalized * maxSpeed;

            bool grounded = !_swimming && _controller.isGrounded;

            if (_swimming)
            {
                move.y = SwimVertical(keyboard, water);
            }
            else
            {
                // --- Gravity and jump --------------------------------------
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
            }

            _controller.Move(move * Time.deltaTime);

            // --- Attack -----------------------------------------------------
            // Left click only attacks when it isn't being used to orbit the camera.
            if (mouse != null && mouse.leftButton.wasPressedThisFrame && !IsSteering && _animator != null)
                _animator.SetTrigger(AttackHash);

            UpdateModelPitch();
            UpdateAnimator(grounded, walking, forward, strafe);
        }

        /// <summary>
        /// Switches into and out of swimming, measured by how far the water climbs the
        /// capsule. The enter and exit thresholds differ so standing at the waterline
        /// doesn't chatter between the two states every frame.
        /// </summary>
        void UpdateSwimState(WaterVolume water)
        {
            if (water == null)
            {
                if (_swimming)
                {
                    _swimming = false;
                    _verticalVelocity = 0f;
                }

                return;
            }

            // transform.position is at the feet, so this is how much of the body is under.
            float submerged = water.SurfaceY - transform.position.y;
            float fraction = submerged / Mathf.Max(_controller.height, 0.001f);

            if (!_swimming && fraction >= swimEnterHeight)
            {
                _swimming = true;
                // Drop whatever fall speed we entered the water with, or you torpedo to the
                // lake bed before the buoyancy can catch you.
                _verticalVelocity = 0f;
            }
            else if (_swimming && fraction <= Mathf.Min(swimExitHeight, swimEnterHeight))
            {
                _swimming = false;
                _verticalVelocity = 0f;
            }
        }

        /// <summary>
        /// Vertical speed while swimming: Space rises, X sinks, and with neither held you hold
        /// the depth you stopped at. Gravity is not involved at all below the surface, which
        /// is what makes holding depth possible.
        /// </summary>
        float SwimVertical(Keyboard keyboard, WaterVolume water)
        {
            float verticalSpeed = swimSpeed * verticalSwimFactor;

            // Floating height: eyes at the waterline rather than the whole capsule under it.
            float floatY = water.SurfaceY - _controller.height * 0.85f;

            float input = 0f;
            if (keyboard.spaceKey.isPressed) input += 1f;
            if (keyboard.xKey.isPressed) input -= 1f;

            if (Mathf.Abs(input) > 0.01f)
            {
                // Rising is capped at the surface — you can't swim up into the air.
                if (input > 0f && transform.position.y >= floatY)
                    return 0f;

                return input * verticalSpeed;
            }

            // No key held: hold depth. Rising is Space's job and nothing else's, so there is
            // deliberately no buoyancy pulling you back up — you stay where you stopped.
            //
            // The one exception is being above the float line, which happens when you jump
            // in from a ledge or wade off a shelf. Holding depth there would leave you
            // hanging in the air over the water, so you settle down onto the surface.
            float aboveSurface = transform.position.y - floatY;
            if (aboveSurface > 0.01f)
                return -Mathf.Min(aboveSurface * settleSpeed, verticalSpeed);

            return 0f;
        }

        /// <summary>
        /// Tips the model forward while swimming. This goes on the model child, which already
        /// carries the +90 degree facing offset, so transform.forward on the root stays honest.
        /// </summary>
        void UpdateModelPitch()
        {
            if (modelRoot == null)
                return;

            float target = _swimming ? swimPitch : 0f;
            _modelPitch = Mathf.MoveTowards(_modelPitch, target, 180f * Time.deltaTime);

            // Left-multiplied, so the pitch is about the root's X axis (the character's right)
            // rather than the model's own rotated axes.
            modelRoot.localRotation = Quaternion.Euler(_modelPitch, 0f, 0f) * _modelRestRotation;
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
            if (_hasSwimmingParameter)
                _animator.SetBool(SwimmingHash, _swimming);
        }
    }
}
