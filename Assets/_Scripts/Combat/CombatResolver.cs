using UnityEngine;

namespace HuntersAndCollectors.Combat
{
    public enum CombatHitType
    {
        Miss,
        Hit,
        Crit
    }

    public readonly struct CombatResolution
    {
        public CombatResolution(CombatHitType outcome, int roll, int attackTotal, int targetDefence, int baseDamage, int finalDamage)
        {
            Outcome = outcome;
            Roll = roll;
            AttackTotal = attackTotal;
            TargetDefence = targetDefence;
            BaseDamage = baseDamage;
            FinalDamage = finalDamage;
        }

        public CombatHitType Outcome { get; }
        public int Roll { get; }
        public int AttackTotal { get; }
        public int TargetDefence { get; }
        public int BaseDamage { get; }
        public int FinalDamage { get; }

        public bool IsHit => Outcome == CombatHitType.Hit || Outcome == CombatHitType.Crit;
    }

    /// <summary>
    /// Shared d20 combat roll resolver.
    ///
    /// Rules:
    /// - Natural 1: automatic miss.
    /// - Natural 20: automatic critical hit (+50% damage).
    /// - Otherwise attack total (d20 + bonus) must meet/exceed target defence.
    /// </summary>
    public static class CombatResolver
    {
        public static CombatResolution ResolveMeleeAttack(int baseDamage, int attackBonus, int targetDefence)
        {
            int clampedBaseDamage = Mathf.Max(1, baseDamage);
            int safeAttackBonus = Mathf.Max(0, attackBonus);
            int safeTargetDefence = Mathf.Max(0, targetDefence);

            int roll = Random.Range(1, 21);
            int total = roll + safeAttackBonus;

            if (roll == 1)
                return new CombatResolution(CombatHitType.Miss, roll, total, safeTargetDefence, clampedBaseDamage, 0);

            if (roll == 20)
            {
                int critDamage = Mathf.Max(1, Mathf.RoundToInt(clampedBaseDamage * 1.5f));
                return new CombatResolution(CombatHitType.Crit, roll, total, safeTargetDefence, clampedBaseDamage, critDamage);
            }

            if (total >= safeTargetDefence)
                return new CombatResolution(CombatHitType.Hit, roll, total, safeTargetDefence, clampedBaseDamage, clampedBaseDamage);

            return new CombatResolution(CombatHitType.Miss, roll, total, safeTargetDefence, clampedBaseDamage, 0);
        }
    }
}
