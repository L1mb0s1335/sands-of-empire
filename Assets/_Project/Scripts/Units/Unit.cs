using System;
using System.Collections.Generic;
using Runeterra.Map;

namespace Runeterra.Units
{
    /// <summary>Юнит в партии. Чистое состояние без MonoBehaviour.</summary>
    public class Unit
    {
        public int Id { get; private set; }
        public UnitData Data { get; }
        public int OwnerIndex { get; }
        public HexCoord Coord { get; private set; }
        public int MovesLeft { get; private set; }
        public int Health { get; set; }
        public int BuildCharges { get; private set; }
        /// <summary>Юнит израсходован (строитель), а не погиб в бою.</summary>
        public bool Consumed { get; private set; }

        public bool IsAlive => Health > 0;

        // ---------- Опыт ----------

        /// <summary>Пороги опыта для званий «Ветеран» и «Элита».</summary>
        public static readonly int[] LevelThresholds = { 2, 5 };
        public const int StrengthPerLevel = 3;

        public int Experience { get; private set; }
        public int Level => Experience >= LevelThresholds[1] ? 2 : Experience >= LevelThresholds[0] ? 1 : 0;
        public string LevelName => Level switch { 2 => "Элита", 1 => "Ветеран", _ => "Новобранец" };

        /// <summary>Дополнительная сила от лидера региона и т.п. (задаётся правилами игры).</summary>
        public int BonusStrength { get; set; }
        /// <summary>Прибавка к силе только при атаке (способность лидера).</summary>
        public int AttackBonus { get; set; }

        /// <summary>Сила в ближнем бою с учётом опыта (0 у мирных юнитов).</summary>
        public int MeleeStrength => Data.strength > 0 ? Data.strength + Level * StrengthPerLevel + BonusStrength : 0;
        public int RangedStrength => Data.IsRanged ? Data.rangedStrength + Level * StrengthPerLevel + BonusStrength : 0;

        /// <summary>Юнит ходил или атаковал в свой последний ход (тогда он не лечится).</summary>
        public bool Acted { get; private set; }

        public event Action<Unit> Promoted;

        /// <summary>Начисляет опыт; возвращает true при повышении звания.</summary>
        public bool GainExperience(int amount)
        {
            if (!IsAlive || amount <= 0) return false;
            int before = Level;
            Experience += amount;
            if (Level == before) return false;
            Promoted?.Invoke(this);
            return true;
        }

        public void Heal(int amount)
        {
            if (!IsAlive || amount <= 0 || Health >= Data.maxHealth) return;
            Health = Math.Min(Data.maxHealth, Health + amount);
            Damaged?.Invoke(this);
        }

        public void MarkActed() => Acted = true;
        public void ClearActed() => Acted = false;

        /// <summary>Клетки, пройденные последним приказом (для анимации).</summary>
        public event Action<Unit, IReadOnlyList<HexCoord>> Moved;
        /// <summary>Юнит атаковал клетку (для анимации).</summary>
        public event Action<Unit, HexCoord> Attacked;
        public event Action<Unit> Damaged;
        public event Action<Unit> Died;

        /// <param name="id">id из счётчика партии (<c>GameState.NextUnitId</c>); 0 — пробный юнит вне партии.</param>
        public Unit(UnitData data, int ownerIndex, HexCoord coord, int id = 0)
        {
            Id = id;
            Data = data;
            OwnerIndex = ownerIndex;
            Coord = coord;
            Health = data.maxHealth;
            MovesLeft = data.movement;
            BuildCharges = data.buildCharges;
        }

        /// <summary>Восстановление из сохранения.</summary>
        internal void Restore(int id, HexCoord coord, int moves, int health, int charges, int experience, bool acted)
        {
            Id = id;
            Coord = coord;
            MovesLeft = moves;
            Health = health;
            BuildCharges = charges;
            Experience = experience;
            Acted = acted;
        }

        /// <summary>Прибавка к движению (способность лидера); задаётся правилами при появлении юнита.</summary>
        public int MovementBonus { get; set; }

        public int MaxMoves => Data.movement + MovementBonus;

        public void ResetMoves() => MovesLeft = MaxMoves;

        public void SpendAllMoves() => MovesLeft = 0;

        /// <summary>Юнит расходуется (поселенец основал город).</summary>
        public void Consume()
        {
            if (!IsAlive) return;
            Consumed = true;
            Health = 0;
            Died?.Invoke(this);
        }

        /// <summary>Тратит одну постройку; когда постройки кончились, юнит расходуется.</summary>
        public void UseBuildCharge()
        {
            if (BuildCharges <= 0) return;
            BuildCharges--;
            SpendAllMoves();
            if (BuildCharges == 0)
            {
                Consumed = true;
                Health = 0;
                Died?.Invoke(this);
            }
        }

        public void TakeDamage(int amount)
        {
            if (!IsAlive) return;
            Health = Math.Max(0, Health - amount);
            Damaged?.Invoke(this);
            if (!IsAlive) Died?.Invoke(this);
        }

        internal void NotifyAttacked(HexCoord target) => Attacked?.Invoke(this, target);

        /// <summary>
        /// Идёт по пути, пока есть очки движения. Возвращает число пройденных клеток.
        /// </summary>
        /// <param name="stopAt">Остановиться, войдя на такую клетку (например, во вражеский город).</param>
        public int MoveAlong(HexGrid grid, IReadOnlyList<HexCoord> path, Func<HexCoord, bool> stopAt = null)
        {
            var walked = new List<HexCoord>();
            foreach (var step in path)
            {
                if (MovesLeft <= 0) break;
                var tile = grid.GetTile(step);
                if (tile == null || tile.MoveCost() == TerrainRules.Impassable) break;
                MovesLeft = Math.Max(0, MovesLeft - tile.MoveCost());
                Coord = step;
                walked.Add(step);
                Acted = true;
                if (stopAt != null && stopAt(step)) break;
            }
            if (walked.Count > 0) Moved?.Invoke(this, walked);
            return walked.Count;
        }
    }
}
