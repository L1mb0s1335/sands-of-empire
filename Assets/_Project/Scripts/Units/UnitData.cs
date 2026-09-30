using System.Collections.Generic;
using Runeterra.Economy;
using UnityEngine;

namespace Runeterra.Units
{
    public enum UnitRole
    {
        Melee,
        Recon,
        Civilian,
        Ranged,
    }

    /// <summary>Тип юнита (контент). Экземпляры юнитов в партии — класс Unit.</summary>
    [CreateAssetMenu(fileName = "Unit_New", menuName = "Runeterra/Unit Data")]
    public class UnitData : ScriptableObject
    {
        public string id;
        public string displayName;
        [TextArea(2, 4)] public string description;
        public UnitRole role;

        [Header("Характеристики")]
        [Min(1)] public int movement = 2;
        [Min(0)] public int strength = 20;
        [Min(1)] public int maxHealth = 100;
        [Min(1)] public int sightRange = 2;
        [Min(0)] public int productionCost = 40;

        [Header("Экономика")]
        [Min(0)] public int goldCost = 40;
        [Tooltip("Может ли юнит захватить вражеский город")]
        public bool canCapture = true;
        [Tooltip("Сколько построек может сделать (строитель)")]
        [Min(0)] public int buildCharges;
        [Tooltip("Может основать город (поселенец)")]
        public bool canFoundCity;
        [Tooltip("Товары со склада города, которые уходят на найм (кони, железо…)")]
        public List<GoodAmount> goodsCost = new List<GoodAmount>();
        [Tooltip("Конный юнит (фигурка верхом)")]
        public bool mounted;

        [Header("Дальний бой")]
        [Min(0)] public int rangedStrength;
        [Min(0)] public int range;

        public bool IsRanged => rangedStrength > 0 && range > 0;
    }
}
