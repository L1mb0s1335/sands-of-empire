using Runeterra.Economy;
using Runeterra.Units;

namespace Runeterra.Cities
{
    /// <summary>Пункт производства города: юнит, район или постройка.</summary>
    public class BuildItem
    {
        public UnitData Unit { get; }
        public DistrictData District { get; }
        public BuildingData Building { get; }

        public BuildItem(UnitData unit) => Unit = unit;
        public BuildItem(DistrictData district) => District = district;
        public BuildItem(BuildingData building) => Building = building;

        public string Name => Unit != null ? Unit.displayName : District != null ? District.displayName : Building.displayName;
        public int Cost => Unit != null ? Unit.productionCost : District != null ? District.productionCost : Building.productionCost;

        public bool Is(BuildItem other) =>
            other != null && other.Unit == Unit && other.District == District && other.Building == Building;
    }
}
