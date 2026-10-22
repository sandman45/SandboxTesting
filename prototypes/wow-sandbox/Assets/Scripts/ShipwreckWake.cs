using UnityEngine;
using UnityEngine.InputSystem;

namespace WowSandbox
{
    /// <summary>
    /// The opening: the player lies on the sand where the wreck washed them up (WoW's Sleep
    /// clip), the storm still at its height, through the title screen and after it. The first
    /// key press wakes them: they sit up (SitGround), then stand (SitGroundUp) and play begins.
    /// The storm rages on until stormSeconds after the game starts (StormHeld, ReleaseStorm). Held in place till they're on their feet. Added to the
    /// player at Play; needs the WashedUp states (WoW Sandbox → Update Warrior Animations).
    /// </summary>
    public class ShipwreckWake : MonoBehaviour
    {
        [Tooltip("Seconds sitting up (the move from lying included) before standing.")]
        public float sitSeconds = 2f;
        [Tooltip("Seconds the wreck's storm stays at full once the game starts (the title screen gone), " +
                 "awake or not, before it starts to clear.")]
        public float stormSeconds = 120f;

        // WashedUp (int): 2 lying, 1 sitting up, 0 standing.
        static readonly int WashedUpHash = Animator.StringToHash("WashedUp");
        static readonly int[] WashedUpStates =
        {
            Animator.StringToHash("WashedUpSleep"), Animator.StringToHash("WashedUpSit"),
            Animator.StringToHash("WashedUpRise"),
        };
        // In case the rise never hands back (a controller without it): free them anyway.
        const float RiseTimeout = 5f;

        /// <summary>True from Play starting until the player wakes.</summary>
        public static bool Lying { get; private set; }

        /// <summary>
        /// True from Play starting until <see cref="ReleaseStorm"/> — StormWeather holds the wreck's
        /// storm at full till then: stormSeconds after the game starts, whether they've woken or not.
        /// </summary>
        public static bool StormHeld { get; private set; }

        WowCharacterController _player;
        Animator _animator;
        bool _locked;
        float _startedAt = -1f;
        float _wokeAt = -1f;
        bool _standing;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void AddToPlayer()
        {
            Lying = StormHeld = false;
            var player = FindAnyObjectByType<WowCharacterController>();
            if (player == null || player.GetComponent<ShipwreckWake>() != null)
                return;
            player.gameObject.AddComponent<ShipwreckWake>();
            // Before any Start, so the weather sees it.
            Lying = StormHeld = true;
        }

        void Start()
        {
            _player = GetComponent<WowCharacterController>();
            _animator = _player != null ? _player.Animator : null;
            if (_animator == null || !HasIntParameter(_animator, WashedUpHash))
            {
                Debug.LogWarning("[ShipwreckWake] No WashedUp (int) parameter on the player's animator — run " +
                                 "WoW Sandbox → Update Warrior Animations. Starting on your feet.", this);
                Destroy(this);
                return;
            }
            _animator.SetInteger(WashedUpHash, 2);
        }

        // Gone early (no WashedUp states, or Play stopping): nothing's left to time the storm, so let it clear.
        void OnDestroy()
        {
            Lying = false;
            ReleaseStorm();
        }

        /// <summary>Lets the wreck's storm start clearing, if it's still being held.</summary>
        public static void ReleaseStorm()
        {
            if (!StormHeld)
                return;
            StormHeld = false;
            if (StormWeather.Active != null)
                StormWeather.Active.ClearAfterWreck();
        }

        void Update()
        {
            // The title screen holds the player itself; take over once it's gone.
            if (TitleScreen.Showing)
                return;
            if (_startedAt < 0f)
                _startedAt = Time.time;
            if (StormHeld && Time.time - _startedAt >= stormSeconds)
                ReleaseStorm();
            // On their feet, it only stays to time the storm.
            if (_standing)
            {
                if (!StormHeld)
                    Destroy(this);
                return;
            }

            if (!_locked)
            {
                _player.SetLocked(true);
                _locked = true;
                // Not this frame: the key that dismissed the title isn't the one that wakes you.
                return;
            }

            if (_wokeAt < 0f)
            {
                var keyboard = Keyboard.current;
                if (keyboard == null || !keyboard.anyKey.wasPressedThisFrame || keyboard.escapeKey.wasPressedThisFrame ||
                    HudPanel.AnyOpen)
                    return;
                _wokeAt = Time.time;
                _animator.SetInteger(WashedUpHash, 1);
                // Awake, but the storm rages on till its time's up (StormHeld).
                Lying = false;
                return;
            }

            if (Time.time - _wokeAt < sitSeconds)
                return;
            _animator.SetInteger(WashedUpHash, 0);

            // On their feet once the animator has left the washed-up states.
            var state = _animator.GetCurrentAnimatorStateInfo(0);
            bool down = System.Array.IndexOf(WashedUpStates, state.shortNameHash) >= 0 || _animator.IsInTransition(0);
            if (down && Time.time - _wokeAt < sitSeconds + RiseTimeout)
                return;
            _player.SetLocked(false);
            _standing = true;
        }

        static bool HasIntParameter(Animator animator, int hash)
        {
            foreach (var parameter in animator.parameters)
                if (parameter.nameHash == hash && parameter.type == AnimatorControllerParameterType.Int)
                    return true;
            return false;
        }
    }
}
