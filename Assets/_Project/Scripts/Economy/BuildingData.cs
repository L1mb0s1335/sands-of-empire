using System;
using System.Collections.Generic;
using UnityEngine;

namespace Runeterra.Economy
{
    [Serializable]
    public struct GoodAmount
    {
        public GoodData good;
        public int amount;
    }

    /// <summary>Постройка внутри города (амбар, мастерская…). Строится производством, может требовать товары со склада.</summary>
    [CreateAssetMenu(fileName = "Building_New", menuName = "Runeterra/Building Data")]
    public class BuildingData : ScriptableObject
    {
        public string id;
        public string displayName;
        [TextArea(2, 4)] public string description;
        [Min(0)] public int productionCost = 40;
        [Tooltip("Товары со склада города, которые уходят на постройку")]
        public List<GoodAmount> goodsCost = new List<GoodAmount>();
        [Tooltip("Содержание золотом за ход (у мастерских 0: они сами приносят доход)")]
        [Min(0)] public int upkeep;

        [Header("Склад")]
        [Tooltip("Прибавка к вместимости склада по каждому товару")]
        public int storageBonus;
        [Tooltip("Множитель порчи (0.5 — портится вдвое медленнее)")]
        public float spoilageMultiplier = 1f;

        [Header("Город")]
        [Tooltip("Минимальная ступень поселения: 0 хутор, 1 деревня, 2 городок, 3 город, 4 столица торговли")]
        [Range(0, 4)] public int requiredTier;
        [Tooltip("Прибавка к запасу воды")]
        public int waterBonus;
        [Tooltip("Снижение шанса пожара за ход (0.02 = −2%)")]
        public float fireReduction;
        [Tooltip("Снижение преступности (доля)")]
        public float crimeReduction;
        [Tooltip("Снижение шанса эпидемии за ход")]
        public float epidemicReduction;

        [Header("Мастерская (рецепт)")]
        [Tooltip("Сколько таких построек можно в одном городе")]
        [Min(1)] public int maxPerCity = 1;
        public List<GoodAmount> inputs = new List<GoodAmount>();
        public GoodAmount output;
        [Tooltip("Сколько раз рецепт выполняется за ход (если хватает сырья)")]
        [Min(0)] public int batchesPerTurn;

        public bool IsWorkshop => output.good != null && batchesPerTurn > 0;

        public string RecipeText()
        {
            if (!IsWorkshop) return "";
            var parts = new List<string>();
            foreach (var i in inputs) if (i.good != null) parts.Add($"{i.good.displayName} {i.amount}");
            return $"{string.Join(" + ", parts)} → {output.good.displayName} {output.amount}";
        }
    }
}
