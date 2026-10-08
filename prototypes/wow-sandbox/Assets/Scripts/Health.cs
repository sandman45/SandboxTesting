using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

namespace WowSandbox
{
    /// <summary>How an NPC regards the player — colours its name on the HUD, as in WoW.</summary>
    public enum Reaction
    {
        Hostile,
        Neutral,
        Friendly,
    }

    /// <summary>
    /// A plain hit-point stat for anything that isn't the player — chickens, and whatever
    /// humanoid NpcSpawner places (thieves included). Nothing deals damage to NPCs except
    /// WowCharacterController's attack so far.
    ///
    /// The player uses HealthController instead, not this — it needs a screen bar and a
    /// CharacterController-based respawn that NavMeshAgent NPCs have no use for. Same
    /// TakeDamage/Heal shape as that one, minus both of those.
    /// </summary>
    public class Health : MonoBehaviour
    {
        public float maxHealth = 100f;
        [Tooltip("Colours this unit's name on the target frame and nameplate.")]
        public Reaction reaction = Reaction.Neutral;

        [Header("Death")]
        [Tooltip("How long the corpse sticks around before despawning, picked at random " +
                 "from this range.")]
        public Vector2 despawnDelaySeconds = new(60f, 120f);

        float _health;
        Animator _animator;
        bool _hasDeathTrigger;
        bool _hasHitTrigger;

        static readonly int DeathHash = Animator.StringToHash("Death");
        static readonly int HitHash = Animator.StringToHash("Hit");

        static readonly List<Health> _all = new();

        /// <summary>Every enabled Health in the scene — what the nameplates iterate.</summary>
        public static IReadOnlyList<Health> All => _all;

        /// <summary>Raised after any NPC takes damage, with the amount dealt (killing blows included).</summary>
        public static event Action<Health, float> AnyDamaged;

        public float Current => _health;
        public float Health01 => maxHealth > 0f ? Mathf.Clamp01(_health / maxHealth) : 1f;
        public bool IsDead { get; private set; }

        /// <summary>
        /// Height of the unit's top above its root, in world units — where nameplates and
        /// damage numbers sit. Measured once from the collider the spawners size to the model,
        /// since that collider gets disabled on death.
        /// </summary>
        public float Height { get; private set; }

        void Awake()
        {
            _health = maxHealth;
            var capsule = GetComponent<CapsuleCollider>();
            Height = capsule != null
                ? (capsule.center.y + capsule.height * 0.5f) * transform.lossyScale.y
                : 2f;
            // Lives on the imported model child, not this root — same as everywhere else
            // in the sandbox that looks one up.
            _animator = GetComponentInChildren<Animator>();
            _hasDeathTrigger = HasParameter(_animator, DeathHash);
            _hasHitTrigger = HasParameter(_animator, HitHash);
        }

        void OnEnable() => _all.Add(this);
        void OnDisable() => _all.Remove(this);

        public void TakeDamage(float amount)
        {
            if (IsDead || amount <= 0f)
                return;

            _health = Mathf.Max(0f, _health - amount);
            AnyDamaged?.Invoke(this, amount);
            if (_health <= 0f)
            {
                Die();
                return;
            }

            // The killing blow goes straight to Death instead — no point flinching first.
            if (_hasHitTrigger)
                _animator.SetTrigger(HitHash);
        }

        public void Heal(float amount)
        {
            if (IsDead || amount <= 0f)
                return;

            _health = Mathf.Min(maxHealth, _health + amount);
        }

        /// <summary>
        /// Plays the Death clip if the model exported one, stops the AI so the corpse
        /// doesn't keep wandering off in a death pose, and despawns it after a delay
        /// instead of vanishing on the spot.
        /// </summary>
        void Die()
        {
            IsDead = true;
            Debug.Log($"[Health] {name} died.", this);

            if (_hasDeathTrigger)
                _animator.SetTrigger(DeathHash);

            // WanderingNpc covers ChickenWanderer too, it's a subclass.
            var wanderer = GetComponent<WanderingNpc>();
            if (wanderer != null)
                wanderer.enabled = false;

            var agent = GetComponent<NavMeshAgent>();
            if (agent != null)
                agent.enabled = false;

            var capsule = GetComponent<Collider>();
            if (capsule != null)
                capsule.enabled = false; // a corpse shouldn't be re-targetable

            StartCoroutine(DespawnAfterDelay(UnityEngine.Random.Range(despawnDelaySeconds.x, despawnDelaySeconds.y)));
        }

        IEnumerator DespawnAfterDelay(float delay)
        {
            yield return new WaitForSeconds(delay);
            Destroy(gameObject);
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
    }
}
