namespace Runeterra.Map
{
    public enum TerrainType
    {
        Ocean,
        Coast,
        Plains,
        Grassland,
        Desert,
        Hills,
        Mountains,
    }

    /// <summary>Объект на клетке поверх местности.</summary>
    public enum TileFeature
    {
        None,
        Forest,
        Oasis,
    }

    public static class TerrainRules
    {
        public const int Impassable = int.MaxValue;

        public static bool IsWater(this TerrainType t) => t == TerrainType.Ocean || t == TerrainType.Coast;

        public static bool IsPassable(this TerrainType t) => !t.IsWater() && t != TerrainType.Mountains;

        /// <summary>Стоимость входа на клетку в очках движения; Impassable — непроходимо.</summary>
        public static int MoveCost(TerrainType t, TileFeature f)
        {
            if (!t.IsPassable()) return Impassable;
            int cost = t == TerrainType.Hills ? 2 : 1;
            if (f == TileFeature.Forest) cost += 1;
            return cost;
        }

        public static int MoveCost(this HexTile tile)
        {
            int cost = MoveCost(tile.Terrain, tile.Feature);
            return tile.HasRoad && cost != Impassable ? 1 : cost;
        }
    }
}
