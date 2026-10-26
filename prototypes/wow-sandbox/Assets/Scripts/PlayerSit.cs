using UnityEngine;
using UnityEngine.InputSystem;

namespace WowSandbox
{
    /// <summary>
    /// X sits him down on the ground, as in WoW (in the water X dives instead — that's the
    /// swimming's). Moving, jumping, X again or taking a blow stands him back up. Sat by a lit
    /// campfire is resting (Campfire, Hunger.Rest) — how he gets his strength back after his
    /// first cooked meal.
    ///
    /// Plays the warrior animator's sitting states (SitDown trigger, Sitting bool — WoW Sandbox →
    /// Update Warrior Animations). Added to the player at Play.
    /// </summary>
    [RequireComponent(typeof(WowCharacterController))]
    public class PlayerSit : MonoBehaviour
    {
        static readonly int SittingHash = Animator.StringToHash("Sitting");
        static readonly int SitDownHash = Animator.StringToHash("SitDown");
        // The stand-up clip, before the legs are his again.
        const float StandSeconds = 0.8f;

        WowCharacterController _player;
        CharacterController _body;
        HealthController _health;
        float _standingUntil = -1f;
        bool _warned;

        /// <summary>Whether he's sat on the ground now.</summary>
        public bool IsSitting { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void AddToPlayer()
        {
            var player = FindAnyObjectByType<WowCharacterController>();
            if (player != null && player.GetComponent<PlayerSit>() == null)
                player.gameObject.AddComponent<PlayerSit>();
        }

        void Awake()
        {
            _player = GetComponent<WowCharacterController>();
            _body = GetComponent<CharacterController>();
            _health = GetComponent<HealthController>();
        }

        void OnEnable() => Combat.PlayerAttacked += OnAttacked;
        void OnDisable() => Combat.PlayerAttacked -= OnAttacked;

        void OnAttacked(HealthController target, AttackResult result)
        {
            if (IsSitting)
                Stand();
        }

        void Update()
        {
            // Getting up: the legs come back once the clip's done.
            if (_standingUntil >= 0f && Time.time >= _standingUntil)
            {
                _standingUntil = -1f;
                _player.SetLocked(false);
            }

            var keyboard = Keyboard.current;
            if (keyboard == null)
                return;

            if (IsSitting)
            {
                // Something else took him over (a conversation, the helm), or he died: up.
                if (_health != null && _health.IsDead)
                {
                    IsSitting = false;
                    SetAnimator(false);
                    return;
                }
                // Asleep by the fire: the keys don't get him up (Esc wakes him — RestPanel).
                if (RestPanel.Resting)
                    return;
                if (keyboard.xKey.wasPressedThisFrame || keyboard.spaceKey.wasPressedThisFrame ||
                    keyboard.wKey.isPressed || keyboard.aKey.isPressed || keyboard.sKey.isPressed ||
                    keyboard.dKey.isPressed || keyboard.qKey.isPressed || keyboard.eKey.isPressed ||
                    keyboard.upArrowKey.isPressed || keyboard.downArrowKey.isPressed ||
                    keyboard.leftArrowKey.isPressed || keyboard.rightArrowKey.isPressed)
                    Stand();
                return;
            }

            if (keyboard.xKey.wasPressedThisFrame && !HudPanel.ModalOpen)
                Sit();
        }

        /// <summary>Sits him down — X, or clicking a campfire. False where he can't (swimming, climbing, busy).</summary>
        public bool Sit()
        {
            if (IsSitting)
                return true;
            if (_standingUntil >= 0f || _player.IsLocked || _player.IsSwimming || _player.IsClimbing ||
                TitleScreen.Showing || ShipwreckWake.Lying || _health != null && _health.IsDead ||
                _body != null && !_body.isGrounded)
                return false;
            var animator = _player.Animator;
            if (!HasSitting(animator))
            {
                if (!_warned)
                    Debug.LogWarning("[PlayerSit] The player's animator has no Sitting states — run WoW Sandbox → " +
                                     "Update Warrior Animations.", this);
                _warned = true;
                return false;
            }

            _player.SetLocked(true);
            IsSitting = true;
            animator.SetTrigger(SitDownHash);
            SetAnimator(true);
            return true;
        }

        /// <summary>Gets him up — moving, X, a blow, or anything that needs him on his feet.</summary>
        public void Stand()
        {
            if (!IsSitting)
                return;
            IsSitting = false;
            SetAnimator(false);
            _standingUntil = Time.time + StandSeconds;
        }

        void SetAnimator(bool sitting)
        {
            var animator = _player.Animator;
            if (HasSitting(animator))
                animator.SetBool(SittingHash, sitting);
        }

        static bool HasSitting(Animator animator)
        {
            if (animator == null || animator.runtimeAnimatorController == null)
                return false;
            foreach (var parameter in animator.parameters)
                if (parameter.nameHash == SittingHash)
                    return true;
            return false;
        }
    }
}
