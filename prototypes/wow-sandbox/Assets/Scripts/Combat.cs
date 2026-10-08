using System;
using UnityEngine;

namespace WowSandbox
{
    /// <summary>The outcome of one attack, for the floating combat text and anything else listening.</summary>
    public struct AttackResult
    {
        public int Roll;
        public int Total;
        public int TargetArmorClass;
        public bool Hit;
        public bool Critical;
        public int Damage;
        public bool Killed;
        public int ExperienceAwarded;
    }

    /// <summary>
    /// D&D attack resolution. Roll a d20, add the attacker's attack bonus, and meet or beat
    /// the target's armour class to hit. A natural 20 always hits and crits (damage dice are
    /// rolled twice); a natural 1 always misses. Damage is the weapon dice plus STR, at least
    /// 1. A killing blow hands the target's XP to the attacker.
    /// </summary>
    public static class Combat
    {
        /// <summary>Armour class for a target that has no CharacterStats — an unarmoured commoner.</summary>
        public const int DefaultArmorClass = 10;
        /// <summary>XP for killing something that has no CharacterStats.</summary>
        public const int DefaultExperience = 10;

        /// <summary>Raised after every resolved attack, hit or miss.</summary>
        public static event Action<Health, AttackResult> AttackResolved;

        public static AttackResult MeleeAttack(CharacterStats attacker, Health target)
        {
            var result = new AttackResult();
            if (attacker == null || target == null || target.IsDead)
                return result;

            var defender = target.GetComponent<CharacterStats>();
            result.TargetArmorClass = defender != null ? defender.ArmorClass : DefaultArmorClass;
            result.Roll = Dice.Roll(1, 20);
            result.Total = result.Roll + attacker.AttackBonus;
            result.Critical = result.Roll == 20;
            result.Hit = result.Critical || (result.Roll != 1 && result.Total >= result.TargetArmorClass);

            if (result.Hit)
            {
                int dice = attacker.weaponDieCount * (result.Critical ? 2 : 1);
                result.Damage = Mathf.Max(1,
                    Dice.Roll(dice, attacker.weaponDieSides) + attacker.Modifier(Ability.Strength));
                target.TakeDamage(result.Damage);

                if (target.IsDead)
                {
                    result.Killed = true;
                    result.ExperienceAwarded = defender != null ? defender.experienceValue : DefaultExperience;
                    attacker.GainExperience(result.ExperienceAwarded);
                }
            }

            Debug.Log($"[Combat] {attacker.characterName} → {target.name}: d20 {result.Roll} " +
                      $"{(attacker.AttackBonus >= 0 ? "+" : "")}{attacker.AttackBonus} = {result.Total} " +
                      $"vs AC {result.TargetArmorClass} — " +
                      (result.Hit ? $"{(result.Critical ? "CRIT " : "")}hit for {result.Damage}" : "miss") +
                      (result.Killed ? $", killed (+{result.ExperienceAwarded} XP)" : ""), attacker);

            AttackResolved?.Invoke(target, result);
            return result;
        }
    }
}
