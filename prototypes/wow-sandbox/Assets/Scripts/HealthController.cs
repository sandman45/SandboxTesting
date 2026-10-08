using System;
using UnityEngine;

namespace WowSandbox
{
    /// <summary>What killed the player — picks the death screen's line.</summary>
    public enum DeathCause
    {
        Unknown,
        Drowned,
        Shark,
        Leviathan,
        Creature,
    }

    /// <summary>
    /// The player's hit points. Damage arrives with a cause (a shark's bite, drowning, the
    /// leviathan) so the death screen knows what to say. PlayerFrameHud draws it.
    ///
    /// Dying is final until you choose otherwise: the body stays where it fell, plays its
    /// Death animation and stays down, controls lock, and nothing respawns you — DeathScreen
    /// offers Respawn (which calls Respawn here) or Quit.
    ///
    /// With CharacterStats on the same object, max health is its D&D hit points instead of
    /// the inspector value, and levelling up raises it and heals you to full.
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public class HealthController : MonoBehaviour
    {
        [Header("Health")]
        public float maxHealth = 100f;

        static readonly int DeathHash = Animator.StringToHash("Death");

        CharacterController _controller;
        BreathController _breath;
        WowCharacterController _movement;
        Animator _animator;
        CharacterStats _stats;
        float _health;
        Vector3 _spawnPosition;
        Quaternion _spawnRotation;

        public float Current => _health;

        /// <summary>Current health as a 0-1 fraction of <see cref="maxHealth"/>.</summary>
        public float Health01 => maxHealth > 0f ? Mathf.Clamp01(_health / maxHealth) : 1f;

        /// <summary>True from the killing blow until Respawn.</summary>
        public bool IsDead { get; private set; }

        /// <summary>What did it, and its name if it had one ("Hunter Shark").</summary>
        public DeathCause LastCause { get; private set; }
        public string LastKiller { get; private set; }

        public event Action<DeathCause> Died;
        public event Action Respawned;

        void Awake()
        {
            _controller = GetComponent<CharacterController>();
            _breath = GetComponent<BreathController>();
            _movement = GetComponent<WowCharacterController>();
            _animator = GetComponentInChildren<Animator>();
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
        public void TakeDamage(float amount) => TakeDamage(amount, DeathCause.Unknown, null);

        /// <summary>Reduces health, remembering the cause in case it's the killing blow.</summary>
        public void TakeDamage(float amount, DeathCause cause, string killer)
        {
            if (IsDead || amount <= 0f)
                return;

            _health = Mathf.Max(0f, _health - amount);
            if (_health <= 0f)
                Die(cause, killer);
        }

        /// <summary>Dies outright, whatever's left — for things nothing survives.</summary>
        public void Kill(DeathCause cause = DeathCause.Unknown, string killer = null) =>
            TakeDamage(_health + maxHealth, cause, killer);

        /// <summary>Restores health, capped at <see cref="maxHealth"/>.</summary>
        public void Heal(float amount)
        {
            if (IsDead || amount <= 0f)
                return;

            _health = Mathf.Min(maxHealth, _health + amount);
        }

        void Die(DeathCause cause, string killer)
        {
            IsDead = true;
            LastCause = cause;
            LastKiller = killer;
            Debug.Log($"[HealthController] Died ({cause}{(killer != null ? $", {killer}" : "")}).", this);

            // Controls off and the body left where it fell. With the movement script off,
            // nothing calls Move, so there's no gravity either — a swimmer stays put in the
            // water rather than sinking out of view.
            if (_movement != null)
            {
                _movement.StopAutoRun();
                _movement.enabled = false;
            }

            if (HasParameter(DeathHash))
                _animator.SetTrigger(DeathHash);

            Died?.Invoke(cause);
        }

        /// <summary>Back to the spawn point at full health and breath, controls restored.</summary>
        public void Respawn()
        {
            if (!IsDead)
                return;

            _controller.enabled = false;
            transform.SetPositionAndRotation(_spawnPosition, _spawnRotation);
            _controller.enabled = true;

            _health = maxHealth;
            if (_breath != null)
                _breath.Refill();

            // Out of the held death pose and back to idle.
            if (_animator != null)
                _animator.Rebind();

            if (_movement != null)
                _movement.enabled = true;

            IsDead = false;
            Respawned?.Invoke();
        }

        bool HasParameter(int hash)
        {
            if (_animator == null || _animator.runtimeAnimatorController == null)
                return false;
            foreach (var parameter in _animator.parameters)
            {
                if (parameter.nameHash == hash)
                    return true;
            }
            return false;
        }
    }
}
