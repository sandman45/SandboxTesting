using UnityEngine;
using UnityEngine.AI;

namespace WowSandbox
{
    /// <summary>
    /// Makes a wandering humanoid fight, WoW-style. A Hostile one that sees you within
    /// aggroRange on land runs at you; anything you hit fights back whatever its reaction.
    /// In reach, it swings every attackInterval (Combat.AttackPlayer, d20 against your AC).
    ///
    /// It gives up and runs home — healing as it goes, as WoW mobs do when they leash —
    /// once you die, take to the water (it can't swim), get
    /// leashRange from its home, or stand somewhere its NavMesh can't reach (the ship's deck).
    /// WanderingNpc is switched off while it fights and back on once it's home.
    /// </summary>
    [RequireComponent(typeof(Health), typeof(NavMeshAgent))]
    public class NpcCombat : MonoBehaviour
    {
        [Tooltip("Hostile NPCs notice you this close.")]
        public float aggroRange = 12f;
        [Tooltip("Gives up once you're this far from its home.")]
        public float leashRange = 40f;
        [Tooltip("Extra reach beyond the two bodies touching.")]
        public float attackReach = 0.8f;
        public float attackInterval = 2f;
        [Tooltip("Chase speed, as a multiple of its walking speed.")]
        public float runMultiplier = 2.2f;

        enum Mode { Idle, Chase, Return }

        static readonly int SpeedHash = Animator.StringToHash("Speed");
        static readonly int AttackHash = Animator.StringToHash("Attack");

        Health _health;
        CharacterStats _stats;
        NavMeshAgent _agent;
        WanderingNpc _wanderer;
        Animator _animator;
        bool _hasSpeed;
        bool _hasAttack;
        bool _hasRun;

        HealthController _player;
        WowCharacterController _playerMovement;
        CharacterController _playerBody;

        Vector3 _home;
        Mode _mode;
        float _walkSpeed;
        float _nextAttack;
        float _nextRepath;
        bool _provoked;

        public bool InCombat => _mode == Mode.Chase;

        float _stunnedUntil;

        /// <summary>Stunned — Shield Bash, Charge: it stands still and doesn't swing.</summary>
        public bool Stunned => Time.time < _stunnedUntil;

        /// <summary>Stops it in its tracks for <paramref name="seconds"/> (no moving, no swinging), and provokes it.</summary>
        public void Stun(float seconds)
        {
            _stunnedUntil = Mathf.Max(_stunnedUntil, Time.time + seconds);
            if (_mode != Mode.Return)
                _provoked = true;
        }

        void Awake()
        {
            _health = GetComponent<Health>();
            _stats = GetComponent<CharacterStats>();
            _agent = GetComponent<NavMeshAgent>();
            _wanderer = GetComponent<WanderingNpc>();
            _animator = GetComponentInChildren<Animator>();
            _walkSpeed = _agent.speed;
            _home = transform.position;
            _health.Damaged += OnDamaged;

            if (_animator != null && _animator.runtimeAnimatorController != null)
            {
                foreach (var parameter in _animator.parameters)
                {
                    _hasSpeed |= parameter.nameHash == SpeedHash;
                    _hasAttack |= parameter.nameHash == AttackHash;
                }
                // A Run clip at Speed 2 in the locomotion blend (M2AnimatorBuilder.AddCombatStates).
                foreach (var clip in _animator.runtimeAnimatorController.animationClips)
                    _hasRun |= clip.name == "Run";
            }
        }

        void OnDestroy()
        {
            if (_health != null)
                _health.Damaged -= OnDamaged;
        }

        void OnDamaged(float amount)
        {
            if (_mode != Mode.Return)
                _provoked = true;
        }

        void Update()
        {
            if (_health.IsDead || !_agent.isActiveAndEnabled || !_agent.isOnNavMesh)
                return;

            FindPlayer();
            if (Stunned)
            {
                _agent.ResetPath();
                SetSpeed(0f);
                // Its next swing waits until it's had a moment to recover.
                _nextAttack = Mathf.Max(_nextAttack, _stunnedUntil + 0.5f);
                return;
            }
            switch (_mode)
            {
                case Mode.Idle:
                    if (ShouldEngage())
                        Engage();
                    break;
                case Mode.Chase:
                    Chase();
                    break;
                case Mode.Return:
                    Return();
                    break;
            }
        }

        bool ShouldEngage()
        {
            if (TitleScreen.Showing || !PlayerFightable())
                return false;
            if (_provoked)
                return true;
            return _health.reaction == Reaction.Hostile &&
                   Vector3.Distance(transform.position, _player.transform.position) <= aggroRange;
        }

        /// <summary>Alive, on land and within the leash — not whether a path exists.</summary>
        bool PlayerFightable() =>
            _player != null && !_player.IsDead &&
            (_playerMovement == null || !_playerMovement.IsSwimming) &&
            Vector3.Distance(_player.transform.position, _home) <= leashRange;

        void Engage()
        {
            _mode = Mode.Chase;
            if (_wanderer != null)
                _wanderer.enabled = false;
            _agent.speed = _walkSpeed * runMultiplier;
            // Measured centre to centre, so the two bodies count too.
            _agent.stoppingDistance = _agent.radius + PlayerRadius() + attackReach * 0.8f;
            _nextRepath = 0f;
            // A beat before the first swing, so it doesn't land the moment you're in reach.
            _nextAttack = Mathf.Max(_nextAttack, Time.time + 0.5f);
        }

        void Chase()
        {
            if (!PlayerFightable())
            {
                StartReturn();
                return;
            }

            if (Time.time >= _nextRepath)
            {
                _nextRepath = Time.time + 0.25f;
                _agent.SetDestination(_player.transform.position);
            }

            // Somewhere it can't get to — up on the ship's deck, say. Give up rather than
            // stand at the water's edge glaring forever.
            if (!_agent.pathPending && _agent.pathStatus != NavMeshPathStatus.PathComplete &&
                Gap() > attackReach + 3f)
            {
                StartReturn();
                return;
            }

            bool inReach = Gap() <= attackReach;
            if (inReach)
            {
                Face(_player.transform.position);
                if (Time.time >= _nextAttack)
                {
                    _nextAttack = Time.time + attackInterval;
                    if (_hasAttack)
                        _animator.SetTrigger(AttackHash);
                    Combat.AttackPlayer(_stats, _player);
                }
            }

            SetSpeed(inReach ? 0f : _hasRun ? 2f : 1f);
        }

        void StartReturn()
        {
            _mode = Mode.Return;
            _provoked = false;
            _agent.speed = _walkSpeed * runMultiplier;
            _agent.stoppingDistance = 0.15f;
            _agent.SetDestination(_home);
        }

        void Return()
        {
            _health.Heal(_health.maxHealth * 0.2f * Time.deltaTime);
            SetSpeed(_hasRun ? 2f : 1f);

            if (_agent.pathPending || _agent.remainingDistance > 1f)
                return;

            _mode = Mode.Idle;
            _health.Heal(_health.maxHealth);
            _agent.speed = _walkSpeed;
            SetSpeed(0f);
            if (_wanderer != null)
                _wanderer.enabled = true;
        }

        /// <summary>How far apart the two bodies are, edge to edge, across the ground.</summary>
        float Gap()
        {
            var between = _player.transform.position - transform.position;
            between.y = 0f;
            return Mathf.Max(between.magnitude - _agent.radius - PlayerRadius(), 0f);
        }

        float PlayerRadius() => _playerBody != null ? _playerBody.radius : 0.4f;

        void Face(Vector3 point)
        {
            var toward = point - transform.position;
            toward.y = 0f;
            if (toward.sqrMagnitude < 0.0001f)
                return;
            transform.rotation = Quaternion.RotateTowards(transform.rotation,
                Quaternion.LookRotation(toward, Vector3.up), 540f * Time.deltaTime);
        }

        void SetSpeed(float value)
        {
            if (_hasSpeed)
                _animator.SetFloat(SpeedHash, value, 0.1f, Time.deltaTime);
        }

        void FindPlayer()
        {
            if (_player != null)
                return;
            _player = FindAnyObjectByType<HealthController>();
            if (_player == null)
                return;
            _playerMovement = _player.GetComponent<WowCharacterController>();
            _playerBody = _player.GetComponent<CharacterController>();
        }

        void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(1f, 0.3f, 0.2f, 0.5f);
            Gizmos.DrawWireSphere(transform.position, aggroRange);
            Gizmos.color = new Color(1f, 0.8f, 0.2f, 0.3f);
            Gizmos.DrawWireSphere(Application.isPlaying ? _home : transform.position, leashRange);
        }
    }
}
