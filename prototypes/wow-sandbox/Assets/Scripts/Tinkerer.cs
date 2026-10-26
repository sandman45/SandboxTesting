using UnityEngine;

namespace WowSandbox
{
    /// <summary>
    /// An NPC busy at a workbench — Sprocket, in the gnome hut. Works (the Working animator
    /// bool, a looping EmoteWork) facing the way he was placed, day and night. When you come
    /// close he downs tools and turns to you, waves when you start talking, gestures as he
    /// answers each reply (Speaker.Talk), and goes back to the bench when you leave.
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

        static readonly int WorkingHash = Animator.StringToHash("Working");
        static readonly int WaveHash = Animator.StringToHash("Wave");

        Animator _animator;
        Quaternion _bench;
        Quaternion _facing;
        bool _turning;
        bool _attending;
        float _backToBenchAt;
        Transform _player;

        void Awake()
        {
            _animator = GetComponentInChildren<Animator>();
            GetComponent<Health>().reaction = Reaction.Friendly;
            Vendor.HideEyeGlow(gameObject);
            _bench = transform.rotation;
        }

        void Start() => SetBool(WorkingHash, true);

        /// <summary>Turns to <paramref name="position"/> and waves.</summary>
        public void Greet(Vector3 position)
        {
            TurnTo(position);
            Trigger(WaveHash);
            // Greeted from across the room (his call-out): a look, then back to work.
            _backToBenchAt = Time.time + 3f;
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
                SetBool(WorkingHash, !near);
                if (near)
                    TurnTo(_player.position);
                else
                {
                    _facing = _bench;
                    _turning = true;
                }
            }
            else if (!near && _backToBenchAt > 0f && Time.time >= _backToBenchAt)
            {
                _backToBenchAt = 0f;
                _facing = _bench;
                _turning = true;
            }

            if (!_turning)
                return;
            transform.rotation = Quaternion.RotateTowards(transform.rotation, _facing, turnSpeed * Time.deltaTime);
            _turning = Quaternion.Angle(transform.rotation, _facing) > 0.5f;
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

        void SetBool(int hash, bool value)
        {
            if (_animator == null || _animator.runtimeAnimatorController == null)
                return;
            foreach (var parameter in _animator.parameters)
                if (parameter.nameHash == hash)
                {
                    _animator.SetBool(hash, value);
                    return;
                }
        }

        void Trigger(int hash)
        {
            if (_animator == null || _animator.runtimeAnimatorController == null)
                return;
            foreach (var parameter in _animator.parameters)
                if (parameter.nameHash == hash)
                {
                    _animator.SetTrigger(hash);
                    return;
                }
        }
    }
}
