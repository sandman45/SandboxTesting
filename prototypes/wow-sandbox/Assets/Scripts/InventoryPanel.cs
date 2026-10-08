using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace WowSandbox
{
    /// <summary>
    /// The bags, toggled with B (or I): a grid of slots and the coin purse. Right-click
    /// equips, drag moves, drops onto the character sheet's equipment slots equip, and
    /// dragging a loot window item in takes it.
    /// </summary>
    [RequireComponent(typeof(Inventory))]
    public class InventoryPanel : HudPanel
    {
        const int Columns = 5;
        const float SlotSize = 54f;
        const float Gap = 6f;
        const float Pad = 18f;

        Inventory _inventory;
        CharacterStats _stats;
        LootPanel _loot;
        ItemSlotView[] _slots;
        Text _money;

        int Rows => Mathf.CeilToInt(_inventory.BagSize / (float)Columns);

        protected override Vector2 PanelSize => new(
            Pad * 2f + Columns * SlotSize + (Columns - 1) * Gap,
            52f + Rows * SlotSize + (Rows - 1) * Gap + 44f);

        protected override Vector2 PanelAnchor => new(1f, 0.5f);
        protected override Vector2 PanelPosition => new(-40f, -40f);

        void Reset()
        {
            toggleKey = Key.B;
            altToggleKey = Key.I;
        }

        protected override void Awake()
        {
            _inventory = GetComponent<Inventory>();
            _stats = GetComponent<CharacterStats>();
            _loot = GetComponent<LootPanel>();
            base.Awake();
        }

        protected override void Build()
        {
            var topLeft = new Vector2(0f, 1f);
            Hud.Label("Title", Root, topLeft, new Vector2(Pad, -14f), new Vector2(200f, 24f),
                20, TextAnchor.MiddleLeft, HudTheme.Text, FontStyle.Bold).text = "Bags";

            _slots = new ItemSlotView[_inventory.BagSize];
            for (int i = 0; i < _slots.Length; i++)
            {
                int index = i;
                var position = new Vector2(Pad + i % Columns * (SlotSize + Gap), -52f - i / Columns * (SlotSize + Gap));
                var slot = ItemSlotView.Create($"Slot{i}", Root, topLeft, position, SlotSize);
                slot.Source = () => _inventory.BagItem(index);
                slot.Ref = SlotRef.Bag(index);
                slot.Reader = _stats;
                slot.TooltipHint = "Right-click to equip · drag to move";
                slot.RightClicked = OnRightClick;
                slot.DroppedOn = OnDropped;
                _slots[i] = slot;
            }

            _money = Hud.Label("Money", Root, new Vector2(1f, 0f), new Vector2(-Pad, 12f), new Vector2(240f, 24f),
                16, TextAnchor.MiddleRight, HudTheme.Text, FontStyle.Bold);
            _money.supportRichText = true;
        }

        protected override void Refresh()
        {
            foreach (var slot in _slots)
                slot.Refresh();
            _money.text = Money.Format(_inventory.copper, rich: true);
        }

        void OnRightClick(ItemSlotView slot)
        {
            if (!slot.Item.IsEquippable)
                return;
            if (!_inventory.EquipFromBag(slot.Ref.Index, out var error) && error != null)
                Hud.Error(error);
        }

        void OnDropped(ItemSlotView from, ItemSlotView to)
        {
            if (from.Ref.Area == SlotArea.Loot)
            {
                if (_loot != null)
                    _loot.Take(from.Ref.Index);
                return;
            }

            if (!_inventory.Move(from.Ref, to.Ref, out var error) && error != null)
                Hud.Error(error);
        }
    }
}
