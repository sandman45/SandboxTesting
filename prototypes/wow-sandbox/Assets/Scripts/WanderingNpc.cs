using UnityEngine;
using UnityEngine.AI;

namespace WowSandbox
{
    /// <summary>
    /// Idle wandering for ambient NPCs and creatures. Picks a reachable point near its
    /// home position, walks there, pauses for a moment, and repeats.
    ///
    /// Obstacle avoidance comes from the NavMesh rather than from raycasts here — the
    /// hut is baked into the navmesh as unwalkable, so an agent simply has nowhere to
    /// path through it. That also means this needs a baked NavMesh to do anything at
    /// all; see WoW Sandbox -> Bake NavMesh.
    ///
    /// Animals (<see cref="FearsFire"/>) run from a lit torch in the player's hand.
    ///
    /// At night (<see cref="NightLife"/>) it walks back to its <see cref="Bed"/> and goes to sleep
    /// (the Asleep animator bool, from WoW Sandbox → Add Night Animations), waking when hit or
    /// when you come close — a Hostile one wakes up fighting. The humanoid nearest a lantern
    /// keeps watch instead, patrolling a small circle around it.
    /// </summary>
    [RequireComponent(typeof(NavMeshAgent))]
    public class WanderingNpc : MonoBehaviour
    {
        [Header("Wander area")]
        [Tooltip("How far from its starting point the NPC will roam.")]
        public float wanderRadius = 12f;
        [Tooltip("Give up and re-pick if a sampled point isn't on the NavMesh within this range.")]
        public float sampleRange = 4f;

        [Header("Pacing")]
        [Tooltip("Seconds to stand still after arriving, picked at random from this range.")]
        public Vector2 pauseSeconds = new Vector2(1.5f, 5f);
        public float arriveThreshold = 0.3f;

        [Header("Animation")]
        [Tooltip("Optional. Animator float parameter driven with normalised speed.")]
        public string speedParameter = "Speed";

        NavMeshAgent _agent;
        Animator _animator;
        Vector3 _home;
        float _resumeAt;
        bool _waiting;
        int _speedHash;
        bool _hasSpeedParameter;
        float _walkSpeed;
        bool _fleeing;
        float _nextFleePath;

        static readonly int AsleepHash = Animator.StringToHash("Asleep");
        bool _hasAsleep;
        Health _health;
        Transform _player;
        float _jitter;
        bool _bedtime;
        bool _guarding;
        Vector3 _post;
        bool _headingToBed;
        float _wokenUntil;

        /// <summary>An animal: it keeps away from a lit torch (HeldLight). People don't.</summary>
        protected virtual bool FearsFire => false;

        public bool IsAnimal => FearsFire;

        /// <summary>Where it started, the middle of its daytime wander.</summary>
        public Vector3 Home => _home;

        /// <summary>Lying down for the night.</summary>
        public bool Asleep { get; private set; }

        /// <summary>Where it sleeps: home, for people.</summary>
        protected virtual Vector3 Bed => _home;

        protected virtual void Start()
        {
            _agent = GetComponent<NavMeshAgent>();
            _animator = GetComponentInChildren<Animator>();
            _home = transform.position;
            _walkSpeed = _agent.speed;
            _jitter = Random.value;
            _health = GetComponent<Health>();
            if (_health != null)
                _health.Damaged += OnDamaged;
            // HD character exports (the Kul Tiran thieves) carry a white EyeGlow sheet.
            Vendor.HideEyeGlow(gameObject);

            CacheSpeedParameter();

            if (!_agent.isOnNavMesh)
            {
                Debug.LogWarning($"[WanderingNpc] {name} isn't on a NavMesh — nothing to walk on. " +
                                 "Bake one via WoW Sandbox -> Bake NavMesh.", this);
                enabled = false;
                return;
            }

            PickDestination();
        }

        void OnDestroy()
        {
            if (_health != null)
                _health.Damaged -= OnDamaged;
        }

        // NpcCombat switches this off while it fights: up on its feet, and a fresh look at the
        // hour once it's back.
        void OnDisable()
        {
            SetAsleep(false);
            _headingToBed = false;
            _bedtime = _guarding = false;
        }

        void OnDamaged(float amount)
        {
            if (Asleep || _bedtime)
                Wake();
        }

        void CacheSpeedParameter()
        {
            if (_animator == null || _animator.runtimeAnimatorController == null
                || string.IsNullOrEmpty(speedParameter))
                return;

            _speedHash = Animator.StringToHash(speedParameter);
            foreach (var parameter in _animator.parameters)
            {
                if (parameter.nameHash == _speedHash && parameter.type == AnimatorControllerParameterType.Float)
                    _hasSpeedParameter = true;
                if (parameter.nameHash == AsleepHash && parameter.type == AnimatorControllerParameterType.Bool)
                    _hasAsleep = true;
            }
        }

        protected virtual void Update()
        {
            if (FearsFire && Flee())
            {
                UpdateSpeedParameter();
                return;
            }

            bool bedtime = NightLife.IsBedtime(_jitter);
            bool guarding = bedtime && !FearsFire && NightLife.IsGuard(this, out _post);
            if (bedtime != _bedtime || guarding != _guarding)
            {
                _bedtime = bedtime;
                _guarding = guarding;
                _headingToBed = false;
                SetAsleep(false);
                if (!bedtime || guarding)
                {
                    // Off to wherever it wanders now — home by day, round its lantern on watch.
                    _waiting = false;
                    PickDestination();
                }
            }
            if (bedtime && !guarding)
            {
                Sleep();
                UpdateSpeedParameter();
                return;
            }

            if (_agent.pathPending)
                return;

            if (_waiting)
            {
                if (Time.time >= _resumeAt)
                {
                    _waiting = false;
                    PickDestination();
                }
            }
            else if (_agent.remainingDistance <= Mathf.Max(_agent.stoppingDistance, arriveThreshold))
            {
                _waiting = true;
                _resumeAt = Time.time + Random.Range(pauseSeconds.x, pauseSeconds.y);
            }

            UpdateSpeedParameter();
        }

        /// <summary>Runs from a lit torch, true while it's near; back to wandering once it isn't.</summary>
        bool Flee()
        {
            if (!HeldLight.TorchNear(transform.position, out var torch))
            {
                if (_fleeing)
                {
                    _fleeing = false;
                    _agent.speed = _walkSpeed;
                    _waiting = true;
                    _resumeAt = Time.time + Random.Range(pauseSeconds.x, pauseSeconds.y);
                }
                return false;
            }

            if (!_fleeing)
            {
                _fleeing = true;
                SetAsleep(false);
                _headingToBed = false;
                _agent.speed = _walkSpeed * 1.8f;
                _nextFleePath = 0f;
            }
            if (Time.time >= _nextFleePath)
            {
                _nextFleePath = Time.time + 0.5f;
                var away = transform.position - torch;
                away.y = 0f;
                away = away.sqrMagnitude > 0.01f ? away.normalized : Random.insideUnitSphere;
                if (NavMesh.SamplePosition(transform.position + away * 6f, out NavMeshHit hit, sampleRange, NavMesh.AllAreas))
                    _agent.SetDestination(hit.position);
            }
            return true;
        }

        /// <summary>Bedtime: walk to bed and lie down; wake if you come too close.</summary>
        void Sleep()
        {
            if (Asleep)
            {
                if (PlayerWithin(NightLife.WakeDistance))
                    Wake();
                return;
            }
            // Just woken: up for a while, and not back down with you still standing over it.
            if (Time.time < _wokenUntil || PlayerWithin(NightLife.WakeDistance * 1.5f))
                return;

            if (!_headingToBed)
            {
                _headingToBed = true;
                _agent.SetDestination(Bed);
                return;
            }
            if (_agent.pathPending ||
                _agent.remainingDistance > Mathf.Max(_agent.stoppingDistance, arriveThreshold))
                return;
            _agent.ResetPath();
            SetAsleep(true);
        }

        void Wake()
        {
            if (Asleep)
                GetComponent<NpcCombat>()?.WakeUp();
            SetAsleep(false);
            _headingToBed = false;
            _agent.ResetPath();
            _wokenUntil = Time.time + 10f;
        }

        bool PlayerWithin(float range)
        {
            if (_player == null)
            {
                var player = FindAnyObjectByType<HealthController>();
                if (player == null)
                    return false;
                _player = player.transform;
            }
            return (_player.position - transform.position).sqrMagnitude <= range * range;
        }

        void SetAsleep(bool asleep)
        {
            Asleep = asleep;
            if (_hasAsleep)
                _animator.SetBool(AsleepHash, asleep);
        }

        void UpdateSpeedParameter()
        {
            if (_hasSpeedParameter)
            {
                // Normalise against the agent's own top speed so the blend tree reads 0..1
                // regardless of how fast this particular NPC walks.
                float normalised = _agent.speed > 0.01f ? _agent.velocity.magnitude / _agent.speed : 0f;
                _animator.SetFloat(_speedHash, Mathf.Clamp01(normalised), 0.15f, Time.deltaTime);
            }
        }

        void PickDestination()
        {
            // A few attempts, because a random point can easily land inside the hut or
            // off the edge of the terrain where there's no NavMesh to stand on.
            Vector3 centre = _guarding ? _post : _home;
            float radius = _guarding ? Mathf.Min(wanderRadius, NightLife.PatrolRadius) : wanderRadius;
            for (int attempt = 0; attempt < 8; attempt++)
            {
                Vector2 offset = Random.insideUnitCircle * radius;
                Vector3 candidate = centre + new Vector3(offset.x, 0f, offset.y);

                if (NavMesh.SamplePosition(candidate, out NavMeshHit hit, sampleRange, NavMesh.AllAreas))
                {
                    _agent.SetDestination(hit.position);
                    return;
                }
            }

            // Nowhere reachable found this time; wait a beat and try again.
            _waiting = true;
            _resumeAt = Time.time + 1f;
        }

        void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(1f, 0.85f, 0.2f, 0.35f);
            Vector3 centre = Application.isPlaying ? _home : transform.position;
            Gizmos.DrawWireSphere(centre, wanderRadius);
        }
    }
}
