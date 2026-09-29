using System;

namespace Runeterra.Units
{
    /// <summary>
    /// Бой. Чистый C#. Урон = 30 + (сила атакующего − сила защитника), в пределах 5..60.
    /// Ближний: удары одновременные, защитник с силой > 0 отвечает по той же формуле.
    /// Дальний (лучник): урон от силы выстрела, без ответа.
    /// defenseBonus — прибавка к защите (местность), считается в правилах игры.
    /// Опыт: +1 участникам боя, +1 за уничтожение врага.
    /// </summary>
    public static class Combat
    {
        public const int BaseDamage = 30;
        public const int MinDamage = 5;
        public const int MaxDamage = 60;

        public static int DamageTo(int attackerStrength, int defenderStrength) =>
            Math.Max(MinDamage, Math.Min(MaxDamage, BaseDamage + attackerStrength - defenderStrength));

        public static bool CanAttackAtAll(Unit attacker) =>
            attacker.IsAlive && attacker.Data.role == UnitRole.Melee && attacker.Data.strength > 0;

        public static bool CanAttack(Unit attacker, Unit defender) =>
            CanAttackAtAll(attacker) && attacker.MovesLeft > 0 && defender.IsAlive &&
            attacker.OwnerIndex != defender.OwnerIndex && attacker.Coord.DistanceTo(defender.Coord) == 1;

        public static bool CanRangedAttack(Unit attacker, Unit defender) =>
            attacker.IsAlive && attacker.Data.IsRanged && attacker.MovesLeft > 0 && defender.IsAlive &&
            attacker.OwnerIndex != defender.OwnerIndex && attacker.Coord.DistanceTo(defender.Coord) <= attacker.Data.range;

        /// <summary>Урон защитнику и ответный урон атакующему.</summary>
        public static (int dealt, int taken) Preview(Unit attacker, Unit defender, int defenseBonus)
        {
            int def = defender.MeleeStrength + defenseBonus;
            int atk = attacker.MeleeStrength + attacker.AttackBonus;
            return (DamageTo(atk, def),
                    defender.MeleeStrength > 0 ? DamageTo(def, atk) : 0);
        }

        /// <summary>Проводит атаку. Атака тратит все очки движения. Возвращает фактический урон.</summary>
        public static (int dealt, int taken) Resolve(Unit attacker, Unit defender, int defenseBonus)
        {
            if (!CanAttack(attacker, defender)) return (0, 0);
            var (dealt, taken) = Preview(attacker, defender, defenseBonus);
            attacker.SpendAllMoves();
            attacker.MarkActed();
            attacker.NotifyAttacked(defender.Coord);
            defender.TakeDamage(dealt);
            attacker.TakeDamage(taken);
            GrantExperience(attacker, defender);
            return (dealt, taken);
        }

        /// <summary>Урон выстрела. Ответного удара нет.</summary>
        public static int RangedPreview(Unit attacker, Unit defender, int defenseBonus) =>
            DamageTo(attacker.RangedStrength + attacker.AttackBonus, defender.MeleeStrength + defenseBonus);

        public static int ResolveRanged(Unit attacker, Unit defender, int defenseBonus)
        {
            if (!CanRangedAttack(attacker, defender)) return 0;
            int dealt = RangedPreview(attacker, defender, defenseBonus);
            attacker.SpendAllMoves();
            attacker.MarkActed();
            attacker.NotifyAttacked(defender.Coord);
            defender.TakeDamage(dealt);
            GrantExperience(attacker, defender);
            return dealt;
        }

        private static void GrantExperience(Unit attacker, Unit defender)
        {
            attacker.GainExperience(defender.IsAlive ? 1 : 2);
            if (defender.Data.strength > 0) defender.GainExperience(attacker.IsAlive ? 1 : 2);
        }
    }
}
