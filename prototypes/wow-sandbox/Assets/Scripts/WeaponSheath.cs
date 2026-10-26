using UnityEngine;
using UnityEngine.InputSystem;

namespace WowSandbox
{
    /// <summary>
    /// Z puts the weapon and shield away and Z again draws them, as in WoW; they stay equipped
    /// either way. A two-hander goes across the back, a one-hander or dagger on the left hip, a
    /// shield on the back; a torch or lantern in the off hand stays lit in hand. The action bar
    /// hides while they're put away. Swinging, any ability, or being attacked draws them again —
    /// a swing or ability at once, a blow with the drawing animation.
    ///
    /// Plays the warrior animator's Sheath layer (Sheath and HipSheath triggers — WoW Sandbox →
    /// Update Warrior Animations) on the upper body, so he can do it on the run, and moves the
    /// meshes over halfway through, when his hand's at the sheath. Without the layer they move
    /// at once. Added to the player at Play.
    /// </summary>
    [RequireComponent(typeof(WowCharacterController))]
    public class WeaponSheath : MonoBehaviour
    {
        // How each item sits put away, in the character's axes (x back, y up, z right; degrees
        // and metres). An item keeps its in-hand facing at rest — blade forward, shield face to
        // the left — so each spot turns it. Tune them in the Inspector in Play: they apply live.
        [Header("Two-handers, across the back")]
        [Tooltip("From the right shoulder, blade down to the left hip, its flat against his back.")]
        public Vector3 backTurn = new(-15.4f, -117.2f, 62.7f);
        public Vector3 backOffset;
        [Header("Shields, on the back")]
        [Tooltip("Face turned from the left to behind him.")]
        public Vector3 shieldTurn = new(0f, -90f, 0f);
        public Vector3 shieldOffset = new(0f, 0f, 0.03f);
        [Header("One-handers and daggers, on the left hip")]
        [Tooltip("Blade down and back.")]
        public Vector3 hipTurn = new(0f, 0f, 116.6f);
        public Vector3 hipOffset;

        const string LayerName = "Sheath";
        static readonly int SheathHash = Animator.StringToHash("Sheath");
        static readonly int HipSheathHash = Animator.StringToHash("HipSheath");
        // How far through the clip his hand reaches the sheath, and how long the layer takes to
        // fade in and out at either end of it (fractions of the clip).
        const float SwapAt = 0.5f;
        const float FadeIn = 0.12f;
        const float FadeOut = 0.15f;

        WowCharacterController _player;
        Inventory _inventory;
        EquipmentVisuals _visuals;
        HealthController _health;
        Animator _animator;
        int _layer = -1;
        float _backSeconds = 1f, _hipSeconds = 1f;

        // The animation under way: when it started, how long it runs, whether it's putting them
        // away or drawing them, and whether the meshes have moved yet.
        float _playStart = -1f, _playSeconds;
        bool _target, _swapped;

        /// <summary>Whether the weapon and shield are put away. The action bar hides while they are.</summary>
        public bool Sheathed { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void AddToPlayer()
        {
            var player = FindAnyObjectByType<WowCharacterController>();
            if (player != null && player.GetComponent<WeaponSheath>() == null)
                player.gameObject.AddComponent<WeaponSheath>();
        }

        void Awake()
        {
            _player = GetComponent<WowCharacterController>();
            _inventory = GetComponent<Inventory>();
            _visuals = GetComponent<EquipmentVisuals>();
            _health = GetComponent<HealthController>();
            _player.Sheath = this;
            if (_visuals != null)
                _visuals.SheathFit = Fit;
        }

        (Quaternion turn, Vector3 offset) Fit(SheathSpot spot) => spot switch
        {
            SheathSpot.Back => (Quaternion.Euler(backTurn), backOffset),
            SheathSpot.Shield => (Quaternion.Euler(shieldTurn), shieldOffset),
            _ => (Quaternion.Euler(hipTurn), hipOffset),
        };

        // A fit changed in the Inspector shows at once.
        void OnValidate()
        {
            if (Application.isPlaying && Sheathed && _visuals != null)
                _visuals.SetSheathed(true);
        }

        void Start()
        {
            _animator = _player.Animator;
            if (_animator == null || _animator.runtimeAnimatorController == null)
                return;
            _layer = _animator.GetLayerIndex(LayerName);
            foreach (var clip in _animator.runtimeAnimatorController.animationClips)
            {
                if (clip.name == "Sheath_v0") _backSeconds = clip.length;
                if (clip.name == "HipSheath_v0") _hipSeconds = clip.length;
            }
            if (_layer < 0)
                Debug.Log("[WeaponSheath] The player's animator has no Sheath layer — weapons are put away with no " +
                          "animation. Run WoW Sandbox → Update Warrior Animations.", this);
        }

        void OnEnable() => Combat.PlayerAttacked += OnAttacked;
        void OnDisable() => Combat.PlayerAttacked -= OnAttacked;

        // A blow draws them, the way WoW does when a fight starts.
        void OnAttacked(HealthController target, AttackResult result)
        {
            if (target != null && target.gameObject == gameObject && Sheathed && _playStart < 0f)
                Begin(false);
        }

        /// <summary>A weapon in the main hand or a shield in the off hand: something to put away.</summary>
        bool HasGear => _inventory != null &&
                        (_inventory.Equipped(EquipSlot.MainHand) is { kind: ItemKind.Weapon } ||
                         _inventory.Equipped(EquipSlot.OffHand) is { kind: ItemKind.Shield });

        void Update()
        {
            // Everything taken off while put away: the next weapon comes out in hand.
            if (Sheathed && !HasGear)
                Set(false);

            UpdateAnimation();

            var keyboard = Keyboard.current;
            if (keyboard == null || !keyboard.zKey.wasPressedThisFrame || HudPanel.ModalOpen)
                return;
            if (_playStart >= 0f || _player.IsLocked || TitleScreen.Showing || ShipwreckWake.Lying ||
                _health != null && _health.IsDead || _visuals != null && _visuals.Holding)
                return;
            if (!HasGear)
            {
                Hud.Error("You have no weapon to put away.");
                return;
            }
            Begin(!Sheathed);
        }

        /// <summary>
        /// Draws them at once, with no animation — before a swing or an ability
        /// (WowCharacterController.TryBeginAction), so the blow is struck with the weapon in hand.
        /// </summary>
        public void DrawNow()
        {
            if (!Sheathed && _playStart < 0f)
                return;
            StopAnimation();
            Set(false);
        }

        /// <summary>Puts them away or draws them at once (loading a save).</summary>
        public void Restore(bool sheathed)
        {
            StopAnimation();
            Set(sheathed && HasGear);
        }

        void Begin(bool sheathe)
        {
            if (_layer < 0)
            {
                Set(sheathe);
                return;
            }
            // Over the shoulder for a two-hander, or a shield on its own; to the hip otherwise.
            var weapon = _inventory.Equipped(EquipSlot.MainHand);
            bool hip = weapon is { kind: ItemKind.Weapon, twoHanded: false };
            _animator.SetTrigger(hip ? HipSheathHash : SheathHash);
            _playStart = Time.time;
            _playSeconds = Mathf.Max(hip ? _hipSeconds : _backSeconds, 0.1f);
            _swapped = false;
            _target = sheathe;
        }

        void UpdateAnimation()
        {
            if (_playStart < 0f)
                return;
            float t = (Time.time - _playStart) / _playSeconds;
            if (!_swapped && t >= SwapAt)
            {
                _swapped = true;
                Set(_target);
            }
            if (t >= 1f)
            {
                StopAnimation();
                return;
            }
            _animator.SetLayerWeight(_layer, Mathf.Clamp01(t / FadeIn) * Mathf.Clamp01((1f - t) / FadeOut));
        }

        void StopAnimation()
        {
            if (_playStart >= 0f && !_swapped)
                Set(_target);
            _playStart = -1f;
            if (_layer >= 0 && _animator != null)
            {
                _animator.SetLayerWeight(_layer, 0f);
                _animator.ResetTrigger(SheathHash);
                _animator.ResetTrigger(HipSheathHash);
            }
        }

        void Set(bool sheathed)
        {
            Sheathed = sheathed;
            if (_visuals != null)
                _visuals.SetSheathed(sheathed);
        }
    }
}
