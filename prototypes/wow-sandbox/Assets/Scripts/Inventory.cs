using System;
using System.Collections.Generic;
using UnityEngine;

namespace WowSandbox
{
    public enum SlotArea
    {
        Bag,
        Equipment,
        Loot,
    }

    /// <summary>Where an item sits: a bag slot, an equipment slot, or a row of a loot window.</summary>
    public readonly struct SlotRef : IEquatable<SlotRef>
    {
        public readonly SlotArea Area;
        public readonly int Index;

        public SlotRef(SlotArea area, int index)
        {
            Area = area;
            Index = index;
        }

        public static SlotRef Bag(int index) => new(SlotArea.Bag, index);
        public static SlotRef Equip(EquipSlot slot) => new(SlotArea.Equipment, (int)slot);
        public static SlotRef Loot(int index) => new(SlotArea.Loot, index);

        public bool Equals(SlotRef other) => Area == other.Area && Index == other.Index;
        public override bool Equals(object obj) => obj is SlotRef other && Equals(other);
        public override int GetHashCode() => ((int)Area * 397) ^ Index;
    }

    /// <summary>
    /// The player's bags, coin purse and equipped items. Every equipment change rebuilds
    /// a Gear summary and hands it to CharacterStats, which is where AC, attack and damage
    /// actually get worked out — this only decides what's worn.
    ///
    /// Two-handed weapons follow the usual rule: equipping one takes the shield off, and
    /// equipping a shield takes the two-hander off, so long as there's bag room for
    /// whatever comes off. Methods that can fail return an error to show the player.
    /// </summary>
    [RequireComponent(typeof(CharacterStats))]
    public class Inventory : MonoBehaviour
    {
        public int bagSize = 20;
        [Tooltip("Coin purse, in copper pieces (100 cp = 1 gp).")]
        public int copper = 15 * Money.Gold;
        [Tooltip("Start with a fighter's kit: chain mail, longsword and shield worn, a dagger and cap in the bags.")]
        public bool starterKit = true;

        Item[] _bagSlots;
        readonly Item[] _equipped = new Item[Enum.GetValues(typeof(EquipSlot)).Length];
        CharacterStats _stats;

        /// <summary>Raised whenever bags, equipment or coin change.</summary>
        public event Action Changed;

        // Made on first use rather than in Awake: the panels on the same object read it from
        // their own Awake, and Unity doesn't promise which of the two runs first.
        Item[] Bag => _bagSlots ??= new Item[Mathf.Max(1, bagSize)];

        public int BagSize => Bag.Length;
        public Item BagItem(int index) => Bag[index];
        public Item Equipped(EquipSlot slot) => _equipped[(int)slot];

        public Item At(SlotRef slot) => slot.Area switch
        {
            SlotArea.Bag => Bag[slot.Index],
            SlotArea.Equipment => _equipped[slot.Index],
            _ => null,
        };

        void Awake()
        {
            _stats = GetComponent<CharacterStats>();
        }

        void Start()
        {
            if (starterKit)
            {
                _equipped[(int)EquipSlot.Chest] = ItemCatalog.Create("Chain Mail");
                _equipped[(int)EquipSlot.MainHand] = ItemCatalog.Create("Longsword");
                _equipped[(int)EquipSlot.OffHand] = ItemCatalog.Create("Shield");
                Add(ItemCatalog.Create("Dagger"));
                Add(ItemCatalog.Create("Leather Cap"));
            }

            // Even with nothing worn: tells CharacterStats this character's weapon now comes
            // from equipment, so an empty main hand fights unarmed.
            EquipmentChanged();
        }

        public void AddCopper(int amount)
        {
            if (amount <= 0)
                return;
            copper += amount;
            Changed?.Invoke();
        }

        /// <summary>Whether all of <paramref name="item"/> would fit, stacks included.</summary>
        public bool CanAdd(Item item)
        {
            if (!item.Stackable)
                return FirstEmpty() >= 0;

            int room = 0;
            foreach (var slot in Bag)
                room += slot == null ? item.maxStack : slot.StacksWith(item) ? slot.maxStack - slot.count : 0;
            return room >= item.count;
        }

        /// <summary>Puts <paramref name="item"/> in the bags, topping up stacks first. All or nothing.</summary>
        public bool Add(Item item)
        {
            if (item == null || !CanAdd(item))
                return false;

            if (item.Stackable)
            {
                foreach (var slot in Bag)
                {
                    if (item.count == 0)
                        break;
                    if (slot == null || !slot.StacksWith(item))
                        continue;
                    int moved = Mathf.Min(item.count, slot.maxStack - slot.count);
                    slot.count += moved;
                    item.count -= moved;
                }
            }

            while (item.count > 0)
            {
                var stack = item.Clone();
                stack.count = Mathf.Min(item.count, item.maxStack);
                item.count -= stack.count;
                Bag[FirstEmpty()] = stack;
                if (!item.Stackable)
                    break;
            }

            Changed?.Invoke();
            return true;
        }

        /// <summary>
        /// Drag-and-drop between bag and equipment slots: bag to bag swaps (or merges
        /// stacks), bag to equipment equips, equipment to bag unequips — swapping with what's
        /// there if it fits the slot being emptied.
        /// </summary>
        public bool Move(SlotRef from, SlotRef to, out string error)
        {
            error = null;
            var item = At(from);
            if (item == null || from.Equals(to) || from.Area == SlotArea.Loot || to.Area == SlotArea.Loot)
                return false;

            if (to.Area == SlotArea.Equipment)
            {
                if (item.slot != (EquipSlot)to.Index)
                {
                    error = item.IsEquippable ? "That doesn't go in that slot." : "You can't equip that.";
                    return false;
                }
                return from.Area == SlotArea.Bag && EquipFromBag(from.Index, out error);
            }

            if (from.Area == SlotArea.Bag)
            {
                var other = Bag[to.Index];
                if (other != null && other.StacksWith(item))
                {
                    int moved = Mathf.Min(item.count, other.maxStack - other.count);
                    other.count += moved;
                    item.count -= moved;
                    if (item.count == 0)
                        Bag[from.Index] = null;
                }
                else
                {
                    Bag[from.Index] = other;
                    Bag[to.Index] = item;
                }
                Changed?.Invoke();
                return true;
            }

            // Equipment to bag.
            var target = Bag[to.Index];
            if (target == null)
            {
                Bag[to.Index] = item;
                _equipped[from.Index] = null;
                EquipmentChanged();
                return true;
            }
            if (target.slot == (EquipSlot)from.Index)
                return EquipFromBag(to.Index, out error);
            return Unequip((EquipSlot)from.Index, out error);
        }

        /// <summary>Wears the item in bag slot <paramref name="index"/>, bagging whatever it replaces.</summary>
        public bool EquipFromBag(int index, out string error)
        {
            error = null;
            var item = Bag[index];
            if (item == null)
                return false;
            if (!item.IsEquippable)
            {
                error = "You can't equip that.";
                return false;
            }

            var displaced = new List<EquipSlot>();
            if (Equipped(item.slot) != null)
                displaced.Add(item.slot);
            if (item.twoHanded && Equipped(EquipSlot.OffHand) != null)
                displaced.Add(EquipSlot.OffHand);
            if (item.slot == EquipSlot.OffHand && Equipped(EquipSlot.MainHand) is { twoHanded: true })
                displaced.Add(EquipSlot.MainHand);

            // The item's own bag slot frees up, so that's one place for what comes off.
            if (displaced.Count > EmptyCount() + 1)
            {
                error = "Inventory is full.";
                return false;
            }

            Bag[index] = null;
            var removed = new List<Item>();
            foreach (var slot in displaced)
            {
                removed.Add(_equipped[(int)slot]);
                _equipped[(int)slot] = null;
            }
            _equipped[(int)item.slot] = item;

            foreach (var off in removed)
                Bag[Bag[index] == null ? index : FirstEmpty()] = off;

            EquipmentChanged();
            return true;
        }

        public bool Unequip(EquipSlot slot, out string error)
        {
            error = null;
            var item = Equipped(slot);
            if (item == null)
                return false;

            int free = FirstEmpty();
            if (free < 0)
            {
                error = "Inventory is full.";
                return false;
            }

            Bag[free] = item;
            _equipped[(int)slot] = null;
            EquipmentChanged();
            return true;
        }

        int FirstEmpty() => Array.IndexOf(Bag, null);

        int EmptyCount()
        {
            int count = 0;
            foreach (var slot in Bag)
                if (slot == null)
                    count++;
            return count;
        }

        void EquipmentChanged()
        {
            var weapon = Equipped(EquipSlot.MainHand);
            var armor = Equipped(EquipSlot.Chest);
            var shield = Equipped(EquipSlot.OffHand);

            var gear = new Gear
            {
                HasWeapon = weapon != null,
                WeaponDieCount = weapon?.damageDieCount ?? 0,
                WeaponDieSides = weapon?.damageDieSides ?? 0,
                WeaponBonus = weapon?.magicBonus ?? 0,
                Armor = armor?.armorCategory ?? ArmorCategory.None,
                ArmorBase = armor?.armorClass ?? 0,
                ArmorBonus = armor?.magicBonus ?? 0,
                StrengthRequirement = armor?.strengthRequirement ?? 0,
                HasShield = shield is { kind: ItemKind.Shield },
                ShieldBonus = shield?.magicBonus ?? 0,
                Abilities = new int[6],
            };

            foreach (var item in _equipped)
            {
                if (item == null)
                    continue;
                gear.Protection += item.protectionBonus;
                for (int i = 0; i < 6; i++)
                    gear.Abilities[i] += item.abilityBonuses[i];
            }

            _stats.SetGear(gear);
            Changed?.Invoke();
        }

        // Ways to see the higher tiers while drops are capped at Uncommon.
        [ContextMenu("Give Random Uncommon")] void GiveUncommon() => GiveOrWarn(ItemCatalog.RandomMagic(ItemRarity.Uncommon));
        [ContextMenu("Give Random Rare")] void GiveRare() => GiveOrWarn(ItemCatalog.RandomMagic(ItemRarity.Rare));
        [ContextMenu("Give Random Very Rare")] void GiveVeryRare() => GiveOrWarn(ItemCatalog.RandomMagic(ItemRarity.VeryRare));

        [ContextMenu("Give Legendaries")]
        void GiveLegendaries()
        {
            foreach (var item in ItemCatalog.Legendaries)
                GiveOrWarn(item.Clone());
        }

        void GiveOrWarn(Item item)
        {
            if (!Application.isPlaying)
                Debug.LogWarning("[Inventory] Enter Play mode first — bags only exist at runtime.", this);
            else if (!Add(item))
                Debug.LogWarning($"[Inventory] No room for {item.name}.", this);
        }
    }
}
