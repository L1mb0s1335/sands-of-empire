using System.Collections.Generic;
using Runeterra.Map;
using UnityEngine;

namespace Runeterra.Economy
{
    /// <summary>
    /// Товар (контент). Сырьё добывается на клетках территории города:
    /// выход = (подходящих клеток / tilesPerUnit) × множитель сезона.
    /// </summary>
    [CreateAssetMenu(fileName = "Good_New", menuName = "Runeterra/Good Data")]
    public class GoodData : ScriptableObject
    {
        public string id;
        public string displayName;
        [Tooltip("Базовая цена в золоте")]
        public float basePrice = 2f;
        [Tooltip("Доля запаса, которая портится за ход (0.1 = 10%)")]
        [Range(0f, 1f)] public float spoilagePerTurn;
        [Tooltip("Сколько товара жители потребляют за ход на 1 жителя (предметы спроса: ткань, сласти)")]
        public float consumedPerPop;
        [Tooltip("Ступень цепочки: 0 — сырьё, 1 — полуфабрикат, 2 — товар, 3 — роскошь")]
        public int tier;

        [Header("Добыча (для сырья)")]
        public List<TerrainType> sourceTerrain = new List<TerrainType>();
        public List<TileFeature> sourceFeatures = new List<TileFeature>();
        [Min(1)] public int tilesPerUnit = 1;
        [Tooltip("Множитель выхода по сезонам: посев, урожай, засуха, торговый")]
        public float[] seasonMultipliers = { 1f, 1f, 1f, 1f };

        public bool IsRaw => sourceTerrain.Count > 0 || sourceFeatures.Count > 0;

        public bool FromTile(HexTile tile) =>
            sourceTerrain.Contains(tile.Terrain) || (tile.Feature != TileFeature.None && sourceFeatures.Contains(tile.Feature));

        public float SeasonMultiplier(Season s) =>
            seasonMultipliers != null && seasonMultipliers.Length == 4 ? seasonMultipliers[(int)s] : 1f;
    }
}
