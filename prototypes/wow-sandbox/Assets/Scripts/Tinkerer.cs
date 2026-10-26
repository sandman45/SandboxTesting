using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace WowSandbox
{
    /// <summary>
    /// An NPC busy about his workshop — Sprocket, in the gnome hut — day and night. He works (the
    /// Working animator bool, a looping EmoteWork) at his bench, where he was placed and facing
    /// the way he was turned, then walks round the gears of his workshop (the gear models under
    /// "Sprocket's Workshop", from WoW Sandbox → Dress Sprocket's Workshop) tinkering at each,
    /// and back to the bench. When you come close he downs tools and turns to you, waves when you
    /// start talking, gestures as he answers each reply (Speaker.Talk), and carries on with his
    /// round when you leave.
    ///
    /// Friendly, so he can be clicked and talked to but not fought. WoW Sandbox → Add Sprocket
    /// sets up the model and animator.
    /// </summary>
    [RequireComponent(typeof(Health))]
    public class Tinkerer : MonoBehaviour
    {
        [Tooltip("How close you come before he stops work and turns to you.")]
        public float noticeRange = 4f;
        [Tooltip("Degrees per second he turns.")]
        public float turnSpeed = 240f;
        [Tooltip("How fast he walks between jobs, in units a second.")]
        public float walkSpeed = 1.1f;
        [Tooltip("Seconds at the bench before he goes round the gears.")]
        public Vector2 benchSeconds = new(20f, 35f);
        [Tooltip("Seconds tinkering at each gear.")]
        public Vector2 gearSeconds = new(8f, 14f);
        [Tooltip("Walk round the workshop's gears. Off: he stays at the bench.")]
        public bool makeRounds = true;

        const string WorkshopName = "Sprocket's Workshop";
        const string GearMarker = "gearcog";
        /// <summary>How far from a gear's edge he stands to work on it.</summary>
        const float GearReach = 0.3f;

        static readonly int WorkingHash = Animator.StringToHash("Working");
        static readonly int WaveHash = Animator.StringToHash("Wave");
        static readonly int SpeedHash = Animator.StringToHash("Speed");

        struct Spot
        {
            public Vector3 position;
            public Quaternion facing;
            public Vector2 seconds;
        }

        Animator _animator;
        readonly List<Spot> _spots = new();
        int _at;
        bool _walking;
        float _workLeft;
        Quaternion _facing;
        bool _turning;
        bool _attending;
        float _lookBackAt;
        Transform _player;

        void Awake()
        {
            _animator = GetComponentInChildren<Animator>();
            GetComponent<Health>().reaction = Reaction.Friendly;
            Vendor.HideEyeGlow(gameObject);
        }

        void Start()
        {
            _spots.Add(new Spot { position = transform.position, facing = transform.rotation, seconds = benchSeconds });
            if (makeRounds)
                FindGears();
            _workLeft = Random.Range(benchSeconds.x, benchSeconds.y);
            SetBool(WorkingHash, true);
        }

        /// <summary>
        /// A spot by each gear in the workshop, on the side toward his bench and facing the gear —
        /// the big stack first, then the loose cogs nearest first.
        /// </summary>
        void FindGears()
        {
            var workshop = GameObject.Find(WorkshopName);
            if (workshop == null)
                return;
            var bench = transform.position;
            var gears = workshop.GetComponentsInChildren<Transform>()
                                .Where(child => child.parent == workshop.transform &&
                                                child.name.IndexOf(GearMarker, System.StringComparison.OrdinalIgnoreCase) >= 0)
                                .Select(child => (child, bounds: Bounds(child)))
                                .OrderByDescending(gear => gear.bounds.size.y)
                                .ThenBy(gear => Vector3.Distance(gear.bounds.center, bench));
            foreach (var (_, bounds) in gears)
            {
                var away = bench - bounds.center;
                away.y = 0f;
                if (away.sqrMagnitude < 0.01f)
                    continue;
                away.Normalize();
                float reach = Mathf.Max(bounds.extents.x, bounds.extents.z) + GearReach;
                var stand = bounds.center + away * reach;
                stand.y = bench.y;
                _spots.Add(new Spot { position = stand, facing = Quaternion.LookRotation(-away), seconds = gearSeconds });
            }
        }

        static Bounds Bounds(Transform root)
        {
            var renderers = root.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0)
                return new Bounds(root.position, Vector3.zero);
            var bounds = renderers[0].bounds;
            foreach (var renderer in renderers.Skip(1))
                bounds.Encapsulate(renderer.bounds);
            return bounds;
        }

        /// <summary>Turns to <paramref name="position"/> and waves.</summary>
        public void Greet(Vector3 position)
        {
            TurnTo(position);
            Trigger(WaveHash);
            // Greeted from across the room (his call-out): a look, then back to it.
            _lookBackAt = Time.time + 3f;
        }

        void Update()
        {
            if (_player == null)
            {
                var player = FindAnyObjectByType<WowCharacterController>();
                if (player != null)
                    _player = player.transform;
            }

            bool near = _player != null && Vector3.Distance(_player.position, transform.position) <= noticeRange;
            if (near != _attending)
            {
                _attending = near;
                if (near)
                {
                    SetBool(WorkingHash, false);
                    SetFloat(SpeedHash, 0f);
                    TurnTo(_player.position);
                }
                else
                    CarryOn();
            }
            else if (!near && _lookBackAt > 0f && Time.time >= _lookBackAt)
            {
                _lookBackAt = 0f;
                CarryOn();
            }

            if (!_attending)
            {
                if (_walking)
                    Walk();
                else if (_spots.Count > 1 && (_workLeft -= Time.deltaTime) <= 0f)
                    SetOff();
            }

            if (!_turning)
                return;
            transform.rotation = Quaternion.RotateTowards(transform.rotation, _facing, turnSpeed * Time.deltaTime);
            _turning = Quaternion.Angle(transform.rotation, _facing) > 0.5f;
        }

        /// <summary>Back to whatever he was at: the walk, or the job in front of him.</summary>
        void CarryOn()
        {
            if (_walking)
            {
                SetFloat(SpeedHash, 1f);
                return;
            }
            SetBool(WorkingHash, true);
            _facing = _spots[_at].facing;
            _turning = true;
        }

        /// <summary>Downs tools and heads for the next job on his round.</summary>
        void SetOff()
        {
            _at = (_at + 1) % _spots.Count;
            _walking = true;
            SetBool(WorkingHash, false);
            SetFloat(SpeedHash, 1f);
        }

        void Walk()
        {
            var target = _spots[_at];
            var to = target.position - transform.position;
            to.y = 0f;
            float step = walkSpeed * Time.deltaTime;
            if (to.magnitude <= step)
            {
                transform.position = target.position;
                _walking = false;
                SetFloat(SpeedHash, 0f);
                SetBool(WorkingHash, true);
                _facing = target.facing;
                _turning = true;
                _workLeft = Random.Range(target.seconds.x, target.seconds.y);
                return;
            }
            transform.position += to.normalized * step;
            _facing = Quaternion.LookRotation(to);
            _turning = true;
        }

        void TurnTo(Vector3 position)
        {
            var toward = position - transform.position;
            toward.y = 0f;
            if (toward.sqrMagnitude < 0.01f)
                return;
            _facing = Quaternion.LookRotation(toward);
            _turning = true;
        }

        bool Has(int hash)
        {
            if (_animator == null || _animator.runtimeAnimatorController == null)
                return false;
            foreach (var parameter in _animator.parameters)
                if (parameter.nameHash == hash)
                    return true;
            return false;
        }

        void SetBool(int hash, bool value)
        {
            if (Has(hash))
                _animator.SetBool(hash, value);
        }

        void SetFloat(int hash, float value)
        {
            if (Has(hash))
                _animator.SetFloat(hash, value);
        }

        void Trigger(int hash)
        {
            if (Has(hash))
                _animator.SetTrigger(hash);
        }
    }
}
