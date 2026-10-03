using System.Collections.Generic;
using Runeterra.Economy;
using Runeterra.Map;

namespace Runeterra.Cities
{
    /// <summary>Город в партии. Чистое состояние.</summary>
    public class City
    {
        public CityData Data { get; }
        public HexCoord Coord { get; }
        public int OwnerIndex { get; internal set; }
        /// <summary>Регион, который основал город (для «столицы противника»).</summary>
        public int FounderIndex { get; internal set; }
        public bool IsCapital => Data.isCapital;

        /// <summary>Клетки территории города (по координатам: порядок обхода не зависит от истории изменений).</summary>
        public SortedSet<HexCoord> Territory { get; } = new SortedSet<HexCoord>(ContentOrder.Coords);

        /// <summary>Клетка рынка или null.</summary>
        public HexCoord? MarketCoord { get; internal set; }
        public bool HasMarket => MarketCoord != null;

        /// <summary>Клетка порта или null. Порт открывает морские пути и заморскую торговлю.</summary>
        public HexCoord? PortCoord { get; internal set; }
        public bool HasPort => PortCoord != null;

        // ---------- Стены ----------

        public const int MaxWalls = 100;
        public const int WallRepairPerTurn = 10;
        public const int BaseStrength = 15;
        public const int CapitalStrengthBonus = 5;
        public const int Range = 2;

        /// <summary>Прочность стен. При нуле город можно занять.</summary>
        public int Walls { get; private set; } = MaxWalls;
        /// <summary>Стены получали урон с начала последнего хода владельца (тогда не чинятся).</summary>
        public bool UnderSiege { get; private set; }

        public void DamageWalls(int amount)
        {
            Walls = System.Math.Max(0, Walls - amount);
            UnderSiege = true;
        }

        /// <summary>Начало хода владельца: ремонт стен, если осады не было.</summary>
        /// <param name="blockaded">Рядом стоит вражеский боевой юнит — каменщики не выходят, стены не чинятся.</param>
        public void BeginOwnerTurn(int extraRepair = 0, bool blockaded = false)
        {
            if (!UnderSiege && !blockaded) Walls = System.Math.Min(MaxWalls, Walls + WallRepairPerTurn + extraRepair);
            UnderSiege = false;
        }

        internal void RestoreWalls(int walls, bool underSiege)
        {
            Walls = walls;
            UnderSiege = underSiege;
        }

        /// <summary>После захвата стены частично восстановлены.</summary>
        public void ResetWallsAfterCapture() => Walls = MaxWalls / 4;

        // ---------- Рост и производство ----------

        public int Population { get; internal set; }
        public int FoodStock { get; internal set; }
        public int ProductionStock { get; internal set; }
        /// <summary>Что строится: юнит или район. null — ничего.</summary>
        public BuildItem CurrentBuild { get; internal set; }

        // ---------- Склад и постройки ----------

        public Warehouse Warehouse { get; } = new Warehouse();
        /// <summary>Постройки и их количество (мастерских одного типа может быть несколько).</summary>
        public SortedDictionary<BuildingData, int> Buildings { get; } = new SortedDictionary<BuildingData, int>(ContentOrder.Buildings);

        public int Count(BuildingData building) => building != null && Buildings.TryGetValue(building, out var n) ? n : 0;
        public bool Has(BuildingData building) => Count(building) > 0;
        public bool IsFull(BuildingData building) => Count(building) >= building.maxPerCity;

        public void AddBuilding(BuildingData building)
        {
            if (IsFull(building)) return;
            Buildings[building] = Count(building) + 1;
            Warehouse.Capacity += building.storageBonus;
            Warehouse.SpoilageMultiplier *= building.spoilageMultiplier;
        }

        // ---------- Работники ----------

        /// <summary>Мастера мастерских: тип мастерской → стаж каждого мастера в ходах.</summary>
        public SortedDictionary<BuildingData, List<int>> Masters { get; } = new SortedDictionary<BuildingData, List<int>>(ContentOrder.Buildings);

        public int Employed
        {
            get { int n = 0; foreach (var list in Masters.Values) n += list.Count; return n; }
        }

        /// <summary>Свободные жители (один всегда работает на земле).</summary>
        public int FreeWorkers => System.Math.Max(0, Population - 1 - Employed);

        public List<int> MastersOf(BuildingData workshop)
        {
            if (!Masters.TryGetValue(workshop, out var list)) Masters[workshop] = list = new List<int>();
            return list;
        }

        /// <summary>Стоимость сырья, добытого в последний ход (база земельного налога).</summary>
        public float LastRawValue { get; internal set; }
        /// <summary>Стоимость потреблённой роскоши в последний ход (база налога на роскошь).</summary>
        public float LastLuxuryValue { get; internal set; }
        /// <summary>В последний ход был неурожай (болезнь или нехватка зерна в засуху).</summary>
        public bool BadHarvest { get; internal set; }

        /// <summary>Текущая ступень поселения (пересчитывается каждый ход владельца).</summary>
        public SettlementTier Tier { get; internal set; }
        public int TierFailTurns { get; internal set; }

        /// <summary>Ход, раньше которого нельзя снова призывать ополчение.</summary>
        public int DraftCooldownUntil { get; internal set; }

        /// <summary>Болезнь урожая: товар → сколько ходов ещё вдвое меньше добыча.</summary>
        public SortedDictionary<GoodData, int> Blights { get; } = new SortedDictionary<GoodData, int>(ContentOrder.Goods);

        /// <summary>Радиус границ: 1, при населении 3 — 2, при 6 — 3.</summary>
        public int BorderRadius => Population >= 6 ? 3 : Population >= 3 ? 2 : 1;

        public City(CityData data, HexCoord coord, int ownerIndex)
        {
            Data = data;
            Coord = coord;
            OwnerIndex = ownerIndex;
            FounderIndex = ownerIndex;
            Population = System.Math.Max(1, data.startingPopulation);
        }
    }
}
