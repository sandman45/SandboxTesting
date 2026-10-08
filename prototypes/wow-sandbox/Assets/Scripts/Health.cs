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

        /// <summary>Raised once, when this unit dies — LootDrop rolls its loot on it.</summary>
        public event Action Died;

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
            // With stats present, D&D hit points win over the inspector number.
            var stats = GetComponent<CharacterStats>();
            if (stats != null)
                maxHealth = stats.MaxHitPoints;

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
            {
                // A Hit trigger can still be pending here — set by a blow that landed while
                // the Hit clip was already playing, which Any State won't re-enter. Left set,
                // it fires the moment Death starts, plays the flinch, and Hit's exit hands
                // back to locomotion: a corpse standing back up. Clear it first.
                if (_hasHitTrigger)
                    _animator.ResetTrigger(HitHash);
                _animator.SetTrigger(DeathHash);
            }

            Died?.Invoke();

            // WanderingNpc covers ChickenWanderer too, it's a subclass.
            var wanderer = GetComponent<WanderingNpc>();
            if (wanderer != null)
                wanderer.enabled = false;

            var agent = GetComponent<NavMeshAgent>();
            if (agent != null)
                agent.enabled = false;

            MakeCorpseClickable();

            StartCoroutine(DespawnAfterDelay(UnityEngine.Random.Range(despawnDelaySeconds.x, despawnDelaySeconds.y)));
        }

        /// <summary>
        /// The corpse stays clickable so it can be looted, but stops being a solid
        /// obstacle: the collider turns into a trigger (raycasts still hit triggers; the
        /// player's CharacterController walks through them). An upright capsule would also
        /// miss a body lying on the ground, so it's squashed into a low, wide blob around
        /// where the corpse falls.
        /// </summary>
        void MakeCorpseClickable()
        {
            var collider = GetComponent<Collider>();
            if (collider == null)
                return;

            collider.isTrigger = true;
            if (collider is CapsuleCollider capsule)
            {
                capsule.radius = Mathf.Max(capsule.radius, capsule.height * 0.35f);
                capsule.height = capsule.radius * 2f;
                capsule.center = new Vector3(0f, capsule.radius * 0.6f, 0f);
            }
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
