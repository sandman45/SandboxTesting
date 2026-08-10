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

        protected virtual void Start()
        {
            _agent = GetComponent<NavMeshAgent>();
            _animator = GetComponentInChildren<Animator>();
            _home = transform.position;

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

        void CacheSpeedParameter()
        {
            if (_animator == null || _animator.runtimeAnimatorController == null
                || string.IsNullOrEmpty(speedParameter))
                return;

            _speedHash = Animator.StringToHash(speedParameter);
            foreach (var parameter in _animator.parameters)
            {
                if (parameter.nameHash == _speedHash && parameter.type == AnimatorControllerParameterType.Float)
                {
                    _hasSpeedParameter = true;
                    return;
                }
            }
        }

        protected virtual void Update()
        {
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
            for (int attempt = 0; attempt < 8; attempt++)
            {
                Vector2 offset = Random.insideUnitCircle * wanderRadius;
                Vector3 candidate = _home + new Vector3(offset.x, 0f, offset.y);

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
