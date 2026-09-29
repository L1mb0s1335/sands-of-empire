using UnityEngine;

namespace Runeterra.Map
{
    /// <summary>Перевод гекс-координат в мир. Гексы «pointy-top», плоскость XZ.</summary>
    public static class HexMetrics
    {
        public const float Sqrt3 = 1.7320508f;

        public static Vector3 ToWorld(HexCoord c, float size) =>
            new Vector3(size * Sqrt3 * (c.q + c.r * 0.5f), 0f, -size * 1.5f * c.r);

        public static Vector3 Corner(int i, float size)
        {
            float angle = Mathf.Deg2Rad * (60f * i - 30f);
            return new Vector3(size * Mathf.Cos(angle), 0f, size * Mathf.Sin(angle));
        }
    }
}
