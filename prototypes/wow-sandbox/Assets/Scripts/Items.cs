using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Random = UnityEngine.Random;

namespace WowSandbox
{
    /// <summary>D&D's rarity tiers. Each has its colour in HudTheme.RarityColor.</summary>
    public enum ItemRarity
    {
        Common,
        Uncommon,
        Rare,
        VeryRare,
        Legendary,
    }

    public enum EquipSlot
    {
        None,
        MainHand,
        OffHand,
        Head,
        Chest,
        Hands,
        Feet,
        Neck,
        Ring,
    }

    /// <summary>5e armour categories — they decide how much DEX counts toward AC.</summary>
    public enum ArmorCategory
    {
        None,
        Light,
        Medium,
        Heavy,
    }

    public enum ItemKind
    {
        TradeGood,
        Weapon,
        Armor,
        Shield,
        Clothing,
        Jewelry,
    }

    /// <summary>
    /// One item, or one stack of a stackable one. Plain data, built in code by ItemCatalog
    /// rather than as assets — magic items are rolled at runtime ("+1 Longsword of Strength"),
    /// so most items never exist until they drop.
    /// </summary>
    [Serializable]
    public class Item
    {
        public string name;
        /// <summary>The mundane item this was made from, e.g. "Longsword" for "+1 Longsword".</summary>
        public string baseName;
        public ItemKind kind;
        public ItemRarity rarity;
        public EquipSlot slot;

        [Tooltip("Letters drawn on the placeholder icon.")]
        public string glyph;
        public Color iconColor;

        [Header("Weapon")]
        public int damageDieCount;
        public int damageDieSides;
        public bool twoHanded;

        [Header("Armour")]
        public ArmorCategory armorCategory;
        [Tooltip("Body armour: its base AC. Shields: the AC they add (2).")]
        public int armorClass;
        public int strengthRequirement;

        [Header("Magic")]
        [Tooltip("+N: to hit and damage on a weapon, AC on armour or a shield.")]
        public int magicBonus;
        [Tooltip("Flat AC from jewellery and clothing (\"of Protection\").")]
        public int protectionBonus;
        public int[] abilityBonuses = new int[6];

        [Header("Stack and value")]
        public int valueCopper;
        public int maxStack = 1;
        public int count = 1;

        public bool IsEquippable => slot != EquipSlot.None;
        public bool Stackable => maxStack > 1;
        public int AbilityBonus(Ability ability) => abilityBonuses[(int)ability];
        public bool HasAbilityBonuses => abilityBonuses.Any(bonus => bonus != 0);

        public bool StacksWith(Item other) =>
            Stackable && other != null && other.name == name && other.rarity == rarity;

        public Item Clone()
        {
            var copy = (Item)MemberwiseClone();
            copy.abilityBonuses = (int[])abilityBonuses.Clone();
            return copy;
        }
    }

    /// <summary>D&D coinage: 1 gp = 10 sp = 100 cp. Everything is stored in copper.</summary>
    public static class Money
    {
        public const int Silver = 10;
        public const int Gold = 100;

        /// <summary>"12g 5s 3c", skipping empty denominations; coloured when <paramref name="rich"/>.</summary>
        public static string Format(int copper, bool rich = false)
        {
            if (copper <= 0)
                return rich ? "<color=#c87533>0c</color>" : "0c";

            var parts = new List<string>();
            int gold = copper / Gold, silver = copper % Gold / Silver, cp = copper % Silver;
            if (gold > 0) parts.Add(rich ? $"<color=#ffd34d>{gold}g</color>" : $"{gold}g");
            if (silver > 0) parts.Add(rich ? $"<color=#d0d4dc>{silver}s</color>" : $"{silver}s");
            if (cp > 0) parts.Add(rich ? $"<color=#c87533>{cp}c</color>" : $"{cp}c");
            return string.Join(" ", parts);
        }
    }

    /// <summary>
    /// Every mundane item, with 5e SRD numbers (damage dice, armour AC and STR
    /// requirements, prices), plus the generator that turns one into a magic item of a
    /// given rarity, and the hand-made legendaries.
    /// </summary>
    public static class ItemCatalog
    {
        static readonly Color WeaponColor = new(0.42f, 0.47f, 0.56f);
        static readonly Color ArmorColor = new(0.5f, 0.37f, 0.24f);
        static readonly Color ShieldColor = new(0.32f, 0.4f, 0.55f);
        static readonly Color ClothingColor = new(0.44f, 0.34f, 0.3f);
        static readonly Color JewelryColor = new(0.66f, 0.55f, 0.22f);
        static readonly Color TradeColor = new(0.36f, 0.36f, 0.32f);

        public static readonly Item[] Weapons =
        {
            Weapon("Dagger", "Dg", 1, 4, 2 * Money.Gold),
            Weapon("Handaxe", "Ha", 1, 6, 5 * Money.Gold),
            Weapon("Mace", "Mc", 1, 6, 5 * Money.Gold),
            Weapon("Shortsword", "SS", 1, 6, 10 * Money.Gold),
            Weapon("Battleaxe", "BA", 1, 8, 10 * Money.Gold),
            Weapon("Longsword", "LS", 1, 8, 15 * Money.Gold),
            Weapon("Warhammer", "WH", 1, 8, 15 * Money.Gold),
            Weapon("Greataxe", "GA", 1, 12, 30 * Money.Gold, twoHanded: true),
            Weapon("Greatsword", "GS", 2, 6, 50 * Money.Gold, twoHanded: true),
        };

        public static readonly Item[] Armors =
        {
            Armor("Leather Armor", "LA", ArmorCategory.Light, 11, 10 * Money.Gold),
            Armor("Studded Leather", "SL", ArmorCategory.Light, 12, 45 * Money.Gold),
            Armor("Hide Armor", "Hd", ArmorCategory.Medium, 12, 10 * Money.Gold),
            Armor("Chain Shirt", "CS", ArmorCategory.Medium, 13, 50 * Money.Gold),
            Armor("Scale Mail", "SM", ArmorCategory.Medium, 14, 50 * Money.Gold),
            Armor("Chain Mail", "CM", ArmorCategory.Heavy, 16, 75 * Money.Gold, strength: 13),
            Armor("Splint Armor", "Sp", ArmorCategory.Heavy, 17, 200 * Money.Gold, strength: 15),
            Armor("Plate Armor", "PA", ArmorCategory.Heavy, 18, 1500 * Money.Gold, strength: 15),
        };

        public static readonly Item Shield = new()
        {
            name = "Shield", baseName = "Shield", kind = ItemKind.Shield, slot = EquipSlot.OffHand,
            glyph = "Sh", iconColor = ShieldColor, armorClass = 2, valueCopper = 10 * Money.Gold,
        };

        // 5e has no mundane AC for these slots — they're only worth wearing once magic.
        public static readonly Item[] Clothing =
        {
            Wearable("Leather Cap", "Cp", EquipSlot.Head, ItemKind.Clothing, 5 * Money.Silver),
            Wearable("Iron Helm", "He", EquipSlot.Head, ItemKind.Clothing, 2 * Money.Gold),
            Wearable("Leather Gloves", "Gl", EquipSlot.Hands, ItemKind.Clothing, 5 * Money.Silver),
            Wearable("Gauntlets", "Ga", EquipSlot.Hands, ItemKind.Clothing, 2 * Money.Gold),
            Wearable("Leather Boots", "Bt", EquipSlot.Feet, ItemKind.Clothing, 1 * Money.Gold),
            Wearable("Traveler's Boots", "TB", EquipSlot.Feet, ItemKind.Clothing, 2 * Money.Gold),
        };

        public static readonly Item[] Jewelry =
        {
            Wearable("Copper Ring", "Rg", EquipSlot.Ring, ItemKind.Jewelry, 1 * Money.Gold),
            Wearable("Silver Ring", "Rg", EquipSlot.Ring, ItemKind.Jewelry, 5 * Money.Gold),
            Wearable("Bone Amulet", "Am", EquipSlot.Neck, ItemKind.Jewelry, 1 * Money.Gold),
            Wearable("Silver Pendant", "Pd", EquipSlot.Neck, ItemKind.Jewelry, 10 * Money.Gold),
        };

        public static readonly Item[] TradeGoods =
        {
            Trade("Chicken Feather", "Fe", 1),
            Trade("Raw Chicken", "Mt", 5),
            Trade("Frayed Rope", "Rp", 1 * Money.Silver),
            Trade("Loaded Dice", "Di", 1 * Money.Silver),
            Trade("Bent Lockpick", "Lp", 2 * Money.Silver),
            Trade("Stolen Trinket", "Tr", 5 * Money.Silver),
            Trade("Silver Earring", "Er", 2 * Money.Gold),
            Trade("Raw Fish", "Fi", 5),
            Trade("Shark Tooth", "Th", 5 * Money.Silver),
            Trade("Shark Fin", "Fn", 1 * Money.Gold),
            Trade("Giant Shark Tooth", "GT", 5 * Money.Gold),
        };

        /// <summary>Hand-made, never rolled. Reach them through Inventory's context menu for now.</summary>
        public static readonly Item[] Legendaries =
        {
            // Straight from Weapons, not Find: Find searches this array too, which isn't built yet.
            Legendary(Weapons.First(weapon => weapon.name == "Longsword"), "Ashbringer", magic: 3,
                (Ability.Strength, 2)),
            Legendary(Shield, "Aegis of the Dawn", magic: 3, (Ability.Constitution, 1), (Ability.Wisdom, 1)),
        };

        static IEnumerable<Item> Equippable => Weapons.Concat(Armors).Append(Shield).Concat(Clothing).Concat(Jewelry);
        static IEnumerable<Item> All => Equippable.Concat(TradeGoods).Concat(Legendaries);

        /// <summary>A fresh copy of the named item.</summary>
        public static Item Create(string name, int count = 1)
        {
            var item = Find(name).Clone();
            item.count = Mathf.Clamp(count, 1, item.maxStack);
            return item;
        }

        public static Item RandomTradeGood() => Pick(TradeGoods).Clone();

        public static Item RandomMundane() => Pick(Equippable.ToArray()).Clone();

        /// <summary>
        /// A random equippable item made magic at <paramref name="rarity"/>, D&D-style.
        /// Weapons, armour and shields get +N (+1 uncommon, +2 rare, +3 very rare) or a
        /// smaller +N with ability bonuses; clothing and jewellery get ability bonuses and,
        /// higher up, Protection (+1 AC). Common returns a mundane item.
        /// </summary>
        public static Item RandomMagic(ItemRarity rarity)
        {
            var item = RandomMundane();
            if (rarity == ItemRarity.Common || rarity == ItemRarity.Legendary)
                return item;

            item.rarity = rarity;
            int tier = rarity switch { ItemRarity.Uncommon => 1, ItemRarity.Rare => 2, _ => 3 };
            bool split = Random.value < 0.5f;
            var abilities = new List<Ability>();

            if (item.kind is ItemKind.Weapon or ItemKind.Armor or ItemKind.Shield)
            {
                // Uncommon is always a plain +1; above that, half are a +N one lower with
                // one ability (rare) or two (very rare).
                if (tier == 1 || !split)
                {
                    item.magicBonus = tier;
                }
                else
                {
                    item.magicBonus = tier - 1;
                    for (int i = 0; i < tier - 1; i++)
                    {
                        var ability = RandomAbility(abilities);
                        abilities.Add(ability);
                        item.abilityBonuses[(int)ability] = 1;
                    }
                }
            }
            else
            {
                // Uncommon: +1 to an ability. Rare: +2, or +1 and Protection. Very rare: both.
                var ability = RandomAbility(abilities);
                abilities.Add(ability);
                bool smallRare = tier == 2 && split;
                item.abilityBonuses[(int)ability] = tier == 1 || smallRare ? 1 : 2;
                if (tier == 3 || smallRare)
                    item.protectionBonus = 1;
            }

            item.name = MagicName(item, abilities);
            item.valueCopper += rarity switch
            {
                ItemRarity.Uncommon => 100 * Money.Gold,
                ItemRarity.Rare => 500 * Money.Gold,
                _ => 5000 * Money.Gold,
            };
            return item;
        }

        static string MagicName(Item item, List<Ability> abilities)
        {
            string name = item.magicBonus > 0 ? $"+{item.magicBonus} {item.baseName}" : item.baseName;

            var suffixes = abilities.Distinct().Select(AbilityNoun).ToList();
            if (item.protectionBonus > 0)
                suffixes.Add("Protection");
            return suffixes.Count == 0 ? name : $"{name} of {string.Join(" and ", suffixes)}";
        }

        static string AbilityNoun(Ability ability) => ability switch
        {
            Ability.Strength => "Strength",
            Ability.Dexterity => "Agility",
            Ability.Constitution => "Fortitude",
            Ability.Intelligence => "Intellect",
            Ability.Wisdom => "Wisdom",
            _ => "Charm",
        };

        /// <summary>Weighted toward what a fighter can use: STR and CON most, then DEX.</summary>
        static Ability RandomAbility(List<Ability> exclude)
        {
            var weighted = new (Ability ability, int weight)[]
            {
                (Ability.Strength, 3), (Ability.Constitution, 3), (Ability.Dexterity, 2),
                (Ability.Wisdom, 1), (Ability.Intelligence, 1), (Ability.Charisma, 1),
            }.Where(entry => !exclude.Contains(entry.ability)).ToArray();

            int roll = Random.Range(0, weighted.Sum(entry => entry.weight));
            foreach (var (ability, weight) in weighted)
            {
                if (roll < weight)
                    return ability;
                roll -= weight;
            }
            return weighted[0].ability;
        }

        static Item Find(string name) =>
            All.FirstOrDefault(item => item.name == name)
            ?? throw new ArgumentException($"No item called \"{name}\" in the catalog.");

        static T Pick<T>(T[] items) => items[Random.Range(0, items.Length)];

        static Item Weapon(string name, string glyph, int dice, int sides, int value, bool twoHanded = false) => new()
        {
            name = name, baseName = name, kind = ItemKind.Weapon, slot = EquipSlot.MainHand, glyph = glyph,
            iconColor = WeaponColor, damageDieCount = dice, damageDieSides = sides, twoHanded = twoHanded,
            valueCopper = value,
        };

        static Item Armor(string name, string glyph, ArmorCategory category, int ac, int value, int strength = 0) => new()
        {
            name = name, baseName = name, kind = ItemKind.Armor, slot = EquipSlot.Chest, glyph = glyph,
            iconColor = ArmorColor, armorCategory = category, armorClass = ac, strengthRequirement = strength,
            valueCopper = value,
        };

        static Item Wearable(string name, string glyph, EquipSlot slot, ItemKind kind, int value) => new()
        {
            name = name, baseName = name, kind = kind, slot = slot, glyph = glyph,
            iconColor = kind == ItemKind.Jewelry ? JewelryColor : ClothingColor, valueCopper = value,
        };

        static Item Trade(string name, string glyph, int value) => new()
        {
            name = name, baseName = name, kind = ItemKind.TradeGood, glyph = glyph, iconColor = TradeColor,
            valueCopper = value, maxStack = 20,
        };

        static Item Legendary(Item baseItem, string name, int magic, params (Ability ability, int bonus)[] abilities)
        {
            var item = baseItem.Clone();
            item.name = name;
            item.rarity = ItemRarity.Legendary;
            item.magicBonus = magic;
            foreach (var (ability, bonus) in abilities)
                item.abilityBonuses[(int)ability] = bonus;
            item.valueCopper += 50000 * Money.Gold;
            return item;
        }
    }
}
