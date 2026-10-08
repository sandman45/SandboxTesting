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
    /// rolled twice); a natural 1 always misses. Damage is CharacterStats.RollDamage —
    /// weapon dice plus STR and any magic bonus. A killing blow hands the target's XP to
    /// the attacker.
    /// </summary>
    public static class Combat
    {
        /// <summary>Armour class for a target that has no CharacterStats — an unarmoured commoner.</summary>
        public const int DefaultArmorClass = 10;
        /// <summary>XP for killing something that has no CharacterStats.</summary>
        public const int DefaultExperience = 10;

        /// <summary>Raised after every resolved attack on an NPC, hit or miss.</summary>
        public static event Action<Health, AttackResult> AttackResolved;

        /// <summary>Raised after every attack on the player, hit or miss.</summary>
        public static event Action<HealthController, AttackResult> PlayerAttacked;

        public static AttackResult MeleeAttack(CharacterStats attacker, Health target)
        {
            if (attacker == null || target == null || target.IsDead)
                return new AttackResult();

            var defender = target.GetComponent<CharacterStats>();
            var result = Roll(attacker, defender != null ? defender.ArmorClass : DefaultArmorClass);

            if (result.Hit)
            {
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

        /// <summary>
        /// An NPC attacking the player — same roll, against the player's armour class (their
        /// equipment counts, in water too). Dying is HealthController's business.
        /// </summary>
        public static AttackResult AttackPlayer(CharacterStats attacker, HealthController target)
        {
            if (attacker == null || target == null || target.IsDead)
                return new AttackResult();

            var defender = target.GetComponent<CharacterStats>();
            var result = Roll(attacker, defender != null ? defender.ArmorClass : DefaultArmorClass);
            if (result.Hit)
            {
                var cause = attacker.GetComponent<SwimmingCreature>() != null ? DeathCause.Shark : DeathCause.Creature;
                target.TakeDamage(result.Damage, cause, attacker.characterName);
            }

            Debug.Log($"[Combat] {attacker.characterName} → player: d20 {result.Roll} = {result.Total} " +
                      $"vs AC {result.TargetArmorClass} — " +
                      (result.Hit ? $"{(result.Critical ? "CRIT " : "")}hit for {result.Damage}" : "miss"), attacker);

            PlayerAttacked?.Invoke(target, result);
            return result;
        }

        /// <summary>The d20 roll and, on a hit, the damage — shared by both directions of attack.</summary>
        static AttackResult Roll(CharacterStats attacker, int armorClass)
        {
            var result = new AttackResult { TargetArmorClass = armorClass, Roll = Dice.Roll(1, 20) };
            result.Total = result.Roll + attacker.AttackBonus;
            result.Critical = result.Roll == 20;
            result.Hit = result.Critical || (result.Roll != 1 && result.Total >= armorClass);
            if (result.Hit)
                result.Damage = attacker.RollDamage(result.Critical);
            return result;
        }
    }
}
