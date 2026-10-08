using System;
using UnityEngine;

namespace WowSandbox
{
    public enum Ability
    {
        Strength,
        Dexterity,
        Constitution,
        Intelligence,
        Wisdom,
        Charisma,
    }

    /// <summary>Dice rolls, D&D notation.</summary>
    public static class Dice
    {
        /// <summary>Rolls <paramref name="count"/>d<paramref name="sides"/> and sums them.</summary>
        public static int Roll(int count, int sides)
        {
            int total = 0;
            for (int i = 0; i < count; i++)
                total += UnityEngine.Random.Range(1, sides + 1);
            return total;
        }

        public static string Format(int count, int sides, int modifier = 0)
        {
            string dice = $"{count}d{sides}";
            return modifier == 0 ? dice : $"{dice}{(modifier > 0 ? "+" : "")}{modifier}";
        }
    }

    /// <summary>
    /// What equipped items add up to, pushed into CharacterStats by Inventory. Built fresh
    /// on every equipment change rather than tracked incrementally.
    /// </summary>
    public struct Gear
    {
        public bool HasWeapon;
        public int WeaponDieCount;
        public int WeaponDieSides;
        public int WeaponBonus;

        public ArmorCategory Armor;
        public int ArmorBase;
        public int ArmorBonus;
        public int StrengthRequirement;

        public bool HasShield;
        public int ShieldBonus;

        public int Protection;
        public int[] Abilities;
    }

    /// <summary>
    /// D&D 5e-style character numbers: six ability scores, level and experience, and what
    /// derives from them — modifiers, proficiency, armour class, hit points, attack bonus.
    /// Used by the player and NPCs alike. Health/HealthController take their max HP from
    /// here when it's present, and Combat rolls against it.
    ///
    /// Hit points use 5e's fixed-average rule: full hit die at level 1, then the die's
    /// average rounded up (d10 → 6) plus CON per level after, so levelling is predictable.
    ///
    /// Once Inventory pushes Gear in, equipment replaces the weapon fields below and
    /// armour follows 5e: light adds all of DEX, medium up to +2, heavy none; a shield adds
    /// 2; magic +N adds on top; ability bonuses raise scores (capped at 20). No weapon
    /// equipped means an unarmed strike, 1 + STR. Heavy armour without the STR it asks for
    /// slows you, 30 ft to 20 ft.
    /// </summary>
    public class CharacterStats : MonoBehaviour
    {
        public string characterName = "Warrior";
        public string className = "Fighter";

        [Header("Level")]
        [Range(1, MaxLevel)] public int level = 1;
        public int experience;
        [Tooltip("XP awarded to whoever lands the killing blow on this character.")]
        public int experienceValue = 25;

        [Header("Ability scores")]
        [Range(1, 20)] public int strength = 10;
        [Range(1, 20)] public int dexterity = 10;
        [Range(1, 20)] public int constitution = 10;
        [Range(1, 20)] public int intelligence = 10;
        [Range(1, 20)] public int wisdom = 10;
        [Range(1, 20)] public int charisma = 10;

        [Header("Combat")]
        [Tooltip("Sides on the hit die: d10 fighter, d8 rogue, d4 for something tiny.")]
        public int hitDie = 8;
        [Tooltip("Flat AC on top of everything else — stands in for armour on NPCs, which " +
                 "don't carry equipment.")]
        public int armorBonus;
        [Tooltip("Weapon damage dice, before the STR modifier: 1d8 is a longsword. Ignored " +
                 "once an Inventory supplies equipment.")]
        public int weaponDieCount = 1;
        public int weaponDieSides = 8;

        public const int MaxLevel = 20;

        // 5e's experience thresholds: index is the level you're trying to reach, minus one.
        static readonly int[] LevelThresholds =
        {
            0, 300, 900, 2700, 6500, 14000, 23000, 34000, 48000, 64000,
            85000, 100000, 120000, 140000, 165000, 195000, 225000, 265000, 305000, 355000,
        };

        Gear? _gear;

        /// <summary>Raised whenever anything here changes — XP, level, scores, equipment.</summary>
        public event Action Changed;

        /// <summary>Raised once per level gained, with the new level.</summary>
        public event Action<int> LeveledUp;

        /// <summary>The score including equipment bonuses, capped at 20.</summary>
        public int Score(Ability ability) => Mathf.Min(20, BaseScore(ability) + GearAbility(ability));

        /// <summary>How much equipment is adding to <paramref name="ability"/>.</summary>
        public int GearAbility(Ability ability) =>
            _gear?.Abilities != null ? _gear.Value.Abilities[(int)ability] : 0;

        public int BaseScore(Ability ability) => ability switch
        {
            Ability.Strength => strength,
            Ability.Dexterity => dexterity,
            Ability.Constitution => constitution,
            Ability.Intelligence => intelligence,
            Ability.Wisdom => wisdom,
            _ => charisma,
        };

        public static int ModifierFor(int score) => Mathf.FloorToInt((score - 10) / 2f);
        public int Modifier(Ability ability) => ModifierFor(Score(ability));

        public int ProficiencyBonus => 2 + (level - 1) / 4;
        public int Initiative => Modifier(Ability.Dexterity);
        public int PassivePerception => 10 + Modifier(Ability.Wisdom);

        public int ArmorClass
        {
            get
            {
                int dex = Modifier(Ability.Dexterity);
                if (_gear == null)
                    return 10 + dex + armorBonus;

                var gear = _gear.Value;
                int ac = gear.Armor switch
                {
                    ArmorCategory.Light => gear.ArmorBase + dex,
                    ArmorCategory.Medium => gear.ArmorBase + Mathf.Min(dex, 2),
                    ArmorCategory.Heavy => gear.ArmorBase,
                    _ => 10 + dex,
                };
                if (gear.Armor != ArmorCategory.None)
                    ac += gear.ArmorBonus;
                if (gear.HasShield)
                    ac += 2 + gear.ShieldBonus;
                return ac + gear.Protection + armorBonus;
            }
        }

        public ArmorCategory ArmorWorn => _gear?.Armor ?? ArmorCategory.None;

        /// <summary>Wearing heavy armour without the STR it asks for.</summary>
        public bool ArmorTooHeavy =>
            _gear is { Armor: ArmorCategory.Heavy } gear && Score(Ability.Strength) < gear.StrengthRequirement;

        /// <summary>Movement multiplier: 5e's 30 ft dropping to 20 ft under too-heavy armour.</summary>
        public float SpeedMultiplier => ArmorTooHeavy ? 2f / 3f : 1f;
        public int SpeedFeet => ArmorTooHeavy ? 20 : 30;

        int WeaponDieCount => _gear is { } gear ? (gear.HasWeapon ? gear.WeaponDieCount : 0) : weaponDieCount;
        int WeaponDieSides => _gear is { } gear ? gear.WeaponDieSides : weaponDieSides;
        int WeaponBonus => _gear?.WeaponBonus ?? 0;

        public int AttackBonus => Modifier(Ability.Strength) + ProficiencyBonus + WeaponBonus;

        public string DamageDice
        {
            get
            {
                int flat = Modifier(Ability.Strength) + WeaponBonus;
                return WeaponDieCount > 0
                    ? Dice.Format(WeaponDieCount, WeaponDieSides, flat)
                    : Mathf.Max(1, 1 + flat).ToString();
            }
        }

        /// <summary>
        /// One hit's damage: weapon dice (doubled on a crit, as 5e does) plus STR and any
        /// magic bonus, at least 1. Unarmed is a flat 1 + STR, which a crit can't double.
        /// </summary>
        public int RollDamage(bool critical)
        {
            int flat = Modifier(Ability.Strength) + WeaponBonus;
            int dice = WeaponDieCount > 0
                ? Dice.Roll(WeaponDieCount * (critical ? 2 : 1), WeaponDieSides)
                : 1;
            return Mathf.Max(1, dice + flat);
        }

        public void SetGear(Gear gear)
        {
            _gear = gear;
            Changed?.Invoke();
        }

        public int MaxHitPoints
        {
            get
            {
                int con = Modifier(Ability.Constitution);
                int perLevel = hitDie / 2 + 1 + con;
                return Mathf.Max(1, hitDie + con + (level - 1) * Mathf.Max(1, perLevel));
            }
        }

        /// <summary>XP at which the current level began.</summary>
        public int CurrentLevelExperience => LevelThresholds[level - 1];

        /// <summary>XP needed for the next level, or the current threshold at the cap.</summary>
        public int NextLevelExperience => LevelThresholds[Mathf.Min(level, MaxLevel - 1)];

        /// <summary>Progress through the current level, 0-1. Full at the level cap.</summary>
        public float LevelProgress01
        {
            get
            {
                if (level >= MaxLevel)
                    return 1f;
                int span = NextLevelExperience - CurrentLevelExperience;
                return span > 0 ? Mathf.Clamp01((experience - CurrentLevelExperience) / (float)span) : 1f;
            }
        }

        public void GainExperience(int amount)
        {
            if (amount <= 0)
                return;

            experience += amount;
            while (level < MaxLevel && experience >= NextLevelExperience)
            {
                level++;
                Debug.Log($"[CharacterStats] {characterName} reached level {level}.", this);
                LeveledUp?.Invoke(level);
            }

            Changed?.Invoke();
        }

        void OnValidate()
        {
            level = Mathf.Clamp(level, 1, MaxLevel);
            hitDie = Mathf.Max(1, hitDie);
            weaponDieCount = Mathf.Max(1, weaponDieCount);
            weaponDieSides = Mathf.Max(1, weaponDieSides);
            Changed?.Invoke();
        }
    }
}
