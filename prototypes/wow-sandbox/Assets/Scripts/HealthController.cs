using UnityEngine;

namespace WowSandbox
{
    /// <summary>
    /// Hit points. Nothing deals damage yet except BreathController once you run out of
    /// air underwater — TakeDamage is public so combat, fall damage, etc. can hang off it
    /// later without this needing to know about them. PlayerFrameHud draws it.
    ///
    /// With CharacterStats on the same object, max health is its D&D hit points instead of
    /// the inspector value, and levelling up raises it and heals you to full.
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public class HealthController : MonoBehaviour
    {
        [Header("Health")]
        public float maxHealth = 100f;


        CharacterController _controller;
        BreathController _breath;
        float _health;
        Vector3 _spawnPosition;
        Quaternion _spawnRotation;

        public float Current => _health;

        /// <summary>Current health as a 0-1 fraction of <see cref="maxHealth"/>.</summary>
        public float Health01 => maxHealth > 0f ? Mathf.Clamp01(_health / maxHealth) : 1f;

        /// <summary>True once health has hit zero and Die() has fired for it.</summary>
        public bool IsDead { get; private set; }

        CharacterStats _stats;

        void Awake()
        {
            _controller = GetComponent<CharacterController>();
            _breath = GetComponent<BreathController>();
            _stats = GetComponent<CharacterStats>();
            if (_stats != null)
                maxHealth = _stats.MaxHitPoints;
            _health = maxHealth;
            _spawnPosition = transform.position;
            _spawnRotation = transform.rotation;
        }

        void OnEnable()
        {
            if (_stats == null)
                return;
            _stats.Changed += OnStatsChanged;
            _stats.LeveledUp += OnLeveledUp;
        }

        void OnDisable()
        {
            if (_stats == null)
                return;
            _stats.Changed -= OnStatsChanged;
            _stats.LeveledUp -= OnLeveledUp;
        }

        void OnStatsChanged()
        {
            maxHealth = _stats.MaxHitPoints;
            _health = Mathf.Min(_health, maxHealth);
        }

        void OnLeveledUp(int newLevel)
        {
            maxHealth = _stats.MaxHitPoints;
            _health = maxHealth;
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
    }
}
