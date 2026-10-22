using UnityEngine;

namespace WowSandbox
{
    /// <summary>
    /// Scenery that swims: the little fish schools in the shallows. A pared-down
    /// SwimmingCreature with no Health, collider or hunting — it can't be targeted or hurt,
    /// it just cruises between random points near home, under the waves and over the
    /// seabed, and never strays out past the shallows into the open sea.
    ///
    /// The sharks hunt them (SwimmingCreature): a shark closing in sends the school bolting
    /// (Scare), and one that's caught is gone (Eat) until it reforms at home some game hours on.
    ///
    /// Root forward is the facing (the spawner turns the model child to match), and the
    /// root sits at the body's centre.
    /// </summary>
    public class AmbientSwimmer : MonoBehaviour
    {
        public float cruiseSpeed = 1.5f;
        [Tooltip("Degrees per second.")]
        public float turnSpeed = 120f;
        [Range(0f, 60f)] public float maxPitch = 20f;
        [Tooltip("How far from home it roams.")]
        public float roamRadius = 20f;
        [Tooltip("Depth band below the calm sea level it cruises in (min, max).")]
        public Vector2 depthRange = new(1f, 4f);
        [Tooltip("Keep at least this much water between the body's centre and the surface or seabed.")]
        public float clearance = 0.6f;
        [Tooltip("Never roams further than this past the edge of the island's terrain — keep it " +
                 "inside DeepSeaDanger's margin so the fish stay in the safe shallows.")]
        public float maxOffshore = 45f;
        public Vector2 pauseSeconds = new(0.5f, 4f);

        static readonly int SpeedHash = Animator.StringToHash("Speed");

        /// <summary>Every school in the sea, eaten ones included — what the sharks look through.</summary>
        public static readonly System.Collections.Generic.List<AmbientSwimmer> All = new();

        Animator _animator;
        bool _hasSpeed;
        Renderer[] _renderers;
        float _returnIn;
        float _fleeUntil = -1f;
        Vector3 _fleeFrom;
        Vector3 _home;
        Vector3 _goal;
        float _speed;
        float _pauseUntil;
        bool _paused;

        void Awake()
        {
            _animator = GetComponentInChildren<Animator>();
            if (_animator != null)
            {
                // Dozens of these: don't animate the ones nobody can see.
                _animator.cullingMode = AnimatorCullingMode.CullCompletely;
                foreach (var parameter in _animator.parameters)
                    _hasSpeed |= parameter.nameHash == SpeedHash;
            }
            _home = transform.position;
            _renderers = GetComponentsInChildren<Renderer>(true);
        }

        void OnEnable() => All.Add(this);
        void OnDisable() => All.Remove(this);

        void Start() => PickGoal();

        /// <summary>Eaten by a shark: gone for now, reforming at home in a few game hours.</summary>
        public bool Eaten => _returnIn > 0f;

        /// <summary>Caught: gone, back at home in <paramref name="hours"/> game hours.</summary>
        public void Eat(float hours)
        {
            _returnIn = Mathf.Max(0.01f, hours);
            Show(false);
        }

        /// <summary>A shark closing in from <paramref name="from"/>: it bolts the other way for a moment.</summary>
        public void Scare(Vector3 from)
        {
            _fleeUntil = Time.time + 0.6f;
            _fleeFrom = from;
        }

        void Show(bool shown)
        {
            foreach (var renderer in _renderers)
                if (renderer != null)
                    renderer.enabled = shown;
            if (_animator != null)
                _animator.enabled = shown;
        }

        void Update()
        {
            if (Eaten)
            {
                _returnIn -= DayNightCycle.HoursThisFrame;
                if (_returnIn > 0f)
                    return;
                // Reformed, back where it lives.
                _returnIn = 0f;
                transform.position = _home;
                _speed = 0f;
                PickGoal();
                Show(true);
            }

            if (Time.time < _fleeUntil)
            {
                // Straight away from the shark, if there's water that way.
                var away = transform.position - _fleeFrom;
                if (away.sqrMagnitude > 0.01f)
                {
                    var bolt = transform.position + away.normalized * 6f;
                    if (IsOpenWater(bolt))
                        _goal = bolt;
                }
                _paused = false;
            }

            if (_paused)
            {
                if (Time.time >= _pauseUntil)
                {
                    _paused = false;
                    PickGoal();
                }
            }
            else if (Vector3.Distance(transform.position, _goal) < 1f)
            {
                _paused = true;
                _pauseUntil = Time.time + Random.Range(pauseSeconds.x, pauseSeconds.y);
            }

            bool fleeing = Time.time < _fleeUntil;
            float wanted = _paused ? 0f : fleeing ? cruiseSpeed * 3f : cruiseSpeed;
            _speed = Mathf.MoveTowards(_speed, wanted, cruiseSpeed * (fleeing ? 6f : 1f) * Time.deltaTime);
            Steer(_goal);
            Move();

            if (_hasSpeed)
                _animator.SetFloat(SpeedHash, cruiseSpeed > 0f ? _speed / cruiseSpeed : 0f);
        }

        void Steer(Vector3 destination)
        {
            Vector3 toward = destination - transform.position;
            if (toward.sqrMagnitude < 0.01f)
                return;

            var euler = Quaternion.LookRotation(toward.normalized, Vector3.up).eulerAngles;
            float pitch = Mathf.Clamp(Mathf.DeltaAngle(0f, euler.x), -maxPitch, maxPitch);
            var wanted = Quaternion.Euler(pitch, euler.y, 0f);
            transform.rotation = Quaternion.RotateTowards(transform.rotation, wanted, turnSpeed * Time.deltaTime);
        }

        /// <summary>Swims forward unless that leaves the water — then stops and picks somewhere else.</summary>
        void Move()
        {
            if (_speed <= 0.001f)
                return;

            Vector3 next = transform.position + transform.forward * (_speed * Time.deltaTime);
            if (!IsOpenWater(next))
            {
                _speed = 0f;
                PickGoal();
                return;
            }

            transform.position = next;
        }

        void PickGoal()
        {
            var water = WaterVolume.Containing(_home);
            float sea = water != null ? water.SurfaceY : _home.y;
            for (int i = 0; i < 12; i++)
            {
                var flat = Random.insideUnitCircle * roamRadius;
                var candidate = new Vector3(_home.x + flat.x, sea - Random.Range(depthRange.x, depthRange.y), _home.z + flat.y);
                if (IsOpenWater(candidate) && ClearPath(transform.position, candidate))
                {
                    _goal = candidate;
                    return;
                }
            }

            _goal = _home;
        }

        bool ClearPath(Vector3 from, Vector3 to)
        {
            for (int i = 1; i <= 4; i++)
            {
                if (!IsOpenWater(Vector3.Lerp(from, to, i / 4f)))
                    return false;
            }
            return true;
        }

        /// <summary>In the sea, under the waves, above the seabed and inside the shallows.</summary>
        bool IsOpenWater(Vector3 point)
        {
            var water = WaterVolume.Containing(point);
            if (water == null || point.y > water.SurfaceHeightAt(point) - clearance)
                return false;
            return point.y > Seabed.HeightAt(point, water) + clearance && Offshore(point) <= maxOffshore;
        }

        /// <summary>How far past the terrain's edge <paramref name="point"/> is (0 over the island).</summary>
        public static float Offshore(Vector3 point)
        {
            var terrain = Terrain.activeTerrain;
            if (terrain == null)
                return 0f;
            var origin = terrain.GetPosition();
            var size = terrain.terrainData.size;
            float dx = Mathf.Max(origin.x - point.x, 0f, point.x - (origin.x + size.x));
            float dz = Mathf.Max(origin.z - point.z, 0f, point.z - (origin.z + size.z));
            return Mathf.Sqrt(dx * dx + dz * dz);
        }

        void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(0.2f, 0.8f, 0.9f, 0.5f);
            Gizmos.DrawWireSphere(Application.isPlaying ? _home : transform.position, roamRadius);
        }
    }
}
