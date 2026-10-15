using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace WowSandbox
{
    /// <summary>
    /// The loot window. Clicking a corpse within reach opens it: coin goes straight into
    /// the purse, and the items are listed to take one at a time (click the row, or drag
    /// it into the bags) or all at once. Closes when it's empty, when you walk away, or
    /// when the corpse despawns.
    /// </summary>
    [RequireComponent(typeof(Inventory), typeof(TargetingController))]
    public class LootPanel : HudPanel
    {
        [Tooltip("How close you need to be to loot. 0 uses twice the attack range.")]
        public float lootRange;

        const int MaxRows = 5;
        const float RowHeight = 48f;
        const float Width = 300f;
        const float Pad = 14f;

        static readonly int LootingHash = Animator.StringToHash("Looting");
        static readonly int LootGrabHash = Animator.StringToHash("LootGrab");
        static readonly int LootDownHash = Animator.StringToHash("LootDown");
        bool _kneeling;

        Inventory _inventory;
        CharacterStats _stats;
        WowCharacterController _movement;
        CharacterController _body;
        TargetingController _targeting;
        LootDrop _loot;

        Text _title;
        readonly RectTransform[] _rows = new RectTransform[MaxRows];
        readonly ItemSlotView[] _slots = new ItemSlotView[MaxRows];
        readonly Text[] _names = new Text[MaxRows];

        protected override Vector2 PanelSize => new(Width, 50f + MaxRows * RowHeight + 50f);
        protected override Vector2 PanelAnchor => new(0.5f, 0.5f);
        protected override Vector2 PanelPosition => new(-280f, 40f);

        float Range
        {
            get
            {
                if (lootRange > 0f)
                    return lootRange;
                var controller = GetComponent<WowCharacterController>();
                return controller != null ? controller.attackRange * 2f : 5f;
            }
        }

        protected override void Awake()
        {
            toggleKey = Key.None; // opened by clicking a corpse, not a key
            altToggleKey = Key.None;
            _inventory = GetComponent<Inventory>();
            _stats = GetComponent<CharacterStats>();
            _targeting = GetComponent<TargetingController>();
            _movement = GetComponent<WowCharacterController>();
            _body = GetComponent<CharacterController>();
            base.Awake();
        }

        /// <summary>
        /// Kneels over the corpse while the window is open and you stand still (Update Warrior
        /// Animations' looting states); walking off, or closing it, gets you up.
        /// </summary>
        protected override void Update()
        {
            base.Update();
            var animator = _movement != null ? _movement.Animator : null;
            if (animator == null || !HasParameter(animator, LootingHash))
                return;
            var velocity = _body != null ? _body.velocity : Vector3.zero;
            velocity.y = 0f;
            bool still = velocity.sqrMagnitude < 0.25f && (_movement == null || !_movement.IsSwimming);
            bool kneel = IsOpen && still;
            if (kneel && !_kneeling && HasParameter(animator, LootDownHash))
                animator.SetTrigger(LootDownHash);
            _kneeling = kneel;
            animator.SetBool(LootingHash, kneel);
        }

        static bool HasParameter(Animator animator, int hash)
        {
            if (animator.runtimeAnimatorController == null)
                return false;
            foreach (var parameter in animator.parameters)
                if (parameter.nameHash == hash)
                    return true;
            return false;
        }

        /// <summary>Turns you to face the corpse, as WoW does when you loot.</summary>
        void Face(Transform corpse)
        {
            var toward = corpse.position - transform.position;
            toward.y = 0f;
            if (toward.sqrMagnitude > 0.01f)
                transform.rotation = Quaternion.LookRotation(toward, Vector3.up);
        }

        void OnEnable() => _targeting.Clicked += OnClicked;

        protected override void OnDisable()
        {
            _targeting.Clicked -= OnClicked;
            base.OnDisable();
        }

        void OnClicked(Health clicked)
        {
            if (!clicked.IsDead)
                return;

            var loot = clicked.GetComponent<LootDrop>();
            if (loot == null || !loot.HasLoot)
                return;

            if (Vector3.Distance(transform.position, clicked.transform.position) > Range)
            {
                Hud.Error("You are too far away.");
                return;
            }

            OpenFor(loot, _targeting.TargetName);
        }

        /// <summary>
        /// Loots <paramref name="loot"/> — a corpse or a chest — titled <paramref name="title"/>:
        /// turns you to it, takes the coin straight away, and opens the window on the items, or
        /// just reaches down if there's only coin.
        /// </summary>
        public void OpenFor(LootDrop loot, string title)
        {
            if (loot == null || !loot.HasLoot)
                return;
            Face(loot.transform);
            if (loot.Copper > 0)
            {
                int copper = loot.TakeCopper();
                _inventory.AddCopper(copper);
                Hud.Info($"You loot {Money.Format(copper, rich: true)}.");
            }

            if (!loot.HasLoot)
            {
                // Coin only: a quick reach rather than kneeling down for nothing.
                var animator = _movement != null ? _movement.Animator : null;
                if (animator != null && HasParameter(animator, LootGrabHash))
                    animator.SetTrigger(LootGrabHash);
                return;
            }

            _loot = loot;
            _title.text = title;
            Open();
        }

        protected override void Build()
        {
            var topLeft = new Vector2(0f, 1f);
            _title = Hud.Label("Title", Root, topLeft, new Vector2(Pad, -12f), new Vector2(Width - 60f, 24f),
                18, TextAnchor.MiddleLeft, HudTheme.Text, FontStyle.Bold);

            for (int i = 0; i < MaxRows; i++)
            {
                int index = i;
                var row = Hud.Rect($"Row{i}", Root, topLeft, new Vector2(Pad, -46f - i * RowHeight),
                    new Vector2(Width - Pad * 2f, RowHeight - 6f));
                var back = Hud.Rounded(row.gameObject, HudTheme.SubPanel, 6f);
                back.raycastTarget = true;
                var button = row.gameObject.AddComponent<Button>();
                button.targetGraphic = back;
                button.onClick.AddListener(() => Take(index));

                var slot = ItemSlotView.Create("Icon", row, topLeft, Vector2.zero, RowHeight - 6f);
                slot.Source = () => _loot != null && index < _loot.Items.Count ? _loot.Items[index] : null;
                slot.Ref = SlotRef.Loot(index);
                slot.Reader = _stats;
                slot.TooltipHint = "Click to loot";
                slot.LeftClicked = slot.RightClicked = _ => Take(index);

                var name = Hud.Label("Name", row, 15, TextAnchor.MiddleLeft, HudTheme.Text, FontStyle.Bold);
                name.rectTransform.offsetMin = new Vector2(RowHeight + 4f, 0f);

                _rows[i] = row;
                _slots[i] = slot;
                _names[i] = name;
            }

            Hud.TextButton("Take All", Root, new Vector2(0.5f, 0f), new Vector2(0f, 12f), new Vector2(120f, 30f), TakeAll);
        }

        protected override void Refresh()
        {
            // The corpse despawned, everything's been taken, or we walked off.
            if (_loot == null || !_loot.HasLoot ||
                Vector3.Distance(transform.position, _loot.transform.position) > Range * 1.5f)
            {
                _loot = null;
                Close();
                return;
            }

            for (int i = 0; i < MaxRows; i++)
            {
                bool used = i < _loot.Items.Count;
                _rows[i].gameObject.SetActive(used);
                if (!used)
                    continue;

                var item = _loot.Items[i];
                _slots[i].Refresh();
                _names[i].text = item.count > 1 ? $"{item.name} x{item.count}" : item.name;
                _names[i].color = HudTheme.RarityColor(item.rarity);
            }
        }

        /// <summary>Moves loot row <paramref name="index"/> into the bags, if there's room.</summary>
        public void Take(int index)
        {
            if (_loot == null || index >= _loot.Items.Count)
                return;

            var item = _loot.Items[index];
            if (!_inventory.CanAdd(item))
            {
                Hud.Error("Inventory is full.");
                return;
            }

            string name = $"<color=#{HudTheme.Hex(HudTheme.RarityColor(item.rarity))}>{item.name}</color>";
            int count = item.count;
            _inventory.Add(_loot.Take(index));
            Hud.Info(count > 1 ? $"You receive loot: {name} x{count}." : $"You receive loot: {name}.");
            HudTooltip.Hide();
        }

        void TakeAll()
        {
            while (_loot != null && _loot.Items.Count > 0)
            {
                if (!_inventory.CanAdd(_loot.Items[0]))
                {
                    Hud.Error("Inventory is full.");
                    return;
                }
                Take(0);
            }
        }
    }
}
