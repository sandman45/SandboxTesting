using UnityEngine;

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
    /// already using. PlayerFrameHud draws the bar.
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
        [Tooltip("Percent of max health lost per second once breath hits zero and you're still " +
                 "under — a percentage so it drowns a level 1 and a level 20 equally fast. Only " +
                 "applies if a HealthController is present; otherwise drowning just respawns " +
                 "you at the last breath, same as before health existed.")]
        public float drowningPercentPerSecond = 12f;


        CharacterController _controller;
        HealthController _health;
        float _breath;
        // Fallback only: used to respawn directly if there's no HealthController to hand
        // drowning damage to. Kept updated regardless, so it's ready if that ever happens.
        Vector3 _lastSafePosition;
        Quaternion _lastSafeRotation;

        /// <summary>Current breath as a 0-1 fraction of <see cref="maxBreath"/>.</summary>
        public float Breath01 => maxBreath > 0f ? Mathf.Clamp01(_breath / maxBreath) : 1f;

        /// <summary>True while the head is under the surface, as of this frame's Update.</summary>
        public bool IsSubmerged { get; private set; }

        void Awake()
        {
            _controller = GetComponent<CharacterController>();
            _health = GetComponent<HealthController>();
            _breath = maxBreath;
            _lastSafePosition = transform.position;
            _lastSafeRotation = transform.rotation;
        }

        void Update()
        {
            IsSubmerged = IsHeadSubmerged();

            if (IsSubmerged)
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
                _health.TakeDamage(_health.maxHealth * drowningPercentPerSecond * 0.01f * Time.deltaTime);
                return;
            }

            Debug.Log("[BreathController] Ran out of air — respawning at the last breath.", this);

            _controller.enabled = false;
            transform.SetPositionAndRotation(_lastSafePosition, _lastSafeRotation);
            _controller.enabled = true;

            _breath = maxBreath;
        }
    }
}
