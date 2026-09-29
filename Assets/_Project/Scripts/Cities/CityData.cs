using Runeterra.Map;
using UnityEngine;

namespace Runeterra.Cities
{
    /// <summary>
    /// Статичные данные города (контент). Состояние города в партии хранится отдельно.
    /// </summary>
    [CreateAssetMenu(fileName = "City_New", menuName = "Runeterra/City Data")]
    public class CityData : ScriptableObject
    {
        [Header("Идентификация")]
        public string id;
        public string displayName;
        [Tooltip("Альтернативное / историческое название")]
        public string altName;
        [TextArea(2, 5)] public string description;

        [Header("Старт")]
        public bool isCapital;
        [Tooltip("Смещение от стартовой точки региона на гекс-карте (осевые q, r)")]
        public HexCoord startOffset;
        [Min(1)] public int startingPopulation = 1;

        [Header("Базовый доход за ход")]
        public int food = 2;
        public int production = 1;
        public int gold = 1;
        public int science = 1;
        public int culture = 1;
        public int faith = 0;
    }
}
