using System.Collections.Generic;
using UnityEngine;

namespace Runeterra.Tech
{
    public enum TechBranch
    {
        LandAndWater,
        Craft,
        Trade,
        Governance,
        Knowledge,
        War,
    }

    /// <summary>
    /// Узел дерева развития (контент). Эффект определяется по id в правилах игры (TechEffects).
    /// Цена растёт с числом уже изученных узлов (широта знаний).
    /// </summary>
    [CreateAssetMenu(fileName = "Tech_New", menuName = "Runeterra/Tech Data")]
    public class TechData : ScriptableObject
    {
        public string id;
        public string displayName;
        public TechBranch branch;
        [Tooltip("Позиция в ветке (0 — корень)")]
        public int order;
        public List<TechData> prerequisites = new List<TechData>();
        [Min(1)] public int baseCost = 30;
        [TextArea(2, 4)] public string effect;
        [Tooltip("Минус узла (если есть)")]
        [TextArea(1, 3)] public string downside;

        [Header("Живое дерево")]
        [Tooltip("Скрытый узел: появляется в дереве, только когда выполнено условие")]
        public bool hidden;
        [Tooltip("Условие появления скрытого узла (описание для игрока)")]
        public string revealHint;
        [Tooltip("Доктрина-развилка: изучив этот узел, второй взять нельзя")]
        public TechData exclusiveWith;
    }

    public static class TechBranches
    {
        public static string Name(TechBranch b) => b switch
        {
            TechBranch.LandAndWater => "Земля и вода",
            TechBranch.Craft => "Ремесло",
            TechBranch.Trade => "Торговля",
            TechBranch.Governance => "Управление",
            TechBranch.Knowledge => "Знание",
            _ => "Война",
        };
    }
}
