namespace Runeterra.Map
{
    /// <summary>Состояние одной клетки карты. Чистый C#.</summary>
    public class HexTile
    {
        public HexCoord Coord { get; }
        public TerrainType Terrain { get; set; }
        public TileFeature Feature { get; set; }

        /// <summary>Сырая высота из генератора (0..1), для визуала и будущих правил.</summary>
        public float Elevation { get; set; }

        /// <summary>Влажность из генератора (0..1).</summary>
        public float Moisture { get; set; }

        /// <summary>По клетке проложена дорога: ход стоит 1, караваны идут быстрее.</summary>
        public bool HasRoad { get; set; }

        /// <summary>Месторождение регионального ресурса (id товара) или null.</summary>
        public string Resource { get; set; }

        /// <summary>id города на клетке или null.</summary>
        public string CityId { get; set; }

        public HexTile(HexCoord coord, TerrainType terrain, float elevation, float moisture)
        {
            Coord = coord;
            Terrain = terrain;
            Elevation = elevation;
            Moisture = moisture;
        }
    }
}
