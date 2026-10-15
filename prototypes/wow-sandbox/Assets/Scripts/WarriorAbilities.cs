using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace WowSandbox
{
    /// <summary>
    /// The warrior's abilities, on keys 3–6 beside the swing (1) and the potion (2), each with
    /// its own cooldown and all sharing the swing's (WowCharacterController.TryBeginAction —
    /// WoW's global cooldown):
    /// - 3 Heroic Strike: your weapon attack with +1d8 damage.
    /// - 4 Shield Bash: needs a shield; d4 + STR, and a hit stuns the target for a moment.
    /// - 5 Whirlwind: a weapon attack on every enemy within reach, all around you.
    /// - 6 Charge: rush a target 8–25 units away, then strike it on arrival, stunning it briefly.
    /// Rolls go through Combat like any swing, so they can miss, crit and kill. An action bar
    /// along the bottom shows all six keys and their cooldowns.
    /// </summary>
    [RequireComponent(typeof(WowCharacterController))]
    public class WarriorAbilities : MonoBehaviour
    {
        [Header("Heroic Strike (3)")]
        public float heroicStrikeCooldown = 6f;
        public int heroicStrikeDice = 1;
        public int heroicStrikeSides = 8;

        [Header("Shield Bash (4)")]
        public float shieldBashCooldown = 12f;
        public int shieldBashSides = 4;
        public float shieldBashStun = 2f;

        [Header("Whirlwind (5)")]
        public float whirlwindCooldown = 10f;

        [Header("Charge (6)")]
        public float chargeCooldown = 15f;
        public float chargeMinRange = 8f;
        public float chargeMaxRange = 25f;
        [Tooltip("Charge speed, as a multiple of your run speed.")]
        public float chargeSpeedMultiplier = 3f;
        public float chargeStun = 1f;

        static readonly int HeroicStrikeHash = Animator.StringToHash("HeroicStrike");
        static readonly int ShieldBashHash = Animator.StringToHash("ShieldBash");
        static readonly int WhirlwindHash = Animator.StringToHash("Whirlwind");

        WowCharacterController _movement;
        Inventory _inventory;
        HealthController _health;

        float _heroicReady, _bashReady, _whirlwindReady, _chargeReady;

        void Awake()
        {
            _movement = GetComponent<WowCharacterController>();
            _inventory = GetComponent<Inventory>();
            _health = GetComponent<HealthController>();
        }

        void Start() => ActionBarHud.Build(this);

        void Update()
        {
            var keyboard = Keyboard.current;
            if (keyboard == null)
                return;
            if (keyboard.digit3Key.wasPressedThisFrame) Use(3);
            if (keyboard.digit4Key.wasPressedThisFrame) Use(4);
            if (keyboard.digit5Key.wasPressedThisFrame) Use(5);
            if (keyboard.digit6Key.wasPressedThisFrame) Use(6);
        }

        bool CanAct => !_movement.IsLocked && !TitleScreen.Showing && !(_health != null && _health.IsDead);

        /// <summary>
        /// What key <paramref name="key"/> does — the abilities on 3–6 — for the action bar's
        /// buttons too. 1 and 2 (the swing and the potion) are handled by their own components
        /// on the keyboard, so a click on those goes to them.
        /// </summary>
        public void Use(int key)
        {
            if (!CanAct)
                return;
            switch (key)
            {
                case 1: _movement.Attack(); break;
                case 2: if (_inventory != null) _inventory.DrinkPotion(); break;
                case 3: HeroicStrike(); break;
                case 4: ShieldBash(); break;
                case 5: Whirlwind(); break;
                case 6: Charge(); break;
            }
        }

        // --- The abilities -----------------------------------------------------------------

        void HeroicStrike()
        {
            if (!Ready(_heroicReady, "Heroic Strike") || !MeleeTarget(out var target) || !_movement.TryBeginAction())
                return;
            _heroicReady = Time.time + heroicStrikeCooldown;
            Play(HeroicStrikeHash);
            _movement.Strike(target, new AttackOptions { BonusDice = heroicStrikeDice, BonusSides = heroicStrikeSides });
        }

        void ShieldBash()
        {
            if (!Ready(_bashReady, "Shield Bash"))
                return;
            if (!(_inventory != null && _inventory.Equipped(EquipSlot.OffHand) is { kind: ItemKind.Shield }))
            {
                Hud.Error("You need a shield equipped.");
                return;
            }
            if (!MeleeTarget(out var target) || !_movement.TryBeginAction())
                return;
            _bashReady = Time.time + shieldBashCooldown;
            Play(ShieldBashHash);
            var result = _movement.Strike(target, new AttackOptions { ReplaceDice = 1, ReplaceSides = shieldBashSides });
            if (result.Hit && !result.Killed)
                Stun(target, shieldBashStun);
        }

        void Whirlwind()
        {
            if (!Ready(_whirlwindReady, "Whirlwind") || !_movement.TryBeginAction())
                return;
            _whirlwindReady = Time.time + whirlwindCooldown;
            Play(WhirlwindHash);

            // Everything that can be hurt, isn't friendly and is within reach — any direction.
            var struck = new HashSet<Health>();
            foreach (var hit in Physics.OverlapSphere(transform.position, _movement.attackRange + 2f, ~0, QueryTriggerInteraction.Ignore))
            {
                var enemy = hit.GetComponentInParent<Health>();
                if (enemy == null || enemy.IsDead || enemy.reaction == Reaction.Friendly || !struck.Add(enemy))
                    continue;
                if (_movement.InReach(enemy, needFacing: false))
                    _movement.Strike(enemy);
            }
        }

        void Charge()
        {
            if (!Ready(_chargeReady, "Charge"))
                return;
            var target = _movement.EnemyTarget(out string error);
            if (target == null)
            {
                Hud.Error(error);
                return;
            }
            if (_movement.IsSwimming)
            {
                Hud.Error("You can't charge while swimming.");
                return;
            }
            var gap = target.transform.position - transform.position;
            gap.y = 0f;
            if (gap.magnitude < chargeMinRange)
            {
                Hud.Error("Too close to charge.");
                return;
            }
            if (gap.magnitude > chargeMaxRange)
            {
                Hud.Error("Out of range.");
                return;
            }
            if (!_movement.TryBeginAction())
                return;

            _chargeReady = Time.time + chargeCooldown;
            _movement.Charge(target, _movement.runSpeed * chargeSpeedMultiplier, () =>
            {
                var result = _movement.Strike(target);
                if (!result.Killed)
                    Stun(target, chargeStun);
            });
        }

        // --- Helpers -------------------------------------------------------------------------

        static bool Ready(float readyAt, string name)
        {
            if (Time.time >= readyAt)
                return true;
            Hud.Error($"{name} isn't ready yet.");
            return false;
        }

        /// <summary>The target, if it's an enemy in reach and in front of you; else an error shown.</summary>
        bool MeleeTarget(out Health target)
        {
            target = _movement.EnemyTarget(out string error);
            if (target == null)
            {
                Hud.Error(error);
                return false;
            }
            if (!_movement.InReach(target, needFacing: true))
            {
                Hud.Error("You are too far away!");
                target = null;
                return false;
            }
            return true;
        }

        void Play(int trigger)
        {
            var animator = _movement.Animator;
            if (animator == null || animator.runtimeAnimatorController == null)
                return;
            foreach (var parameter in animator.parameters)
            {
                if (parameter.nameHash == trigger)
                {
                    animator.SetInteger("WeaponStyle", _movement.CurrentWeaponStyle);
                    animator.SetTrigger(trigger);
                    return;
                }
            }
        }

        static void Stun(Health target, float seconds)
        {
            if (target.TryGetComponent<NpcCombat>(out var npc))
                npc.Stun(seconds);
        }

        // --- For the action bar ---------------------------------------------------------------

        /// <summary>Seconds left on each key's cooldown, 1–6 (1 and 2 are the swing and the potion).</summary>
        public float CooldownLeft(int key)
        {
            float own = key switch
            {
                3 => _heroicReady,
                4 => _bashReady,
                5 => _whirlwindReady,
                6 => _chargeReady,
                _ => 0f,
            } - Time.time;
            // The shared cooldown covers the swing and every ability, not the potion.
            float shared = key == 2 ? 0f : _movement.SwingCooldownLeft;
            return Mathf.Max(own, shared, 0f);
        }

        /// <summary>
        /// Whether pressing the key now could work, cooldowns aside: a shield for Shield Bash,
        /// an enemy in reach for the melee moves, in charge range for Charge. The bar shows the
        /// rest in red, like WoW's out-of-range buttons.
        /// </summary>
        public bool Usable(int key)
        {
            if (key == 2 || key == 5)
                return true;
            var target = _movement.EnemyTarget(out _);
            if (key == 4 && !(_inventory != null && _inventory.Equipped(EquipSlot.OffHand) is { kind: ItemKind.Shield }))
                return false;
            if (target == null)
                return false;
            if (key == 6)
            {
                var gap = target.transform.position - transform.position;
                gap.y = 0f;
                return !_movement.IsSwimming && gap.magnitude >= chargeMinRange && gap.magnitude <= chargeMaxRange;
            }
            return _movement.InReach(target, needFacing: false);
        }

        /// <summary>
        /// The tooltip for key <paramref name="key"/>: name, range and cooldown, what it does
        /// with the weapon you're holding now, and anything stopping you using it.
        /// </summary>
        public string Describe(int key)
        {
            string dim = HudTheme.Hex(HudTheme.TextDim);
            string good = HudTheme.Hex(HudTheme.Friendly);
            string bad = HudTheme.Hex(HudTheme.Hostile);
            var lines = new List<string>();

            var weapon = _inventory != null ? _inventory.Equipped(EquipSlot.MainHand) : null;
            string weaponDamage = weapon != null
                ? $"{Dice.Format(weapon.damageDieCount, weapon.damageDieSides, weapon.magicBonus)} + STR"
                : "1 + STR";
            string reach = $"{_movement.attackRange:0.#} yd range";
            float gcd = _movement.swingInterval;

            void Header(string name, string left, string right)
            {
                lines.Add($"<size=17><b>{name}</b></size>");
                lines.Add($"<color=#{dim}>{left}</color>" + (right != null ? $"<color=#{dim}>  ·  {right}</color>" : ""));
            }

            switch (key)
            {
                case 1:
                    Header("Attack", reach, $"{gcd:0.#} sec swing");
                    lines.Add($"Swing at your target with your {(weapon != null ? weapon.name : "fists")} for {weaponDamage} damage.");
                    break;
                case 2:
                    int potions = 0;
                    if (_inventory != null)
                        for (int i = 0; i < _inventory.BagSize; i++)
                            if (_inventory.BagItem(i) is { IsPotion: true } potion)
                                potions += potion.count;
                    Header("Healing Potion", "Instant", null);
                    lines.Add("Drink the first healing potion in your bags.");
                    lines.Add(potions > 0
                        ? $"<color=#{dim}>{potions} in your bags</color>"
                        : $"<color=#{bad}>You have no potions.</color>");
                    break;
                case 3:
                    Header("Heroic Strike", reach, $"{heroicStrikeCooldown:0.#} sec cooldown");
                    lines.Add($"A mighty blow: your weapon's {weaponDamage} damage, plus " +
                              $"<color=#{good}>{heroicStrikeDice}d{heroicStrikeSides}</color>.");
                    break;
                case 4:
                    Header("Shield Bash", reach, $"{shieldBashCooldown:0.#} sec cooldown");
                    lines.Add($"Bash your target with your shield for 1d{shieldBashSides} + STR damage, " +
                              $"stunning it for <color=#{good}>{shieldBashStun:0.#} sec</color>.");
                    if (!(_inventory != null && _inventory.Equipped(EquipSlot.OffHand) is { kind: ItemKind.Shield }))
                        lines.Add($"<color=#{bad}>Requires a shield</color>");
                    break;
                case 5:
                    Header("Whirlwind", reach + ", all around", $"{whirlwindCooldown:0.#} sec cooldown");
                    lines.Add($"Spin and strike every enemy within reach for {weaponDamage} damage. No target needed.");
                    break;
                case 6:
                    Header("Charge", $"{chargeMinRange:0.#}–{chargeMaxRange:0.#} yd range", $"{chargeCooldown:0.#} sec cooldown");
                    lines.Add($"Rush at your target and strike it for {weaponDamage} damage, " +
                              $"stunning it for <color=#{good}>{chargeStun:0.#} sec</color>. Not while swimming.");
                    break;
                default:
                    return null;
            }

            if (key != 2)
                lines.Add($"<color=#{dim}>Rolls to hit like a swing — it can miss, or crit for double dice.</color>");
            float left = CooldownLeft(key);
            if (left > 0.05f)
                lines.Add($"<color=#{bad}>Cooldown remaining: {left:0.0} sec</color>");
            lines.Add($"<color=#{dim}><i>Key {key}, or click</i></color>");
            return string.Join("\n", lines);
        }

        public float CooldownLength(int key) => key switch
        {
            3 => heroicStrikeCooldown,
            4 => shieldBashCooldown,
            5 => whirlwindCooldown,
            6 => chargeCooldown,
            _ => _movement.swingInterval,
        };
    }

    /// <summary>
    /// A WoW action bar at the bottom middle of the screen, lifted clear of the edge: keys 1–6,
    /// each with its ability's short name, a shade that drains as its cooldown runs out (with
    /// the seconds left on it), and red when it can't be used right now.
    /// </summary>
    public class ActionBarHud : MonoBehaviour
    {
        const float Size = 56f;
        const float Gap = 6f;
        // Space between the bar and the bottom of the screen (the cast bar sits higher still).
        const float Lift = 64f;

        static readonly (string name, string glyph, string caption)[] Slots =
        {
            ("Attack", "Atk", "Attack"), ("Potion", "HP", "Potion"), ("Heroic Strike", "HS", "Heroic"),
            ("Shield Bash", "SB", "Bash"), ("Whirlwind", "WW", "Whirlwind"), ("Charge", "Ch", "Charge"),
        };

        static readonly Color Unusable = new Color(0.9f, 0.25f, 0.2f);

        WarriorAbilities _abilities;
        ShipHelm _helm;
        CanvasGroup _group;
        readonly RectTransform[] _shades = new RectTransform[6];
        readonly Text[] _seconds = new Text[6];
        readonly Text[] _glyphs = new Text[6];
        readonly Text[] _names = new Text[6];

        public static void Build(WarriorAbilities abilities)
        {
            float width = Slots.Length * Size + (Slots.Length - 1) * Gap + 16f;
            var root = Hud.Rect("ActionBar", Hud.Layer(HudLayer.Frames), new Vector2(0.5f, 0f),
                new Vector2(0f, Lift), new Vector2(width, Size + 16f));
            Hud.Panel(root);
            var bar = root.gameObject.AddComponent<ActionBarHud>();
            bar._abilities = abilities;
            bar._group = root.gameObject.AddComponent<CanvasGroup>();
            bar._group.blocksRaycasts = true;
            bar._helm = FindAnyObjectByType<ShipHelm>();

            var topLeft = new Vector2(0f, 1f);
            for (int i = 0; i < Slots.Length; i++)
            {
                var slot = Hud.Rect(Slots[i].name, root, topLeft, new Vector2(8f + i * (Size + Gap), -8f), new Vector2(Size, Size));
                // A button: clicking it is the same as pressing its key.
                var face = Hud.Rounded(slot.gameObject, Color.white, 6f);
                face.raycastTarget = true;
                var button = slot.gameObject.AddComponent<Button>();
                button.targetGraphic = face;
                var colors = button.colors;
                colors.normalColor = colors.selectedColor = HudTheme.SubPanel;
                colors.highlightedColor = Color.Lerp(HudTheme.SubPanel, Color.white, 0.25f);
                colors.pressedColor = Color.Lerp(HudTheme.SubPanel, Color.black, 0.35f);
                button.colors = colors;
                int key = i + 1;
                button.onClick.AddListener(() => abilities.Use(key));
                var hover = slot.gameObject.AddComponent<ActionSlotHover>();
                hover.Abilities = abilities;
                hover.Key = key;
                var glyph = Hud.Label("Glyph", slot, topLeft, new Vector2(0f, -12f), new Vector2(Size, 24f),
                    18, TextAnchor.MiddleCenter, HudTheme.Heading, FontStyle.Bold);
                glyph.text = Slots[i].glyph;
                bar._glyphs[i] = glyph;
                // The full name, small, along the slot's foot.
                var name = Hud.Label("Name", slot, new Vector2(0.5f, 0f), new Vector2(0f, 3f), new Vector2(Size - 4f, 12f),
                    9, TextAnchor.LowerCenter, HudTheme.Text);
                name.text = Slots[i].caption;
                bar._names[i] = name;
                Hud.Label("Key", slot, topLeft, new Vector2(4f, -2f), new Vector2(16f, 14f),
                    11, TextAnchor.UpperLeft, HudTheme.Text, FontStyle.Bold).text = (i + 1).ToString();

                // Drains from the top as the cooldown runs out.
                var shade = Hud.Rect("Cooldown", slot, new Vector2(0.5f, 0f), Vector2.zero, new Vector2(Size, 0f));
                shade.pivot = new Vector2(0.5f, 0f);
                shade.gameObject.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0.65f);
                bar._shades[i] = shade;
                bar._seconds[i] = Hud.Label("Seconds", slot, topLeft, new Vector2(0f, -12f), new Vector2(Size, 24f),
                    20, TextAnchor.MiddleCenter, Color.white, FontStyle.Bold);
            }
        }

        void Update()
        {
            if (_abilities == null)
            {
                Destroy(gameObject);
                return;
            }
            // The helm's own panel sits where the bar does.
            bool helm = _helm != null && _helm.IsDriving;
            _group.alpha = helm ? 0f : 1f;
            _group.blocksRaycasts = !helm;

            for (int i = 0; i < Slots.Length; i++)
            {
                int key = i + 1;
                float left = _abilities.CooldownLeft(key);
                float length = Mathf.Max(_abilities.CooldownLength(key), 0.01f);
                _shades[i].sizeDelta = new Vector2(Size, Size * Mathf.Clamp01(left / length));
                // Seconds only for the long cooldowns, not the one-second shared one — in place
                // of the glyph while they count down.
                bool counting = left > 1.05f;
                _seconds[i].text = counting ? Mathf.CeilToInt(left).ToString() : "";
                _glyphs[i].enabled = !counting;
                _glyphs[i].color = _abilities.Usable(key) ? HudTheme.Heading : Unusable;
            }
        }
    }

    /// <summary>An action-bar slot's tooltip, shown on hover and kept current (the cooldown ticks down).</summary>
    public class ActionSlotHover : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        public WarriorAbilities Abilities;
        public int Key;

        bool _hovered;

        public void OnPointerEnter(PointerEventData eventData)
        {
            _hovered = true;
            Refresh();
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            _hovered = false;
            HudTooltip.Hide();
        }

        void Update()
        {
            if (_hovered)
                Refresh();
        }

        void Refresh()
        {
            if (Abilities != null)
                HudTooltip.Show(Abilities.Describe(Key));
        }

        void OnDisable()
        {
            if (_hovered)
                HudTooltip.Hide();
            _hovered = false;
        }
    }
}
