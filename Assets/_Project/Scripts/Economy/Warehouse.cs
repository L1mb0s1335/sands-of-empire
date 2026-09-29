using System;
using System.Collections.Generic;
using System.Linq;

namespace Runeterra.Economy
{
    /// <summary>Склад одного города. Общего склада у региона нет.</summary>
    public class Warehouse
    {
        public const int BaseCapacity = 20;

        private readonly Dictionary<GoodData, float> _stock = new Dictionary<GoodData, float>();

        public int Capacity { get; set; } = BaseCapacity;
        public float SpoilageMultiplier { get; set; } = 1f;

        public IEnumerable<KeyValuePair<GoodData, float>> Items => _stock.Where(kv => kv.Value > 0.001f);

        public float Get(GoodData good) => good != null && _stock.TryGetValue(good, out var v) ? v : 0f;

        public bool Has(GoodData good, float amount) => Get(good) + 0.0001f >= amount;

        /// <summary>Восстановление из сохранения (без ограничения вместимостью).</summary>
        internal void Set(GoodData good, float amount) => _stock[good] = amount;

        /// <summary>Кладёт на склад, лишнее сверх вместимости пропадает. Возвращает, сколько вошло.</summary>
        public float Add(GoodData good, float amount)
        {
            if (good == null || amount <= 0f) return 0f;
            float before = Get(good);
            float after = Math.Min(Capacity, before + amount);
            _stock[good] = after;
            return after - before;
        }

        /// <summary>Забирает со склада сколько есть (не больше amount). Возвращает забранное.</summary>
        public float Take(GoodData good, float amount)
        {
            float have = Get(good);
            float taken = Math.Min(have, Math.Max(0f, amount));
            if (good != null) _stock[good] = have - taken;
            return taken;
        }

        /// <summary>Порча за ход. Возвращает потери по товарам.</summary>
        public Dictionary<GoodData, float> Spoil()
        {
            var lost = new Dictionary<GoodData, float>();
            foreach (var good in _stock.Keys.ToList())
            {
                float loss = _stock[good] * good.spoilagePerTurn * SpoilageMultiplier;
                if (loss <= 0.0001f) continue;
                _stock[good] -= loss;
                lost[good] = loss;
            }
            return lost;
        }
    }
}
