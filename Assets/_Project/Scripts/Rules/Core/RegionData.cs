using System.Collections.Generic;
using Runeterra.Cities;
using Runeterra.Units;
using UnityEngine;

namespace Runeterra.Core
{
    /// <summary>Способность лидера региона.</summary>
    public enum LeaderAbility
    {
        None,
        /// <summary>Салах ад-Дин: юниты лечатся быстрее.</summary>
        Mercy,
        /// <summary>Ричард Львиное Сердце: юниты сильнее в атаке.</summary>
        Lionheart,
        /// <summary>Исаак II Ангел: +1 золота с каждого города.</summary>
        ImperialTreasury,
        /// <summary>Кылыч-Арслан II: конные юниты +1 к движению.</summary>
        SteppeRiders,
        /// <summary>Ан-Насир: +25% знаний.</summary>
        HouseOfWisdom,
        /// <summary>Изз ад-Дин Масуд: города +5 к силе.</summary>
        MosulCitadel,
    }

    /// <summary>Облик юнитов региона.</summary>
    public enum UnitStyle
    {
        Saracen,
        Crusader,
    }

    /// <summary>
    /// Регион (цивилизация): оформление, стартовые города. Контент, не состояние партии.
    /// </summary>
    [CreateAssetMenu(fileName = "Region_New", menuName = "Runeterra/Region Data")]
    public class RegionData : ScriptableObject
    {
        [Header("Идентификация")]
        public string id;
        public string displayName;
        [TextArea(3, 8)] public string description;
        public string leaderName;
        public LeaderAbility leaderAbility;
        public string leaderAbilityName;
        [TextArea(1, 3)] public string leaderAbilityText;

        [Header("Оформление")]
        public Color primaryColor = Color.white;
        public Color secondaryColor = Color.black;
        public UnitStyle unitStyle;

        [Header("Города")]
        [Tooltip("Первый город с isCapital = true считается столицей")]
        public List<CityData> cities = new List<CityData>();

        [Header("Старт")]
        [Tooltip("Юниты, которые появляются у столицы в начале партии")]
        public List<UnitData> startingUnits = new List<UnitData>();
        [Min(0)] public int startingGold = 20;

        [Tooltip("Названия для новых городов, по порядку")]
        public List<string> cityNames = new List<string>();

        [Header("Хозяйство")]
        [Tooltip("Региональные ресурсы страны: у столицы и городов гарантированы месторождения")]
        public List<Runeterra.Economy.GoodData> specialties = new List<Runeterra.Economy.GoodData>();

        public CityData Capital => cities.Find(c => c != null && c.isCapital);
    }
}
