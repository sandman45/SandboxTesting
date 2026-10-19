using UnityEngine;
using UnityEngine.InputSystem;

namespace WowSandbox
{
    /// <summary>
    /// The opening: the player lies on the sand where the wreck washed them up (WoW's Sleep
    /// clip), the storm still at its height, through the title screen and after it. The first
    /// key press wakes them: the storm starts dying down, they sit up (SitGround), then stand
    /// (SitGroundUp) and play begins. Held in place till they're on their feet. Added to the
    /// player at Play; needs the WashedUp states (WoW Sandbox → Update Warrior Animations).
    /// </summary>
    public class ShipwreckWake : MonoBehaviour
    {
        [Tooltip("Seconds sitting up (the move from lying included) before standing.")]
        public float sitSeconds = 2f;

        // WashedUp (int): 2 lying, 1 sitting up, 0 standing.
        static readonly int WashedUpHash = Animator.StringToHash("WashedUp");
        static readonly int[] WashedUpStates =
        {
            Animator.StringToHash("WashedUpSleep"), Animator.StringToHash("WashedUpSit"),
            Animator.StringToHash("WashedUpRise"),
        };
        // In case the rise never hands back (a controller without it): free them anyway.
        const float RiseTimeout = 5f;

        /// <summary>True from Play starting until the player wakes — StormWeather holds its storm at full till then.</summary>
        public static bool Lying { get; private set; }

        WowCharacterController _player;
        Animator _animator;
        bool _locked;
        float _wokeAt = -1f;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void AddToPlayer()
        {
            Lying = false;
            var player = FindAnyObjectByType<WowCharacterController>();
            if (player == null || player.GetComponent<ShipwreckWake>() != null)
                return;
            player.gameObject.AddComponent<ShipwreckWake>();
            // Before any Start, so the weather sees it.
            Lying = true;
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

        void OnDestroy() => ClearStorm();

        static void ClearStorm()
        {
            if (!Lying)
                return;
            Lying = false;
            if (StormWeather.Active != null)
                StormWeather.Active.ClearAfterWreck();
        }

        void Update()
        {
            // The title screen holds the player itself; take over once it's gone.
            if (TitleScreen.Showing)
                return;
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
                ClearStorm();
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
            Destroy(this);
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
