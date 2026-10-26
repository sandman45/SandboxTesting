using System;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace WowSandbox
{
    /// <summary>
    /// The bags, toggled with B (or I): a grid of slots and the coin purse. Right-click
    /// equips, drag moves, drops onto the character sheet's equipment slots equip, and
    /// dragging a loot window item in takes it. Dragging a bag item off every window asks
    /// "Drop it on the ground?" and lays it at his feet (a GroundItem, to pick up again).
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
        RectTransform _confirm;
        Text _confirmText;
        Action _confirmed;

        // How far in front of him a dropped item lands.
        const float DropAhead = 0.9f;

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
                slot.DroppedOutside = OnDroppedOutside;
                _slots[i] = slot;
            }

            _money = Hud.Label("Money", Root, new Vector2(1f, 0f), new Vector2(-Pad, 12f), new Vector2(240f, 24f),
                16, TextAnchor.MiddleRight, HudTheme.Text, FontStyle.Bold);
            _money.supportRichText = true;
            BuildConfirm();
        }

        protected override void Update()
        {
            // Esc says no to the question before it closes the bags.
            if (_confirm != null && _confirm.gameObject.activeSelf && Keyboard.current != null &&
                Keyboard.current.escapeKey.wasPressedThisFrame && !EscapeUsedThisFrame)
            {
                UseEscape();
                HideConfirm();
            }
            base.Update();
        }

        protected override void Closed() => HideConfirm();

        protected override void Refresh()
        {
            for (int i = 0; i < _slots.Length; i++)
            {
                _slots[i].TooltipHint = HintFor(i);
                _slots[i].Refresh();
            }
            _money.text = Money.Format(_inventory.copper, rich: true);
        }

        void OnRightClick(ItemSlotView slot)
        {
            // With a merchant open, right-click sells, as in WoW.
            var vendor = GetComponent<VendorPanel>();
            if (vendor != null && vendor.IsOpen)
            {
                vendor.Sell(slot.Ref.Index);
                return;
            }

            string error = null;
            if (slot.Item.IsPotion || slot.Item.IsFood)
                _inventory.Use(slot.Ref.Index, out error);
            else if (slot.Item.baseName == Campfire.FlintName)
                Campfire.StartBuilding(_inventory);
            else if (slot.Item.IsEquippable)
                _inventory.EquipFromBag(slot.Ref.Index, out error);
            if (error != null)
                Hud.Error(error);
        }

        /// <summary>The hint under a bag item's tooltip — what right-click will do with it.</summary>
        string HintFor(int index)
        {
            var item = _inventory.BagItem(index);
            var vendor = GetComponent<VendorPanel>();
            if (item != null && vendor != null && vendor.IsOpen)
            {
                int price = Money.SellPrice(item);
                return price > 0 ? $"Right-click to sell for {Money.Format(price)}" : "No sell price";
            }
            if (item != null && item.IsPotion)
                return "Right-click to drink (or press 6) · drag to move";
            if (item != null && item.IsFood)
                return "Right-click to eat · drag to move";
            if (item != null && item.baseName == Campfire.FlintName)
                return "Right-click to build a campfire · drag to move";
            return "Right-click to equip · drag to move";
        }

        void OnDropped(ItemSlotView from, ItemSlotView to)
        {
            if (from.Ref.Area == SlotArea.Loot)
            {
                if (_loot != null)
                    _loot.Take(from.Ref.Index);
                return;
            }

            // Dragged in from the merchant: buy it (or buy it back).
            if (from.Ref.Area == SlotArea.Vendor)
            {
                GetComponent<VendorPanel>()?.BuyFromSlot(from);
                return;
            }

            if (!_inventory.Move(from.Ref, to.Ref, out var error) && error != null)
                Hud.Error(error);
        }

        // --- Dropping on the ground ----------------------------------------------------------

        void OnDroppedOutside(ItemSlotView slot)
        {
            if (slot.Ref.Area != SlotArea.Bag)
                return;
            var item = slot.Item;
            int index = slot.Ref.Index;
            if (!DropSpot(out _, out _))
            {
                Hud.Error("You can't drop that here.");
                return;
            }
            string rarity = HudTheme.Hex(HudTheme.RarityColor(item.rarity));
            string count = item.count > 1 ? $" x{item.count}" : "";
            Confirm($"Drop <color=#{rarity}>{item.name}</color>{count} on the ground?", () => DropNow(index, item));
        }

        void DropNow(int index, Item expected)
        {
            // The bags may have changed while the question was up, or he may have moved somewhere he can't drop it.
            if (_inventory.BagItem(index) != expected)
                return;
            if (!DropSpot(out var point, out float yaw))
            {
                Hud.Error("You can't drop that here.");
                return;
            }
            var item = _inventory.TakeFromBag(index);
            GroundItem.Drop(item, point, yaw);
            Hud.Info($"You drop {item.name}{(item.count > 1 ? $" x{item.count}" : "")}.");
        }

        /// <summary>
        /// The ground just in front of him, and a turn for the item; false where it can't lie —
        /// in the water, on a ship's deck, on a ladder.
        /// </summary>
        bool DropSpot(out Vector3 point, out float yaw)
        {
            // His feet, if there's nothing under the spot in front of him.
            point = transform.position;
            yaw = transform.eulerAngles.y + UnityEngine.Random.Range(-40f, 40f);
            var player = GetComponent<WowCharacterController>();
            if (player != null && (player.IsSwimming || player.IsClimbing || player.Platform != null))
                return false;

            var origin = transform.position + transform.forward * DropAhead + Vector3.up * 1.5f;
            var hits = Physics.RaycastAll(origin, Vector3.down, 4f, ~0, QueryTriggerInteraction.Ignore);
            float nearest = float.MaxValue;
            foreach (var hit in hits)
            {
                if (hit.collider.transform.IsChildOf(transform) || hit.distance >= nearest)
                    continue;
                nearest = hit.distance;
                point = hit.point;
            }
            return true;
        }

        void BuildConfirm()
        {
            _confirm = Hud.Stretch("Confirm", Root, 2f);
            var shade = Hud.Rounded(_confirm.gameObject, new Color(0f, 0f, 0f, 0.75f), 8f);
            shade.raycastTarget = true;   // nothing behind it clickable while it's up

            var box = Hud.Rect("Box", _confirm, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(300f, 120f));
            Hud.Panel(box);
            _confirmText = Hud.Label("Text", box, new Vector2(0.5f, 1f), new Vector2(0f, -16f), new Vector2(270f, 50f),
                15, TextAnchor.MiddleCenter, HudTheme.Text);
            _confirmText.supportRichText = true;
            _confirmText.horizontalOverflow = HorizontalWrapMode.Wrap;
            Hud.TextButton("Drop", box, new Vector2(0.5f, 0f), new Vector2(-60f, 16f), new Vector2(100f, 30f), () =>
            {
                var action = _confirmed;
                HideConfirm();
                action?.Invoke();
            });
            Hud.TextButton("Cancel", box, new Vector2(0.5f, 0f), new Vector2(60f, 16f), new Vector2(100f, 30f), HideConfirm);
            HideConfirm();
        }

        void Confirm(string question, Action onYes)
        {
            _confirmText.text = question;
            _confirmed = onYes;
            _confirm.gameObject.SetActive(true);
            _confirm.SetAsLastSibling();
        }

        void HideConfirm()
        {
            _confirmed = null;
            if (_confirm != null)
                _confirm.gameObject.SetActive(false);
        }
    }
}
