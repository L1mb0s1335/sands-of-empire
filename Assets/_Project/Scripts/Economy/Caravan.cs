using System.Collections.Generic;
using Runeterra.Cities;
using Runeterra.Map;

namespace Runeterra.Economy
{
    /// <summary>
    /// Караван торговца-NPC: везёт товар из города в город (или морем на внешний рынок).
    /// Игрок не управляет им — только строит дороги/порты и ставит пошлину.
    /// </summary>
    public class Caravan
    {
        /// <summary>id из счётчика торговли (ключ бросков риска в пути).</summary>
        public int Id { get; }
        public int OwnerIndex { get; }
        public GoodData Good { get; }
        public float Amount { get; }
        public City From { get; }
        /// <summary>Город назначения; null — внешний рынок за морем.</summary>
        public City To { get; }
        public bool BySea { get; }
        public List<HexCoord> Path { get; }
        /// <summary>Индекс в пути; -1 — ещё в городе отправления.</summary>
        public int Position { get; set; } = -1;
        public int StalledTurns { get; set; }
        /// <summary>Цена покупки в городе отправления (для отчёта о прибыли).</summary>
        public float BuyPrice { get; }

        public HexCoord Coord => Position < 0 ? From.Coord : Path[Position];
        public bool AtEnd => Position >= Path.Count - 1;
        public string DestinationName => To != null ? To.Data.displayName : "заморский рынок";

        public Caravan(int id, int owner, GoodData good, float amount, City from, City to, bool bySea, List<HexCoord> path, float buyPrice)
        {
            Id = id;
            OwnerIndex = owner;
            Good = good;
            Amount = amount;
            From = from;
            To = to;
            BySea = bySea;
            Path = path;
            BuyPrice = buyPrice;
        }
    }
}
