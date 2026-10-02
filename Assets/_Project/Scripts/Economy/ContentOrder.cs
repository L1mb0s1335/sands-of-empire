using System.Collections.Generic;
using Runeterra.Map;

namespace Runeterra.Economy
{
    /// <summary>
    /// Порядок обхода коллекций партии по id контента и координатам. Словари и множества,
    /// которые обходятся правилами, отсортированы: результат не зависит от истории вставок
    /// (и одинаков после загрузки сохранения и у всех участников сетевой партии).
    /// </summary>
    public static class ContentOrder
    {
        public static readonly IComparer<GoodData> Goods =
            Comparer<GoodData>.Create((a, b) => string.CompareOrdinal(a?.id, b?.id));

        public static readonly IComparer<BuildingData> Buildings =
            Comparer<BuildingData>.Create((a, b) => string.CompareOrdinal(a?.id, b?.id));

        public static readonly IComparer<(int, GoodData)> OwnerGood =
            Comparer<(int, GoodData)>.Create((a, b) => a.Item1 != b.Item1 ? a.Item1.CompareTo(b.Item1) : Goods.Compare(a.Item2, b.Item2));

        public static readonly IComparer<HexCoord> Coords =
            Comparer<HexCoord>.Create((a, b) => a.q != b.q ? a.q.CompareTo(b.q) : a.r.CompareTo(b.r));
    }
}
