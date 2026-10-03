using UnityEngine;

namespace Runeterra.Cities
{
    /// <summary>Район города (контент). Строится строителем на клетке рядом с городом.</summary>
    [CreateAssetMenu(fileName = "District_New", menuName = "Runeterra/District Data")]
    public class DistrictData : ScriptableObject
    {
        public string id;
        public string displayName;
        [TextArea(2, 4)] public string description;
        [Tooltip("Золото городу за ход")]
        public int goldPerTurn = 3;
        [Tooltip("Стоимость постройки производством города")]
        public int productionCost = 45;
    }
}
