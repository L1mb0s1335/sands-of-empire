namespace Runeterra.Map
{
    /// <summary>Местность клетки. Новые значения — только в конец (в ассетах хранятся числом).</summary>
    public enum TerrainType
    {
        Ocean,
        Coast,
        Plains,
        Grassland,
        Desert,
        Hills,
        Mountains,
        /// <summary>Речная долина: плодородный пойменный берег реки.</summary>
        River,
        Marsh,
    }

    /// <summary>Объект на клетке поверх местности.</summary>
    public enum TileFeature
    {
        None,
        Forest,
        Oasis,
    }

    /// <summary>Восемь биомов карты — по ним распределены ресурсы и специализация стран.</summary>
    public enum Biome
    {
        Water,
        Desert,
        Oasis,
        RiverValley,
        Steppe,
        Mountains,
        Forest,
        Coast,
        Marsh,
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
            int cost = t == TerrainType.Hills || t == TerrainType.Marsh ? 2 : 1;
            if (f == TileFeature.Forest) cost += 1;
            return cost;
        }

        public static int MoveCost(this HexTile tile)
        {
            int cost = MoveCost(tile.Terrain, tile.Feature);
            return tile.HasRoad && cost != Impassable ? 1 : cost;
        }

        public static string BiomeName(Biome b) => b switch
        {
            Biome.Desert => "Пустыня",
            Biome.Oasis => "Оазис",
            Biome.RiverValley => "Речная долина",
            Biome.Steppe => "Степь",
            Biome.Mountains => "Горы",
            Biome.Forest => "Лес",
            Biome.Coast => "Побережье",
            Biome.Marsh => "Болото",
            _ => "Море",
        };
    }
}
