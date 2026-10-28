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
        // Added after the rest so the numbers above keep their meaning.
        Shoulders,
        Legs,
        Back,
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
        /// <summary>Used up when used — potions.</summary>
        Consumable,
        /// <summary>Carried rather than worn, for a job — the Fishing Pole. Never sold as junk.</summary>
        Tool,
        /// <summary>Light for the dark: torches and lanterns (held in the off hand), and lamp oil.</summary>
        Light,
        /// <summary>A page of the story (journalPage): taking it adds it to the Journal, never the bags.</summary>
        JournalPage,
        /// <summary>Wanted for a story errand — Sprocket's compass parts. Worth nothing to a merchant.</summary>
        Quest,
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

        [Header("Potion")]
        [Tooltip("Health restored when drunk: healDieCount d healDieSides + healBonus.")]
        public int healDieCount;
        public int healDieSides;
        public int healBonus;

        [Header("Light")]
        [Tooltip("Torch: game hours left on the one burning (0 = none lit yet). Lantern: hours of oil left in it.")]
        public float fuel;

        [Header("Food")]
        [Tooltip("How much of an empty stomach eating it fills, 0-1. 0 isn't food.")]
        public float nourish;
        [Tooltip("Cooked at a fire: ends the opening's starving, and leaves you Well Fed. Raw food makes you sick.")]
        public bool cooked;

        [Header("Story")]
        [Tooltip("Journal pages: which page, by its number in JournalPages.")]
        public int journalPage;

        [Header("Stack and value")]
        public int valueCopper;
        public int maxStack = 1;
        public int count = 1;

        public bool IsEquippable => slot != EquipSlot.None;
        public bool IsPotion => kind == ItemKind.Consumable && healDieCount > 0;
        public bool IsFood => nourish > 0f;
        public bool IsFishingPole => kind == ItemKind.Tool && baseName != null && baseName.EndsWith("Fishing Pole");
        public bool IsTorch => kind == ItemKind.Light && baseName == "Torch";
        public bool IsLantern => kind == ItemKind.Light && baseName != null && baseName.EndsWith("Lantern");
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

        /// <summary>What a vendor pays for the whole stack: half its value, as the D&D rules have it,
        /// and never nothing for something that's worth something.</summary>
        public static int SellPrice(Item item) =>
            item == null || item.valueCopper <= 0 ? 0 : Mathf.Max(1, item.valueCopper * item.count / 2);

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
            Weapon("Maul", "Ml", 2, 6, 10 * Money.Gold, twoHanded: true),
            Weapon("Greatsword", "GS", 2, 6, 50 * Money.Gold, twoHanded: true),
            // Not in the SRD: a greatsword by another name, for the second two-handed sword model.
            Weapon("Claymore", "Cl", 2, 6, 50 * Money.Gold, twoHanded: true),
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
            Wearable("Leather Spaulders", "Sp", EquipSlot.Shoulders, ItemKind.Clothing, 5 * Money.Silver),
            // The exceptions: iron plates over the shoulders or legs turn a blow (+1 AC).
            Protective(Wearable("Iron Pauldrons", "Pa", EquipSlot.Shoulders, ItemKind.Clothing, 2 * Money.Gold), 1),
            Wearable("Leather Leggings", "LL", EquipSlot.Legs, ItemKind.Clothing, 5 * Money.Silver),
            Protective(Wearable("Iron Legguards", "IL", EquipSlot.Legs, ItemKind.Clothing, 2 * Money.Gold), 1),
            Wearable("Leather Boots", "Bt", EquipSlot.Feet, ItemKind.Clothing, 1 * Money.Gold),
            Wearable("Traveler's Boots", "TB", EquipSlot.Feet, ItemKind.Clothing, 2 * Money.Gold),
            // The player's starting outfit. These three have a look (EquipmentVisuals).
            Wearable("Linen Shirt", "Ln", EquipSlot.Chest, ItemKind.Clothing, 5 * Money.Silver),
            Wearable("Cloth Shorts", "Sr", EquipSlot.Legs, ItemKind.Clothing, 5 * Money.Silver),
            Wearable("Worn Sandals", "Sd", EquipSlot.Feet, ItemKind.Clothing, 3 * Money.Silver),
        };

        /// <summary>
        /// Only ever looted from humanoids (LootDrop.HumanoidDrops) — never on a vendor's shelf
        /// or rolled as a random magic item.
        /// </summary>
        public static readonly Item[] LootOnly =
        {
            Wearable("Pirate Hat", "PH", EquipSlot.Head, ItemKind.Clothing, 2 * Money.Gold),
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
            Raw(Trade("Raw Chicken", "Mt", 5)),
            Trade("Frayed Rope", "Rp", 1 * Money.Silver),
            Trade("Loaded Dice", "Di", 1 * Money.Silver),
            Trade("Bent Lockpick", "Lp", 2 * Money.Silver),
            Trade("Stolen Trinket", "Tr", 5 * Money.Silver),
            Trade("Silver Earring", "Er", 2 * Money.Gold),
            Raw(Trade("Raw Fish", "Fi", 5)),
            Trade("Shark Tooth", "Th", 5 * Money.Silver),
            Trade("Shark Fin", "Fn", 1 * Money.Gold),
            Trade("Giant Shark Tooth", "GT", 5 * Money.Gold),
            // Washed up on the beach: what a campfire's built from.
            Trade("Driftwood", "Dw", 1),
        };

        /// <summary>
        /// What FishingSpot can land, Common to Legendary — index by (int)ItemRarity. Kept out
        /// of TradeGoods so a prize fish never turns up in a chicken's loot.
        /// </summary>
        public static readonly Item[] Fish =
        {
            TradeGoods.First(item => item.name == "Raw Fish"),
            Catch("Silverscale Snapper", "Sn", ItemRarity.Uncommon, 5 * Money.Silver),
            Catch("Tidegrave Grouper", "Gr", ItemRarity.Rare, 5 * Money.Gold),
            Catch("Moonfin Eel", "Ee", ItemRarity.VeryRare, 25 * Money.Gold),
            Catch("Gilded Kingfish", "Kf", ItemRarity.Legendary, 100 * Money.Gold),
        };

        /// <summary>
        /// Fish that only bite after dark, by rarity like <see cref="Fish"/> (null where there's
        /// none): at night FishingSpot lands one of these half the times it rolls that tier.
        /// </summary>
        public static readonly Item[] NightFish =
        {
            null,
            Catch("Luminous Squid", "Sq", ItemRarity.Uncommon, 25 * Money.Silver),
            Catch("Moonlit Ray", "Ry", ItemRarity.Rare, 8 * Money.Gold),
            null,
            null,
        };

        /// <summary>
        /// Rubbish hauled up from floating wreckage (FishingSpot.wreckage) — only ever found
        /// there, and sold off by Sell Junk.
        /// </summary>
        public static readonly Item[] Flotsam =
        {
            Trade("Waterlogged Boot", "Bt", 3),
            Trade("Barnacled Bottle", "Bo", 5),
            Trade("Sodden Sailcloth", "Sc", 8),
            Trade("Rusted Nails", "Na", 2),
            Trade("Splintered Plank", "Pl", 1),
            Trade("Tangled Net", "Nt", 10),
        };

        /// <summary>Carried in the bags to do something: FishingSpot wants a fishing pole.</summary>
        public static readonly Item[] Tools =
        {
            Tool("Twig Fishing Pole", "TP", 2 * Money.Silver),
            Tool("Fishing Pole", "FP", 1 * Money.Gold),
            // 5e's tinderbox, by another name: with three Driftwood, a campfire (Campfire).
            Tool("Flint & Steel", "Fl", 5 * Money.Silver),
        };

        /// <summary>
        /// Light for the dark, D&D-priced. A torch burns TorchHours and is gone (they stack, and the
        /// next one lights); a lantern burns OilHours per flask of oil, filled from your bags.
        /// Longer than D&D's hour: a night here is about twelve game hours.
        /// Kept out of Equippable, so there's never a "+1 Torch of Strength".
        /// </summary>
        public static readonly Item[] Lights =
        {
            Light("Torch", "To", EquipSlot.OffHand, 1, stack: 10),
            Light("Hooded Lantern", "La", EquipSlot.OffHand, 5 * Money.Gold, fuel: OilHours),
            // D&D's: a brighter, longer beam for the same oil.
            Light("Bullseye Lantern", "BL", EquipSlot.OffHand, 10 * Money.Gold, fuel: OilHours),
            Light("Flask of Oil", "Oi", EquipSlot.None, 1 * Money.Silver, stack: 10),
        };

        // Game hours (1 real minute each at the default speed): about three torches, or a flask
        // and a half, to see out a night.
        public const float TorchHours = 4f;
        public const float OilHours = 8f;

        /// <summary>
        /// What Sprocket needs for his compass (CompassQuest), one from each of the isle's wrecks.
        /// No value, so no merchant buys them.
        /// </summary>
        public static readonly Item[] QuestItems =
        {
            Quest("Brass Gimbal", "Gb"),
            Quest("Lodestone Needle", "Nd"),
            Quest("Cracked Dial Glass", "Dg"),
            Quest("Clockwork Mainspring", "Ms"),
        };

        /// <summary>Cooked at a campfire: what ends the opening's hunger and leaves you Well Fed.</summary>
        public static readonly Item[] Meals =
        {
            Meal("Grilled Fish", "GF", 0.35f, 2 * Money.Silver),
            Meal("Roast Chicken", "RC", 0.45f, 3 * Money.Silver),
        };

        public static readonly Item[] Potions =
        {
            Potion("Potion of Healing", 2, 4, 2, 50 * Money.Gold),
            Potion("Potion of Greater Healing", 4, 4, 4, 150 * Money.Gold),
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
        static IEnumerable<Item> All => Equippable.Concat(LootOnly).Concat(Tools).Concat(Lights).Concat(TradeGoods).Concat(Flotsam).Concat(Fish.Skip(1)).Concat(NightFish.Where(fish => fish != null)).Concat(Meals).Concat(Potions).Concat(Legendaries).Concat(QuestItems);

        /// <summary>A merchant's shelves: every mundane piece of gear, the tools and lights, then the potions — fresh copies.</summary>
        public static Item[] VendorStock() => Equippable.Concat(Tools).Concat(Lights).Concat(Potions).Select(item => item.Clone()).ToArray();

        /// <summary>Every item name in the catalog (Harvest Gear checks its rows against these).</summary>
        public static IEnumerable<string> Names => All.Select(item => item.name);

        /// <summary>The catalog's own copy of the named item — read it, don't change it.</summary>
        public static bool TryFind(string name, out Item item)
        {
            item = All.FirstOrDefault(entry => entry.name == name);
            return item != null;
        }

        /// <summary>A fresh copy of the named item.</summary>
        public static Item Create(string name, int count = 1)
        {
            var item = Find(name).Clone();
            item.count = Mathf.Clamp(count, 1, item.maxStack);
            return item;
        }

        public static Item RandomTradeGood() => Pick(TradeGoods).Clone();

        /// <summary>
        /// What floating wreckage gives up at <paramref name="rarity"/> (FishingSpot rolls it the
        /// way it rolls a fish): Common is flotsam, now and then driftwood or rope; Uncommon is
        /// plain gear, lights or a trinket; Rare a magic Uncommon item or jewellery; Very Rare and
        /// Legendary a magic Rare or Very Rare item — never one of the hand-made Legendaries.
        /// </summary>
        public static Item Salvage(ItemRarity rarity)
        {
            float roll = Random.value;
            switch (rarity)
            {
                case ItemRarity.Common:
                    if (roll < 0.15f)
                        return Create("Driftwood");
                    return roll < 0.25f ? Create("Frayed Rope") : Pick(Flotsam).Clone();
                case ItemRarity.Uncommon:
                    if (roll < 0.4f)
                        return RandomMundane();
                    if (roll < 0.6f)
                        return Create("Torch", Random.Range(1, 4));
                    if (roll < 0.75f)
                        return Create("Flask of Oil", Random.Range(1, 3));
                    return Create(roll < 0.9f ? "Stolen Trinket" : "Silver Earring");
                case ItemRarity.Rare:
                    return roll < 0.6f ? RandomMagic(ItemRarity.Uncommon) : Pick(Jewelry).Clone();
                case ItemRarity.VeryRare:
                    return RandomMagic(ItemRarity.Rare);
                default:
                    return RandomMagic(ItemRarity.VeryRare);
            }
        }

        public static Item RandomMundane() => Pick(Equippable.ToArray()).Clone();

        /// <summary>
        /// Treasure-chest odds, far kinder than a thief's pockets: never mundane, and a real
        /// chance at the good stuff — Rare (blue) 35%, Very Rare (epic, purple) 18% and
        /// Legendary (orange) 7%, the rest Uncommon.
        /// </summary>
        public static Item RandomTreasure()
        {
            float roll = Random.value;
            if (roll < 0.07f)
                return Pick(Legendaries).Clone();
            if (roll < 0.25f)
                return RandomMagic(ItemRarity.VeryRare);
            if (roll < 0.60f)
                return RandomMagic(ItemRarity.Rare);
            return RandomMagic(ItemRarity.Uncommon);
        }

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
            // The iron pieces' own +1 AC isn't magic, and doesn't make them "of Protection".
            int mundaneProtection = item.protectionBonus;
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
                    item.protectionBonus = mundaneProtection + 1;
            }

            item.name = MagicName(item, abilities, mundaneProtection);
            item.valueCopper += rarity switch
            {
                ItemRarity.Uncommon => 100 * Money.Gold,
                ItemRarity.Rare => 500 * Money.Gold,
                _ => 5000 * Money.Gold,
            };
            return item;
        }

        static string MagicName(Item item, List<Ability> abilities, int mundaneProtection)
        {
            string name = item.magicBonus > 0 ? $"+{item.magicBonus} {item.baseName}" : item.baseName;

            var suffixes = abilities.Distinct().Select(AbilityNoun).ToList();
            if (item.protectionBonus > mundaneProtection)
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

        static Item Protective(Item item, int armorClass)
        {
            item.protectionBonus = armorClass;
            return item;
        }

        /// <summary>Journal page <paramref name="number"/> as loot: a chest's or a corpse's, taken to the Journal.</summary>
        public static Item JournalPage(int number) => new()
        {
            name = "Water-stained Page", baseName = "Water-stained Page", kind = ItemKind.JournalPage,
            glyph = "Pg", iconColor = new Color(0.72f, 0.64f, 0.46f), journalPage = number,
        };

        static Item Trade(string name, string glyph, int value) => new()
        {
            name = name, baseName = name, kind = ItemKind.TradeGood, glyph = glyph, iconColor = TradeColor,
            valueCopper = value, maxStack = 20,
        };

        static Item Catch(string name, string glyph, ItemRarity rarity, int value)
        {
            var item = Raw(Trade(name, glyph, value));
            item.rarity = rarity;
            return item;
        }

        /// <summary>Edible as it is, but raw: a little food, and a bad stomach.</summary>
        static Item Raw(Item item)
        {
            item.nourish = RawNourish;
            return item;
        }

        const float RawNourish = 0.15f;

        static Item Meal(string name, string glyph, float nourish, int value) => new()
        {
            name = name, baseName = name, kind = ItemKind.Consumable, glyph = glyph,
            iconColor = new Color(0.62f, 0.4f, 0.2f), valueCopper = value, maxStack = 20,
            nourish = nourish, cooked = true,
        };

        static Item Quest(string name, string glyph) => new()
        {
            name = name, baseName = name, kind = ItemKind.Quest, glyph = glyph,
            iconColor = new Color(0.72f, 0.56f, 0.2f),
        };

        static Item Tool(string name, string glyph, int value) => new()
        {
            name = name, baseName = name, kind = ItemKind.Tool, glyph = glyph,
            iconColor = new Color(0.45f, 0.36f, 0.22f), valueCopper = value,
        };

        static Item Light(string name, string glyph, EquipSlot slot, int value, int stack = 1, float fuel = 0f) => new()
        {
            name = name, baseName = name, kind = ItemKind.Light, slot = slot, glyph = glyph,
            iconColor = new Color(0.62f, 0.42f, 0.16f), valueCopper = value, maxStack = stack, fuel = fuel,
        };

        static readonly Color PotionColor = new(0.72f, 0.16f, 0.16f);

        static Item Potion(string name, int dice, int sides, int bonus, int value) => new()
        {
            name = name, baseName = name, kind = ItemKind.Consumable, glyph = "HP", iconColor = PotionColor,
            healDieCount = dice, healDieSides = sides, healBonus = bonus, valueCopper = value, maxStack = 5,
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
