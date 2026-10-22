using UnityEngine;

namespace WowSandbox
{
    /// <summary>
    /// Sea creatures: sharks and the like. The NavMesh only covers land, so this steers
    /// itself in 3D instead — cruising between random points around its home, always
    /// below the waves and above the seabed, turning and pitching smoothly rather than
    /// snapping.
    ///
    /// Hostile ones hunt: a player swimming within aggroRange gets chased and bitten
    /// (Combat.AttackPlayer, d20 against your AC). Anything gets provoked by being hit, so
    /// a neutral whale shark fights back. It gives up and swims home — healing as it goes,
    /// as WoW mobs do when they leash — once you leave the water, die, or drag it too far
    /// from home. Standing on the beach is safe.
    ///
    /// Hostile ones feed, too: every feedEveryHours game hours one goes after the nearest fish
    /// school (AmbientSwimmer) round its patch, which bolts as it closes in. Caught, the school's
    /// eaten (with a splash, near the surface) and reforms a few game hours later. A swimming
    /// player comes first; a hunt it can't reach, or that drags on, it gives up.
    ///
    /// Root forward is the creature's facing (the spawner turns the model child to match),
    /// and the root sits at the body's centre.
    /// </summary>
    [RequireComponent(typeof(Health))]
    public class SwimmingCreature : MonoBehaviour
    {
        [Header("Swimming")]
        public float cruiseSpeed = 3f;
        public float chaseSpeed = 7f;
        [Tooltip("Degrees per second.")]
        public float turnSpeed = 90f;
        [Range(0f, 60f)] public float maxPitch = 25f;
        [Tooltip("How far from home it roams while cruising.")]
        public float roamRadius = 60f;
        [Tooltip("Depth band below the calm sea level it cruises in (min, max).")]
        public Vector2 depthRange = new(2f, 12f);
        [Tooltip("Keep at least this much water between the body's centre and the surface or seabed.")]
        public float clearance = 1.5f;
        public Vector2 pauseSeconds = new(0f, 3f);
        [Tooltip("Playback speed of its animations. 0 = by size: a creature scaled up beats its tail " +
                 "slower (1 / square root of its scale), the way big animals do.")]
        [Range(0f, 2f)] public float animationSpeed = 0f;

        [Header("Hunting")]
        [Tooltip("Hostile creatures notice a swimming player this close.")]
        public float aggroRange = 25f;
        [Tooltip("Gives up the chase once the player is this far from its home.")]
        public float leashRange = 90f;
        [Tooltip("How close the player must be to the body to get bitten.")]
        public float biteReach = 1.5f;
        public float attackInterval = 2f;

        [Header("Feeding")]
        // Not serialized, so these tuned values reach the sharks already in the scene.
        [System.NonSerialized] public Vector2 feedEveryHours = new(3f, 6f);
        [System.NonSerialized] public Vector2 schoolReturnHours = new(3f, 6f);
        [System.NonSerialized] public float feedRange = 45f;
        const float FeedGiveUpSeconds = 30f;

        enum Mode { Cruise, Pause, Chase, Return, Feed }

        static readonly int SpeedHash = Animator.StringToHash("Speed");
        static readonly int AttackHash = Animator.StringToHash("Attack");

        Health _health;
        CharacterStats _stats;
        Collider _collider;
        Animator _animator;
        bool _hasSpeed;
        bool _hasAttack;

        HealthController _player;
        WowCharacterController _playerMovement;
        CharacterController _playerBody;

        Vector3 _home;
        Vector3 _goal;
        Mode _mode;
        float _speed;
        float _pauseUntil;
        float _nextAttack;
        bool _provoked;
        float _hoursToFeed;
        AmbientSwimmer _prey;
        float _feedStarted;

        void Awake()
        {
            _health = GetComponent<Health>();
            _stats = GetComponent<CharacterStats>();
            _collider = GetComponent<Collider>();
            _animator = GetComponentInChildren<Animator>();
            _hasSpeed = HasParameter(SpeedHash);
            _hasAttack = HasParameter(AttackHash);
            ApplyAnimationSpeed();
            _home = transform.position;
            _health.Damaged += OnDamaged;
        }

        void OnDestroy()
        {
            if (_health != null)
                _health.Damaged -= OnDamaged;
        }

        void Start()
        {
            PickGoal();
            // Not every shark hungry at once.
            _hoursToFeed = Random.Range(0.5f, feedEveryHours.y);
        }

        void OnDamaged(float amount) => _provoked = true;

        /// <summary>
        /// Sets it on the player straight away and keeps it on them however far they swim —
        /// for sharks summoned by DeepSeaDanger rather than met by chance.
        /// </summary>
        public void Hunt()
        {
            _provoked = true;
            leashRange = float.MaxValue;
            aggroRange = Mathf.Max(aggroRange, 80f);
        }

        void Update()
        {
            if (_health.IsDead)
            {
                SetAnimatorSpeed(0f);
                return;
            }

            FindPlayer();
            UpdateMode();

            float targetSpeed = _mode switch
            {
                Mode.Chase => chaseSpeed,
                Mode.Feed => chaseSpeed * 0.85f,
                Mode.Return => chaseSpeed * 0.7f,
                Mode.Cruise => cruiseSpeed,
                _ => 0f,
            };
            _speed = Mathf.MoveTowards(_speed, targetSpeed, chaseSpeed * Time.deltaTime);

            Vector3 destination = _mode switch
            {
                Mode.Chase => KeepInWater(PlayerChest()),
                Mode.Feed => KeepInWater(_prey.transform.position),
                Mode.Return => _home,
                _ => _goal,
            };

            Steer(destination);
            Move();

            if (_mode == Mode.Chase)
                TryBite();
            else if (_mode == Mode.Feed)
                TryEat();
            else if (_mode == Mode.Return)
                _health.Heal(_health.maxHealth * 0.1f * Time.deltaTime);

            SetAnimatorSpeed(cruiseSpeed > 0f ? _speed / cruiseSpeed : 0f);
        }

        void UpdateMode()
        {
            bool canHunt = _health.reaction == Reaction.Hostile || _provoked;
            bool playerHuntable = _player != null && !_player.IsDead && _playerMovement != null &&
                                  _playerMovement.IsSwimming &&
                                  Vector3.Distance(_player.transform.position, _home) <= leashRange;

            if (_health.reaction == Reaction.Hostile && _mode != Mode.Feed && _mode != Mode.Chase)
                _hoursToFeed -= DayNightCycle.HoursThisFrame;

            switch (_mode)
            {
                case Mode.Feed:
                    // The player swims by: that's better eating.
                    if (canHunt && playerHuntable && Vector3.Distance(transform.position, PlayerChest()) <= aggroRange)
                    {
                        _mode = Mode.Chase;
                        _prey = null;
                    }
                    else if (_prey == null || _prey.Eaten || Time.time - _feedStarted > FeedGiveUpSeconds ||
                             Vector3.Distance(_prey.transform.position, _home) > roamRadius + feedRange)
                        GiveUpFeeding();
                    return;

                case Mode.Chase:
                    if (!playerHuntable)
                    {
                        _mode = Mode.Return;
                        _provoked = false;
                    }
                    return;

                case Mode.Return:
                    if (Vector3.Distance(transform.position, _home) < 3f)
                    {
                        _mode = Mode.Pause;
                        _pauseUntil = Time.time + 1f;
                    }
                    return;
            }

            if (canHunt && playerHuntable &&
                (_provoked || Vector3.Distance(transform.position, PlayerChest()) <= aggroRange))
            {
                _mode = Mode.Chase;
                return;
            }

            if (_hoursToFeed <= 0f && _health.reaction == Reaction.Hostile && StartFeeding())
                return;

            if (_mode == Mode.Pause && Time.time >= _pauseUntil)
            {
                PickGoal();
                _mode = Mode.Cruise;
            }
            else if (_mode == Mode.Cruise && Vector3.Distance(transform.position, _goal) < 2f)
            {
                _mode = Mode.Pause;
                _pauseUntil = Time.time + Random.Range(pauseSeconds.x, pauseSeconds.y);
            }
        }

        /// <summary>Turns toward <paramref name="destination"/> at turnSpeed, pitch clamped.</summary>
        void Steer(Vector3 destination)
        {
            Vector3 toward = destination - transform.position;
            if (toward.sqrMagnitude < 0.01f)
                return;

            var wanted = Quaternion.LookRotation(toward.normalized, Vector3.up);
            var euler = wanted.eulerAngles;
            float pitch = Mathf.DeltaAngle(0f, euler.x);
            wanted = Quaternion.Euler(Mathf.Clamp(pitch, -maxPitch, maxPitch), euler.y, 0f);
            transform.rotation = Quaternion.RotateTowards(transform.rotation, wanted, turnSpeed * Time.deltaTime);
        }

        /// <summary>
        /// Swims forward unless that would leave the water, hit the seabed or run into
        /// something solid — in which case it stops and, when cruising, picks somewhere else.
        /// </summary>
        void Move()
        {
            if (_speed <= 0.001f)
                return;

            Vector3 step = transform.forward * (_speed * Time.deltaTime);
            Vector3 next = transform.position + step;

            bool blocked = !IsOpenWater(next) ||
                           Physics.Raycast(transform.position, transform.forward, out var hit,
                               BodyLength() * 0.5f + step.magnitude, ~0, QueryTriggerInteraction.Ignore) &&
                           !IsPart(hit.collider) && hit.collider.GetComponentInParent<HealthController>() == null;

            if (blocked)
            {
                _speed = 0f;
                if (_mode == Mode.Cruise)
                    PickGoal();
                else if (_mode == Mode.Feed)
                    GiveUpFeeding();
                return;
            }

            transform.position = next;
        }

        void TryBite()
        {
            if (Time.time < _nextAttack || _stats == null || _collider == null)
                return;

            // Gap between the two bodies, not centre to centre: a shark has to stay under the
            // waves while you swim at the surface, so it can only ever reach your legs.
            Vector3 jaws = _collider.ClosestPoint(PlayerChest());
            Vector3 you = _playerBody != null ? _playerBody.ClosestPoint(jaws) : PlayerChest();
            if (Vector3.Distance(jaws, you) > biteReach)
                return;

            _nextAttack = Time.time + attackInterval;
            if (_hasAttack)
                _animator.SetTrigger(AttackHash);
            Combat.AttackPlayer(_stats, _player);
        }

        /// <summary>Hungry now: off after a school as soon as it's free — the Developer panel.</summary>
        public void GetHungry() => _hoursToFeed = 0f;

        /// <summary>Goes after the nearest uneaten school round its patch. False if there's none.</summary>
        bool StartFeeding()
        {
            AmbientSwimmer nearest = null;
            float best = feedRange;
            foreach (var school in AmbientSwimmer.All)
            {
                if (school == null || school.Eaten ||
                    Vector3.Distance(school.transform.position, _home) > roamRadius + feedRange)
                    continue;
                float distance = Vector3.Distance(school.transform.position, transform.position);
                if (distance < best)
                {
                    best = distance;
                    nearest = school;
                }
            }
            if (nearest == null)
            {
                // Nothing about: look again in a while.
                _hoursToFeed = 0.5f;
                return false;
            }
            _prey = nearest;
            _feedStarted = Time.time;
            _mode = Mode.Feed;
            return true;
        }

        void GiveUpFeeding()
        {
            _prey = null;
            _hoursToFeed = 0.5f;
            _mode = Mode.Cruise;
            PickGoal();
        }

        /// <summary>Close enough to the school, the jaws close: it's eaten.</summary>
        void TryEat()
        {
            if (_prey == null)
                return;
            float distance = Vector3.Distance(transform.position, _prey.transform.position);
            if (distance < 10f)
                _prey.Scare(transform.position);
            if (distance > BodyLength() * 0.5f + 1.2f)
                return;

            if (_hasAttack)
                _animator.SetTrigger(AttackHash);
            var water = WaterVolume.Containing(_prey.transform.position);
            if (water != null)
            {
                var spot = _prey.transform.position;
                float surface = water.SurfaceHeightAt(spot);
                if (surface - spot.y < 2.5f)
                    WaterSplash.Play(new Vector3(spot.x, surface, spot.z), 0.6f);
            }
            _prey.Eat(Random.Range(schoolReturnHours.x, schoolReturnHours.y));
            _prey = null;
            _hoursToFeed = Random.Range(feedEveryHours.x, feedEveryHours.y);
            _mode = Mode.Pause;
            _pauseUntil = Time.time + 1.5f;
        }

        void PickGoal()
        {
            for (int i = 0; i < 12; i++)
            {
                var flat = Random.insideUnitCircle * roamRadius;
                var water = WaterVolume.Containing(_home);
                float sea = water != null ? water.SurfaceY : _home.y;
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
            for (int i = 1; i <= 6; i++)
            {
                if (!IsOpenWater(Vector3.Lerp(from, to, i / 6f)))
                    return false;
            }
            return true;
        }

        /// <summary>In the sea, under the waves and above the seabed, with clearance both ways.</summary>
        bool IsOpenWater(Vector3 point)
        {
            var water = WaterVolume.Containing(point);
            if (water == null)
                return false;
            if (point.y > water.SurfaceHeightAt(point) - clearance)
                return false;
            return point.y > Seabed.HeightAt(point, water) + clearance;
        }

        /// <summary>Where the chase is headed, pulled down out of the waves if the player's at the surface.</summary>
        Vector3 KeepInWater(Vector3 point)
        {
            var water = WaterVolume.Containing(point) ?? WaterVolume.Containing(_home);
            if (water == null)
                return point;
            point.y = Mathf.Min(point.y, water.SurfaceHeightAt(point) - clearance);
            return point;
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

        Vector3 PlayerChest() =>
            _player.transform.position + Vector3.up * (_playerBody != null ? _playerBody.height * 0.6f : 1f);

        float BodyLength() => _collider != null ? _collider.bounds.size.magnitude * 0.6f : 2f;

        bool IsPart(Collider other) => other.transform.IsChildOf(transform);

        void ApplyAnimationSpeed()
        {
            if (_animator == null)
                return;
            // The model's scale, not the root's: the spawner sizes the model under it.
            float scale = Mathf.Max(_animator.transform.lossyScale.y, 0.01f);
            _animator.speed = animationSpeed > 0f ? animationSpeed : 1f / Mathf.Sqrt(scale);
        }

        // So the slider can be tuned live in Play.
        void OnValidate()
        {
            if (Application.isPlaying)
                ApplyAnimationSpeed();
        }

        void SetAnimatorSpeed(float value)
        {
            if (_hasSpeed)
                _animator.SetFloat(SpeedHash, value);
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

        void OnDrawGizmosSelected()
        {
            Vector3 home = Application.isPlaying ? _home : transform.position;
            Gizmos.color = new Color(0.2f, 0.7f, 1f, 0.5f);
            Gizmos.DrawWireSphere(home, roamRadius);
            Gizmos.color = new Color(1f, 0.3f, 0.2f, 0.5f);
            Gizmos.DrawWireSphere(transform.position, aggroRange);
        }
    }
}
